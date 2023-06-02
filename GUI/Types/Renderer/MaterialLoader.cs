using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace GUI.Types.Renderer
{
    public class MaterialLoader
    {
        private readonly Dictionary<string, RenderMaterial> Materials = new();
        private readonly VrfGuiContext VrfGuiContext;
        private RenderTexture ErrorTexture;
        private RenderTexture DefaultNormal;
        private RenderTexture DefaultMask;
        public static int MaxTextureMaxAnisotropy { get; set; }

        private readonly Dictionary<string, string[]> TextureAliases = new()
        {
            ["g_tLayer2Color"] = new[] { "g_tColorB" },
            ["g_tColor"] = new[] { "g_tColor2", "g_tColor1", "g_tColorA", "g_tColorB", "g_tColorC" },
            ["g_tNormal"] = new[] { "g_tNormalA", "g_tNormalRoughness", "g_tLayer1NormalRoughness" },
            ["g_tLayer2NormalRoughness"] = new[] { "g_tNormalB" },
            ["g_tBlendModulation"] = new[] { "g_tMask" },

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

            var mat = new RenderMaterial((VrfMaterial)resource.DataBlock, VrfGuiContext.ShaderLoader);

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
                    mat.Textures[name] = LoadTexture(path);
                    return true;
                }

                return false;
            }

            if (mat.Material.IntParams.ContainsKey("F_SOLID_COLOR") && mat.Material.IntParams["F_SOLID_COLOR"] == 1)
            {
                var a = mat.Material.VectorParams["g_vColorTint"];

                mat.Textures["g_tColor"] = GenerateColorTexture(1, 1, new[] { a.X, a.Y, a.Z, a.W });
            }


            mat.Textures.TryAdd("g_tColor", GetErrorTexture());
            mat.Textures.TryAdd("g_tNormal", GetDefaultNormal());
            mat.Textures.TryAdd("g_tTintMask", GetDefaultMask());
            mat.Material.VectorParams.TryAdd("g_vTexCoordScale", Vector4.One);
            mat.Material.VectorParams.TryAdd("g_vTexCoordOffset", Vector4.Zero);
            mat.Material.VectorParams.TryAdd("g_vColorTint", Vector4.One);

            return mat;
        }

        public RenderTexture LoadTexture(string name, TextureTarget targetHint = TextureTarget.Texture2D)
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
            var target = data.Flags.HasFlag(VTexFlags.CUBE_TEXTURE)
                ? TextureTarget.TextureCubeMap
                : TextureTarget.Texture2D;

            var tex = new RenderTexture(target, GL.GenTexture())
            {
                SpritesheetData = data.GetSpriteSheetData(),
            };

            var internalFormat = GetPixelInternalFormat(data.Format);
            var format = GetInternalFormat(data.Format);

            tex.Bind();
            if (target == TextureTarget.TextureCubeMap)
            {
                // TODO: merge loaders in one place
                return LoadCubemap(data, tex, format, internalFormat);
            }

            if (!format.HasValue && !internalFormat.HasValue)
            {
                Console.Error.WriteLine($"Don't support {data.Format} but don't want to crash either. Using error texture!");
                return GetErrorTexture();
            }

            var buffer = ArrayPool<byte>.Shared.Rent(data.GetBiggestBufferSize());

            var maxMipLevel = 0;
            var minMipLevel = 0;

            try
            {
                foreach (var (i, width, height, bufferSize) in data.GetEveryMipLevelTexture(buffer, Settings.Config.MaxTextureSize))
                {
                    if (maxMipLevel == 0)
                    {
                        maxMipLevel = i;
                    }

                    minMipLevel = i;

                    if (internalFormat.HasValue)
                    {
                        var pixelFormat = GetPixelFormat(data.Format);
                        var pixelType = GetPixelType(data.Format);

                        GL.TexImage2D(TextureTarget.Texture2D, i, internalFormat.Value, width, height, 0, pixelFormat, pixelType, buffer);
                    }
                    else
                    {
                        GL.CompressedTexImage2D(TextureTarget.Texture2D, i, format.Value, width, height, 0, bufferSize, buffer);
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

            var clampModeS = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPS)
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;
            var clampModeT = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPT)
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;

            GL.TexParameter(target, TextureParameterName.TextureWrapS, (int)clampModeS);
            GL.TexParameter(target, TextureParameterName.TextureWrapT, (int)clampModeT);

            tex.Unbind();
            return tex;
        }

        private static RenderTexture LoadCubemap(Texture data, RenderTexture tex, InternalFormat? format, PixelInternalFormat? internalFormat)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(data.GetBiggestBufferSize());
            try
            {
                var it = data.GetEveryMipLevelTexture(buffer);
                var c = it.Last();
                for (var face = Texture.CubemapFace.PositiveX; face <= Texture.CubemapFace.NegativeZ; face++)
                {
                    var pixelFormat = GetPixelFormat(data.Format);
                    var pixelType = GetPixelType(data.Format);

                    var faceSize = c.BufferSize / 6;
                    var faceOffset = faceSize * (int)face;

                    GL.CompressedTexImage2D(TextureTarget.TextureCubeMapPositiveX + (int)face, 0,
                        format.Value, c.Width, c.Height, 0, faceSize, buffer[faceOffset..(faceOffset + faceSize)]);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            GL.TexParameter(tex.Target, TextureParameterName.TextureBaseLevel, 0);
            GL.TexParameter(tex.Target, TextureParameterName.TextureMaxLevel, 0);

            var clampModeS = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPS)
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;
            var clampModeT = data.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPT)
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;

            GL.TexParameter(tex.Target, TextureParameterName.TextureWrapS, (int)clampModeS);
            GL.TexParameter(tex.Target, TextureParameterName.TextureWrapT, (int)clampModeT);

            return tex;
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
                _ => null // Unsupported texture format
            };

        private static PixelFormat GetPixelFormat(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.R16 => PixelFormat.Red,
                VTexFormat.R16F => PixelFormat.Red,
                VTexFormat.RG1616 => PixelFormat.Rg,
                VTexFormat.RG1616F => PixelFormat.Rg,
                _ => PixelFormat.Rgba
            };

        private static PixelType GetPixelType(VTexFormat vformat)
            => vformat switch
            {
                VTexFormat.R16 => PixelType.UnsignedShort,
                VTexFormat.R16F => PixelType.Float,
                VTexFormat.RG1616 => PixelType.UnsignedShort,
                VTexFormat.RG1616F => PixelType.Float,
                _ => PixelType.UnsignedByte
            };


        public RenderMaterial GetErrorMaterial()
        {
            var materialData = new VrfMaterial { ShaderName = "vrf.error" };
            var errorMat = new RenderMaterial(materialData, VrfGuiContext.ShaderLoader);
            errorMat.Textures["g_tColor"] = GetErrorTexture();

            return errorMat;
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
    }
}
