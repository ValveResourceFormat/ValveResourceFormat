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
    private record class RemapInstruction(
        string ChannelName,
        ChannelMapping ValveChannel,
        ChannelMapping GltfChannel,
        bool Invert = false
    );

    public static readonly Dictionary<string, (ChannelMapping Channel, string Name)[]> GltfTextureMappings = new()
    {
        ["BaseColor"] = [(ChannelMapping.RGB, "TextureColor"), (ChannelMapping.A, "TextureTranslucency")],
        ["Normal"] = [(ChannelMapping.RGB, "TextureNormal")],
        ["MetallicRoughness"] = [
            (ChannelMapping.R, string.Empty),
            (ChannelMapping.G, "TextureRoughness"),
            (ChannelMapping.B, "TextureMetalness")
        ],
        ["Occlusion"] = [(ChannelMapping.R, "TextureAmbientOcclusion")],
        ["Emissive"] = [(ChannelMapping.R, "TextureSelfIllumMask")],
    };

    // In SatelliteImages mode, SharpGLTF will still load and validate images.
    // To save memory, we initiate MemoryImage with a a dummy image instead.
    private static readonly byte[] DummyPng = [137, 80, 78, 71, 0, 0, 0, 0, 0, 0, 0, 0];

    private int TexturesExportedSoFar;
    private TextureSampler TextureSampler;
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

        var allGltfInputs = GltfTextureMappings.Values.SelectMany(x => x);
        var blendNameComparer = new MaterialExtract.LayeredTextureNameComparer(new HashSet<string>(allGltfInputs.Select(x => x.Name)));
        var blendInputComparer = new MaterialExtract.ChannelMappingComparer(blendNameComparer);

        // Remap vtex texture parameters into instructions that can be exported
        var remapDict = new Dictionary<string, List<RemapInstruction>>();
        foreach (var (textureKey, texturePath) in renderMaterial.TextureParams)
        {
            List<(ChannelMapping Channel, string Name)> inputImages = null;
            try
            {
                inputImages = shaderDataProvider.GetInputsForTexture(textureKey, renderMaterial).ToList();
            }
            catch (Exception e)
            {
                // Shaders are complicated, so do not stop exporting if they throw
                ProgressReporter?.Report($"Failed to get texture inputs for \"{textureKey}\": {e.Message}");
                Console.Error.WriteLine(e.ToString());
            }

            inputImages ??= shaderDataProviderFallback.GetInputsForTexture(textureKey, renderMaterial).ToList();
            var remapInstructions = GetRemapInstructions(inputImages);
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
        string ormRedChannel = null; // Can be Occlusion or Emissive

        // Find and split ORM textures into separate instructions
        if (AdaptTextures)
        {
            // TODO: too many loops over instructions here
            // If this texture contains a MetallicRoughness parameter, also pack Occlusion or Emissive into the ORM texture for optimization
            var allRemapInstructions = remapDict.Values.SelectMany(i => i).ToList();
            if (allRemapInstructions.Any(i => i.ChannelName == "MetallicRoughness"))
            {
                ormRedChannel = allRemapInstructions.FirstOrDefault(i => i.ChannelName == "Occlusion" || i.ChannelName == "Emissive")?.ChannelName;
            }

            foreach (var (texturePath, instructions) in remapDict)
            {
                var ormInstructions = instructions
                    .Where(i => i.ChannelName == ormRedChannel || i.ChannelName == "MetallicRoughness")
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
        }

        var openBitmaps = new Dictionary<string, SKBitmap>();

        try
        {
            // Actually go through the remapped textures and write them to disk
            foreach (var (texturePath, instructions) in remapDict)
            {
                // There should be only one
                var mainInstruction = instructions.FirstOrDefault();
                if (mainInstruction == null)
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

                TieTextureToMaterial(texture, mainInstruction.ChannelName);
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

                TieTextureToMaterial(texture, "MetallicRoughness");
            }
        }
        finally
        {
            foreach (var bitmap in openBitmaps.Values)
            {
                bitmap.Dispose();
            }
        }

        SKBitmap GetBitmap(string texturePath)
        {
            SKBitmap bitmap;

            lock (openBitmaps)
            {
                if (openBitmaps.TryGetValue(texturePath, out bitmap))
                {
                    return bitmap;
                }
            }

            // Not being disposed because ORM may use same texture multiple times and there's issues with concurrency
            Resource textureResource;

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
                var textureBlock = (ResourceTypes.Texture)textureResource.DataBlock;
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
                        instruction.Invert,
                        texturePath // Used for logging
                    );
                }
            }

            var pngBytes = TextureExtract.ToPngImage(occlusionRoughnessMetal.Bitmap);

            await LinkAndSaveImage(image, pngBytes).ConfigureAwait(false);
        }

        void TieTextureToMaterial(Texture tex, string gltfPackedName)
        {
            var materialChannel = material.FindChannel(gltfPackedName);
            materialChannel?.SetTexture(0, tex);

            if (gltfPackedName == "MetallicRoughness")
            {
                materialChannel?.SetFactor("MetallicFactor", 1.0f); // Ignore g_flMetalness

                if (ormRedChannel != null)
                {
                    material.FindChannel(ormRedChannel)?.SetTexture(0, tex);
                }
            }
        }

        List<RemapInstruction> GetRemapInstructions(List<(ChannelMapping Channel, string Name)> renderTextureInputs)
        {
            var instructions = new List<RemapInstruction>();

            foreach (var (GltfType, GltfInputs) in GltfTextureMappings)
            {
                // Old behavior, use the texture directly if the first input matches.
                if (!AdaptTextures)
                {
                    var renderTextureFirst = renderTextureInputs.FirstOrDefault();
                    var gltfTextureFirst = GltfInputs.First();
                    if (renderTextureFirst.Name is null || !blendNameComparer.Equals(renderTextureFirst.Name, gltfTextureFirst.Name))
                    {
                        continue;
                    }

                    instructions.Add(new RemapInstruction(GltfType, ChannelMapping.RGBA, ChannelMapping.RGBA));
                    break;
                }

                // Render texture matches the glTF spec.
                if (Enumerable.SequenceEqual(renderTextureInputs, GltfInputs, blendInputComparer))
                {
                    instructions.Add(new RemapInstruction(GltfType, ChannelMapping.RGBA, ChannelMapping.RGBA));
                    break;
                }

                foreach (var gltfInput in GltfInputs)
                {
                    foreach (var renderInput in renderTextureInputs)
                    {
                        if (blendNameComparer.Equals(renderInput.Name, gltfInput.Name))
                        {
                            instructions.Add(new RemapInstruction(GltfType, renderInput.Channel, gltfInput.Channel));
                            continue;
                        }

                        if (blendNameComparer.Equals(renderInput.Name, "TextureMetalnessMask"))
                        {
                            instructions.Add(new RemapInstruction("MetallicRoughness", renderInput.Channel, ChannelMapping.B));
                        }
                        else if (blendNameComparer.Equals(renderInput.Name, "TextureSpecularMask")) // Ideally we should use material.WithSpecular()
                        {
                            instructions.Add(new RemapInstruction("MetallicRoughness", renderInput.Channel, ChannelMapping.G, Invert: true));
                        }
                    }
                }
            }

            return instructions;
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
    }
}
