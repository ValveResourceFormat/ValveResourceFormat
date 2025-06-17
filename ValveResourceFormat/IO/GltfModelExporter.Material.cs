using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SkiaSharp;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ThirdParty;
using VMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.IO;

public partial class GltfModelExporter
{
    private record struct RemapInstruction(string ChannelName, ChannelMapping ValveChannel, ChannelMapping GltfChannel)
    {
        public static RemapInstruction Default = new(string.Empty, ChannelMapping.RGBA, ChannelMapping.RGBA);
    }

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

    public static readonly (ChannelMapping Channel, string Name)[] SupportedGltfChannels = [.. GltfTextureMappings.Values.SelectMany(x => x)];
    internal static MaterialExtract.LayeredTextureNameComparer BlendNameComparer = new([.. SupportedGltfChannels.Select(x => x.Name)]);
    internal static MaterialExtract.ChannelMappingComparer BlendInputComparer = new(BlendNameComparer);


    // In SatelliteImages mode, SharpGLTF will still load and validate images.
    // To save memory, we initiate MemoryImage with a a dummy image instead.
    private static readonly byte[] DummyPng = [137, 80, 78, 71, 0, 0, 0, 0, 0, 0, 0, 0];

    private int TexturesExportedSoFar;
    private TextureSampler? TextureSampler;
    private readonly Lock TextureReadLock = new();
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

        baseColor = Vector4.Clamp(baseColor, Vector4.Zero, Vector4.One);

        material.WithPBRMetallicRoughness(baseColor, null, metallicFactor: metalValue);

        var openBitmaps = new Dictionary<string, SKBitmap>();

        if (!AdaptTextures)
        {
            var textures = new Dictionary<string, string>(renderMaterial.TextureParams.Count);

            foreach (var (textureKey, texturePath) in renderMaterial.TextureParams)
            {
                var textureName = Path.GetFileName(texturePath);
                textures[textureKey] = textureName;

                if (!ExportedTextures.TryGetValue(textureName, out var texture))
                {
                    var newImage = CreateNewGLTFImage(model, textureName);
                    texture = model.UseTexture(newImage, TextureSampler);
                    texture.Name = newImage.Name;

                    ExportedTextures[textureName] = texture;

                    var texTask = AddTexture(newImage, texturePath, RemapInstruction.Default);
                    TextureExportingTasks.Add(texTask);
                }

                var gltfChannels = GetGltfChannels(renderMaterial, textureKey);
                if (gltfChannels.FirstOrDefault() is { } gltfChannel)
                {
                    TieTextureToMaterial(texture, gltfChannel.ChannelName, false);
                }
            }

            WriteMaterialExtras(material, renderMaterial, textures);
            return;
        }

        if (renderMaterial.VectorParams.TryGetValue("g_vSpecularColor", out var vSpecularColor))
        {
            // TODO - perhaps material.WithChannelColor?
        }

        // Remap vtex texture parameters into instructions that can be exported
        var remapDict = new Dictionary<string, List<RemapInstruction>>();
        foreach (var (textureKey, texturePath) in renderMaterial.TextureParams)
        {
            var remapInstructions = GetGltfChannels(renderMaterial, textureKey);
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
        using var occlusionRoughnessMetal = new TextureExtract.TexturePacker { DefaultColor = new SKColor(255, 255, 0, 255) };
        var ormTextureInstructions = new Dictionary<string, List<RemapInstruction>>();
        var ormRedChannelForOcclusion = false;

        // Find and split ORM textures into separate instructions
        // TODO: too many loops over instructions here
        // If this texture contains a MetallicRoughness parameter, also pack Occlusion into the ORM texture for optimization
        // MetallicRoughness will use BG channels, and Occlusion only uses R channel
        var allRemapInstructions = remapDict.Values.SelectMany(i => i).ToList();
        if (allRemapInstructions.Any(static i => i.ChannelName == "MetallicRoughness"))
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

            var textureName = Path.GetFileName(texturePath);

#if DEBUG
            if (instructions.Count != 1)
            {
                ProgressReporter?.Report($"Texture {textureName} has {instructions.Count} instructions");
            }
#endif

            if (!ExportedTextures.TryGetValue(textureName, out var texture))
            {
                var newImage = CreateNewGLTFImage(model, textureName);
                texture = model.UseTexture(newImage, TextureSampler);
                texture.Name = newImage.Name;

                ExportedTextures[textureName] = texture;

                var texTask = AddTexture(newImage, texturePath, mainInstruction);
                TextureExportingTasks.Add(texTask);
            }

            TieTextureToMaterial(texture, mainInstruction.ChannelName, ormRedChannelForOcclusion);
        }

        // Now create ORM if there is one
        if (ormTextureInstructions.Count > 0)
        {
            // Generate consistent file name for the ORM
            var ormTexturePaths = ormTextureInstructions.Keys.ToArray();
            Array.Sort(ormTexturePaths);
            var ormHash = MurmurHash2.Hash(string.Join("|", ormTexturePaths), StringToken.MURMUR2SEED);
            var ormFileName = Path.GetFileNameWithoutExtension(ormTexturePaths[0]) + $"_orm_{ormHash}.png";

            if (!ExportedTextures.TryGetValue(ormFileName, out var texture))
            {
                var newImage = CreateNewGLTFImage(model, ormFileName);
                texture = model.UseTexture(newImage, TextureSampler);
                texture.Name = newImage.Name;

                ExportedTextures[ormFileName] = texture;

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

            // Our file loader is not specified to be safe for concurrency, even though it will work fine on most cases
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

        async Task AddTexture(Image image, string texturePath, RemapInstruction mainInstruction)
        {
            await Task.Yield();

            // Maybe GltfChannel should be preferred instead.
            var channel = mainInstruction.ValveChannel;

            if (mainInstruction.ValveChannel == ChannelMapping.RGBA && mainInstruction.GltfChannel == ChannelMapping.RGB)
            {
                // Some apps such as Blender do not like the excess alpha channel.
                channel = ChannelMapping.RGB;
            }

            var bitmap = GetBitmap(texturePath);
            var pngBytes = TextureExtract.ToPngImageChannels(bitmap, channel);

            await LinkAndSaveImage(image, pngBytes).ConfigureAwait(false);
        }

        async Task AddTextureORM(Image image)
        {
            await Task.Yield();

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

            var pngBytes = TextureExtract.ToPngImage(occlusionRoughnessMetal.Bitmap);

            await LinkAndSaveImage(image, pngBytes).ConfigureAwait(false);
        }

        void TieTextureToMaterial(Texture tex, string gltfPackedName, bool ormRedChannelForOcclusion)
        {
            if (gltfPackedName == "SpecularFactor")
            {
                // TODO: Is there a better way to do this in sharpgltf?
                material.InitializePBRMetallicRoughness("Specular");
            }

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
            List<(ChannelMapping Channel, string Name)>? inputImages = null;
            try
            {
                inputImages = [.. shaderDataProvider.GetInputsForTexture(textureKey, renderMaterial)];
            }
            catch (Exception e)
            {
                // Shaders are complicated, so do not stop exporting if they throw
                ProgressReporter?.Report($"Failed to get texture inputs for \"{textureKey}\": {e.Message}");
                Console.Error.WriteLine(e.ToString());
            }

            inputImages ??= [.. shaderDataProviderFallback.GetInputsForTexture(textureKey, renderMaterial)];
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
            material.Extras = new System.Text.Json.Nodes.JsonObject
            {
                ["vmat"] = System.Text.Json.JsonSerializer.SerializeToNode(new Dictionary<string, object>
                {
                    ["Name"] = renderMaterial.Name,
                    ["ShaderName"] = renderMaterial.ShaderName,
                    ["IntParams"] = renderMaterial.IntParams,
                    ["FloatParams"] = renderMaterial.FloatParams,
                    ["VectorParams"] = renderMaterial.VectorParams.ToDictionary(kvp => kvp.Key, kvp => new float[] { kvp.Value.X, kvp.Value.Y, kvp.Value.Z, kvp.Value.W }),
                    ["TextureParams"] = textures,
                })
            };
        }
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
    /// Links the image to the model and saves it to disk if <see cref="SatelliteImages"/> is true.
    /// </summary>
    private async Task LinkAndSaveImage(Image image, byte[] pngBytes)
    {
        CancellationToken.ThrowIfCancellationRequested();

        if (!SatelliteImages)
        {
            image.Content = pngBytes;
            return;
        }

        // Do not modify Image object here because the gltf will have been saved by now

        var fileName = Path.ChangeExtension(image.Name, "png");

        var exportedTexturePath = Path.Join(DstDir, fileName);
        using var fs = File.Open(exportedTexturePath, FileMode.Create);
        await fs.WriteAsync(pngBytes, CancellationToken).ConfigureAwait(false);

        var count = Interlocked.Increment(ref TexturesExportedSoFar);
        ProgressReporter?.Report($"[{count}/{ExportedTextures.Count}] Exported texture: {image.Name}");
    }

    private void WaitForTexturesToExport()
    {
        if (TextureExportingTasks.Any(static t => !t.IsCompleted))
        {
            ProgressReporter?.Report("Waiting for textures to finish exporting...");
            Task.WaitAll(TextureExportingTasks, CancellationToken);
        }

        TexturesExportedSoFar = 0;
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
