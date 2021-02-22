using System;
using System.IO;
using System.Text;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO
{
    public static class FileExtract
    {
        public static Span<byte> Extract(Resource resource)
        {
            Span<byte> data;

            switch (resource.ResourceType)
            {
                case ResourceType.Panorama:
                case ResourceType.PanoramaLayout:
                case ResourceType.PanoramaScript:
                case ResourceType.PanoramaStyle:
                case ResourceType.PanoramaVectorGraphic:
                    data = ((Panorama)resource.DataBlock).Data;
                    break;

                case ResourceType.Sound:
                    data = ((Sound)resource.DataBlock).GetSound();

                    break;

                case ResourceType.Texture:
                    var bitmap = ((Texture)resource.DataBlock).GenerateBitmap();

                    using (var ms = new MemoryStream())
                    {
                        bitmap.PeekPixels().Encode(ms, SKEncodedImageFormat.Png, 100);

                        data = ms.ToArray();
                    }

                    break;

                case ResourceType.Particle:
                    data = Encoding.UTF8.GetBytes(((ParticleSystem)resource.DataBlock).ToString());
                    break;

                case ResourceType.Mesh:
                    // Wrap it around a KV3File object to get the header.
                    data = Encoding.UTF8.GetBytes(((BinaryKV3)resource.DataBlock).GetKV3File().ToString());
                    break;

                case ResourceType.Material:
                    data = ((Material)resource.DataBlock).ToValveMaterial();
                    break;

                // These all just use ToString() and WriteText() to do the job
                case ResourceType.SoundEventScript:
                case ResourceType.SoundStackScript:
                    data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    break;

                default:
                    Console.WriteLine("-- (I don't know how to dump this resource type)"); // TODO: What do we do with this
                    data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    break;
            }

            return data;
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
