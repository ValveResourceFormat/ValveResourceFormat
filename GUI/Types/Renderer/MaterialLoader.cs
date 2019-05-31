using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    internal class MaterialLoader
    {
        public List<string> LoadedTextures { get; } = new List<string>();
        private readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private readonly Package CurrentPackage;
        private readonly string CurrentFileName;
        private int ErrorTextureID;
        public int MaxTextureMaxAnisotropy { get; set; }

        public MaterialLoader(string currentFileName, Package currentPackage)
        {
            CurrentPackage = currentPackage;
            CurrentFileName = currentFileName;

            MaxTextureMaxAnisotropy = 0;
        }

        public Material GetMaterial(string name)
        {
            if (!Materials.ContainsKey(name))
            {
                return LoadMaterial(name);
            }

            return Materials[name];
        }

        private Material LoadMaterial(string name)
        {
            var mat = new Material();
            mat.Textures["g_tColor"] = GetErrorTexture();

            var resource = FileExtensions.LoadFileByAnyMeansNecessary(name + "_c", CurrentFileName, CurrentPackage);

            Materials.Add(name, mat);

            if (resource == null)
            {
                Console.Error.WriteLine("File " + name + " not found");

                mat.Textures["g_tNormal"] = GetErrorTexture();

                return mat;
            }

            mat.Parameters = (ValveResourceFormat.ResourceTypes.Material)resource.Blocks[BlockType.DATA];

            foreach (var textureReference in mat.Parameters.TextureParams)
            {
                var key = textureReference.Key;

                mat.Textures[key] = LoadTexture(textureReference.Value);
            }

            if (mat.Parameters.IntParams.ContainsKey("F_SOLID_COLOR") && mat.Parameters.IntParams["F_SOLID_COLOR"] == 1)
            {
                var a = mat.Parameters.VectorParams["g_vColorTint"];

                mat.Textures["g_tColor"] = GenerateSolidColorTexture(new[] { a.X, a.Y, a.Z, a.W });
            }

            // TODO: Perry, this probably needs to be in shader or something
            if (mat.Textures.ContainsKey("g_tColor2") && mat.Textures["g_tColor"] == GetErrorTexture())
            {
                mat.Textures["g_tColor"] = mat.Textures["g_tColor2"];
            }

            if (mat.Textures.ContainsKey("g_tColor1") && mat.Textures["g_tColor"] == GetErrorTexture())
            {
                mat.Textures["g_tColor"] = mat.Textures["g_tColor1"];
            }

            // Set default values for scale and positions
            if (!mat.Parameters.VectorParams.ContainsKey("g_vTexCoordScale"))
            {
                mat.Parameters.VectorParams["g_vTexCoordScale"] = Vector4.One;
            }

            if (!mat.Parameters.VectorParams.ContainsKey("g_vTexCoordOffset"))
            {
                mat.Parameters.VectorParams["g_vTexCoordOffset"] = Vector4.Zero;
            }

            return mat;
        }

        private int LoadTexture(string name)
        {
            var textureResource = FileExtensions.LoadFileByAnyMeansNecessary(name + "_c", CurrentFileName, CurrentPackage);

            if (textureResource == null)
            {
                Console.Error.WriteLine("File " + name + " not found");

                return GetErrorTexture();
            }

            LoadedTextures.Add(name);

            var tex = (Texture)textureResource.Blocks[BlockType.DATA];

            var id = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, id);

            var textureReader = textureResource.Reader;
            textureReader.BaseStream.Position = tex.Offset + tex.Size;

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, tex.NumMipLevels - 1);

            var width = tex.Width / (int)Math.Pow(2.0, tex.NumMipLevels);
            var height = tex.Height / (int)Math.Pow(2.0, tex.NumMipLevels);

            int blockSize;
            InternalFormat format;

            if (tex.Format.HasFlag(VTexFormat.DXT1))
            {
                blockSize = 8;
                format = InternalFormat.CompressedRgbaS3tcDxt1Ext;
            }
            else if (tex.Format.HasFlag(VTexFormat.DXT5))
            {
                blockSize = 16;
                format = InternalFormat.CompressedRgbaS3tcDxt5Ext;
            }
            else
            {
                Console.Error.WriteLine($"Don't support {tex.Format} but don't want to crash either. Using error texture!");
                return GetErrorTexture();
            }

            for (var i = tex.NumMipLevels - 1; i >= 0; i--)
            {
                if ((width *= 2) == 0)
                {
                    width = 1;
                }

                if ((height *= 2) == 0)
                {
                    height = 1;
                }

                var size = ((width + 3) / 4) * ((height + 3) / 4) * blockSize;

                GL.CompressedTexImage2D(TextureTarget.Texture2D, i, format, width, height, 0, size, textureReader.ReadBytes(size));
            }

            // Dispose texture otherwise we run out of memory
            // TODO: This might conflict when opening multiple files due to shit caching
            textureResource.Dispose();

            if (MaxTextureMaxAnisotropy > 0)
            {
                GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, MaxTextureMaxAnisotropy);
            }

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)(tex.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPS) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)(tex.Flags.HasFlag(VTexFlags.SUGGEST_CLAMPT) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat));

            return id;
        }

        private int GetErrorTexture()
        {
            if (ErrorTextureID == 0)
            {
                ErrorTextureID = GenerateSolidColorTexture(new[] { 173 / 255f, 255 / 255f, 47 / 255f, 255 / 255f });
            }

            return ErrorTextureID;
        }

        private static int GenerateSolidColorTexture(float[] color)
        {
            var texture = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, color);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            return texture;
        }
    }
}
