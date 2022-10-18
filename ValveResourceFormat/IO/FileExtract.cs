using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO
{
    public class ContentFile
    {
        public byte[] Data { get; set; }
        public List<ContentSubFile> SubFiles { get; init; } = new List<ContentSubFile>();

        public void AddSubFile(string fileName, Func<byte[]> extractFunction)
        {
            var subFile = new ContentSubFile
            {
                FileName = fileName,
                Extract = extractFunction
            };

            SubFiles.Add(subFile);
        }
    }

    public class ContentSubFile
    {
        public string FileName { get; set; }
        public Func<byte[]> Extract { get; set; }
    }

    public static class FileExtract
    {
        /// <summary>
        /// Extract content file from a compiled resource.
        /// </summary>
        /// <param name="resource">The resource to be extracted or decompiled.</param>
        public static ContentFile Extract(Resource resource)
        {
            var contentFile = new ContentFile();

            switch (resource.ResourceType)
            {
                case ResourceType.Map:
                    throw new NotImplementedException("Export the vwrld_c file if you are trying to export a map. vmap_c is simply a metadata file.");

                case ResourceType.Panorama:
                case ResourceType.PanoramaScript:
                case ResourceType.PanoramaTypescript:
                case ResourceType.PanoramaVectorGraphic:
                    contentFile.Data = ((Panorama)resource.DataBlock).Data;
                    break;

                case ResourceType.Sound:
                    {
                        using var soundStream = ((Sound)resource.DataBlock).GetSoundStream();
                        soundStream.TryGetBuffer(out var buffer);
                        contentFile.Data = buffer.ToArray();

                        break;
                    }

                case ResourceType.Texture:
                    {
                        var textureExtract = new TextureExtract(resource.FileName, (Texture)resource.DataBlock);
                        if (IsChildResource(resource))
                        {
                            contentFile.Data = textureExtract.ToPngImage();
                            break;
                        }

                        contentFile = textureExtract.ToContentFile();
                        break;
                    }

                case ResourceType.Particle:
                    contentFile.Data = Encoding.UTF8.GetBytes(((ParticleSystem)resource.DataBlock).ToString());
                    break;

                case ResourceType.Material:
                    contentFile.Data = Encoding.UTF8.GetBytes(((Material)resource.DataBlock).ToValveMaterial());
                    break;

                case ResourceType.EntityLump:
                    contentFile.Data = Encoding.UTF8.GetBytes(((EntityLump)resource.DataBlock).ToEntityDumpString());
                    break;

                case ResourceType.PostProcessing:
                    {
                        var lutFileName = Path.ChangeExtension(resource.FileName, "raw");
                        contentFile.Data = Encoding.UTF8.GetBytes(
                            ((PostProcessing)resource.DataBlock).ToValvePostProcessing(preloadLookupTable: true, lutFileName: lutFileName.Replace(Path.DirectorySeparatorChar, '/'))
                        );

                        contentFile.AddSubFile(
                            fileName: lutFileName,
                            extractFunction: () => ((PostProcessing)resource.DataBlock).GetRAWData()
                        );

                        break;
                    }

                // These all just use ToString() and WriteText() to do the job
                case ResourceType.PanoramaStyle:
                case ResourceType.PanoramaLayout:
                case ResourceType.SoundEventScript:
                case ResourceType.SoundStackScript:
                    contentFile.Data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    break;

                default:
                    {
                        if (resource.DataBlock is BinaryKV3 dataKv3)
                        {
                            // Wrap it around a KV3File object to get the header.
                            contentFile.Data = Encoding.UTF8.GetBytes(dataKv3.GetKV3File().ToString());
                        }
                        else
                        {
                            contentFile.Data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                        }

                        break;
                    }
            }

            return contentFile;
        }

        public static bool IsChildResource(Resource resource)
        {
            if (resource.EditInfo is ResourceEditInfo2 redi2)
            {
                return redi2.SearchableUserData.GetProperty<long>("IsChildResource") == 1;
            }

            var extraIntData = (Blocks.ResourceEditInfoStructs.ExtraIntData)resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.ExtraIntData];
            return extraIntData.List.Where(x => x.Name == "IsChildResource").Select(x => x.Value).FirstOrDefault() == 1;
        }

        public static string GetExtension(Resource resource)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.PanoramaLayout: return "xml";
                case ResourceType.PanoramaScript: return "js";
                case ResourceType.PanoramaTypescript: return "js";
                case ResourceType.PanoramaStyle: return "css";
                case ResourceType.PanoramaVectorGraphic: return "svg";

                case ResourceType.Texture:
                    if (IsChildResource(resource))
                    {
                        return "png";
                    }

                    break;

                case ResourceType.Sound:
                    switch (((Sound)resource.DataBlock).SoundType)
                    {
                        case Sound.AudioFileType.MP3: return "mp3";
                        case Sound.AudioFileType.WAV: return "wav";
                    }

                    break;
            }

            if (resource.ResourceType != ResourceType.Unknown)
            {
                var type = typeof(ResourceType).GetMember(resource.ResourceType.ToString())[0];
                return ((ExtensionAttribute)type.GetCustomAttributes(typeof(ExtensionAttribute), false)[0]).Extension;
            }

            return null;
        }
    }
}
