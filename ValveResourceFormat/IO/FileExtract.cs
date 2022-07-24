using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO
{

    public ref struct ExtractedResource
    {
        public Span<byte> Data { get; set; }
        public List<ChildExtractedResource> Children { get; private set;}

        public ExtractedResource()
        {
            Data = new Span<byte>();
            Children = new List<ChildExtractedResource>();
        }
    }

    public class ChildExtractedResource
    {
        public byte[] Data { get; set; }
        public string Filename { get; set; }
    }

    public static class FileExtract
    {
        /// <summary>
        /// Extract a compiled Resource to source file(s).
        /// </summary>
        /// <param name="resource">The resource to be extracted/decompiled.</param>
        public static ExtractedResource Extract(Resource resource)
        {
            var exportFile = new ExtractedResource();

            switch (resource.ResourceType)
            {
                case ResourceType.Panorama:
                case ResourceType.PanoramaScript:
                case ResourceType.PanoramaVectorGraphic:
                    exportFile.Data = ((Panorama)resource.DataBlock).Data;
                    break;

                case ResourceType.Sound:
                    {
                        var soundStream = ((Sound)resource.DataBlock).GetSoundStream();
                        soundStream.TryGetBuffer(out var buffer);
                        exportFile.Data = buffer;

                        break;
                    }

                case ResourceType.Texture:
                    {
                        var bitmap = ((Texture)resource.DataBlock).GenerateBitmap();

                        using var ms = new MemoryStream();
                        bitmap.PeekPixels().Encode(ms, SKEncodedImageFormat.Png, 100);

                        ms.TryGetBuffer(out var buffer);
                        exportFile.Data = buffer;

                        break;
                    }

                case ResourceType.Particle:
                    exportFile.Data = Encoding.UTF8.GetBytes(((ParticleSystem)resource.DataBlock).ToString());
                    break;

                case ResourceType.Material:
                    exportFile.Data = ((Material)resource.DataBlock).ToValveMaterial();
                    break;

                case ResourceType.EntityLump:
                    exportFile.Data = Encoding.UTF8.GetBytes(((EntityLump)resource.DataBlock).ToEntityDumpString());
                    break;

                // These all just use ToString() and WriteText() to do the job
                case ResourceType.PanoramaStyle:
                case ResourceType.PanoramaLayout:
                case ResourceType.SoundEventScript:
                case ResourceType.SoundStackScript:
                    exportFile.Data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    break;

                default:
                    exportFile.Data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    break;
            }

            return exportFile;
        }

        public static string GetExtension(Resource resource)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.PanoramaLayout: return "xml";
                case ResourceType.PanoramaScript: return "js";
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
