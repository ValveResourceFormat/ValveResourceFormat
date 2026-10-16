using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SkiaSharp;
using ValveResourceFormat.CompiledShader;
using VMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.IO;

public partial class GltfModelExporter
{
    private record struct RemapInstruction(string ChannelName, ChannelMapping ValveChannel, ChannelMapping GltfChannel)
    {
        public static RemapInstruction Default = new(string.Empty, ChannelMapping.RGBA, ChannelMapping.RGBA);
    }

    /// <summary>
    /// Maps glTF texture types to their channel configurations and Valve texture names.
    /// </summary>
    public static readonly Dictionary<string, (ChannelMapping Channel, string Name)[]> GltfTextureMappings = new()
    {
        ["BaseColor"] = [(ChannelMapping.RGB, "TextureColor"), (ChannelMapping.A, "TextureTranslucency")],
        ["Normal"] = [(ChannelMapping.RGB, "TextureNormal")],
        ["MetallicRoughness"] = [
            (ChannelMapping.G, "TextureRoughness"),
            (ChannelMapping.B, "TextureMetalness")
        ],
        ["SpecularFactor"] = [(ChannelMapping.A, "TextureSpecularMask")],
        ["Occlusion"] = [(ChannelMapping.R, "TextureAmbientOcclusion")],
        ["Emissive"] = [(ChannelMapping.RGB, "TextureSelfIllumMask")],
    };

    /// <summary>
    /// Gets all supported glTF texture channels from the mappings.
    /// </summary>
    public static readonly (ChannelMapping Channel, string Name)[] SupportedGltfChannels = [.. GltfTextureMappings.Values.SelectMany(x => x)];
    internal static MaterialExtract.LayeredTextureNameComparer BlendNameComparer = new([.. SupportedGltfChannels.Select(x => x.Name)]);
    internal static MaterialExtract.ChannelMappingComparer BlendInputComparer = new(BlendNameComparer);

    // In SatelliteImages mode, SharpGLTF will still load and validate images.
    // To save memory, we initiate MemoryImage with a dummy image instead.
    private static readonly byte[] DummyPng = [137, 80, 78, 71, 0, 0, 0, 0, 0, 0, 0, 0];

    private int TexturesExportedSoFar;
    private TextureSampler? TextureSampler;
    private readonly Lock TextureReadLock;
    private readonly List<Task> TextureExportingTasks = [];
    private readonly Dictionary<string, Texture> ExportedTextures = [];

    private void GenerateGLTFMaterialFromRenderMaterial(Material material, VMaterial renderMaterial, ModelRoot model, Vector4 modelTintColor)
    {
        renderMaterial.IntParams.TryGetValue("F_TRANSLUCENT", out var isTranslucent);
        renderMaterial.IntParams.TryGetValue("F_ALPHA_TEST", out var isAlphaTest);

        if (renderMaterial.ShaderName.EndsWith("_glass.vfx", StringComparison.InvariantCulture))
        {
            isTranslucent = 1;
        }

        material.Alpha = isTranslucent > 0 ? AlphaMode.BLEND : (isAlphaTest > 0 ? AlphaMode.MASK : AlphaMode.OPAQUE);
        if (isAlphaTest > 0 && renderMaterial.FloatParams.TryGetValue("g_flAlphaTestReference", out var alphaTestReference))
        {
            material.AlphaCutoff = alphaTestReference;
        }

        if (renderMaterial.IntParams.TryGetValue("F_RENDER_BACKFACES", out var doubleSided)
            && doubleSided > 0)
        {
            material.DoubleSided = true;
        }

        if (renderMaterial.IntParams.GetValueOrDefault("F_UNLIT") > 0)
        {
            material.WithUnlit();
        }

        // assume non-metallic unless prompted
        float metalValue = 0;

        if (renderMaterial.FloatParams.TryGetValue("g_flMetalness", out var flMetalness))
        {
            metalValue = float.Clamp(flMetalness, 0, 1);
        }

        var baseColor = modelTintColor;

        if (renderMaterial.FloatParams.TryGetValue("g_flModelTintAmount", out var flModelTintAmount))
        {
            baseColor = Vector4.Lerp(Vector4.One, baseColor, flModelTintAmount);
        }

        if (renderMaterial.VectorParams.TryGetValue("g_vColorTint", out var vColorTint))
        {
            baseColor *= vColorTint;
            baseColor.W = 1; //Tint only affects color
        }

        var baseColorLinear = ColorSpace.SrgbGammaToLinear(baseColor.AsVector3());
        baseColor = new Vector4(baseColorLinear, baseColor.W);
        baseColor = Vector4.Clamp(baseColor, Vector4.Zero, Vector4.One);

        var gltfChannelsCache = new Dictionary<string, List<RemapInstruction>>();
        var needsSpecularMode = false;

        foreach (var (textureKey, _) in renderMaterial.TextureParams)
        {
            var remapInstructions = GetGltfChannels(renderMaterial, textureKey);
            gltfChannelsCache[textureKey] = remapInstructions;

            if (remapInstructions.Any(static i => i.ChannelName == "SpecularFactor"))
            {
                needsSpecularMode = true;
            }
        }

        if (needsSpecularMode)
        {
            material.InitializePBRMetallicRoughness("Specular");
        }
        else
        {
            material.InitializePBRMetallicRoughness();
        }

        material
            .WithChannelColor("BaseColor", baseColor)
            .WithChannelFactor("MetallicRoughness", "MetallicFactor", metalValue)
            .WithChannelFactor("MetallicRoughness", "RoughnessFactor", 1f);

        var openBitmaps = new Dictionary<string, SKBitmap>();

        if (!AdaptTextures)
        {
            var textures = new Dictionary<string, string>(renderMaterial.TextureParams.Count);
            var channel = GetImageChannel(RemapInstruction.Default);

            foreach (var (textureKey, texturePath) in renderMaterial.TextureParams)
            {
                textures[textureKey] = Path.GetFileName(texturePath);

                var recipe = GetTextureRecipe(texturePath, channel);

                if (!ExportedTextures.TryGetValue(recipe, out var texture))
                {
                    var newImage = CreateNewGLTFImage(model, GetImageName(Path.GetFileNameWithoutExtension(texturePath), recipe));
                    texture = model.UseTexture(newImage, TextureSampler);
                    texture.Name = newImage.Name;

                    ExportedTextures[recipe] = texture;

                    var texTask = AddTexture(newImage, texturePath, channel);
                    TextureExportingTasks.Add(texTask);
                }

                var gltfChannels = gltfChannelsCache[textureKey];
                if (gltfChannels.Count > 0)
                {
                    TieTextureToMaterial(texture, gltfChannels[0].ChannelName, false);
                }
            }

            WriteMaterialExtras(material, renderMaterial, textures);
            return;
        }

        if (needsSpecularMode && renderMaterial.VectorParams.TryGetValue("g_vSpecularColor", out var vSpecularColor))
        {
            material.WithChannelColor("SpecularColor", vSpecularColor); // This might need SrgbGammaToLinear
        }

        // Remap vtex texture parameters into instructions that can be exported
        var remapDict = new Dictionary<string, List<RemapInstruction>>();
        foreach (var (textureKey, texturePath) in renderMaterial.TextureParams)
        {
            var remapInstructions = gltfChannelsCache[textureKey];
            if (remapInstructions.Count == 0)
            {
                continue;
            }

            remapDict[texturePath] = remapInstructions;
#if DEBUG
            ProgressReporter?.Report($"Remapping {texturePath} {string.Join(", ", remapInstructions.Select(i => i.ValveChannel + "->" + i.GltfChannel))}");
#endif
        }

        // ORM is a texture that may be compiled from multiple inputs
        var ormTextureInstructions = new Dictionary<string, List<RemapInstruction>>();
        var ormRedChannelForOcclusion = false;

        // Find and split ORM textures into separate instructions
        // TODO: too many loops over instructions here
        // If this texture contains a MetallicRoughness parameter, also pack Occlusion into the ORM texture for optimization
        // MetallicRoughness will use BG channels, and Occlusion only uses R channel
        if (remapDict.Values.SelectMany(static i => i).Any(static i => i.ChannelName == "MetallicRoughness"))
        {
            ormRedChannelForOcclusion = true;
        }

        foreach (var (texturePath, instructions) in remapDict)
        {
            var ormInstructions = instructions
                .Where(static i => i.ChannelName is "Occlusion" or "MetallicRoughness")
                .ToList();

            if (ormInstructions.Count > 0)
            {
                ormTextureInstructions[texturePath] = ormInstructions;

                foreach (var instruction in ormInstructions)
                {
                    instructions.Remove(instruction);
                }
            }
        }

        // Actually go through the remapped textures and write them to disk
        foreach (var (texturePath, instructions) in remapDict)
        {
            // There should be only one
            var mainInstruction = instructions.FirstOrDefault();
            if (mainInstruction == default)
            {
                continue;
            }

#if DEBUG
            if (instructions.Count != 1)
            {
                ProgressReporter?.Report($"Texture {texturePath} has {instructions.Count} instructions");
            }
#endif

            var channel = GetImageChannel(mainInstruction);
            var recipe = GetTextureRecipe(texturePath, channel);

            if (!ExportedTextures.TryGetValue(recipe, out var texture))
            {
                var newImage = CreateNewGLTFImage(model, GetImageName(Path.GetFileNameWithoutExtension(texturePath), recipe));
                texture = model.UseTexture(newImage, TextureSampler);
                texture.Name = newImage.Name;

                ExportedTextures[recipe] = texture;

                var texTask = AddTexture(newImage, texturePath, channel);
                TextureExportingTasks.Add(texTask);
            }

            TieTextureToMaterial(texture, mainInstruction.ChannelName, ormRedChannelForOcclusion);
        }

        // Now create ORM if there is one
        if (ormTextureInstructions.Count > 0)
        {
            var ormRecipe = new StringBuilder();

            foreach (var (texturePath, instructions) in ormTextureInstructions)
            {
                ormRecipe.Append(texturePath);

                foreach (var instruction in instructions)
                {
                    ormRecipe.Append(CultureInfo.InvariantCulture, $"|{instruction.ValveChannel.PackedValue:X8}>{instruction.GltfChannel.PackedValue:X8}");
                }

                ormRecipe.Append('\n');
            }

            var recipe = ormRecipe.ToString();

            if (!ExportedTextures.TryGetValue(recipe, out var texture))
            {
                var firstTexturePath = ormTextureInstructions.Keys.Min(StringComparer.Ordinal)!;
                var newImage = CreateNewGLTFImage(model, GetImageName($"{Path.GetFileNameWithoutExtension(firstTexturePath)}_orm", recipe));
                texture = model.UseTexture(newImage, TextureSampler);
                texture.Name = newImage.Name;

                ExportedTextures[recipe] = texture;

                var texTask = AddTextureORM(newImage);
                TextureExportingTasks.Add(texTask);
            }

            TieTextureToMaterial(texture, "MetallicRoughness", ormRedChannelForOcclusion);
        }

        WriteMaterialExtras(material, renderMaterial, renderMaterial.TextureParams);

        SKBitmap GetBitmap(string texturePath)
        {
            SKBitmap? bitmap;

            lock (openBitmaps)
            {
                if (openBitmaps.TryGetValue(texturePath, out bitmap))
                {
                    return bitmap;
                }
            }

            // Not being disposed because ORM may use same texture multiple times and there's issues with concurrency
            Resource? textureResource;

            // Our file loader is not specified to be safe for concurrency, even though it will work fine in most cases
            // because we use memory mapped files or read new files from disk. But some cases may read into memory stream,
            // and the tracking file loader has a hash set that is not concurrent.
            lock (TextureReadLock)
            {
                textureResource = FileLoader.LoadFileCompiled(texturePath);
            }

            if (textureResource == null)
            {
                bitmap = new SKBitmap(1, 1, ResourceTypes.Texture.DefaultBitmapColorType, SKAlphaType.Unpremul);

                lock (openBitmaps)
                {
                    openBitmaps[texturePath] = bitmap;
                }

                return bitmap;
            }

            lock (textureResource)
            {
                var textureBlock = (ResourceTypes.Texture)textureResource.DataBlock!;
                bitmap = textureBlock.GenerateBitmap();
            }

            bitmap.SetImmutable();

            lock (openBitmaps)
            {
                openBitmaps[texturePath] = bitmap;
            }

            return bitmap;
        }

        async Task AddTexture(Image image, string texturePath, ChannelMapping channel)
        {
            await Task.Yield();

            var bitmap = GetBitmap(texturePath);
            var pngBytes = TextureExtract.ToPngImageChannels(bitmap, channel);

            LinkAndSaveImage(image, pngBytes);
        }

        async Task AddTextureORM(Image image)
        {
            await Task.Yield();

            using var occlusionRoughnessMetal = new TextureExtract.TexturePacker { DefaultColor = new SKColor(255, 255, 0, 255) };

            // Collect channels for the ORM texture
            foreach (var (texturePath, instructions) in ormTextureInstructions)
            {
                var bitmap = GetBitmap(texturePath);
                using var pixels = bitmap.PeekPixels();

                foreach (var instruction in instructions)
                {
                    occlusionRoughnessMetal.Collect(pixels,
                        instruction.ValveChannel.Count == 1 ? instruction.ValveChannel : ChannelMapping.R,
                        instruction.GltfChannel.Count == 1 ? instruction.GltfChannel : ChannelMapping.R,
                        texturePath // Used for logging
                    );
                }
            }

            if (occlusionRoughnessMetal.Bitmap == null)
            {
                throw new InvalidOperationException("Failed to create ORM texture bitmap");
            }

            var pngBytes = TextureExtract.ToPngImage(occlusionRoughnessMetal.Bitmap);

            LinkAndSaveImage(image, pngBytes);
        }

        void TieTextureToMaterial(Texture tex, string gltfPackedName, bool ormRedChannelForOcclusion)
        {
            var materialChannel = material.FindChannel(gltfPackedName);
            materialChannel?.SetTexture(0, tex);

            if (gltfPackedName == "MetallicRoughness")
            {
                materialChannel?.SetFactor("MetallicFactor", 1.0f); // Ignore g_flMetalness

                if (ormRedChannelForOcclusion)
                {
                    material.FindChannel("Occlusion")?.SetTexture(0, tex);
                }
            }
        }

        List<RemapInstruction> GetGltfChannels(VMaterial renderMaterial, string textureKey)
        {
            List<(ChannelMapping Channel, string Name)> inputImages = [.. shaderDataProvider.GetInputsForTexture(textureKey, renderMaterial)];
            var remapInstructions = RemapValveChannelsToGltf(inputImages);
            return remapInstructions;
        }

        List<RemapInstruction> RemapValveChannelsToGltf(List<(ChannelMapping Channel, string Name)> renderTextureChannels)
        {
            var instructions = new List<RemapInstruction>();

            foreach (var (GltfType, GltfInputs) in GltfTextureMappings)
            {
                // Render texture channels match the glTF channels exactly.
                if (Enumerable.SequenceEqual(renderTextureChannels, GltfInputs, BlendInputComparer))
                {
                    instructions.Add(new RemapInstruction(GltfType, ChannelMapping.RGBA, ChannelMapping.RGBA));
                    break;
                }

                foreach (var gltfInput in GltfInputs)
                {
                    foreach (var renderInput in renderTextureChannels)
                    {
                        if (BlendNameComparer.Equals(renderInput.Name, gltfInput.Name))
                        {
                            instructions.Add(new RemapInstruction(GltfType, renderInput.Channel, gltfInput.Channel));
                            continue;
                        }
                    }
                }
            }

            foreach (var renderInput in renderTextureChannels)
            {
                // TextureMetalness alias
                if (BlendNameComparer.Equals(renderInput.Name, "TextureMetalnessMask"))
                {
                    instructions.Add(new RemapInstruction("MetallicRoughness", renderInput.Channel, ChannelMapping.B));
                }
            }

            return instructions;
        }

        static void WriteMaterialExtras(Material material, VMaterial renderMaterial, Dictionary<string, string> textures)
        {
            var intParamsJson = new System.Text.Json.Nodes.JsonObject();
            foreach (var kvp in renderMaterial.IntParams)
            {
                intParamsJson[kvp.Key] = kvp.Value;
            }

            var floatParamsJson = new System.Text.Json.Nodes.JsonObject();
            foreach (var kvp in renderMaterial.FloatParams)
            {
                floatParamsJson[kvp.Key] = kvp.Value;
            }

            var vectorParamsJson = new System.Text.Json.Nodes.JsonObject();
            foreach (var kvp in renderMaterial.VectorParams)
            {
                var vectorArray = new System.Text.Json.Nodes.JsonArray(
                    kvp.Value.X,
                    kvp.Value.Y,
                    kvp.Value.Z,
                    kvp.Value.W
                );
                vectorParamsJson[kvp.Key] = vectorArray;
            }

            var textureParamsJson = new System.Text.Json.Nodes.JsonObject();
            foreach (var kvp in textures)
            {
                textureParamsJson[kvp.Key] = kvp.Value;
            }

            material.Extras = new System.Text.Json.Nodes.JsonObject
            {
                ["vmat"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["Name"] = renderMaterial.Name,
                    ["ShaderName"] = renderMaterial.ShaderName,
                    ["IntParams"] = intParamsJson,
                    ["FloatParams"] = floatParamsJson,
                    ["VectorParams"] = vectorParamsJson,
                    ["TextureParams"] = textureParamsJson,
                }
            };
        }
    }

    private static ChannelMapping GetImageChannel(RemapInstruction mainInstruction)
    {
        // Some apps such as Blender do not like the excess alpha channel.
        if (mainInstruction.ValveChannel == ChannelMapping.RGBA && mainInstruction.GltfChannel == ChannelMapping.RGB)
        {
            return ChannelMapping.RGB;
        }

        // Maybe GltfChannel should be preferred instead.
        return mainInstruction.ValveChannel;
    }

    private static string GetTextureRecipe(string texturePath, ChannelMapping channel)
        => string.Create(CultureInfo.InvariantCulture, $"{texturePath}|{channel.PackedValue:X8}");

    /// <summary>
    /// Gets the name of an image from a hash of everything its contents are made from, so images with different contents
    /// never share a name and the same image exported by several glTF files into one folder is written once.
    /// </summary>
    private static string GetImageName(string baseName, string recipe)
    {
        var hash = XxHash32.HashToUInt32(MemoryMarshal.AsBytes(recipe.AsSpan()));
        return string.Create(CultureInfo.InvariantCulture, $"{baseName}_{hash:x8}.png");
    }

    private Image CreateNewGLTFImage(ModelRoot model, string textureName)
    {
        var newImage = model.CreateImage(textureName);
        newImage.Content = new MemoryImage(DummyPng);

        if (SatelliteImages)
        {
            newImage.AlternateWriteFileName = Path.ChangeExtension(newImage.Name, "png");
        }

        return newImage;
    }

    /// <summary>
    /// In embedded mode links the PNG bytes into the glTF <see cref="Image"/>; in satellite mode
    /// (<see cref="SatelliteImages"/> true) instead writes the PNG to a separate file next to the glTF, which is
    /// serialized once every image is written.
    /// </summary>
    private void LinkAndSaveImage(Image image, byte[] pngBytes)
    {
        CancellationToken.ThrowIfCancellationRequested();

        if (!SatelliteImages || DstDir == null)
        {
            image.Content = pngBytes;
            return;
        }

        var path = Path.Join(DstDir, image.AlternateWriteFileName);
        Exporter!.WriteFile(ContentPaths.GetRelativePath(Exporter.OutputRoot, path), pngBytes, path);

        var count = Interlocked.Increment(ref TexturesExportedSoFar);
        ProgressReporter?.Report($"[{count}/{ExportedTextures.Count}] Exported texture: {image.Name}");
    }

    private void WaitForTexturesToExport()
    {
        if (TextureExportingTasks.Any(static t => !t.IsCompleted))
        {
            ProgressReporter?.Report("Waiting for textures to finish exporting...");
            Task.WhenAll(TextureExportingTasks).WaitAsync(CancellationToken).GetAwaiter().GetResult();
        }

        TexturesExportedSoFar = 0;
        TextureExportingTasks.Clear();
    }

    /// <summary>
    /// Waits for the textures of a failed export, so none of them is written once the export has returned. Their
    /// failures are not reported, the export already failed.
    /// </summary>
    private void ObserveTextureTasks()
    {
        try
        {
            Task.WaitAll(TextureExportingTasks);
        }
        catch (AggregateException)
        {
        }

        TextureExportingTasks.Clear();
    }

    // :MaterialIsOverlay
    private static bool IsMaterialOverlay(VMaterial material)
    {
        if (material.IntParams.GetValueOrDefault("F_OVERLAY") == 1)
        {
            return true;

        }

        // Renderer only assumes depth bias is overlay for transparent materials of specific shader - but for gltf it should be fine for all
        if (material.IntParams.GetValueOrDefault("F_DEPTHBIAS") == 1 || material.IntParams.GetValueOrDefault("F_DEPTH_BIAS") == 1)
        {
            return true;
        }

        if (material.ShaderName.EndsWith("static_overlay.vfx", StringComparison.Ordinal) || material.ShaderName is "citadel_overlay.vfx")
        {
            return true;
        }

        return false;
    }
}
