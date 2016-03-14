using System;
using System.Collections.Generic;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;
using Vector4 = OpenTK.Vector4;

namespace GUI.Types.Renderer
{
    internal class MaterialLoader
    {
        public static Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        public struct Material
        {
            public string Name;
            public string ShaderName;
            public int ColorTextureID;
            public Dictionary<string, int> OtherTextureIDs;
            public Dictionary<string, int> IntParams;
            public Dictionary<string, float> FloatParams;
            public Dictionary<string, Vector4> VectorParams;
            public Dictionary<string, ResourceExtRefList.ResourceReferenceInfo> TextureParams;
            //public Dictionary<string, ????> dynamicParams;
            //public Dictionary<string, ????> dynamicTextureParams;
            public Dictionary<string, int> IntAttributes;
            public Dictionary<string, float> FloatAttributes;
            public Dictionary<string, Vector4> VectorAttributes;
            //public Dictionary<string, long> textureAttributes;
            public Dictionary<string, string> StringAttributes;
            //public string[] renderAttributesUsed; // ?
        }

        public static int LoadMaterial(string name, string currentFileName, Package currentPackage, int maxTextureMaxAnisotropy)
        {
            if (name != "materials/debug/debugempty.vmat" && !Materials.ContainsKey("materials/debug/debugempty.vmat"))
            {
                LoadMaterial("materials/debug/debugempty.vmat", currentFileName, null, maxTextureMaxAnisotropy);
            }

            Console.WriteLine("Loading material " + name);

            var resource = new Resource();

            if (!FileExtensions.LoadFileByAnyMeansNecessary(resource, name + "_c", currentFileName, currentPackage))
            {
                Console.WriteLine("File " + name + " not found");
                return 1;
            }

            var mat = default(Material);
            var matData = (NTRO)resource.Blocks[BlockType.DATA];
            mat.Name = ((NTROValue<string>)matData.Output["m_materialName"]).Value;
            mat.ShaderName = ((NTROValue<string>)matData.Output["m_shaderName"]).Value;
            //mat.renderAttributesUsed = ((ValveResourceFormat.ResourceTypes.NTROSerialization.NTROValue<string>)matData.Output["m_renderAttributesUsed"]).Value; //TODO: string array?
            var intParams = (NTROArray)matData.Output["m_intParams"];
            mat.IntParams = new Dictionary<string, int>();
            for (var i = 0; i < intParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)intParams[i]).Value;
                mat.IntParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<int>)subStruct["m_nValue"]).Value);
            }

            var floatParams = (NTROArray)matData.Output["m_floatParams"];
            mat.FloatParams = new Dictionary<string, float>();
            for (var i = 0; i < floatParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)floatParams[i]).Value;
                mat.FloatParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<float>)subStruct["m_flValue"]).Value);
            }

            var vectorParams = (NTROArray)matData.Output["m_vectorParams"];
            mat.VectorParams = new Dictionary<string, Vector4>();
            for (var i = 0; i < vectorParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)vectorParams[i]).Value;
                var ntroVector = ((NTROValue<ValveResourceFormat.ResourceTypes.NTROSerialization.Vector4>)subStruct["m_value"]).Value;
                mat.VectorParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, new Vector4(ntroVector.field0, ntroVector.field1, ntroVector.field2, ntroVector.field3));
            }

            var textureParams = (NTROArray)matData.Output["m_textureParams"];
            mat.TextureParams = new Dictionary<string, ResourceExtRefList.ResourceReferenceInfo>();
            for (var i = 0; i < textureParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)textureParams[i]).Value;
                mat.TextureParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)subStruct["m_pValue"]).Value);
            }

            var dynamicParams = (NTROArray)matData.Output["m_dynamicParams"];
            var dynamicTextureParams = (NTROArray)matData.Output["m_dynamicTextureParams"];

            var intAttributes = (NTROArray)matData.Output["m_intAttributes"];
            mat.IntAttributes = new Dictionary<string, int>();
            for (var i = 0; i < intAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)intAttributes[i]).Value;
                mat.IntAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<int>)subStruct["m_nValue"]).Value);
            }

            var floatAttributes = (NTROArray)matData.Output["m_floatAttributes"];
            mat.FloatAttributes = new Dictionary<string, float>();
            for (var i = 0; i < floatAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)floatAttributes[i]).Value;
                mat.FloatAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<float>)subStruct["m_flValue"]).Value);
            }

            var vectorAttributes = (NTROArray)matData.Output["m_vectorAttributes"];
            mat.VectorAttributes = new Dictionary<string, Vector4>();
            for (var i = 0; i < vectorAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)vectorAttributes[i]).Value;
                var ntroVector = ((NTROValue<ValveResourceFormat.ResourceTypes.NTROSerialization.Vector4>)subStruct["m_value"]).Value;
                mat.VectorAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, new Vector4(ntroVector.field0, ntroVector.field1, ntroVector.field2, ntroVector.field3));
            }

            var textureAttributes = (NTROArray)matData.Output["m_textureAttributes"];
            //TODO
            var stringAttributes = (NTROArray)matData.Output["m_stringAttributes"];
            mat.StringAttributes = new Dictionary<string, string>();
            for (var i = 0; i < stringAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)stringAttributes[i]).Value;
                mat.StringAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<string>)subStruct["m_value"]).Value);
            }

            mat.OtherTextureIDs = new Dictionary<string, int>();
            foreach (var textureReference in mat.TextureParams)
            {
                switch (textureReference.Key)
                {
                    //TODO: Investigate why tColor and tNormal have differently numbered textures
                    case "g_tColor":
                    case "g_tColor1":
                    case "g_tColor2":
                        mat.ColorTextureID = LoadTexture(textureReference.Value.Name, currentFileName, currentPackage, maxTextureMaxAnisotropy, TextureUnit.Texture0);
                        break;
                    case "g_tNormal":
                    case "g_tNormal2":
                        mat.OtherTextureIDs.Add(textureReference.Key, LoadTexture(textureReference.Value.Name, currentFileName, currentPackage, maxTextureMaxAnisotropy, TextureUnit.Texture1));
                        break;
                    case "g_tCubeMap":
                        mat.OtherTextureIDs.Add(textureReference.Key, LoadTexture(textureReference.Value.Name, currentFileName, currentPackage, maxTextureMaxAnisotropy, TextureUnit.Texture2));
                        break;
                    case "g_tGloss":
                        mat.OtherTextureIDs.Add(textureReference.Key, LoadTexture(textureReference.Value.Name, currentFileName, currentPackage, maxTextureMaxAnisotropy, TextureUnit.Texture3));
                        break;
                    case "g_tRoughness":
                        mat.OtherTextureIDs.Add(textureReference.Key, LoadTexture(textureReference.Value.Name, currentFileName, currentPackage, maxTextureMaxAnisotropy, TextureUnit.Texture4));
                        break;
                    case "g_tSelfIllumMask":
                        mat.OtherTextureIDs.Add(textureReference.Key, LoadTexture(textureReference.Value.Name, currentFileName, currentPackage, maxTextureMaxAnisotropy, TextureUnit.Texture5));
                        break;
                    case "g_tMetalnessReflectanceFresnel":
                        mat.OtherTextureIDs.Add(textureReference.Key, LoadTexture(textureReference.Value.Name, currentFileName, currentPackage, maxTextureMaxAnisotropy, TextureUnit.Texture6));
                        break;
                    default:
                        Console.WriteLine("Unknown texture type: " + textureReference.Key);
                        break;
                }
            }

            Materials.Add(name, mat);

            return mat.ColorTextureID;
        }

        private static int LoadTexture(string name, string currentFileName, Package currentPackage, int maxTextureMaxAnisotropy, TextureUnit textureUnit)
        {
            var textureResource = new Resource();

            if (!FileExtensions.LoadFileByAnyMeansNecessary(textureResource, name + "_c", currentFileName, currentPackage))
            {
                Console.WriteLine("File " + name + " not found");
                return 1;
            }

            var tex = (Texture)textureResource.Blocks[BlockType.DATA];

            Console.WriteLine("     Loading texture " + name + " " + tex.Flags);

            var id = GL.GenTexture();

            GL.ActiveTexture(textureUnit);
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

            if (tex.NumMipLevels < 2)
            {
                Console.WriteLine("Texture only has " + tex.NumMipLevels + " mipmap levels, should probably generate");
            }

            //var bmp = tex.GenerateBitmap();
            //System.Drawing.Imaging.BitmapData bmp_data = bmp.LockBits(new Rectangle(0, 0, tex.Width, tex.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            //GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp_data.Width, bmp_data.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bmp_data.Scan0);
            if (maxTextureMaxAnisotropy > 0)
            {
                GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, maxTextureMaxAnisotropy);
            }

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            // bmp.UnlockBits(bmp_data);
            return id;
        }
    }
}
