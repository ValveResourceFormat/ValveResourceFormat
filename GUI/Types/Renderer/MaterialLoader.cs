using System;
using System.Collections.Generic;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;

namespace GUI.Types.Renderer
{
    internal class MaterialLoader
    {
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
            //Console.WriteLine("\n>> Loading material " + name);

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

            var matData = (NTRO)resource.Blocks[BlockType.DATA];
            mat.Name = ((NTROValue<string>)matData.Output["m_materialName"]).Value;
            mat.ShaderName = ((NTROValue<string>)matData.Output["m_shaderName"]).Value;
            //mat.renderAttributesUsed = ((ValveResourceFormat.ResourceTypes.NTROSerialization.NTROValue<string>)matData.Output["m_renderAttributesUsed"]).Value; //TODO: string array?
            var intParams = (NTROArray)matData.Output["m_intParams"];
            for (var i = 0; i < intParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)intParams[i]).Value;
                mat.IntParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<int>)subStruct["m_nValue"]).Value);
            }

            var floatParams = (NTROArray)matData.Output["m_floatParams"];
            for (var i = 0; i < floatParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)floatParams[i]).Value;
                mat.FloatParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<float>)subStruct["m_flValue"]).Value);
            }

            var vectorParams = (NTROArray)matData.Output["m_vectorParams"];
            for (var i = 0; i < vectorParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)vectorParams[i]).Value;
                var ntroVector = ((NTROValue<Vector4>)subStruct["m_value"]).Value;
                // Prevent error
                if (!mat.VectorParams.ContainsKey(((NTROValue<string>)subStruct["m_name"]).Value))
                {
                    mat.VectorParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, new OpenTK.Vector4(ntroVector.X, ntroVector.Y, ntroVector.Z, ntroVector.W));
                }
            }

            var textureParams = (NTROArray)matData.Output["m_textureParams"];
            for (var i = 0; i < textureParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)textureParams[i]).Value;
                mat.TextureParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)subStruct["m_pValue"]).Value);
            }

            var dynamicParams = (NTROArray)matData.Output["m_dynamicParams"];
            var dynamicTextureParams = (NTROArray)matData.Output["m_dynamicTextureParams"];

            var intAttributes = (NTROArray)matData.Output["m_intAttributes"];
            for (var i = 0; i < intAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)intAttributes[i]).Value;
                mat.IntAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<int>)subStruct["m_nValue"]).Value);
            }

            var floatAttributes = (NTROArray)matData.Output["m_floatAttributes"];
            for (var i = 0; i < floatAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)floatAttributes[i]).Value;
                mat.FloatAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<float>)subStruct["m_flValue"]).Value);
            }

            var vectorAttributes = (NTROArray)matData.Output["m_vectorAttributes"];
            for (var i = 0; i < vectorAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)vectorAttributes[i]).Value;
                var ntroVector = ((NTROValue<Vector4>)subStruct["m_value"]).Value;
                mat.VectorAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, new OpenTK.Vector4(ntroVector.X, ntroVector.Y, ntroVector.Z, ntroVector.W));
            }

            var textureAttributes = (NTROArray)matData.Output["m_textureAttributes"];
            //TODO
            var stringAttributes = (NTROArray)matData.Output["m_stringAttributes"];
            for (var i = 0; i < stringAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)stringAttributes[i]).Value;
                mat.StringAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<string>)subStruct["m_value"]).Value);
            }

            foreach (var textureReference in mat.TextureParams)
            {
                var key = textureReference.Key;

                //Console.WriteLine(">>> " + textureReference.Key + " - " + textureReference.Value.Name);

                mat.Textures[key] = LoadTexture(textureReference.Value.Name);
            }

            if (mat.IntParams.ContainsKey("F_SOLID_COLOR") && mat.IntParams["F_SOLID_COLOR"] == 1)
            {
                var a = mat.VectorParams["g_vColorTint"];

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
            if (!mat.VectorParams.ContainsKey("g_vTexCoordScale"))
            {
                mat.VectorParams["g_vTexCoordScale"] = OpenTK.Vector4.One;
            }

            if (!mat.VectorParams.ContainsKey("g_vTexCoordOffset"))
            {
                mat.VectorParams["g_vTexCoordOffset"] = OpenTK.Vector4.Zero;
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

            var tex = (Texture)textureResource.Blocks[BlockType.DATA];

            //Console.WriteLine(">>>> Loading texture " + name + " " + tex.Flags);

            var id = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, id);

            var textureReader = textureResource.Reader;
            textureReader.BaseStream.Position = tex.Offset + tex.Size;

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, tex.NumMipLevels - 1);

            var width = tex.Width / (int)Math.Pow(2.0, tex.NumMipLevels);
            var height = tex.Height / (int)Math.Pow(2.0, tex.NumMipLevels);

            int blockSize;
            PixelInternalFormat format;

            if (tex.Format.HasFlag(VTexFormat.DXT1))
            {
                blockSize = 8;
                format = PixelInternalFormat.CompressedRgbaS3tcDxt1Ext;
            }
            else if (tex.Format.HasFlag(VTexFormat.DXT5))
            {
                blockSize = 16;
                format = PixelInternalFormat.CompressedRgbaS3tcDxt5Ext;
            }
            else if (tex.Format.HasFlag(VTexFormat.RGBA8888))
            {
                //blockSize = 4;
                //format = PixelInternalFormat.Rgba8i;
                Console.Error.WriteLine("Don't support RGBA8888 but don't want to crash either. Using error texture!");
                return GetErrorTexture();
            }
            else
            {
                throw new Exception("Unsupported texture format: " + tex.Format);
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
