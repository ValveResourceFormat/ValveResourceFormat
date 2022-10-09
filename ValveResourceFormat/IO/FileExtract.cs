using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkiaSharp;
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
        /// <param name="resource">The resource to be extracted/decompiled.</param>
        public static ContentFile Extract(Resource resource)
        {
            var extract = new ContentFile();

            switch (resource.ResourceType)
            {
                case ResourceType.Map:
                    throw new NotImplementedException("Export the vwrld_c file if you are trying to export a map. vmap_c is simply a metadata file.");

                case ResourceType.Panorama:
                case ResourceType.PanoramaScript:
                case ResourceType.PanoramaTypescript:
                case ResourceType.PanoramaVectorGraphic:
                    extract.Data = ((Panorama)resource.DataBlock).Data;
                    break;

                case ResourceType.Sound:
                    {
                        using var soundStream = ((Sound)resource.DataBlock).GetSoundStream();
                        soundStream.TryGetBuffer(out var buffer);
                        extract.Data = buffer.ToArray();

                        break;
                    }

                case ResourceType.Texture:
                    {

                        using var bitmap = ((Texture)resource.DataBlock).GenerateBitmap();
                        using var pixels = bitmap.PeekPixels();
                        using var png = pixels.Encode(SKPngEncoderOptions.Default);
                        extract.Data = png.ToArray();

                        break;
                    }

                case ResourceType.Particle:
                    extract.Data = Encoding.UTF8.GetBytes(((ParticleSystem)resource.DataBlock).ToString());
                    break;

                case ResourceType.Material:
                    extract.Data = Encoding.UTF8.GetBytes(((Material)resource.DataBlock).ToValveMaterial());
                    break;

                case ResourceType.EntityLump:
                    extract.Data = Encoding.UTF8.GetBytes(((EntityLump)resource.DataBlock).ToEntityDumpString());
                    break;

                // These all just use ToString() and WriteText() to do the job
                case ResourceType.PanoramaStyle:
                case ResourceType.PanoramaLayout:
                case ResourceType.SoundEventScript:
                case ResourceType.SoundStackScript:
                    extract.Data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    break;

                default:
                    {
                        if (resource.DataBlock is BinaryKV3 dataKv3)
                        {
                            // Wrap it around a KV3File object to get the header.
                            extract.Data = Encoding.UTF8.GetBytes(dataKv3.GetKV3File().ToString());
                        }
                        else
                        {
                            extract.Data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                        }

                        break;
                    }
            }

            return extract;
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
                case ResourceType.Texture: return "png";

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
