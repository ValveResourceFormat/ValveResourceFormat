using System;
using System.Collections.Generic;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;
using OpenTK.Graphics.OpenGL;
using System.IO;

namespace GUI.Types.Renderer
{
    class MaterialLoader
    {
        public static Dictionary<string, Material> materials = new Dictionary<string, Material>();

        public struct Material
        {
            public string name;
            public string shaderName;
            public int textureID;
            public Dictionary<string, int> intParams;
            public Dictionary<string, float> floatParams;
            public Dictionary<string, OpenTK.Vector4> vectorParams;
            public Dictionary<string, long> textureParams;
            //public Dictionary<string, ????> dynamicParams;
            //public Dictionary<string, ????> dynamicTextureParams;
            public Dictionary<string, int> intAttributes;
            public Dictionary<string, float> floatAttributes;
            public Dictionary<string, OpenTK.Vector4> vectorAttributes;
            //public Dictionary<string, long> textureAttributes;
            //public Dictionary<string, string> stringAttributes;
            //public string[] renderAttributesUsed; // ?
        }

        public static void loadMaterial(string name, string currentFileName, int maxTextureMaxAnisotropy)
        {
            Console.WriteLine("Loading material " + name);

            string path = Utils.FileExtensions.FindResourcePath(name, currentFileName);
            var mat = new Material();

            if (path == null)
            {
                Console.WriteLine("File " + name + " not found");
                return;
            }

            var resource = new Resource();
            resource.Read(path);

            string texturePath = Utils.FileExtensions.FindResourcePath(resource.ExternalReferences.ResourceRefInfoList[0].Name, currentFileName);

            if (texturePath == null)
            {
                Console.WriteLine("File " + resource.ExternalReferences.ResourceRefInfoList[0].Name + " not found");
                return;
            }

            var matData = (NTRO)resource.Blocks[BlockType.DATA];
            mat.name = ((NTROValue<string>)matData.Output["m_materialName"]).Value;
            mat.shaderName = ((NTROValue<string>)matData.Output["m_shaderName"]).Value;
            //mat.renderAttributesUsed = ((ValveResourceFormat.ResourceTypes.NTROSerialization.NTROValue<string>)matData.Output["m_renderAttributesUsed"]).Value; //TODO: string array?

            var intParams = (NTROArray)matData.Output["m_intParams"];
            mat.intParams = new Dictionary<string, int>();
            for (int i = 0; i < intParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)intParams[i]).Value;
                mat.intParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<int>)subStruct["m_nValue"]).Value);
            }

            var floatParams = (NTROArray)matData.Output["m_floatParams"];
            mat.floatParams = new Dictionary<string, float>();
            for (int i = 0; i < floatParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)floatParams[i]).Value;
                mat.floatParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<float>)subStruct["m_flValue"]).Value);
            }

            var vectorParams = (NTROArray)matData.Output["m_vectorParams"];
            mat.vectorParams = new Dictionary<string, OpenTK.Vector4>();
            for (int i = 0; i < vectorParams.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)vectorParams[i]).Value;
                var ntroVector = ((NTROValue<ValveResourceFormat.ResourceTypes.NTROSerialization.Vector4>)subStruct["m_value"]).Value;
                mat.vectorParams.Add(((NTROValue<string>)subStruct["m_name"]).Value, new OpenTK.Vector4(ntroVector.field0, ntroVector.field1, ntroVector.field2, ntroVector.field3));
            }

            var textureParams = (NTROArray)matData.Output["m_textureParams"];
            mat.textureParams = new Dictionary<string, long>();
            //TODO

            var dynamicParams = (NTROArray)matData.Output["m_dynamicParams"];
            var dynamicTextureParams = (NTROArray)matData.Output["m_dynamicTextureParams"];

            var intAttributes = (NTROArray)matData.Output["m_intAttributes"];
            mat.intAttributes = new Dictionary<string, int>();
            for (int i = 0; i < intAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)intAttributes[i]).Value;
                mat.intAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<int>)subStruct["m_nValue"]).Value);
            }

            var floatAttributes = (NTROArray)matData.Output["m_floatAttributes"];
            mat.floatAttributes = new Dictionary<string, float>();
            for (int i = 0; i < floatAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)floatAttributes[i]).Value;
                mat.floatAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, ((NTROValue<float>)subStruct["m_flValue"]).Value);
            }

            var vectorAttributes = (NTROArray)matData.Output["m_vectorAttributes"];
            mat.vectorAttributes = new Dictionary<string, OpenTK.Vector4>();
            for (int i = 0; i < vectorAttributes.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)vectorAttributes[i]).Value;
                var ntroVector = ((NTROValue<ValveResourceFormat.ResourceTypes.NTROSerialization.Vector4>)subStruct["m_value"]).Value;
                mat.vectorAttributes.Add(((NTROValue<string>)subStruct["m_name"]).Value, new OpenTK.Vector4(ntroVector.field0, ntroVector.field1, ntroVector.field2, ntroVector.field3));
            }

            var textureAttributes = (NTROArray)matData.Output["m_textureAttributes"];
            //TODO

            var stringAttributes = (NTROArray)matData.Output["m_stringAttributes"];
            //TODO

            var textureResource = new Resource();

            textureResource.Read(texturePath);

            var tex = (Texture)textureResource.Blocks[BlockType.DATA];

            mat.textureID = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, mat.textureID);

            BinaryReader textureReader = new BinaryReader(File.OpenRead(texturePath));
            textureReader.BaseStream.Position = tex.Offset + tex.Size;

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, tex.NumMipLevels - 1);

            int width = tex.Width / (int)Math.Pow(2.0, tex.NumMipLevels);
            int height = tex.Height / (int)Math.Pow(2.0, tex.NumMipLevels);

            int blockSize;
            PixelInternalFormat format;

            if (tex.Format.HasFlag(VTexFormat.DXT1))
            {
                Console.WriteLine("Texture is DXT1");
                blockSize = 8;
                format = PixelInternalFormat.CompressedRgbaS3tcDxt1Ext;
            }
            else if (tex.Format.HasFlag(VTexFormat.DXT5))
            {
                Console.WriteLine("Texture is DXT5");
                blockSize = 16;
                format = PixelInternalFormat.CompressedRgbaS3tcDxt5Ext;
            }
            else
            {
                throw new Exception("Unsupported texture format: " + tex.Format.ToString());
            }

            for (int i = tex.NumMipLevels - 1; i >= 0; i--)
            {
                if ((width *= 2) == 0) width = 1;
                if ((height *= 2) == 0) height = 1;

                int size = ((width + 3) / 4) * ((height + 3) / 4) * blockSize;

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

            materials.Add(name, mat);
        }

    }
}
