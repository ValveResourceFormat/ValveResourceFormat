using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO
{
    public class SpritesheetImageData
    {
        public class Sequence
        {
            public class Frame
            {
                public byte[] data { get; }

                public Frame(byte[] imageData)
                {
                    data = imageData;
                }
            }

            public Frame[] Frames { get; set; }

            public float FramesPerSecond { get; set; }
        }

        public Sequence[] Sequences { get; set; }
    }
    public static class FileExtract
    {
        public static Span<byte> Extract(Resource resource)
        {
            Span<byte> data;

            switch (resource.ResourceType)
            {
                case ResourceType.Map:
                    throw new NotImplementedException("Export the vwrld_c file if you are trying to export a map. vmap_c is simply a metadata file.");

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

                case ResourceType.Material:
                    data = Encoding.UTF8.GetBytes(((Material)resource.DataBlock).ToValveMaterial());
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
                    {
                        if (resource.DataBlock is BinaryKV3 dataKv3)
                        {
                            // Wrap it around a KV3File object to get the header.
                            data = Encoding.UTF8.GetBytes(dataKv3.GetKV3File().ToString());
                        }
                        else
                        {
                            data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                        }

                        break;
                    }
            }

            return data;
        }

        public static SpritesheetImageData ExtractTextureSheet(Texture texture)
        {
            var imageData = new SpritesheetImageData();
            var spriteSheet = texture.GetSpriteSheetData();
            if (spriteSheet == null) return imageData;

            var sequences = spriteSheet.Sequences;
            var bitmap = texture.GenerateBitmap();
            SKBitmap subset = new SKBitmap();

            var imageSequences = new SpritesheetImageData.Sequence[sequences.Length];

            for (int i = 0; i < sequences.Length; i++)
            {
                var sequence = sequences[i];
                if (sequence.Frames.Length < 1) continue;

                var imageFrames = new SpritesheetImageData.Sequence.Frame[sequence.Frames.Length];

                for (int j = 0; j < sequence.Frames.Length; j++)
                {
                    var frame = sequence.Frames[j];
                    SKRectI imageRect = frame.GetBoundingRect(bitmap.Width, bitmap.Height);
                    var success = bitmap.ExtractSubset(subset, imageRect);

                    if (success)
                    {
                        using var ms = new MemoryStream();
                        subset.PeekPixels().Encode(ms, SKEncodedImageFormat.Png, 100);
                        ms.TryGetBuffer(out var buffer);
                        imageFrames[j] = new SpritesheetImageData.Sequence.Frame(buffer.Array);
                    }
                }

                imageSequences[i] = new SpritesheetImageData.Sequence
                {
                    Frames = imageFrames,
                    FramesPerSecond = sequence.FramesPerSecond
                };
            }
            imageData.Sequences = imageSequences;

            return imageData;
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
