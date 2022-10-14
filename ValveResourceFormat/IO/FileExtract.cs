using System;
using System.Collections.Generic;
using System.Globalization;
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
                        var bitmap = ((Texture)resource.DataBlock).GenerateBitmap();
                        using var pixels = bitmap.PeekPixels();
                        using var png = pixels.Encode(SKPngEncoderOptions.Default);
                        contentFile.Data = png.ToArray();

                        var spriteSheetData = ((Texture)resource.DataBlock).GetSpriteSheetData();

                        if (spriteSheetData is null)
                        {
                            bitmap.Dispose();
                            break;
                        }

                        var mks = new StringBuilder();
                        var textureName = Path.GetFileNameWithoutExtension(resource.FileName);
                        var packmodeNonFlat = false;

                        var rects = new Dictionary<SKRect, string>();

                        for (var s = 0; s < spriteSheetData.Sequences.Length; s++)
                        {
                            var sequence = spriteSheetData.Sequences[s];

                            mks.AppendLine();

                            switch (sequence.NoColor, sequence.NoAlpha)
                            {
                                case (false, false):
                                    mks.AppendLine($"sequence {s}");
                                    break;

                                case (false, true):
                                    mks.AppendLine($"sequence-rgb {s}");
                                    packmodeNonFlat = true;
                                    break;

                                case (true, false):
                                    mks.AppendLine($"sequence-a {s}");
                                    packmodeNonFlat = true;
                                    break;

                                case (true, true):
                                    throw new Exception($"Unexpected combination of {nameof(sequence.NoColor)} and {nameof(sequence.NoAlpha)}");
                            }


                            if (!sequence.Clamp)
                            {
                                mks.AppendLine("LOOP");
                            }

                            for (var f = 0; f < sequence.Frames.Length; f++)
                            {
                                var frame = sequence.Frames[f];

                                var imageFileName = sequence.Frames.Length == 1
                                    ? $"{textureName}_seq{s}.png"
                                    : $"{textureName}_seq{s}_{f}.png";

                                // These images seem to be duplicates. So only extract the first one.
                                var image = frame.Images[0];
                                SKRectI imageRect = image.GetCroppedRect(bitmap.Width, bitmap.Height);

                                if (imageRect.IsEmpty)
                                {
                                    continue;
                                }

                                var displayTime = frame.DisplayTime;
                                if (sequence.Clamp && displayTime == 0)
                                {
                                    displayTime = 1;
                                }

                                var addForExtract = rects.TryAdd(imageRect, imageFileName);
                                mks.AppendLine($"frame {rects[imageRect]} {displayTime.ToString(CultureInfo.InvariantCulture)}");

                                if (!addForExtract)
                                {
                                    continue;
                                }

                                var ImageExtract = () =>
                                {
                                    using var subset = new SKBitmap();
                                    bitmap.ExtractSubset(subset, imageRect);

                                    using var pixels = subset.PeekPixels();
                                    using var png = pixels.Encode(SKPngEncoderOptions.Default);
                                    return png.ToArray();
                                };

                                contentFile.AddSubFile(imageFileName, ImageExtract);
                            }
                        }

                        if (packmodeNonFlat)
                        {
                            mks.Insert(0, "packmode rgb+a\n");
                        }

                        mks.Insert(0, "// Reconstructed by VRF - https://vrf.steamdb.info/\n\n");

                        contentFile.AddSubFile($"{textureName}.mks", () => Encoding.UTF8.GetBytes(mks.ToString()));
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
