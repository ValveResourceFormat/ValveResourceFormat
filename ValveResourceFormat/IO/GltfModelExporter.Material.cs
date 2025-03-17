using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SkiaSharp;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ThirdParty;
using ValveResourceFormat.Utils;
using VMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.IO;

public partial class GltfModelExporter
{
    internal record class RemapInstruction(
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

    private async Task GenerateGLTFMaterialFromRenderMaterial(Material material, VMaterial renderMaterial, ModelRoot model)
    {
        await Task.Yield(); // Yield as the first step so it doesn't actually block

        CancellationToken.ThrowIfCancellationRequested();

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

        var baseColor = Vector4.One;

        if (renderMaterial.VectorParams.TryGetValue("g_vColorTint", out var vColorTint))
        {
            baseColor = Vector4.Clamp(vColorTint, Vector4.Zero, Vector4.One);
            baseColor.W = 1; //Tint only affects color
        }

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
                await Console.Error.WriteLineAsync(e.ToString()).ConfigureAwait(false);
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

                Task<SharpGLTF.Schema2.Texture> texTask;

                lock (TextureWriteSynchronizationLock)
                {
                    if (!ExportedTextures.TryGetValue(textureName, out texTask))
                    {
                        texTask = AddTexture(textureName, texturePath, mainInstruction);
                        ExportedTextures[textureName] = texTask;
                    }
                }

#if DEBUG
                ProgressReporter?.Report($"Task for texture {textureName} = {texTask.Status}");
#endif

                var tex = await texTask.ConfigureAwait(false);

                TieTextureToMaterial(tex, mainInstruction.ChannelName);
            }

            // Now create ORM if there is one
            if (ormTextureInstructions.Count > 0)
            {
                // Generate consistent file name for the ORM
                var ormTexturePaths = ormTextureInstructions.Keys.ToArray();
                Array.Sort(ormTexturePaths);
                var ormHash = MurmurHash2.Hash(string.Join("|", ormTexturePaths), StringToken.MURMUR2SEED);
                var ormFileName = Path.GetFileNameWithoutExtension(ormTexturePaths[0]) + $"_orm_{ormHash}.png";

                Task<SharpGLTF.Schema2.Texture> texTask;

                lock (TextureWriteSynchronizationLock)
                {
                    if (!ExportedTextures.TryGetValue(ormFileName, out texTask))
                    {
                        texTask = AddTextureORM(ormFileName);
                        ExportedTextures[ormFileName] = texTask;
                    }
                }

#if DEBUG
                ProgressReporter?.Report($"Task for ORM texture {ormFileName} = {texTask.Status}");
#endif

                var tex = await texTask.ConfigureAwait(false);

                TieTextureToMaterial(tex, "MetallicRoughness");
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
            if (openBitmaps.TryGetValue(texturePath, out var bitmap))
            {
                return bitmap;
            }

            // Not being disposed because ORM may use same texture multiple times and there's issues with concurrency
            var textureResource = FileLoader.LoadFileCompiled(texturePath);

            if (textureResource == null)
            {
                bitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                openBitmaps[texturePath] = bitmap;
                return bitmap;
            }

            lock (textureResource)
            {
                var textureBlock = (ResourceTypes.Texture)textureResource.DataBlock;
                bitmap = textureBlock.GenerateBitmap();
            }

            bitmap.SetImmutable();

            openBitmaps[texturePath] = bitmap;

            return bitmap;
        }

        async Task<SharpGLTF.Schema2.Texture> AddTexture(string key, string texturePath, RemapInstruction mainInstruction)
        {
            await Task.Yield();

#if DEBUG
            ProgressReporter?.Report($"Adding texture {key}");
#endif

            // Maybe GltfChannel should be preferred instead.
            var channel = mainInstruction.ValveChannel;

            if (mainInstruction.ValveChannel == ChannelMapping.RGBA && mainInstruction.GltfChannel == ChannelMapping.RGB)
            {
                // Some apps such as Blender do not like the excess alpha channel.
                channel = ChannelMapping.RGB;
            }

            var bitmap = GetBitmap(texturePath);
            var pngBytes = TextureExtract.ToPngImageChannels(bitmap, channel);

            return await WriteTexture(key, pngBytes).ConfigureAwait(false);
        }

        async Task<SharpGLTF.Schema2.Texture> AddTextureORM(string key)
        {
            await Task.Yield();

#if DEBUG
            ProgressReporter?.Report($"Adding ORM texture {key}");
#endif

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

            return await WriteTexture(key, TextureExtract.ToPngImage(occlusionRoughnessMetal.Bitmap)).ConfigureAwait(false);
        }

        async Task<SharpGLTF.Schema2.Texture> WriteTexture(string textureName, byte[] pngBytes)
        {
            Image image;

            lock (TextureWriteSynchronizationLock)
            {
                image = model.CreateImage(textureName);
            }

            await LinkAndSaveImage(image, pngBytes).ConfigureAwait(false);

            lock (TextureWriteSynchronizationLock)
            {
                var tex = model.UseTexture(image, TextureSampler);
                tex.Name = textureName;

                return tex;
            }
        }

        void TieTextureToMaterial(SharpGLTF.Schema2.Texture tex, string gltfPackedName)
        {
            var materialChannel = material.FindChannel(gltfPackedName);
            materialChannel?.SetTexture(0, tex);

            if (gltfPackedName == "BaseColor")
            {
                // TODO: Do we actually need this extras? sharpgltf writes pbrMetallicRoughness.baseColorTexture for us
                material.Extras = new System.Text.Json.Nodes.JsonObject
                    {
                        {
                            "baseColorTexture",
                            new System.Text.Json.Nodes.JsonObject
                            {
                                { "index", System.Text.Json.Nodes.JsonValue.Create(tex.PrimaryImage.LogicalIndex) }
                            }
                        }
                    };
            }
            else if (gltfPackedName == "MetallicRoughness")
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

    /// <summary>
    /// Links the image to the model and saves it to disk if <see cref="SatelliteImages"/> is true.
    /// </summary>
    private async Task LinkAndSaveImage(Image image, byte[] pngBytes)
    {
        CancellationToken.ThrowIfCancellationRequested();

        TexturesExportedSoFar++;
        ProgressReporter?.Report($"[{TexturesExportedSoFar}/{ExportedTextures.Count}] Exporting texture: {image.Name}");

        if (!SatelliteImages)
        {
            image.Content = pngBytes;
            return;
        }

        var fileName = Path.ChangeExtension(image.Name, "png");
        image.Content = new MemoryImage(dummyPng);
        image.AlternateWriteFileName = fileName;

        var exportedTexturePath = Path.Join(DstDir, fileName);
        using var fs = File.Open(exportedTexturePath, FileMode.Create);
        await fs.WriteAsync(pngBytes, CancellationToken).ConfigureAwait(false);
    }
}
