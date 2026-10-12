using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO.Hashing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.RHI;
using ValveResourceFormat.ResourceTypes;
using Vortice.Vulkan;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;
using VrfTexture = ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>
    /// Loads and caches materials and textures from Source 2 resources.
    /// </summary>
    public partial class MaterialLoader
    {
        //private readonly Dictionary<ulong, RenderMaterial> Materials = [];

        // Materials are loaded on loader threads and patched on the render thread
        private readonly List<RenderMaterial> OwnedMaterials = [];
        private readonly Lock OwnedMaterialsLock = new();

        // Keyed by sRGB too, a shader member declared [SrgbRead] samples the same file through an sRGB format
        private readonly Dictionary<(string Path, bool Srgb), RHI.Texture> Textures = [];
        private readonly Lock TexturesLock = new();
        //private readonly Dictionary<(RsTextureAddressMode AddressU, RsTextureAddressMode AddressV, bool Mipmaps, bool AnisotropicFiltering), int> Samplers = [];
        private readonly RendererContext RendererContext;
        private RHI.Texture? ErrorTexture;
        private readonly RHI.Texture?[] FallbackTextures = new RHI.Texture?[Enum.GetValues<TextureDimension>().Length];
        //private RenderTexture? DefaultNormal;
        //private RenderTexture? DefaultMask;
        //private RenderTexture? DefaultColor;
        /// <summary>
        /// Gets or sets the maximum anisotropy level applied to newly loaded textures, clamped to what the device supports.
        /// Zero uses the device maximum. Anisotropic filtering is off below 4.
        /// </summary>
        public static float MaxTextureMaxAnisotropy { get; set; }

        /// <summary>Gets the number of materials currently held in the cache.</summary>
        //public int MaterialCount => Materials.Count;

        /// <summary>
        /// Maps a material texture parameter name to the shader uniforms it can feed, in preference order.
        /// The first candidate the shader declares and that is not already bound wins.
        /// </summary>
        private static readonly Dictionary<string, string[]> TextureAliases = new(StringComparer.Ordinal)
        {
            ["g_tColor1"] = ["g_tColor"],
            ["g_tColor2"] = ["g_tColor", "g_tLayer2Color"],
            ["g_tColorA"] = ["g_tColor"],
            ["g_tColorB"] = ["g_tLayer2Color", "g_tColor"],
            ["g_tColorC"] = ["g_tColor"],
            ["g_tGlassDust"] = ["g_tColor"],
            ["g_tNormalA"] = ["g_tNormal"],
            ["g_tNormalB"] = ["g_tLayer2NormalRoughness"],
            ["g_tNormalRoughness"] = ["g_tNormal"],
            ["g_tNormalRoughness1"] = ["g_tNormal"],
            ["g_tNormalRoughness2"] = ["g_tLayer2NormalRoughness"],
            ["g_tLayer1NormalRoughness"] = ["g_tNormal"],
            ["g_tLayer1AmbientOcclusion"] = ["g_tAmbientOcclusion"],
        };

        /// <summary>Initializes a new instance of the <see cref="MaterialLoader"/> class.</summary>
        /// <param name="rendererContext">The renderer context used for file loading and shader access.</param>
        public MaterialLoader(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
        }

        private static readonly byte[] NewLineArray = "\n"u8.ToArray();

        /// <summary>
        /// Clears the material cache and disposes any cached textures and samplers.
        /// </summary>
        //        public void Clear()
        //        {
        //            foreach (var material in OwnedMaterials)
        //            {
        //                material.Delete();
        //            }

        //            OwnedMaterials.Clear();
        //            Materials.Clear();

        //            foreach (var item in Textures)
        //            {
        //                item.Value.Delete();
        //            }

        //            Textures.Clear();

        //            foreach (var item in TexturesSrgb)
        //            {
        //                item.Value.Delete();
        //            }

        //            TexturesSrgb.Clear();

        //            foreach (var sampler in Samplers.Values)
        //            {
        //                GL.DeleteSampler(sampler);
        //            }

        //            Samplers.Clear();

        //            RendererContext.TextureStreaming.CancelAllStreaming();
        //        }

        //        /// <summary>Returns a cached <see cref="RenderMaterial"/> for the given resource path and shader arguments, loading and caching it on first access.</summary>
        //        /// <param name="name">The compiled material resource path, or <see langword="null"/> to return the error material.</param>
        //        /// <param name="shaderArguments">Optional static combo overrides to pass to the shader.</param>
        public RenderMaterial GetMaterial(string? name, VBIB vbib, Dictionary<string, byte>? shaderArguments)
        {
            //    // HL:VR has a world node that has a draw call with no material
            //    if (name == null)
            //    {
            //        return GetErrorMaterial();
            //    }

            //    Span<byte> valueSpan = stackalloc byte[1];
            //    var hash = new XxHash3(StringToken.MURMUR2SEED);
            //    hash.Append(MemoryMarshal.AsBytes(name.AsSpan()));

            //    if (shaderArguments != null)
            //    {
            //        foreach (var (key, value) in shaderArguments)
            //        {
            //            hash.Append(NewLineArray);
            //            hash.Append(MemoryMarshal.AsBytes(key.AsSpan()));
            //            hash.Append(NewLineArray);

            //            valueSpan[0] = value;
            //            hash.Append(valueSpan);
            //        }
            //    }

            //    var cacheKey = hash.GetCurrentHashAsUInt64();

            //if (Materials.TryGetValue(cacheKey, out var mat))
            //{
            //    return mat;
            //}

            var resource = RendererContext.FileLoader.LoadFileCompiled(name);
            var mat = LoadMaterial(resource, vbib, shaderArguments);

            //Materials.Add(cacheKey, mat);

            return mat;
        }

        /// <summary>Creates a <see cref="RenderMaterial"/> from an already-loaded resource, binding textures and resolving aliases.</summary>
        /// <param name="resource">The material resource, or <see langword="null"/> to return the error material.</param>
        /// <param name="shaderArguments">Optional static combo overrides to pass to the shader.</param>
        public RenderMaterial LoadMaterial(Resource? resource, VBIB vbib, Dictionary<string, byte>? shaderArguments = null)
        {
            if (resource == null)
            {
                //return GetErrorMaterial();
            }

            var vrfMaterial = (VrfMaterial?)resource.DataBlock;
            Debug.Assert(vrfMaterial != null);
            var mat = new RenderMaterial(
                vrfMaterial,
                RendererContext,
                vbib,
                shaderArguments
            );

            // Bound against the layout the material is created with. Members a shader hot reload adds later
            // stay unbound until the material is loaded again.
            var layout = mat.Pipeline.Current.ParameterLayout;

            if (layout == null)
            {
                AddOwnedMaterial(mat);
                return mat;
            }

            foreach (var (textureName, texturePath) in mat.Material.TextureParams)
            {
                TryBindTexture(mat, layout, textureName, texturePath);
            }

            foreach (var (textureName, texturePath) in mat.Material.TextureParams)
            {
                if (mat.Textures.ContainsKey(textureName)
                || !TextureAliases.TryGetValue(textureName, out var aliases))
                {
                    continue;
                }

                foreach (var alias in aliases)
                {
                    if (mat.Textures.ContainsKey(alias))
                    {
                        continue;
                    }

                    if (TryBindTexture(mat, layout, alias, texturePath))
                    {
                        break;
                    }
                }
            }

            // Only once its textures are bound, the render thread reads them as soon as it is listed
            AddOwnedMaterial(mat);

            return mat;
        }

        private void AddOwnedMaterial(RenderMaterial mat)
        {
            using var _ = OwnedMaterialsLock.EnterScope();
            OwnedMaterials.Add(mat);
        }

        /// <summary>
        /// Updates the texture handles in every material's parameter buffer that changed since they were written, such
        /// as when streamed mips arrive. Must be recorded outside rendering, before the frame's draws.
        /// </summary>
        /// <param name="cmd">The command list to record the updates into.</param>
        internal void PatchTextureHandles(CommandList cmd)
        {
            const VkPipelineStageFlags2 ShaderStages = VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader;

            // Earlier frames may still be reading the handles being replaced
            cmd.MemoryBarrier(ShaderStages, VkAccessFlags2.None, VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite);

            using (OwnedMaterialsLock.EnterScope())
            {
                foreach (var material in OwnedMaterials)
                {
                    material.PatchTextureHandles(cmd);
                }
            }

            cmd.MemoryBarrier(VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite, ShaderStages, VkAccessFlags2.UniformRead);
        }

        /// <summary>Releases the GPU resources of every material this loader created.</summary>
        public void Clear()
        {
            using var _ = OwnedMaterialsLock.EnterScope();

            foreach (var material in OwnedMaterials)
            {
                material.Delete();
            }

            OwnedMaterials.Clear();
        }

        private bool TryBindTexture(RenderMaterial mat, ParameterLayout layout, string name, string path)
        {
            if (!layout.Members.TryGetValue(name, out var member) || !member.IsTexture)
            {
                return false;
            }

            var texture = GetTexture(path, member.IsSrgbRead);

            // Bound anyway, the parameter buffer falls back to a texture of the right shape for it
            if (texture.Dimension != member.TextureDimension)
            {
                RendererContext.Logger.LogWarning("Texture '{Path}' is a {TextureDimension} but '{Name}' in '{Shader}' samples a {MemberDimension}",
                    path, texture.Dimension, name, mat.ShaderName, member.TextureDimension);
            }

            mat.Textures[name] = texture;
            return true;
        }

        /// <summary>Returns a cached texture for the given path, loading it on first access. Its mips stream in over the following frames.</summary>
        /// <param name="name">The compiled texture resource path.</param>
        /// <param name="srgbRead">Whether to read the texture data through an sRGB format, where the format has one.</param>
        public RHI.Texture GetTexture(string name, bool srgbRead = false)
        {
            using var _ = TexturesLock.EnterScope();

            if (Textures.TryGetValue((name, srgbRead), out var texture))
            {
                return texture;
            }

            texture = LoadTexture(name, srgbRead);
            Textures.Add((name, srgbRead), texture);

            return texture;
        }

        private RHI.Texture LoadTexture(string name, bool srgbRead)
        {
            var textureResource = RendererContext.FileLoader.LoadFileCompiled(name);

            if (textureResource == null)
            {
                return GetErrorTexture();
            }

            return LoadTexture(textureResource, srgbRead);
        }

        /// <summary>Creates a texture for a texture resource and starts streaming its mips in.</summary>
        /// <param name="textureResource">The loaded texture resource.</param>
        /// <param name="srgbRead">Whether to use the sRGB format when there is one.</param>
        public RHI.Texture LoadTexture(Resource textureResource, bool srgbRead = false)
        {
            var data = (VrfTexture?)textureResource.DataBlock
                ?? throw new ArgumentException($"{textureResource.FileName} has no data block, it was never read", nameof(textureResource));

            var textureName = Path.GetFileName(textureResource.FileName) ?? "UnnamedTexture";

            if (data.IsRawAnyImage)
            {
                using var bitmap = data.GenerateBitmap();
                return LoadBitmapTexture(bitmap, srgbRead, textureName);
            }

            var dimension = TextureDimension.Texture2D;

            if ((data.Flags & VTexFlags.CUBE_TEXTURE) != 0)
            {
                dimension = (data.Flags & VTexFlags.TEXTURE_ARRAY) != 0 ? TextureDimension.TextureCubeArray : TextureDimension.TextureCube;
            }
            else if ((data.Flags & (VTexFlags.TEXTURE_ARRAY | VTexFlags.VOLUME_TEXTURE)) != 0)
            {
                dimension = (data.Flags & VTexFlags.VOLUME_TEXTURE) != 0 ? TextureDimension.Texture3D : TextureDimension.Texture2DArray;
            }

            var srgb = srgbRead && TextureFormats.HasSrgbVariant(data.Format);
            var format = TextureFormats.GetVkFormat(data.Format, srgb);
            var decodeToRgba8 = false;

            // Compressed formats depend on device features, and most of them are optional for volume textures
            if (format == VkFormat.Undefined || !RenderDevice!.SupportsSampledImage(format, dimension))
            {
                if (!TextureFormats.CanDecodeToRgba8(data.Format))
                {
                    RendererContext.Logger.LogWarning("Texture '{Name}' is {Format} as a {Dimension}, which this device cannot sample", textureName, data.Format, dimension);
                    return GetErrorTexture();
                }

                decodeToRgba8 = true;
                format = srgb ? VkFormat.R8G8B8A8Srgb : VkFormat.R8G8B8A8Unorm;
            }

            var minMipLevelAllowed = 0;
            var texWidth = data.Width;
            var texHeight = data.Height;

            if (dimension == TextureDimension.Texture2D && data.NumMipLevels > 1)
            {
                var maxUserTextureSize = RendererContext.MaxTextureSize;

                while (minMipLevelAllowed + 1 < data.NumMipLevels && (texWidth > maxUserTextureSize || texHeight > maxUserTextureSize))
                {
                    minMipLevelAllowed++;

                    texWidth >>= 1;
                    texHeight >>= 1;
                }
            }

            var texture = new RHI.Texture((uint)texWidth, (uint)texHeight, format, (uint)(data.NumMipLevels - minMipLevelAllowed), dimension,
                (uint)Math.Max(1, (int)data.Depth), CreateSamplerInfo(data.Flags, dimension));

            RenderDevice!.SetObjectDebugName(texture.ImageHandle, VkObjectType.Image, textureName);

            var streaming = RendererContext.TextureStreaming;
            streaming.BeginStreaming(new StreamedTexture(streaming, texture, textureName, data, minMipLevelAllowed, decodeToRgba8));

            return texture;
        }

        private static VkSamplerCreateInfo CreateSamplerInfo(VTexFlags flags, TextureDimension dimension)
        {
            var device = RenderDevice!;
            var info = RHI.Texture.DefaultSamplerInfo;

            if (RHI.Image.IsCube(dimension))
            {
                info.addressModeU = VkSamplerAddressMode.ClampToEdge;
                info.addressModeV = VkSamplerAddressMode.ClampToEdge;
                info.addressModeW = VkSamplerAddressMode.ClampToEdge;
            }
            else
            {
                info.addressModeU = (flags & VTexFlags.SUGGEST_CLAMPS) != 0 ? VkSamplerAddressMode.ClampToBorder : VkSamplerAddressMode.Repeat;
                info.addressModeV = (flags & VTexFlags.SUGGEST_CLAMPT) != 0 ? VkSamplerAddressMode.ClampToBorder : VkSamplerAddressMode.Repeat;
                info.addressModeW = (flags & VTexFlags.SUGGEST_CLAMPU) != 0 ? VkSamplerAddressMode.ClampToBorder : VkSamplerAddressMode.Repeat;
                info.borderColor = VkBorderColor.FloatTransparentBlack;
            }

            var anisotropy = MaxTextureMaxAnisotropy > 0f
                ? MathF.Min(MaxTextureMaxAnisotropy, device.MaxSamplerAnisotropy)
                : device.MaxSamplerAnisotropy;

            if (device.EnabledFeatures.samplerAnisotropy && anisotropy >= 4f)
            {
                info.anisotropyEnable = true;
                info.maxAnisotropy = anisotropy;
            }

            return info;
        }

        //        /// <summary>
        //        /// Gets a sampler object for the supplied texture address modes, creating and caching one per <see cref="MaterialLoader" />.
        //        /// </summary>
        //        public int GetOrCreateSampler(RsTextureAddressMode addressModeU, RsTextureAddressMode addressModeV, bool mipmaps = true, bool anisotropicFiltering = true)
        //        {
        //            var key = (addressModeU, addressModeV, mipmaps, anisotropicFiltering);

        //            if (key == (RsTextureAddressMode.Wrap, RsTextureAddressMode.Wrap, true, true))
        //            {
        //                return 0; // the default sampler state already wraps
        //            }

        //            if (Samplers.TryGetValue(key, out var sampler))
        //            {
        //                return sampler;
        //            }

        //            var newSampler = new Sampler($"Sampler{addressModeU}{addressModeV}");

        //            newSampler.SetWrapMode(addressModeU, addressModeV);
        //            newSampler.SetFiltering(mipmaps ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear, TextureMagFilter.Linear);

        //            if (anisotropicFiltering && MaxTextureMaxAnisotropy >= 4)
        //            {
        //                newSampler.SetMaxAnisotropy(MaxTextureMaxAnisotropy);
        //            }

        //            Samplers[key] = newSampler.Handle;
        //            return newSampler.Handle;
        //        }

        //        /// <summary>Gets the texture unit each reserved sampler uniform is bound to.</summary>
        //        public static readonly FrozenDictionary<string, ReservedTextureSlots> ReservedTextureSlotByName = BuildReservedTextureSlotByName();

        //        private static FrozenDictionary<string, ReservedTextureSlots> BuildReservedTextureSlotByName()
        //        {
        //            var slotByName = new Dictionary<string, ReservedTextureSlots>(StringComparer.Ordinal);

        //            foreach (var field in typeof(ReservedTextureSlots).GetFields(BindingFlags.Public | BindingFlags.Static))
        //            {
        //                var attribute = field.GetCustomAttribute<SamplerNameAttribute>();

        //                if (attribute == null)
        //                {
        //                    continue; // Aliases such as Last carry no names of their own.
        //                }

        //                var slot = (ReservedTextureSlots)field.GetRawConstantValue()!;

        //                foreach (var name in attribute.Names)
        //                {
        //                    // Add, not assign: two slots claiming one sampler name is a mistake worth failing on.
        //                    slotByName.Add(name, slot);
        //                }
        //            }

        //            return slotByName.ToFrozenDictionary(StringComparer.Ordinal);
        //        }

        //        /// <summary>Returns whether a uniform name is bound to one of the <see cref="ReservedTextureSlots"/>.</summary>
        //        public static bool IsReservedTexture(string uniformName) => ReservedTextureSlotByName.ContainsKey(uniformName);

        //        /// <summary>
        //        /// Material invariant textures, requested by shaders. They become scene-wide textures.
        //        /// </summary>
        //        public static readonly List<(ReservedTextureSlots Slot, string Name, string Path)> ShaderTextures =
        //        [
        //            (ReservedTextureSlots.WetnessWaves, "g_tWetnessWaves", "materials/dev/water_waves.vtex"),
        //        ];

        //        private RenderMaterial GetErrorMaterial()
        //        {
        //            var errorMat = new RenderMaterial(RendererContext.ShaderLoader.LoadShader("error"));
        //            OwnedMaterials.Add(errorMat);
        //            return errorMat;
        //        }

        /// <summary>Returns a lazily created 4x4 checkerboard texture, used for textures that are missing or cannot be loaded.</summary>
        public RHI.Texture GetErrorTexture()
        {
            using var _ = TexturesLock.EnterScope();

            if (ErrorTexture == null)
            {
                ReadOnlySpan<byte> color1 = [100, 25, 75, 255];
                ReadOnlySpan<byte> color2 = [0, 127, 0, 255];

                var pixels = new byte[16 * 4];

                for (var i = 0; i < 16; i++)
                {
                    var checkerboardX = i / 4 % 2;
                    var colorToUse = i % 2 == checkerboardX ? color1 : color2;
                    colorToUse.CopyTo(pixels.AsSpan(i * 4, 4));
                }

                var samplerInfo = RHI.Texture.DefaultSamplerInfo;
                samplerInfo.magFilter = VkFilter.Nearest;
                samplerInfo.minFilter = VkFilter.Nearest;

                ErrorTexture = new RHI.Texture(4, 4, VkFormat.R8G8B8A8Unorm, samplerInfo: samplerInfo);

                var streaming = RendererContext.TextureStreaming;
                streaming.BeginStreaming(new StreamedTexture(streaming, ErrorTexture, "ErrorTexture", pixels));
            }

            return ErrorTexture;
        }

        //        private static RenderTexture CreateSolidTexture(byte r, byte g, byte b) => GenerateColorTexture(1, 1, [r, g, b]);
        //        /// <summary>Returns a lazily created 1×1 flat normal map texture (127, 127, 255).</summary>
        //        public RenderTexture GetDefaultNormal() => DefaultNormal ??= CreateSolidTexture(127, 127, 255);

        //        /// <summary>Returns a lazily created 1×1 solid white mask texture.</summary>
        //        public RenderTexture GetDefaultMask() => DefaultMask ??= CreateSolidTexture(255, 255, 255);

        //        /// <summary>Returns a lazily created 1×1 solid white colour texture, a neutral fallback albedo.</summary>
        //        public RenderTexture GetDefaultColor() => DefaultColor ??= CreateSolidTexture(255, 255, 255);

        /// <summary>
        /// Gets a white 1x1 texture of the given shape, which parameter buffers use in place of textures that have
        /// no mips yet or do not match what the shader samples. Resident from the first frame on.
        /// </summary>
        /// <param name="dimension">The shape the shader samples.</param>
        public RHI.Texture GetFallbackTexture(TextureDimension dimension)
            => FallbackTextures[(int)dimension] ?? throw new InvalidOperationException("Fallback textures are created by the first frame's uploads.");

        /// <summary>Creates the fallback textures and reads them in right away, to be uploaded by the frame calling this.</summary>
        internal void CreateFallbackTextures()
        {
            var streaming = RendererContext.TextureStreaming;

            foreach (var dimension in Enum.GetValues<TextureDimension>())
            {
                if (dimension == TextureDimension.None)
                {
                    continue;
                }

                var texture = new RHI.Texture(1, 1, VkFormat.R8G8B8A8Unorm, dimension: dimension);
                var pixels = new byte[texture.ArrayLayers * 4];
                pixels.AsSpan().Fill(255);

                streaming.LoadNow(new StreamedTexture(streaming, texture, $"Fallback{dimension}", pixels));
                FallbackTextures[(int)dimension] = texture;
            }
        }

        //        /// <summary>Returns the readback format appropriate for exporting a rendered image: 8-bit BGRA, or 32-bit float RGBA for HDR.</summary>
        //        /// <param name="hdr">Whether to use the HDR (32-bit float) format.</param>
        //        public static ImageFormat GetImageExportFormat(bool hdr)
        //            => hdr ? ImageFormat.RGBA32323232F : ImageFormat.BGRA8888;

        /// <summary>Creates a 2D texture from an <see cref="SKBitmap"/> and starts uploading it.</summary>
        /// <param name="bitmap">The bitmap whose pixels are uploaded to the GPU.</param>
        /// <param name="srgbRead">Whether to read 8 bit colour through an sRGB format.</param>
        /// <param name="name">Name used for the texture in graphics debuggers and logs.</param>
        public RHI.Texture LoadBitmapTexture(SKBitmap bitmap, bool srgbRead = false, string name = "BitmapTexture")
        {
            var (format, bytesPerPixel) = bitmap.ColorType switch
            {
                SKColorType.Rgba8888 => (srgbRead ? VkFormat.R8G8B8A8Srgb : VkFormat.R8G8B8A8Unorm, 4),
                SKColorType.Bgra8888 => (srgbRead ? VkFormat.B8G8R8A8Srgb : VkFormat.B8G8R8A8Unorm, 4),
                SKColorType.Rgb888x => (srgbRead ? VkFormat.R8G8B8A8Srgb : VkFormat.R8G8B8A8Unorm, 4),
                SKColorType.Gray8 => (VkFormat.R8Unorm, 1),
                SKColorType.RgbaF16 => (VkFormat.R16G16B16A16Sfloat, 8),
                SKColorType.RgbaF32 => (VkFormat.R32G32B32A32Sfloat, 16),
                _ => throw new NotSupportedException($"Unsupported bitmap color type for GPU upload {bitmap.ColorType}"),
            };

            var rowSize = bitmap.Width * bytesPerPixel;
            var pixels = new byte[rowSize * bitmap.Height];
            var source = bitmap.GetPixelSpan();

            // Rows can be padded, uploads are tightly packed
            for (var y = 0; y < bitmap.Height; y++)
            {
                source.Slice(y * bitmap.RowBytes, rowSize).CopyTo(pixels.AsSpan(y * rowSize, rowSize));
            }

            if (bitmap.ColorType == SKColorType.Rgb888x)
            {
                // The fourth byte is undefined, the format is opaque by definition
                for (var i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }
            }

            var texture = new RHI.Texture((uint)bitmap.Width, (uint)bitmap.Height, format);
            RenderDevice!.SetObjectDebugName(texture.ImageHandle, VkObjectType.Image, name);

            var streaming = RendererContext.TextureStreaming;
            streaming.BeginStreaming(new StreamedTexture(streaming, texture, name, pixels));

            return texture;
        }

        //        /// <summary>
        //        /// Builds a one-dimensional colour ramp from a list of gradient stops.
        //        /// </summary>
        //        /// <param name="stops">Gradient stops, each a position in 0-1 and its colour. Need not be sorted.</param>
        //        public static RenderTexture GenerateGradientTexture(ReadOnlySpan<(float Position, Color32 Color)> stops)
        //        {
        //            const int Width = 256;

        //            var texels = new byte[Width * 4];

        //            for (var x = 0; x < Width; x++)
        //            {
        //                var position = x / (Width - 1f);
        //                var color = SampleGradient(stops, position);

        //                texels[(x * 4) + 0] = color.R;
        //                texels[(x * 4) + 1] = color.G;
        //                texels[(x * 4) + 2] = color.B;
        //                texels[(x * 4) + 3] = color.A;
        //            }

        //            var texture = new RenderTexture(TextureTarget.Texture2D, Width, 1, 1, 1, "GeneratedGradient");

        //            // Clamped and filtered: the ramp is addressed by a luminance, so the ends have to hold rather
        //            // than wrap, and the steps between stops should not be visible.
        //            texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        //            texture.SetWrapMode(RsTextureAddressMode.Clamp);

        //            // sRGB storage, so a sample lands in linear space like every other layer's texture.
        //            GL.TextureStorage2D(texture.Handle, 1, SizedInternalFormat.Srgb8Alpha8, Width, 1);
        //            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, Width, 1, PixelFormat.Rgba, PixelType.UnsignedByte, texels);

        //            return texture;
        //        }

        //        private static Color32 SampleGradient(ReadOnlySpan<(float Position, Color32 Color)> stops, float position)
        //        {
        //            if (stops.Length == 0)
        //            {
        //                return new Color32(255, 255, 255);
        //            }

        //            // Stops are authored in order, but nothing guarantees it, so pick the bracketing pair by value
        //            // rather than by index.
        //            var lower = stops[0];
        //            var upper = stops[0];
        //            var hasLower = false;
        //            var hasUpper = false;

        //            foreach (var stop in stops)
        //            {
        //                if (stop.Position <= position && (!hasLower || stop.Position >= lower.Position))
        //                {
        //                    lower = stop;
        //                    hasLower = true;
        //                }

        //                if (stop.Position >= position && (!hasUpper || stop.Position <= upper.Position))
        //                {
        //                    upper = stop;
        //                    hasUpper = true;
        //                }
        //            }

        //            if (!hasLower)
        //            {
        //                return upper.Color;
        //            }

        //            if (!hasUpper)
        //            {
        //                return lower.Color;
        //            }

        //            var span = upper.Position - lower.Position;
        //            var t = span > 0f ? (position - lower.Position) / span : 0f;

        //            return new Color32(
        //                (byte)float.Round(float.Lerp(lower.Color.R, upper.Color.R, t)),
        //                (byte)float.Round(float.Lerp(lower.Color.G, upper.Color.G, t)),
        //                (byte)float.Round(float.Lerp(lower.Color.B, upper.Color.B, t)),
        //                (byte)float.Round(float.Lerp(lower.Color.A, upper.Color.A, t)));
        //        }
    }
}
