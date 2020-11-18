using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;

namespace GUI.Types.Renderer
{
    public class MaterialLoader
    {
        private readonly Dictionary<string, RenderMaterial> Materials = new Dictionary<string, RenderMaterial>();
        private readonly VrfGuiContext VrfGuiContext;
        private int ErrorTextureID;
        public static int MaxTextureMaxAnisotropy { get; set; }

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

            if (Materials.ContainsKey(name))
            {
                return Materials[name];
            }

            var resource = VrfGuiContext.LoadFileByAnyMeansNecessary(name + "_c");
            var mat = LoadMaterial(resource);

            Materials.Add(name, mat);

            return mat;
        }

        public RenderMaterial LoadMaterial(Resource resource)
        {
            if (resource == null)
            {
                return GetErrorMaterial();
            }

            var mat = new RenderMaterial((VrfMaterial)resource.DataBlock);

            foreach (var textureReference in mat.Material.TextureParams)
            {
                var key = textureReference.Key;

                mat.Textures[key] = LoadTexture(textureReference.Value);
            }

            if (mat.Material.IntParams.ContainsKey("F_SOLID_COLOR") && mat.Material.IntParams["F_SOLID_COLOR"] == 1)
            {
                var a = mat.Material.VectorParams["g_vColorTint"];

                mat.Textures["g_tColor"] = GenerateColorTexture(1, 1, new[] { a.X, a.Y, a.Z, a.W });
            }

            if (!mat.Textures.ContainsKey("g_tColor"))
            {
                mat.Textures["g_tColor"] = GetErrorTexture();
            }

            // Since our shaders only use g_tColor, we have to find at least one texture to use here
            if (mat.Textures["g_tColor"] == GetErrorTexture())
            {
                var namesToTry = new[] { "g_tColor2", "g_tColor1", "g_tColorA", "g_tColorB", "g_tColorC" };

                foreach (var name in namesToTry)
                {
                    if (mat.Textures.ContainsKey(name))
                    {
                        mat.Textures["g_tColor"] = mat.Textures[name];
                        break;
                    }
                }
            }

            // Set default values for scale and positions
            if (!mat.Material.VectorParams.ContainsKey("g_vTexCoordScale"))
            {
                mat.Material.VectorParams["g_vTexCoordScale"] = Vector4.One;
            }

            if (!mat.Material.VectorParams.ContainsKey("g_vTexCoordOffset"))
            {
                mat.Material.VectorParams["g_vTexCoordOffset"] = Vector4.Zero;
            }

            if (!mat.Material.VectorParams.ContainsKey("g_vColorTint"))
            {
                mat.Material.VectorParams["g_vColorTint"] = Vector4.One;
            }

            return mat;
        }

        public int LoadTexture(string name)
        {
            var textureResource = VrfGuiContext.LoadFileByAnyMeansNecessary(name + "_c");

            if (textureResource == null)
            {
                return GetErrorTexture();
            }

            return LoadTexture(textureResource);
        }

        public int LoadTexture(Resource textureResource)
        {
            var tex = (Texture)textureResource.DataBlock;

            var id = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, id);

            var textureReader = textureResource.Reader;
            textureReader.BaseStream.Position = tex.Offset + tex.Size;

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, tex.NumMipLevels - 1);

            var internalFormat = GetPixelInternalFormat(tex.Format);
            var format = GetInternalFormat(tex.Format);

            if (!format.HasValue && !internalFormat.HasValue)
            {
                Console.Error.WriteLine($"Don't support {tex.Format} but don't want to crash either. Using error texture!");
                return GetErrorTexture();
            }

            for (var i = tex.NumMipLevels - 1; i >= 0; i--)
            {
                var width = tex.Width >> i;
                var height = tex.Height >> i;
                var bytes = tex.GetDecompressedTextureAtMipLevel(i);

                if (internalFormat.HasValue)
                {
                    var pixelFormat = GetPixelFormat(tex.Format);
                    var pixelType = GetPixelType(tex.Format);

                    GL.TexImage2D(TextureTarget.Texture2D, i, internalFormat.Value, width, height, 0, pixelFormat, pixelType, bytes);
                }
                else
                {
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, i, format.Value, width, height, 0, bytes.Length, bytes);
                }
            }

            // Dispose texture otherwise we run out of memory
            // TODO: This might conflict when opening multiple files due to shit caching
            textureResource.Dispose();

            if (MaxTextureMaxAnisotropy >= 4)
            {
                GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, MaxTextureMaxAnisotropy);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            }

            var clampModeS = tex.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPS)
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;
            var clampModeT = tex.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPT)
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)clampModeS);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)clampModeT);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            return id;
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
                VTexFormat.RGBA16161616 => InternalFormat.Rgba16f,
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
            var errorMat = new RenderMaterial(new VrfMaterial());
            errorMat.Textures["g_tColor"] = GetErrorTexture();
            errorMat.Material.ShaderName = "vrf.error";

            return errorMat;
        }


        public int GetErrorTexture()
        {
            if (ErrorTextureID == 0)
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

                ErrorTextureID = GenerateColorTexture(4, 4, color);
            }

            return ErrorTextureID;
        }

        public static int CreateSolidTexture(float r, float g, float b)
            => GenerateColorTexture(1, 1, new[] { r, g, b, 1f });

        private static int GenerateColorTexture(int width, int height, float[] color)
        {
            var texture = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, width, height, 0, PixelFormat.Rgba, PixelType.Float, color);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            return texture;
        }
    }
}
