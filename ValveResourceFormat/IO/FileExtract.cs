using System;
using System.IO;
using System.Text;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO
{
    public static class FileExtract
    {
        public static Span<byte> Extract(Resource resource, GltfModelExporter exporter = null, string targetPath = null)
        {
            Span<byte> data;

            switch (resource.ResourceType)
            {
                case ResourceType.Panorama:
                case ResourceType.PanoramaScript:
                case ResourceType.PanoramaVectorGraphic:
                    data = ((Panorama)resource.DataBlock).Data;
                    break;

                case ResourceType.Sound:
                    {
                        var soundStream = ((Sound)resource.DataBlock).GetSoundStream();
                        soundStream.TryGetBuffer(out var buffer);
                        data = buffer;

                        break;
                    }

                case ResourceType.Texture:
                    {
                        var bitmap = ((Texture)resource.DataBlock).GenerateBitmap();

                        using var ms = new MemoryStream();
                        bitmap.PeekPixels().Encode(ms, SKEncodedImageFormat.Png, 100);

                        ms.TryGetBuffer(out var buffer);
                        data = buffer;

                        break;
                    }

                case ResourceType.Particle:
                    data = Encoding.UTF8.GetBytes(((ParticleSystem)resource.DataBlock).ToString());
                    break;

                case ResourceType.Mesh:
                case ResourceType.Model:
                case ResourceType.World:
                case ResourceType.WorldNode:
                {
                    if (exporter != null && targetPath != null)
                    {
                        data = Array.Empty<byte>();
                        switch(resource.ResourceType)
                        {
                            case ResourceType.Mesh:
                                exporter.ExportToFile(resource.FileName, targetPath, new Mesh(resource));
                                break;
                            case ResourceType.Model:
                                exporter.ExportToFile(resource.FileName, targetPath, (Model)resource.DataBlock);
                                break;
                            case ResourceType.WorldNode:
                                exporter.ExportToFile(resource.FileName, targetPath, (WorldNode)resource.DataBlock);
                                break;
                            case ResourceType.World:
                                exporter.ExportToFile(resource.FileName, targetPath, (World)resource.DataBlock);
                                break;
                        }
                    }
                    else
                    {
                        data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    }

                    break;
                }

                case ResourceType.Material:
                    data = ((Material)resource.DataBlock).ToValveMaterial();
                    break;

                case ResourceType.EntityLump:
                    data = Encoding.UTF8.GetBytes(((EntityLump)resource.DataBlock).ToEntityDumpString());
                    break;

                // These all just use ToString() and WriteText() to do the job
                case ResourceType.PanoramaStyle:
                case ResourceType.PanoramaLayout:
                case ResourceType.SoundEventScript:
                case ResourceType.SoundStackScript:
                    data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    break;

                default:
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
