using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace GUI.Types.Renderer
{
    class MaterialLoader
    {
        private readonly Dictionary<string, RenderMaterial> Materials = new();
        private readonly Dictionary<string, RenderTexture> Textures = new();
        private readonly VrfGuiContext VrfGuiContext;
        private RenderTexture ErrorTexture;
        private RenderTexture DefaultNormal;
        private RenderTexture DefaultMask;
        public static float MaxTextureMaxAnisotropy { get; set; }

        private readonly Dictionary<string, string[]> TextureAliases = new()
        {
            ["g_tLayer2Color"] = new[] { "g_tColorB" },
            ["g_tColor"] = new[] { "g_tColor2", "g_tColor1", "g_tColorA", "g_tColorB", "g_tColorC", "g_tGlassDust" },
            ["g_tNormal"] = new[] { "g_tNormalA", "g_tNormalRoughness", "g_tLayer1NormalRoughness" },
            ["g_tLayer2NormalRoughness"] = new[] { "g_tNormalB" },
        };

        public MaterialLoader(VrfGuiContext guiContext)
        {
            VrfGuiContext = guiContext;
        }

        public RenderMaterial GetMaterial(string name)
        {
            // HL:VR has a world node that has a draw call with no material
            if (name == null)
            {
                return GetErrorMaterial();
            }

            if (Materials.TryGetValue(name, out var mat))
            {
                return mat;
            }

            var resource = VrfGuiContext.LoadFileByAnyMeansNecessary(name + "_c");
            mat = LoadMaterial(resource);

            Materials.Add(name, mat);

            return mat;
        }

        public RenderMaterial LoadMaterial(Resource resource)
        {
            if (resource == null)
            {
                return GetErrorMaterial();
            }

            var mat = new RenderMaterial(
                (VrfMaterial)resource.DataBlock,
                GetInputSignature(resource),
                VrfGuiContext.ShaderLoader
            );

            foreach (var (textureName, texturePath) in mat.Material.TextureParams)
            {
                if (TryBindTexture(mat, textureName, texturePath))
                {
                    continue;
                }

                foreach (var (possibleAlias, aliases) in TextureAliases)
                {
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
                    mat.Textures[name] = GetTexture(path);
                    return true;
                }

                return false;
            }

            ApplyMaterialDefaults(mat);

            return mat;
        }


        public RenderTexture GetTexture(string name)
        {
            if (Textures.TryGetValue(name, out var tex))
            {
                return tex;
            }

            tex = LoadTexture(name);
            Textures.Add(name, tex);
            return tex;
        }

        public RenderTexture LoadTexture(string name)
        {
            var textureResource = VrfGuiContext.LoadFileByAnyMeansNecessary(name + "_c");

            if (textureResource == null)
            {
                return GetErrorTexture();
            }

            return LoadTexture(textureResource);
        }

        public RenderTexture LoadTexture(Resource textureResource)
        {
            var data = (Texture)textureResource.DataBlock;

            var target = TextureTarget.Texture2D;
            var clampModeS = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPS) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            var clampModeT = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPT) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            var clampModeU = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPU) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;

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
            else if (data.Flags.HasFlag(VTexFlags.TEXTURE_ARRAY))
            {
                target = TextureTarget.Texture2DArray;
                clampModeS = TextureWrapMode.ClampToEdge;
                clampModeT = TextureWrapMode.ClampToEdge;
                clampModeU = TextureWrapMode.ClampToEdge;
            }

            var tex = new RenderTexture(target, GL.GenTexture())
            {
                SpritesheetData = data.GetSpriteSheetData(),
            };

            var internalFormat = GetPixelInternalFormat(data.Format);
            var format = GetInternalFormat(data.Format);

            tex.Bind();

            if (!format.HasValue && !internalFormat.HasValue)
            {
                Console.Error.WriteLine($"Don't support {data.Format} but don't want to crash either. Using error texture!");
                return GetErrorTexture();
            }

            var buffer = ArrayPool<byte>.Shared.Rent(data.GetBiggestBufferSize());

            var maxMipLevel = 0;
            var minMipLevel = 0;

            var maxTextureSize = Settings.Config.MaxTextureSize;

            if (data.Flags.HasFlag(VTexFlags.TEXTURE_ARRAY) || data.Flags.HasFlag(VTexFlags.VOLUME_TEXTURE))
            {
                maxTextureSize = int.MaxValue;
            }

            try
            {
                foreach (var (i, width, height, bufferSize) in data.GetEveryMipLevelTexture(buffer, maxTextureSize))
                {
                    if (maxMipLevel == 0)
                    {
                        maxMipLevel = i;
                    }

                    minMipLevel = i;

                    if (target == TextureTarget.TextureCubeMap)
                    {
                        for (var face = Texture.CubemapFace.PositiveX; face <= Texture.CubemapFace.NegativeZ; face++)
                        {
                            var faceSize = bufferSize / 6;
                            var faceOffset = faceSize * (int)face;
                            LoadTextureImplShared(data.Format, internalFormat, format, i, width, height, data.Depth,
                                faceSize, buffer[faceOffset..(faceOffset + faceSize)], TextureTarget.TextureCubeMapPositiveX + (int)face);
                        }
                    }
                    else
                    {
                        LoadTextureImplShared(data.Format, internalFormat, format, i, width, height, data.Depth, bufferSize, buffer, target);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            GL.TexParameter(target, TextureParameterName.TextureBaseLevel, minMipLevel);
            GL.TexParameter(target, TextureParameterName.TextureMaxLevel, maxMipLevel);

            // Dispose texture otherwise we run out of memory
            // TODO: This might conflict when opening multiple files due to shit caching
            textureResource.Dispose();

            if (MaxTextureMaxAnisotropy >= 4)
            {
                GL.TexParameter(target, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, MaxTextureMaxAnisotropy);
                GL.TexParameter(target, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                GL.TexParameter(target, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            }
            else
            {
                GL.TexParameter(target, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(target, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            }

            GL.TexParameter(target, TextureParameterName.TextureWrapS, (int)clampModeS);
            GL.TexParameter(target, TextureParameterName.TextureWrapT, (int)clampModeT);
            GL.TexParameter(target, TextureParameterName.TextureWrapR, (int)clampModeU);

            tex.Unbind();
            return tex;
        }

        private static void LoadTextureImplShared(VTexFormat vtexFormat, PixelInternalFormat? internalFormat, InternalFormat? format,
            int level, int width, int height, int depth, int bufferSize, byte[] buffer, TextureTarget target)
        {
            if (target == TextureTarget.TextureCubeMapArray)
            {
                depth *= 6;
            }

            var is3d = target == TextureTarget.Texture2DArray || target == TextureTarget.TextureCubeMapArray;

            if (internalFormat.HasValue)
            {
                var pixelFormat = GetPixelFormat(vtexFormat);
                var pixelType = GetPixelType(vtexFormat);

                if (is3d)
                {
                    GL.TexImage3D(target, level, internalFormat.Value, width, height, depth, 0, pixelFormat, pixelType, buffer);
                }
                else
                {
                    GL.TexImage2D(target, level, internalFormat.Value, width, height, 0, pixelFormat, pixelType, buffer);
                }
            }
            else
            {
                if (is3d)
                {
                    GL.CompressedTexImage3D(target, level, format.Value, width, height, depth, 0, bufferSize, buffer);
                }
                else
                {
                    GL.CompressedTexImage2D(target, level, format.Value, width, height, 0, bufferSize, buffer);
                }
            }
        }

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
                _ => null // Unsupported texture format
            };

        private static PixelFormat GetPixelFormat(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.R16 => PixelFormat.Red,
                VTexFormat.R16F => PixelFormat.Red,
                VTexFormat.R32F => PixelFormat.Red,
                VTexFormat.RG1616 => PixelFormat.Rg,
                VTexFormat.RG1616F => PixelFormat.Rg,
                VTexFormat.RG3232F => PixelFormat.Rg,
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


        public RenderMaterial GetErrorMaterial()
        {
            var materialData = new VrfMaterial { ShaderName = "vrf.error" };
            var errorMat = new RenderMaterial(materialData, null, VrfGuiContext.ShaderLoader);

            ApplyMaterialDefaults(errorMat);

            return errorMat;
        }

        private void ApplyMaterialDefaults(RenderMaterial mat)
        {
            if (!mat.Textures.ContainsKey("g_tColor"))
            {
                mat.Textures["g_tColor"] = GetErrorTexture();
            }

            if (!mat.Textures.ContainsKey("g_tNormal"))
            {
                mat.Textures["g_tNormal"] = GetDefaultNormal();
            }

            if (!mat.Textures.ContainsKey("g_tTintMask"))
            {
                mat.Textures["g_tTintMask"] = GetDefaultMask();
            }

            // Prevent materials from sharing state when these values are missing
            mat.Material.VectorParams.TryAdd("g_vTexCoordScale", Vector4.One);
            mat.Material.VectorParams.TryAdd("g_vTexCoordOffset", Vector4.Zero);
            mat.Material.VectorParams.TryAdd("g_vColorTint", Vector4.One);
            mat.Material.FloatParams.TryAdd("g_flMetalness", 0f);
        }

        public RenderTexture GetErrorTexture()
        {
            if (ErrorTexture == null)
            {
                var color = new[]
                {
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,

                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,

                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,

                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                    0f, 0.9f, 0f, 1f,
                    0.9f, 0.2f, 0.8f, 1f,
                };

                ErrorTexture = GenerateColorTexture(4, 4, color);
            }

            return ErrorTexture;
        }

        public static RenderTexture CreateSolidTexture(float r, float g, float b)
            => GenerateColorTexture(1, 1, new[] { r, g, b, 1f });

        public RenderTexture GetDefaultNormal()
        {
            DefaultNormal ??= CreateSolidTexture(0.5f, 0.5f, 1.0f);
            return DefaultNormal;
        }

        public RenderTexture GetDefaultMask()
        {
            DefaultMask ??= CreateSolidTexture(1.0f, 1.0f, 1.0f);
            return DefaultMask;
        }

        private static RenderTexture GenerateColorTexture(int width, int height, float[] color)
        {
            var texture = new RenderTexture(TextureTarget.Texture2D, GL.GenTexture());

            texture.Bind();
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, width, height, 0, PixelFormat.Rgba, PixelType.Float, color);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            texture.Unbind();

            return texture;
        }

        private static IKeyValueCollection GetInputSignature(Resource resource)
        {
            if (resource.ContainsBlockType(BlockType.INSG))
            {
                return ((BinaryKV3)resource.GetBlockByType(BlockType.INSG)).Data;
            }

            // Material might not have REDI, or it might have RED2 without INSG
            if (resource.EditInfo != null && resource.EditInfo.Type != BlockType.REDI)
            {
                return null;
            }

            var extraStringData = (ValveResourceFormat.Blocks.ResourceEditInfoStructs.ExtraStringData)resource.EditInfo.Structs[ValveResourceFormat.Blocks.ResourceEditInfo.REDIStruct.ExtraStringData];
            var inputSignatureString = extraStringData.List.Where(x => x.Name == "VSInputSignature").FirstOrDefault()?.Value;

            if (inputSignatureString == null)
            {
                return null;
            }

            if (!inputSignatureString.StartsWith("<!-- kv3", StringComparison.InvariantCulture))
            {
                return null;
            }

            var ms = new MemoryStream(Encoding.UTF8.GetBytes(inputSignatureString));

            return KeyValues3.ParseKVFile(ms).Root;
        }
    }
}
