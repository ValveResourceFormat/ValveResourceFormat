using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace GUI.Types.Renderer
{
    class MaterialLoader
    {
        private readonly Dictionary<ulong, RenderMaterial> Materials = [];
        private readonly Dictionary<string, RenderTexture> Textures = [];
        private readonly VrfGuiContext VrfGuiContext;
        private RenderTexture ErrorTexture;
        private RenderTexture DefaultNormal;
        private RenderTexture DefaultMask;
        public static float MaxTextureMaxAnisotropy { get; set; }
        public int MaterialCount => Materials.Count;

        private readonly Dictionary<string, string[]> TextureAliases = new()
        {
            ["g_tLayer2Color"] = ["g_tColorB"],
            ["g_tColor"] = ["g_tColor2", "g_tColor1", "g_tColorA", "g_tColorB", "g_tColorC", "g_tGlassDust"],
            ["g_tNormal"] = ["g_tNormalA", "g_tNormalRoughness", "g_tLayer1NormalRoughness"],
            ["g_tLayer2NormalRoughness"] = ["g_tNormalB"],
            ["g_tAmbientOcclusion"] = ["g_tLayer1AmbientOcclusion"],
        };

        public MaterialLoader(VrfGuiContext guiContext)
        {
            VrfGuiContext = guiContext;
        }

        private static readonly byte[] NewLineArray = "\n"u8.ToArray();

        public RenderMaterial GetMaterial(string name, Dictionary<string, byte> shaderArguments)
        {
            // HL:VR has a world node that has a draw call with no material
            if (name == null)
            {
                return GetErrorMaterial();
            }

            var hash = new XxHash3(StringToken.MURMUR2SEED);
            hash.Append(Encoding.ASCII.GetBytes(name));

            if (shaderArguments != null)
            {
                foreach (var (key, value) in shaderArguments)
                {
                    hash.Append(NewLineArray);
                    hash.Append(Encoding.ASCII.GetBytes(key));
                    hash.Append(NewLineArray);
                    hash.Append(Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture)));
                }
            }

            var cacheKey = hash.GetCurrentHashAsUInt64();

            if (Materials.TryGetValue(cacheKey, out var mat))
            {
                return mat;
            }

            var resource = VrfGuiContext.LoadFileCompiled(name);
            mat = LoadMaterial(resource, shaderArguments);

            Materials.Add(cacheKey, mat);

            return mat;
        }

        public RenderMaterial LoadMaterial(Resource resource, Dictionary<string, byte> shaderArguments = null)
        {
            if (resource == null)
            {
                return GetErrorMaterial();
            }

            var vrfMaterial = (VrfMaterial)resource.DataBlock;
            var mat = new RenderMaterial(
                vrfMaterial,
                vrfMaterial.GetInputSignature(),
                VrfGuiContext.ShaderLoader,
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
                if (mat.Shader.GetUniformLocation(name) != -1)
                {
                    var srgbRead = mat.Shader.SrgbSamplers.Contains(name);
                    mat.Textures[name] = GetTexture(path, srgbRead);
                    return true;
                }

                return false;
            }

            return mat;
        }


        public RenderTexture GetTexture(string name, bool srgbRead = false)
        {
            if (Textures.TryGetValue(name, out var tex))
            {
                return tex;
            }

            tex = LoadTexture(name, srgbRead);
            Textures.Add(name, tex);
            return tex;
        }

        public RenderTexture LoadTexture(string name, bool srgbRead = false)
        {
            var textureResource = VrfGuiContext.LoadFileCompiled(name);

            if (textureResource == null)
            {
                return GetErrorTexture();
            }

            return LoadTexture(textureResource, srgbRead);
        }

        public RenderTexture LoadTexture(Resource textureResource, bool srgbRead = false, bool isViewerRequest = false)
        {
            var data = (Texture)textureResource.DataBlock;
            var target = TextureTarget.Texture2D;
            var clampModeS = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPS) ? TextureWrapMode.ClampToBorder : TextureWrapMode.Repeat;
            var clampModeT = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPT) ? TextureWrapMode.ClampToBorder : TextureWrapMode.Repeat;
            var clampModeU = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPU) ? TextureWrapMode.ClampToBorder : TextureWrapMode.Repeat;

            if (data.Flags.HasFlag(VTexFlags.CUBE_TEXTURE))
            {
                target = TextureTarget.TextureCubeMap;
                clampModeS = TextureWrapMode.ClampToEdge;
                clampModeT = TextureWrapMode.ClampToEdge;
                clampModeU = TextureWrapMode.ClampToEdge;

                if (data.Flags.HasFlag(VTexFlags.TEXTURE_ARRAY))
                {
                    target = TextureTarget.TextureCubeMapArray;
                }
            }
            else if (data.Flags.HasFlag(VTexFlags.TEXTURE_ARRAY) || data.Flags.HasFlag(VTexFlags.VOLUME_TEXTURE))
            {
                target = TextureTarget.Texture2DArray;
                clampModeS = TextureWrapMode.ClampToEdge;
                clampModeT = TextureWrapMode.ClampToEdge;
                clampModeU = TextureWrapMode.ClampToEdge;
            }

            var tex = new RenderTexture(target, data);

            var internalFormat = GetPixelInternalFormat(data.Format);
            var format = GetInternalFormat(data.Format);

            if (srgbRead)
            {
                internalFormat = (PixelInternalFormat?)ToSrgb((InternalFormat?)internalFormat);
                format = ToSrgb(format);
            }

#if DEBUG
            var textureName = System.IO.Path.GetFileName(textureResource.FileName);
            GL.ObjectLabel(ObjectLabelIdentifier.Texture, tex.Handle, textureName.Length, textureName);
#endif

            if (!format.HasValue && !internalFormat.HasValue)
            {
                Log.Warn(nameof(MaterialLoader), $"Don't support {data.Format} but don't want to crash either. Using error texture!");
                return GetErrorTexture();
            }

            var depth = data.Depth;

            if (target == TextureTarget.TextureCubeMap || target == TextureTarget.TextureCubeMapArray)
            {
                depth *= 6;
            }

            if (target == TextureTarget.Texture2DArray || target == TextureTarget.TextureCubeMapArray)
            {
                GL.TextureStorage3D(tex.Handle, data.NumMipLevels, GetSizedInternalFormat(data.Format), data.Width, data.Height, depth);
            }
            else
            {
                GL.TextureStorage2D(tex.Handle, data.NumMipLevels, GetSizedInternalFormat(data.Format), data.Width, data.Height);
            }

            var maxMipLevelNotSet = true;
            var minMipLevel = 0;

            var maxTextureSize = Settings.Config.MaxTextureSize;

            if (isViewerRequest || data.Flags.HasFlag(VTexFlags.TEXTURE_ARRAY) || data.Flags.HasFlag(VTexFlags.VOLUME_TEXTURE))
            {
                maxTextureSize = int.MaxValue;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(data.GetBiggestBufferSize());

            try
            {
                foreach (var (i, width, height, bufferSize) in data.GetEveryMipLevelTexture(buffer, maxTextureSize))
                {
                    if (maxMipLevelNotSet)
                    {
                        GL.TextureParameter(tex.Handle, TextureParameterName.TextureMaxLevel, i);
                        maxMipLevelNotSet = false;
                    }

                    minMipLevel = i;

                    LoadTextureImplShared(data.Format, internalFormat, i, width, height, depth, bufferSize, buffer, target, tex.Handle);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            GL.TextureParameter(tex.Handle, TextureParameterName.TextureBaseLevel, minMipLevel);

            if (!isViewerRequest)
            {
                // Dispose texture otherwise we run out of memory
                // TODO: This might conflict when opening multiple files due to shit caching
                textureResource.Dispose();

                if (MaxTextureMaxAnisotropy >= 4)
                {
                    GL.TextureParameter(tex.Handle, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, MaxTextureMaxAnisotropy);
                }
            }

            tex.SetFiltering(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);

            GL.TextureParameter(tex.Handle, TextureParameterName.TextureWrapS, (int)clampModeS);
            GL.TextureParameter(tex.Handle, TextureParameterName.TextureWrapT, (int)clampModeT);
            GL.TextureParameter(tex.Handle, TextureParameterName.TextureWrapR, (int)clampModeU);

            return tex;
        }

        private static void LoadTextureImplShared(VTexFormat vtexFormat, PixelInternalFormat? internalFormat,
            int level, int width, int height, int depth, int bufferSize, byte[] buffer, TextureTarget target, int handle)
        {
            var is3d = target == TextureTarget.TextureCubeMap || target == TextureTarget.Texture2DArray || target == TextureTarget.TextureCubeMapArray;
            var pixelFormat = GetPixelFormat(vtexFormat);
            var pixelType = GetPixelType(vtexFormat);

            if (internalFormat.HasValue)
            {
                if (is3d)
                {
                    GL.TextureSubImage3D(handle, level, 0, 0, 0, width, height, depth, pixelFormat, pixelType, buffer);
                }
                else
                {
                    GL.TextureSubImage2D(handle, level, 0, 0, width, height, pixelFormat, pixelType, buffer);
                }
            }
            else
            {
                if (is3d)
                {
                    GL.CompressedTextureSubImage3D(handle, level, 0, 0, 0, width, height, depth, pixelFormat, bufferSize, buffer);
                }
                else
                {
                    GL.CompressedTextureSubImage2D(handle, level, 0, 0, width, height, pixelFormat, bufferSize, buffer);
                }
            }
        }

        private static SizedInternalFormat GetSizedInternalFormat(VTexFormat vformat) => vformat switch
        {
            VTexFormat.DXT1 => (SizedInternalFormat)InternalFormat.CompressedRgbaS3tcDxt1Ext,
            VTexFormat.DXT5 => (SizedInternalFormat)InternalFormat.CompressedRgbaS3tcDxt5Ext,
            VTexFormat.BC6H => (SizedInternalFormat)InternalFormat.CompressedRgbBptcUnsignedFloat,
            VTexFormat.BC7 => (SizedInternalFormat)InternalFormat.CompressedRgbaBptcUnorm,
            VTexFormat.ATI1N => (SizedInternalFormat)InternalFormat.CompressedRedRgtc1,
            VTexFormat.ATI2N => (SizedInternalFormat)InternalFormat.CompressedRgRgtc2,
            VTexFormat.RGBA8888 => SizedInternalFormat.Rgba8,
            VTexFormat.RGBA16161616F => SizedInternalFormat.Rgba16f,
            _ => throw new NotImplementedException($"Unsupported texture format {vformat}")
        };

        private static InternalFormat? GetInternalFormat(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.DXT1 => InternalFormat.CompressedRgbaS3tcDxt1Ext,
                VTexFormat.DXT5 => InternalFormat.CompressedRgbaS3tcDxt5Ext,
                VTexFormat.ETC2 => InternalFormat.CompressedRgb8Etc2,
                VTexFormat.ETC2_EAC => InternalFormat.CompressedRgba8Etc2Eac,
                VTexFormat.ATI1N => InternalFormat.CompressedRedRgtc1,
                VTexFormat.ATI2N => InternalFormat.CompressedRgRgtc2,
                VTexFormat.BC6H => InternalFormat.CompressedRgbBptcUnsignedFloat,
                VTexFormat.BC7 => InternalFormat.CompressedRgbaBptcUnorm,
                VTexFormat.RGBA8888 => InternalFormat.Rgba8,
                VTexFormat.RGBA16161616 => InternalFormat.Rgba16,
                VTexFormat.RGBA16161616F => InternalFormat.Rgba16f,
                VTexFormat.I8 => InternalFormat.Intensity8,
                _ => null // Unsupported texture format
            };

        private static InternalFormat? ToSrgb(InternalFormat? format)
            => format switch
            {
                InternalFormat.CompressedRgbaS3tcDxt1Ext => InternalFormat.CompressedSrgbAlphaS3tcDxt1Ext,
                InternalFormat.CompressedRgbaS3tcDxt5Ext => InternalFormat.CompressedSrgbAlphaS3tcDxt5Ext,
                InternalFormat.CompressedRgb8Etc2 => InternalFormat.CompressedSrgb8Etc2,
                InternalFormat.CompressedRgba8Etc2Eac => InternalFormat.CompressedSrgb8Alpha8Etc2Eac,
                InternalFormat.CompressedRgbaBptcUnorm => InternalFormat.CompressedSrgbAlphaBptcUnorm,
                InternalFormat.Rgba8 => InternalFormat.Srgb8Alpha8,
                InternalFormat.Rgb8 => InternalFormat.Srgb8,
                _ => format
            };

        private static PixelInternalFormat? GetPixelInternalFormat(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.R16 => PixelInternalFormat.R16,
                VTexFormat.R16F => PixelInternalFormat.R16f,
                VTexFormat.RG1616 => PixelInternalFormat.Rg16,
                VTexFormat.RG1616F => PixelInternalFormat.Rg16f,
                VTexFormat.RGBA16161616 => PixelInternalFormat.Rgba16,
                VTexFormat.RGBA16161616F => PixelInternalFormat.Rgba16f,
                VTexFormat.RGBA8888 => PixelInternalFormat.Rgba8,
                VTexFormat.BGRA8888 => PixelInternalFormat.Rgba8,
                _ => null // Unsupported texture format
            };

        private static PixelFormat GetPixelFormat(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.DXT1 => (PixelFormat)InternalFormat.CompressedRgbaS3tcDxt1Ext,
                VTexFormat.DXT5 => (PixelFormat)InternalFormat.CompressedRgbaS3tcDxt5Ext,
                VTexFormat.ATI1N => (PixelFormat)InternalFormat.CompressedRedRgtc1,
                VTexFormat.ATI2N => (PixelFormat)InternalFormat.CompressedRgRgtc2,
                VTexFormat.BC6H => (PixelFormat)InternalFormat.CompressedRgbBptcUnsignedFloat,
                VTexFormat.BC7 => (PixelFormat)InternalFormat.CompressedRgbaBptcUnorm,
                VTexFormat.R16 => PixelFormat.Red,
                VTexFormat.R16F => PixelFormat.Red,
                VTexFormat.R32F => PixelFormat.Red,
                VTexFormat.RG1616 => PixelFormat.Rg,
                VTexFormat.RG1616F => PixelFormat.Rg,
                VTexFormat.RG3232F => PixelFormat.Rg,
                VTexFormat.BGRA8888 => PixelFormat.Bgra,
                _ => PixelFormat.Rgba
            };

        private static PixelType GetPixelType(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.R16 => PixelType.UnsignedShort,
                VTexFormat.RG1616 => PixelType.UnsignedShort,
                VTexFormat.RGBA16161616 => PixelType.UnsignedShort,
                VTexFormat.R16F => PixelType.HalfFloat,
                VTexFormat.RG1616F => PixelType.HalfFloat,
                VTexFormat.RGBA16161616F => PixelType.HalfFloat,
                VTexFormat.R32F => PixelType.Float,
                VTexFormat.RG3232F => PixelType.Float,
                VTexFormat.RGBA32323232F => PixelType.Float,
                _ => PixelType.UnsignedByte
            };

        static readonly string[] NonMaterialUniforms =
        [
            "g_flTime",
            "g_vCameraPositionWs",
            "g_vLightmapUvScale",
            "g_vEnvMapSizeConstants",
            "g_vClearColor",
            "g_vGradientFogBiasAndScale",
            "g_vGradientFogColor_Opacity",
            "g_vCubeFog_Offset_Scale_Bias_Exponent",
            "g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip",
            "g_vCubeFogCullingParams_ExposureBias_MaxOpacity",
        ];

        static readonly string[] ReservedTextures = Enum.GetNames<ReservedTextureSlots>();

        public void ApplyMaterialDefaults(RenderMaterial mat)
        {
            var vec4Val = new float[4];
            var uniforms = mat.Shader.GetAllUniformNames();

            foreach (var uniform in uniforms)
            {
                var name = uniform.Name;
                var type = uniform.Type;
                var index = uniform.Index;
                var size = uniform.Size;

                if (NonMaterialUniforms.Contains(name))
                {
                    continue;
                }

                if (!name.StartsWith("g_", StringComparison.Ordinal) && !name.StartsWith("F_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (size != 1) // arrays
                {
                    continue;
                }

                var isTexture = type >= ActiveUniformType.Sampler1D && type <= ActiveUniformType.Sampler2DRectShadow;
                var isVector = type == ActiveUniformType.FloatVec4;
                var isScalar = type == ActiveUniformType.Float;
                var isBoolean = type == ActiveUniformType.Bool;
                var isInteger = type is ActiveUniformType.Int or ActiveUniformType.UnsignedInt;

                if (isTexture && !mat.Textures.ContainsKey(name)
                    && !ReservedTextures.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase)))
                {
                    mat.Textures[name] = name switch
                    {
                        _ when name.Contains("color", StringComparison.OrdinalIgnoreCase) => GetErrorTexture(),
                        _ when name.Contains("normal", StringComparison.OrdinalIgnoreCase) => GetDefaultNormal(),
                        _ when name.Contains("mask", StringComparison.OrdinalIgnoreCase) => GetDefaultMask(),
                        _ => GetErrorTexture(),
                    };
                }
                else if (isVector && !mat.Material.VectorParams.ContainsKey(name))
                {
                    vec4Val[0] = vec4Val[1] = vec4Val[2] = vec4Val[3] = 0f;
                    GL.GetUniform(mat.Shader.Program, mat.Shader.GetUniformLocation(name), vec4Val);
                    mat.Material.VectorParams[name] = new Vector4(vec4Val[0], vec4Val[1], vec4Val[2], vec4Val[3]);
                }
                else if (isScalar && !mat.Material.FloatParams.ContainsKey(name))
                {
                    GL.GetUniform(mat.Shader.Program, mat.Shader.GetUniformLocation(name), out float flVal);
                    mat.Material.FloatParams[name] = flVal;
                }
                else if ((isBoolean || isInteger) && !mat.Material.IntParams.ContainsKey(name))
                {
                    GL.GetUniform(mat.Shader.Program, mat.Shader.GetUniformLocation(name), out int intVal);
                    mat.Material.IntParams[name] = intVal;
                }
            }
        }

        private RenderMaterial GetErrorMaterial()
        {
            var errorMat = new RenderMaterial(VrfGuiContext.ShaderLoader.LoadShader("vrf.error"));
            return errorMat;
        }

        private RenderTexture GetErrorTexture()
        {
            if (ErrorTexture == null)
            {
                ReadOnlySpan<float> color1 = [0.4f, 0.1f, 0.3f, 1f];
                ReadOnlySpan<float> color2 = [0f, 0.5f, 0f, 1f];

                var color = new float[16 * 4];

                for (var i = 0; i < 16; i++)
                {
                    var checkerboardX = i / 4 % 2;
                    var colorToUse = i % 2 == checkerboardX ? color1 : color2;
                    var pixel = color.AsSpan(i * 4, 4);
                    colorToUse.CopyTo(pixel);
                }

                ErrorTexture = GenerateColorTexture(4, 4, color);
            }

            return ErrorTexture;
        }

        private static RenderTexture CreateSolidTexture(float r, float g, float b)
            => GenerateColorTexture(1, 1, [r, g, b, 1f]);

        private RenderTexture GetDefaultNormal()
        {
            DefaultNormal ??= CreateSolidTexture(0.5f, 0.5f, 1.0f);
            return DefaultNormal;
        }

        private RenderTexture GetDefaultMask()
        {
            DefaultMask ??= CreateSolidTexture(1.0f, 1.0f, 1.0f);
            return DefaultMask;
        }

        private static RenderTexture GenerateColorTexture(int width, int height, float[] color)
        {
            var texture = new RenderTexture(TextureTarget.Texture2D, width, height, 1, 1);
            using var _ = texture.BindingContext();

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, width, height, 0, PixelFormat.Rgba, PixelType.Float, color);
            GL.TextureParameter(texture.Handle, TextureParameterName.TextureMaxLevel, 0);
            texture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            texture.SetWrapMode(TextureWrapMode.Repeat);

            return texture;
        }
    }
}
