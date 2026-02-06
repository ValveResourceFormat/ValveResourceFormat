using System.Buffers;
using System.Diagnostics;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Loads and caches materials and textures from Source 2 resources.
    /// </summary>
    public class MaterialLoader
    {
        private readonly Dictionary<ulong, RenderMaterial> Materials = [];
        private readonly Dictionary<string, RenderTexture> Textures = [];
        private readonly Dictionary<string, RenderTexture> TexturesSrgb = [];
        private readonly RendererContext RendererContext;
        private RenderTexture? ErrorTexture;
        private RenderTexture? DefaultNormal;
        private RenderTexture? DefaultMask;
        public static float MaxTextureMaxAnisotropy { get; set; }
        public int MaterialCount => Materials.Count;

        private readonly Dictionary<string, string[]> TextureAliases = new()
        {
            ["g_tLayer2Color"] = ["g_tColorB", "g_tColor2"],
            ["g_tColor"] = ["g_tColor2", "g_tColor1", "g_tColorA", "g_tColorB", "g_tColorC", "g_tGlassDust"],
            ["g_tNormal"] = ["g_tNormalA", "g_tNormalRoughness", "g_tLayer1NormalRoughness", "g_tNormalRoughness1"],
            ["g_tLayer2NormalRoughness"] = ["g_tNormalB", "g_tNormalRoughness2"],
            ["g_tAmbientOcclusion"] = ["g_tLayer1AmbientOcclusion"],
        };

        public MaterialLoader(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
        }

        private static readonly byte[] NewLineArray = "\n"u8.ToArray();

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

            foreach (var (textureName, texturePath) in mat.Material.TextureParams)
            {
                if (TryBindTexture(mat, textureName, texturePath))
                {
                    continue;
                }

                foreach (var (possibleAlias, aliases) in TextureAliases)
                {
                    if (mat.Textures.ContainsKey(possibleAlias))
                    {
                        continue;
                    }

                    if (aliases.Contains(textureName))
                    {
                        if (TryBindTexture(mat, possibleAlias, texturePath))
                        {
                            break;
                        }
                    }
                }
            }

            bool TryBindTexture(RenderMaterial mat, string name, string path)
            {
                if (mat.Shader.IsSlang && mat.Shader.ResourceBindings.TryGetValue(name, out var texBinding))
                {
                    mat.Textures[name] = GetTexture(path, texBinding.SrgbRead, anisotropicFiltering: true);
                }
                else if (mat.Shader.UniformNames.Contains(name))
                {
                    var srgbRead = mat.Shader.SrgbUniforms.Contains(name);
                    mat.Textures[name] = GetTexture(path, srgbRead, anisotropicFiltering: true);
                    return true;
                }

                return false;
            }

            return mat;
        }


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

            var tex = new RenderTexture(target, data);
            var format = GetTextureFormat(data.Format);
            var sizedInternalFormat = srgbRead && format.InternalSrgbFormat is not null ? format.InternalSrgbFormat.Value : format.InternalFormat;

#if DEBUG
            var textureName = System.IO.Path.GetFileName(textureResource.FileName);

            if (textureName != null)
            {
                tex.SetLabel(textureName);
            }
#endif

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

            try
            {
                foreach (var (level, width, height, depth, bufferSize) in data.GetEveryMipLevelTexture(buffer, minMipLevelAllowed))
                {
                    var realLevel = (int)level - minMipLevelAllowed;

                    if (format.PixelType is not null)
                    {
                        Debug.Assert(format.PixelFormat is not null);

                        if (is3d)
                        {
                            GL.TextureSubImage3D(tex.Handle, realLevel, 0, 0, 0, width, height, depth, format.PixelFormat.Value, format.PixelType.Value, buffer);
                        }
                        else
                        {
                            GL.TextureSubImage2D(tex.Handle, realLevel, 0, 0, width, height, format.PixelFormat.Value, format.PixelType.Value, buffer);
                        }
                    }
                    else
                    {
                        if (is3d)
                        {
                            GL.CompressedTextureSubImage3D(tex.Handle, realLevel, 0, 0, 0, width, height, depth, (PixelFormat)sizedInternalFormat, bufferSize, buffer);
                        }
                        else
                        {
                            GL.CompressedTextureSubImage2D(tex.Handle, realLevel, 0, 0, width, height, (PixelFormat)sizedInternalFormat, bufferSize, buffer);
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
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

        /// <param name="InternalFormat">Specifies the sized internal format to be used to store texture image data.</param>
        /// <param name="InternalSrgbFormat">Same as <see cref="InternalFormat"/>, but for sRGB textures. Null if no sRGB format.</param>
        /// <param name="PixelFormat">Specifies the format of the pixel data. Must be null if the format is compressed.</param>
        /// <param name="PixelType">Specifies the data type of the pixel data. Must be null if the format is compressed.</param>
        /// <see href="https://registry.khronos.org/OpenGL-Refpages/gl4/html/glTexStorage2D.xhtml"/>
        /// <see href="https://registry.khronos.org/OpenGL-Refpages/gl4/html/glTexSubImage2D.xhtml"/>
        record struct TextureFormatMapping(SizedInternalFormat InternalFormat, PixelFormat? PixelFormat = null, PixelType? PixelType = null, SizedInternalFormat? InternalSrgbFormat = null);

        private static TextureFormatMapping GetTextureFormat(VTexFormat vformat) => vformat switch
        {
#pragma warning disable format
            VTexFormat.ATI1N           => new((SizedInternalFormat)InternalFormat.CompressedRedRgtc1),
            VTexFormat.ATI2N           => new((SizedInternalFormat)InternalFormat.CompressedRgRgtc2),
            VTexFormat.BC6H            => new((SizedInternalFormat)InternalFormat.CompressedRgbBptcUnsignedFloat),
            VTexFormat.BC7             => new((SizedInternalFormat)InternalFormat.CompressedRgbaBptcUnorm,        InternalSrgbFormat: (SizedInternalFormat)InternalFormat.CompressedSrgbAlphaBptcUnorm),
            VTexFormat.DXT1            => new((SizedInternalFormat)InternalFormat.CompressedRgbaS3tcDxt1Ext,      InternalSrgbFormat: (SizedInternalFormat)InternalFormat.CompressedSrgbAlphaS3tcDxt1Ext),
            VTexFormat.DXT5            => new((SizedInternalFormat)InternalFormat.CompressedRgbaS3tcDxt5Ext,      InternalSrgbFormat: (SizedInternalFormat)InternalFormat.CompressedSrgbAlphaS3tcDxt5Ext),
            VTexFormat.ETC2            => new((SizedInternalFormat)InternalFormat.CompressedRgb8Etc2,             InternalSrgbFormat: (SizedInternalFormat)InternalFormat.CompressedSrgb8Etc2),
            VTexFormat.ETC2_EAC        => new((SizedInternalFormat)InternalFormat.CompressedRgba8Etc2Eac,         InternalSrgbFormat: (SizedInternalFormat)InternalFormat.CompressedSrgb8Alpha8Etc2Eac),

            VTexFormat.R16             => new(SizedInternalFormat.R16,        PixelFormat.Red,    PixelType.UnsignedShort),
            VTexFormat.RG1616          => new(SizedInternalFormat.Rg16,       PixelFormat.Rg,     PixelType.UnsignedShort),
            VTexFormat.RGBA16161616    => new(SizedInternalFormat.Rgba16,     PixelFormat.Rgba,   PixelType.UnsignedShort),

            VTexFormat.R16F            => new(SizedInternalFormat.R16f,       PixelFormat.Red,    PixelType.HalfFloat),
            VTexFormat.RG1616F         => new(SizedInternalFormat.Rg16f,      PixelFormat.Rg,     PixelType.HalfFloat),
            VTexFormat.RGBA16161616F   => new(SizedInternalFormat.Rgba16f,    PixelFormat.Rgba,   PixelType.HalfFloat),

            VTexFormat.R32F            => new(SizedInternalFormat.R32f,       PixelFormat.Red,    PixelType.Float),
            VTexFormat.RG3232F         => new(SizedInternalFormat.Rg32f,      PixelFormat.Rg,     PixelType.Float),
            VTexFormat.RGBA32323232F   => new(SizedInternalFormat.Rgba32f,    PixelFormat.Rgba,   PixelType.Float),

            VTexFormat.RGBA8888        => new(SizedInternalFormat.Rgba8,      PixelFormat.Rgba,   PixelType.UnsignedByte,     SizedInternalFormat.Srgb8Alpha8),
            VTexFormat.BGRA8888        => new(SizedInternalFormat.Rgba8,      PixelFormat.Bgra,   PixelType.UnsignedByte,     SizedInternalFormat.Srgb8Alpha8),
            VTexFormat.I8              => new(SizedInternalFormat.R8,         PixelFormat.Red,    PixelType.UnsignedByte),

            //VTexFormat.IA88
            //VTexFormat.R11_EAC
            //VTexFormat.RG11_EAC
            //VTexFormat.RGB323232F
#pragma warning restore format

            _ => throw new NotImplementedException($"Unsupported texture format {vformat}")
        };

        public static readonly HashSet<string> ReservedTextures = [.. Enum.GetNames<ReservedTextureSlots>(), "g_tLPV"];

        private RenderMaterial GetErrorMaterial()
        {
            var errorMat = new RenderMaterial(RendererContext.ShaderLoader.LoadShader("vrf.error"));
            return errorMat;
        }

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
        public RenderTexture GetDefaultNormal() => DefaultNormal ??= CreateSolidTexture(127, 127, 255);
        public RenderTexture GetDefaultMask() => DefaultMask ??= CreateSolidTexture(255, 255, 255);

        public static (SizedInternalFormat SizedInternalFormat, PixelFormat PixelFormat, PixelType PixelType) GetImageExportFormat(bool hdr) => hdr switch
        {
            false => (SizedInternalFormat.Rgba8, PixelFormat.Bgra, PixelType.UnsignedByte),
            true => (SizedInternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float),
        };

        public static RenderTexture LoadBitmapTexture(SKBitmap bitmap)
        {
            var texture = new RenderTexture(TextureTarget.Texture2D, bitmap.Width, bitmap.Height, 1, 1);

            var isHdr = bitmap.ColorType == Texture.HdrBitmapColorType;
            var store = GetImageExportFormat(isHdr);

            GL.TextureStorage2D(texture.Handle, 1, store.SizedInternalFormat, texture.Width, texture.Height);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, texture.Width, texture.Height, store.PixelFormat, store.PixelType, bitmap.GetPixels());

            return texture;
        }

        private static RenderTexture GenerateColorTexture(int width, int height, byte[] color)
        {
            var texture = new RenderTexture(TextureTarget.Texture2D, width, height, 1, 1);
            texture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            texture.SetWrapMode(TextureWrapMode.Repeat);

            GL.TextureStorage2D(texture.Handle, 1, SizedInternalFormat.Rgb8, width, height);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, color);

#if DEBUG
            texture.SetLabel(width > 1 ? "ErrorTexture" : "ColorTexture");
#endif

            return texture;
        }
    }
}
