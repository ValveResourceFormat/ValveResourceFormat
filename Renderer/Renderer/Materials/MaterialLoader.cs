using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO.Hashing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>
    /// Loads and caches materials and textures from Source 2 resources.
    /// </summary>
    public class MaterialLoader
    {
        private readonly Dictionary<ulong, RenderMaterial> Materials = [];
        private readonly List<RenderMaterial> OwnedMaterials = [];

        private readonly Dictionary<string, RenderTexture> Textures = [];
        private readonly Dictionary<string, RenderTexture> TexturesSrgb = [];
        private readonly Dictionary<(int AddressU, int AddressV, bool AnisotropicFiltering), int> Samplers = [];
        private readonly RendererContext RendererContext;
        private RenderTexture? ErrorTexture;
        private RenderTexture? DefaultNormal;
        private RenderTexture? DefaultMask;
        private RenderTexture? DefaultColor;
        private RenderTexture? DefaultVolume;
        /// <summary>Gets or sets the maximum anisotropy level applied to newly loaded textures when anisotropic filtering is enabled.</summary>
        public static float MaxTextureMaxAnisotropy { get; set; }

        /// <summary>Gets the number of materials currently held in the cache.</summary>
        public int MaterialCount => Materials.Count;

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
        public void Clear()
        {
            foreach (var material in OwnedMaterials)
            {
                material.Delete();
            }

            OwnedMaterials.Clear();
            Materials.Clear();

            foreach (var item in Textures)
            {
                item.Value.Delete();
            }

            Textures.Clear();

            foreach (var item in TexturesSrgb)
            {
                item.Value.Delete();
            }

            TexturesSrgb.Clear();

            foreach (var sampler in Samplers.Values)
            {
                GL.DeleteSampler(sampler);
            }

            Samplers.Clear();
        }

        /// <summary>Returns a cached <see cref="RenderMaterial"/> for the given resource path and shader arguments, loading and caching it on first access.</summary>
        /// <param name="name">The compiled material resource path, or <see langword="null"/> to return the error material.</param>
        /// <param name="shaderArguments">Optional static combo overrides to pass to the shader.</param>
        public RenderMaterial GetMaterial(string? name, Dictionary<string, byte>? shaderArguments)
        {
            // HL:VR has a world node that has a draw call with no material
            if (name == null)
            {
                return GetErrorMaterial();
            }

            Span<byte> valueSpan = stackalloc byte[1];
            var hash = new XxHash3(StringToken.MURMUR2SEED);
            hash.Append(MemoryMarshal.AsBytes(name.AsSpan()));

            if (shaderArguments != null)
            {
                foreach (var (key, value) in shaderArguments)
                {
                    hash.Append(NewLineArray);
                    hash.Append(MemoryMarshal.AsBytes(key.AsSpan()));
                    hash.Append(NewLineArray);

                    valueSpan[0] = value;
                    hash.Append(valueSpan);
                }
            }

            var cacheKey = hash.GetCurrentHashAsUInt64();

            if (Materials.TryGetValue(cacheKey, out var mat))
            {
                return mat;
            }

            var resource = RendererContext.FileLoader.LoadFileCompiled(name);
            mat = LoadMaterial(resource, shaderArguments);

            Materials.Add(cacheKey, mat);

            return mat;
        }

        /// <summary>Creates a <see cref="RenderMaterial"/> from an already-loaded resource, binding textures and resolving aliases.</summary>
        /// <param name="resource">The material resource, or <see langword="null"/> to return the error material.</param>
        /// <param name="shaderArguments">Optional static combo overrides to pass to the shader.</param>
        public RenderMaterial LoadMaterial(Resource? resource, Dictionary<string, byte>? shaderArguments = null)
        {
            if (resource == null)
            {
                return GetErrorMaterial();
            }

            var vrfMaterial = (VrfMaterial?)resource.DataBlock;
            Debug.Assert(vrfMaterial != null);
            var mat = new RenderMaterial(
                vrfMaterial,
                RendererContext,
                shaderArguments
            );

            OwnedMaterials.Add(mat);

            foreach (var (textureName, texturePath) in mat.Material.TextureParams)
            {
                TryBindTexture(mat, textureName, texturePath);
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

                    if (TryBindTexture(mat, alias, texturePath))
                    {
                        break;
                    }
                }
            }

            bool TryBindTexture(RenderMaterial mat, string name, string path)
            {
                if (mat.Shader.UniformNames.Contains(name))
                {
                    var srgbRead = mat.Shader.SrgbUniforms.Contains(name);
                    mat.Textures[name] = GetTexture(path, srgbRead, anisotropicFiltering: true);
                    return true;
                }

                return false;
            }

            return mat;
        }


        /// <summary>Returns a cached <see cref="RenderTexture"/> for the given path, loading it on first access.</summary>
        /// <param name="name">The compiled texture resource path.</param>
        /// <param name="srgbRead">Whether to interpret the texture data in sRGB color space.</param>
        /// <param name="anisotropicFiltering">Whether to apply anisotropic filtering when <see cref="MaxTextureMaxAnisotropy"/> is sufficient.</param>
        public RenderTexture GetTexture(string name, bool srgbRead = false, bool anisotropicFiltering = false)
        {
            // TODO: Create texture view for srgb textures
            var cache = srgbRead ? TexturesSrgb : Textures;

            if (cache.TryGetValue(name, out var tex))
            {
                return tex;
            }

            tex = LoadTexture(name, srgbRead);
            cache.Add(name, tex);

            if (anisotropicFiltering && MaxTextureMaxAnisotropy >= 4)
            {
                GL.TextureParameter(tex.Handle, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, MaxTextureMaxAnisotropy);
            }

            return tex;
        }

        /// <summary>
        /// Gets a sampler object for the supplied texture address modes, creating and caching one per <see cref="MaterialLoader" />.
        /// </summary>
        public int GetOrCreateSampler(int addressModeU, int addressModeV, bool mipmaps = true, bool anisotropicFiltering = true)
        {
            var key = (addressModeU, addressModeV, anisotropicFiltering);

            if (key == (0, 0, true))
            {
                return 0; // default sampler state with repeat wrap mode
            }

            if (Samplers.TryGetValue(key, out var sampler))
            {
                return sampler;
            }

            GL.CreateSamplers(1, out sampler);

#if DEBUG
            var samplerLabel = $"Sampler{addressModeU}{addressModeV}";
            GL.ObjectLabel(ObjectLabelIdentifier.Sampler, sampler, samplerLabel.Length, samplerLabel);
#endif

            GL.SamplerParameter(sampler, SamplerParameterName.TextureWrapS, (int)MapAddressMode(addressModeU));
            GL.SamplerParameter(sampler, SamplerParameterName.TextureWrapT, (int)MapAddressMode(addressModeV));
            GL.SamplerParameter(sampler, SamplerParameterName.TextureMinFilter, (int)(mipmaps ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear));
            GL.SamplerParameter(sampler, SamplerParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            if (anisotropicFiltering && MaxTextureMaxAnisotropy >= 4)
            {
                GL.SamplerParameter(sampler, (SamplerParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, MaxTextureMaxAnisotropy);
            }

            Samplers[key] = sampler;
            return sampler;
        }

        private static TextureWrapMode MapAddressMode(int mode) => mode switch
        {
            0 => TextureWrapMode.Repeat,
            1 => TextureWrapMode.MirroredRepeat,
            2 => TextureWrapMode.ClampToEdge,
            3 => TextureWrapMode.ClampToBorder,
            _ => TextureWrapMode.Repeat,
        };

        private RenderTexture LoadTexture(string name, bool srgbRead = false)
        {
            var textureResource = RendererContext.FileLoader.LoadFileCompiled(name);

            if (textureResource == null)
            {
                return GetErrorTexture();
            }

            return LoadTexture(textureResource, srgbRead);
        }

#pragma warning disable CA1822 // Mark members as static
        /// <summary>Uploads a texture resource to the GPU and returns the resulting <see cref="RenderTexture"/>.</summary>
        /// <param name="textureResource">The loaded texture resource.</param>
        /// <param name="srgbRead">Whether to use the sRGB internal format when available.</param>
        /// <param name="isViewerRequest">When <see langword="true"/>, skips mip-level capping and keeps the resource alive after upload.</param>
        public RenderTexture LoadTexture(Resource textureResource, bool srgbRead = false, bool isViewerRequest = false)
#pragma warning restore CA1822 // Mark members as static
        {
            var data = (Texture?)textureResource.DataBlock;
            Debug.Assert(data != null);

            if (data.IsRawAnyImage)
            {
                using var bitmap = data.GenerateBitmap();
                return LoadBitmapTexture(bitmap);
            }

            var target = TextureTarget.Texture2D;
            var is3d = false;
            var clampModeS = (data.Flags & VTexFlags.SUGGEST_CLAMPS) != 0 ? TextureWrapMode.ClampToBorder : TextureWrapMode.Repeat;
            var clampModeT = (data.Flags & VTexFlags.SUGGEST_CLAMPT) != 0 ? TextureWrapMode.ClampToBorder : TextureWrapMode.Repeat;
            var clampModeU = (data.Flags & VTexFlags.SUGGEST_CLAMPU) != 0 ? TextureWrapMode.ClampToBorder : TextureWrapMode.Repeat;

            if ((data.Flags & VTexFlags.CUBE_TEXTURE) != 0)
            {
                is3d = true;
                target = (data.Flags & VTexFlags.TEXTURE_ARRAY) != 0 ? TextureTarget.TextureCubeMapArray : TextureTarget.TextureCubeMap;
                clampModeS = TextureWrapMode.ClampToEdge;
                clampModeT = TextureWrapMode.ClampToEdge;
                clampModeU = TextureWrapMode.ClampToEdge;
            }
            else if ((data.Flags & (VTexFlags.TEXTURE_ARRAY | VTexFlags.VOLUME_TEXTURE)) != 0)
            {
                is3d = true;
                target = (data.Flags & VTexFlags.VOLUME_TEXTURE) != 0 ? TextureTarget.Texture3D : TextureTarget.Texture2DArray;
            }

            var tex = new RenderTexture(target, data, System.IO.Path.GetFileName(textureResource.FileName) ?? "UnnamedTexture");
            var format = GetTextureFormat(data.Format);
            var srgb = srgbRead && format.HasSrgbVariant();

            // todo: BC7 and BC6H are also problematic on pre-RDNA AMD GPUs, when using immutable storage
            // see https://github.com/ValveResourceFormat/ValveResourceFormat/issues/721
            var rgba8UncompressedFallback = target == TextureTarget.Texture3D && IsOpenGLUnsupportedTexture3DFormat(data.Format);

            if (rgba8UncompressedFallback)
            {
                format = ImageFormat.RGBA8888;
            }

            var sizedInternalFormat = format.ToGLSizedInternalFormat(srgb);

            var texDepth = data.Depth;

            if (target == TextureTarget.TextureCubeMap || target == TextureTarget.TextureCubeMapArray)
            {
                texDepth *= 6;
            }

            var minMipLevelAllowed = 0;
            var texWidth = data.Width;
            var texHeight = data.Height;

            if (!isViewerRequest && !is3d && data.NumMipLevels > 1)
            {
                var maxUserTextureSize = RendererContext.MaxTextureSize;

                while (minMipLevelAllowed + 1 < data.NumMipLevels && (texWidth > maxUserTextureSize || texHeight > maxUserTextureSize))
                {
                    minMipLevelAllowed++;

                    texWidth >>= 1;
                    texHeight >>= 1;
                }
            }

            if (is3d && target != TextureTarget.TextureCubeMap)
            {
                GL.TextureStorage3D(tex.Handle, data.NumMipLevels - minMipLevelAllowed, sizedInternalFormat, texWidth, texHeight, texDepth);
            }
            else
            {
                GL.TextureStorage2D(tex.Handle, data.NumMipLevels - minMipLevelAllowed, sizedInternalFormat, texWidth, texHeight);
            }

            var buffer = ArrayPool<byte>.Shared.Rent(data.GetBiggestBufferSize());
            byte[]? decodedBuffer = null;

            if (rgba8UncompressedFallback)
            {
                decodedBuffer = ArrayPool<byte>.Shared.Rent(data.Width * data.Height * data.Depth * 4);
            }

            try
            {
                foreach (var (level, width, height, depth, bufferSize) in data.GetEveryMipLevelTexture(buffer, minMipLevelAllowed))
                {
                    var realLevel = (int)level - minMipLevelAllowed;
                    var uploadBuffer = buffer;

                    if (decodedBuffer != null)
                    {
                        data.DecodeTexture(buffer.AsSpan(0, bufferSize), decodedBuffer, width, height, depth);
                        uploadBuffer = decodedBuffer;
                    }

                    if (!format.IsBlockCompressed())
                    {
                        if (is3d)
                        {
                            GL.TextureSubImage3D(tex.Handle, realLevel, 0, 0, 0, width, height, depth, format.ToGLPixelFormat(), format.ToGLPixelType(), uploadBuffer);
                        }
                        else
                        {
                            GL.TextureSubImage2D(tex.Handle, realLevel, 0, 0, width, height, format.ToGLPixelFormat(), format.ToGLPixelType(), uploadBuffer);
                        }
                    }
                    else
                    {
                        if (is3d)
                        {
                            GL.CompressedTextureSubImage3D(tex.Handle, realLevel, 0, 0, 0, width, height, depth, (PixelFormat)sizedInternalFormat, bufferSize, uploadBuffer);
                        }
                        else
                        {
                            GL.CompressedTextureSubImage2D(tex.Handle, realLevel, 0, 0, width, height, (PixelFormat)sizedInternalFormat, bufferSize, uploadBuffer);
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);

                if (decodedBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(decodedBuffer);
                }
            }

            if (!isViewerRequest)
            {
                // Dispose texture otherwise we run out of memory
                // TODO: This might conflict when opening multiple files due to shit caching
                textureResource.Dispose();
            }

            tex.SetFiltering(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);

            GL.TextureParameter(tex.Handle, TextureParameterName.TextureWrapS, (int)clampModeS);
            GL.TextureParameter(tex.Handle, TextureParameterName.TextureWrapT, (int)clampModeT);
            GL.TextureParameter(tex.Handle, TextureParameterName.TextureWrapR, (int)clampModeU);

            return tex;
        }

        /// <summary>
        /// Whether a format has to be decompressed before it can be uploaded to a <see cref="TextureTarget.Texture3D"/>.
        /// Of the block compressed formats only BPTC is specified to work with 3D textures, as a stack of
        /// independently compressed 2D slices. S3TC and RGTC are two-dimensional only:
        /// NVIDIA accepts them through NV_texture_compression_vtc, which reuses the very same format enums but expects
        /// 4x4x4 VTC tiling, so the slices get read back scrambled, and other drivers reject the upload outright.
        /// </summary>
        private static bool IsOpenGLUnsupportedTexture3DFormat(VTexFormat vformat) => vformat
            is VTexFormat.DXT1
            or VTexFormat.DXT5
            or VTexFormat.ATI1N
            or VTexFormat.ATI2N;

        private static ImageFormat GetTextureFormat(VTexFormat vformat) => vformat switch
        {
#pragma warning disable format
            VTexFormat.ATI1N           => ImageFormat.ATI1N,
            VTexFormat.ATI2N           => ImageFormat.ATI2N,
            VTexFormat.BC6H            => ImageFormat.BC6H,
            VTexFormat.BC7             => ImageFormat.BC7,
            VTexFormat.DXT1            => ImageFormat.DXT1,
            VTexFormat.DXT5            => ImageFormat.DXT5,
            VTexFormat.ETC2            => ImageFormat.R8G8B8_ETC2,
            VTexFormat.ETC2_EAC        => ImageFormat.R8G8B8A8_ETC2_EAC,

            VTexFormat.R16             => ImageFormat.R16,
            VTexFormat.RG1616          => ImageFormat.RG1616,
            VTexFormat.RGBA16161616    => ImageFormat.RGBA16161616,

            VTexFormat.R16F            => ImageFormat.R16F,
            VTexFormat.RG1616F         => ImageFormat.RG1616F,
            VTexFormat.RGBA16161616F   => ImageFormat.RGBA16161616F,

            VTexFormat.R32F            => ImageFormat.R32F,
            VTexFormat.RG3232F         => ImageFormat.RG3232F,
            VTexFormat.RGBA32323232F   => ImageFormat.RGBA32323232F,

            VTexFormat.RGBA8888        => ImageFormat.RGBA8888,
            VTexFormat.BGRA8888        => ImageFormat.BGRA8888,
            VTexFormat.I8              => ImageFormat.I8,

            //VTexFormat.IA88
            //VTexFormat.R11_EAC
            //VTexFormat.RG11_EAC
            //VTexFormat.RGB323232F
#pragma warning restore format

            _ => throw new NotImplementedException($"Unsupported texture format {vformat}")
        };

        /// <summary>Gets the texture unit each reserved sampler uniform is bound to.</summary>
        public static readonly FrozenDictionary<string, ReservedTextureSlots> ReservedTextureSlotByName = BuildReservedTextureSlotByName();

        private static FrozenDictionary<string, ReservedTextureSlots> BuildReservedTextureSlotByName()
        {
            var slotByName = new Dictionary<string, ReservedTextureSlots>(StringComparer.Ordinal);

            foreach (var field in typeof(ReservedTextureSlots).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = field.GetCustomAttribute<SamplerNameAttribute>();

                if (attribute == null)
                {
                    continue; // Aliases such as Last carry no names of their own.
                }

                var slot = (ReservedTextureSlots)field.GetRawConstantValue()!;

                foreach (var name in attribute.Names)
                {
                    // Add, not assign: two slots claiming one sampler name is a mistake worth failing on.
                    slotByName.Add(name, slot);
                }
            }

            return slotByName.ToFrozenDictionary(StringComparer.Ordinal);
        }

        /// <summary>Returns whether a uniform name is bound to one of the <see cref="ReservedTextureSlots"/>.</summary>
        public static bool IsReservedTexture(string uniformName) => ReservedTextureSlotByName.ContainsKey(uniformName);

        /// <summary>
        /// Material invariant textures, requested by shaders. They become scene-wide textures.
        /// </summary>
        public static readonly List<(ReservedTextureSlots Slot, string Name, string Path)> ShaderTextures =
        [
            (ReservedTextureSlots.WetnessWaves, "g_tWetnessWaves", "materials/dev/water_waves.vtex"),
        ];

        private RenderMaterial GetErrorMaterial()
        {
            var errorMat = new RenderMaterial(RendererContext.ShaderLoader.LoadShader("error"));
            OwnedMaterials.Add(errorMat);
            return errorMat;
        }

        /// <summary>Returns a lazily created 4×4 checkerboard error texture used as a fallback for missing textures.</summary>
        public RenderTexture GetErrorTexture()
        {
            if (ErrorTexture == null)
            {
                ReadOnlySpan<byte> color1 = [100, 25, 75];
                ReadOnlySpan<byte> color2 = [0, 127, 0];

                var color = new byte[16 * 3];

                for (var i = 0; i < 16; i++)
                {
                    var checkerboardX = i / 4 % 2;
                    var colorToUse = i % 2 == checkerboardX ? color1 : color2;
                    var pixel = color.AsSpan(i * 3, 3);
                    colorToUse.CopyTo(pixel);
                }

                ErrorTexture = GenerateColorTexture(4, 4, color);
            }

            return ErrorTexture;
        }

        private static RenderTexture CreateSolidTexture(byte r, byte g, byte b) => GenerateColorTexture(1, 1, [r, g, b]);
        /// <summary>Returns a lazily created 1×1 flat normal map texture (127, 127, 255).</summary>
        public RenderTexture GetDefaultNormal() => DefaultNormal ??= CreateSolidTexture(127, 127, 255);

        /// <summary>Returns a lazily created 1×1 solid white mask texture.</summary>
        public RenderTexture GetDefaultMask() => DefaultMask ??= CreateSolidTexture(255, 255, 255);

        /// <summary>Returns a lazily created 1×1 solid white colour texture, a neutral fallback albedo.</summary>
        public RenderTexture GetDefaultColor() => DefaultColor ??= CreateSolidTexture(255, 255, 255);

        /// <summary>
        /// Returns a lazily created 1×1×1 white volume texture.
        /// </summary>
        public RenderTexture GetDefaultVolume()
        {
            if (DefaultVolume == null)
            {
                DefaultVolume = new RenderTexture(TextureTarget.Texture3D, 1, 1, 1, 1, "DefaultVolume");
                DefaultVolume.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                DefaultVolume.SetWrapMode(TextureWrapMode.ClampToEdge);

                GL.TextureStorage3D(DefaultVolume.Handle, 1, SizedInternalFormat.Rgba8, 1, 1, 1);
                GL.TextureSubImage3D(DefaultVolume.Handle, 0, 0, 0, 0, 1, 1, 1, PixelFormat.Rgb, PixelType.UnsignedByte, WhiteTexel);
            }

            return DefaultVolume;
        }

        private static readonly byte[] WhiteTexel = [255, 255, 255];

        /// <summary>Returns the readback format appropriate for exporting a rendered image: 8-bit BGRA, or 32-bit float RGBA for HDR.</summary>
        /// <param name="hdr">Whether to use the HDR (32-bit float) format.</param>
        public static ImageFormat GetImageExportFormat(bool hdr)
            => hdr ? ImageFormat.RGBA32323232F : ImageFormat.BGRA8888;

        /// <summary>Uploads an <see cref="SKBitmap"/> as a 2D texture and returns the resulting <see cref="RenderTexture"/>.</summary>
        /// <param name="bitmap">The bitmap whose pixels are uploaded to the GPU.</param>
        public static RenderTexture LoadBitmapTexture(SKBitmap bitmap)
        {
            var texture = new RenderTexture(TextureTarget.Texture2D, bitmap.Width, bitmap.Height, 1, 1, "BitmapTexture");

            var format = bitmap.ColorType switch
            {
                SKColorType.Rgba8888 => ImageFormat.RGBA8888,
                SKColorType.Bgra8888 => ImageFormat.BGRA8888,
                SKColorType.Rgb888x => ImageFormat.RGBA8888,
                SKColorType.Gray8 => ImageFormat.I8,
                SKColorType.RgbaF16 => ImageFormat.RGBA16161616F,
                SKColorType.RgbaF32 => ImageFormat.RGBA32323232F,
                _ => throw new NotSupportedException($"Unsupported bitmap color type for GPU upload {bitmap.ColorType}"),
            };

            GL.TextureStorage2D(texture.Handle, 1, format.ToGLSizedInternalFormat(), texture.Width, texture.Height);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, texture.Width, texture.Height, format.ToGLPixelFormat(), format.ToGLPixelType(), bitmap.GetPixels());

            if (bitmap.ColorType == SKColorType.Rgb888x)
            {
                // DXGI has no RGBX storage; keep alpha reading as one like the old Rgb8 storage did.
                texture.SetParameter(TextureParameterName.TextureSwizzleA, (int)All.One);
            }

            if (bitmap.ColorType == SKColorType.Rgb888x)
            {
                // The uploaded fourth byte is undefined, the format is opaque by definition
                GL.TextureParameter(texture.Handle, TextureParameterName.TextureSwizzleA, (int)All.One);
            }

            return texture;
        }

        /// <summary>
        /// Builds a one-dimensional colour ramp from a list of gradient stops.
        /// </summary>
        /// <param name="stops">Gradient stops, each a position in 0-1 and its colour. Need not be sorted.</param>
        public static RenderTexture GenerateGradientTexture(ReadOnlySpan<(float Position, Color32 Color)> stops)
        {
            const int Width = 256;

            var texels = new byte[Width * 4];

            for (var x = 0; x < Width; x++)
            {
                var position = x / (Width - 1f);
                var color = SampleGradient(stops, position);

                texels[(x * 4) + 0] = color.R;
                texels[(x * 4) + 1] = color.G;
                texels[(x * 4) + 2] = color.B;
                texels[(x * 4) + 3] = color.A;
            }

            var texture = new RenderTexture(TextureTarget.Texture2D, Width, 1, 1, 1, "GeneratedGradient");

            // Clamped and filtered: the ramp is addressed by a luminance, so the ends have to hold rather
            // than wrap, and the steps between stops should not be visible.
            texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            texture.SetWrapMode(TextureWrapMode.ClampToEdge);

            // sRGB storage, so a sample lands in linear space like every other layer's texture.
            GL.TextureStorage2D(texture.Handle, 1, SizedInternalFormat.Srgb8Alpha8, Width, 1);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, Width, 1, PixelFormat.Rgba, PixelType.UnsignedByte, texels);

            return texture;
        }

        private static Color32 SampleGradient(ReadOnlySpan<(float Position, Color32 Color)> stops, float position)
        {
            if (stops.Length == 0)
            {
                return new Color32(255, 255, 255);
            }

            // Stops are authored in order, but nothing guarantees it, so pick the bracketing pair by value
            // rather than by index.
            var lower = stops[0];
            var upper = stops[0];
            var hasLower = false;
            var hasUpper = false;

            foreach (var stop in stops)
            {
                if (stop.Position <= position && (!hasLower || stop.Position >= lower.Position))
                {
                    lower = stop;
                    hasLower = true;
                }

                if (stop.Position >= position && (!hasUpper || stop.Position <= upper.Position))
                {
                    upper = stop;
                    hasUpper = true;
                }
            }

            if (!hasLower)
            {
                return upper.Color;
            }

            if (!hasUpper)
            {
                return lower.Color;
            }

            var span = upper.Position - lower.Position;
            var t = span > 0f ? (position - lower.Position) / span : 0f;

            return new Color32(
                (byte)float.Round(float.Lerp(lower.Color.R, upper.Color.R, t)),
                (byte)float.Round(float.Lerp(lower.Color.G, upper.Color.G, t)),
                (byte)float.Round(float.Lerp(lower.Color.B, upper.Color.B, t)),
                (byte)float.Round(float.Lerp(lower.Color.A, upper.Color.A, t)));
        }

        private static RenderTexture GenerateColorTexture(int width, int height, byte[] color)
        {
            // Full mip chain, because materials may bind a mipmap filtering sampler over this
            // texture, and an incomplete mip chain would then sample as if nothing was bound
            var levels = 1 + BitOperations.Log2((uint)Math.Max(width, height));

            var texture = new RenderTexture(TextureTarget.Texture2D, width, height, 1, levels, width > 1 ? "ErrorTexture" : "ColorTexture");
            texture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            texture.SetWrapMode(TextureWrapMode.Repeat);

            var color32 = new Color32(color[0], color[1], color[2]);
            texture.Reflectivity = color32.ToLinearColor();

            GL.TextureStorage2D(texture.Handle, levels, SizedInternalFormat.Rgba8, width, height);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, color);

            if (levels > 1)
            {
                GL.GenerateTextureMipmap(texture.Handle);
            }

            return texture;
        }
    }
}
