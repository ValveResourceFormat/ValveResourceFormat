using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO
{
    public class ContentFile : IDisposable
    {
        public byte[] Data { get; set; }

        /// <summary>
        /// Additional files that make up this content file.
        /// </summary>
        public List<SubFile> SubFiles { get; init; } = new List<SubFile>();

        /// <summary>
        /// Game file references that can be extracted as is.
        /// </summary>
        public List<string> GameFiles { get; init; } = new List<string>();

        /// <summary>
        /// External resource references handled by this extract.
        /// You will want to extract the respective subfiles, and ignore further extracts on the filenames.
        /// </summary>
        public Dictionary<string, ContentFile> ExternalRefsHandled { get; init; } = new();

        protected bool Disposed { get; private set; }

        public void AddSubFile(string fileName, Func<byte[]> extractMethod)
        {
            var subFile = new SubFile
            {
                FileName = fileName,
                Extract = extractMethod
            };

            SubFiles.Add(subFile);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed && disposing)
            {
                foreach (var externalRef in ExternalRefsHandled.Values)
                {
                    externalRef.Dispose();
                }
            }

            Disposed = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class SubFile
    {
        /// <remarks>
        /// This is relative to the content file.
        /// </remarks>
        public string FileName { get; set; }
        public virtual Func<byte[]> Extract { get; set; }
    }

    /// <summary>
    /// Just a way to track previously loaded (and thus extracted) files.
    /// </summary>
    public class TrackingFileLoader : IFileLoader
    {
        public HashSet<string> LoadedFilePaths { get; } = new HashSet<string>();
        private readonly IFileLoader fileLoader;

        public Resource LoadFile(string file)
        {
            var resource = fileLoader.LoadFile(file);
            if (resource is not null)
            {
                LoadedFilePaths.Add(file.Replace('\\', '/'));
            }

            return resource;
        }

        public ShaderCollection LoadShader(string shaderName) => fileLoader.LoadShader(shaderName);

        public TrackingFileLoader(IFileLoader fileLoader)
        {
            this.fileLoader = fileLoader;
        }
    }

    public static class FileExtract
    {
        /// <summary>
        /// Extract content file from a compiled resource.
        /// </summary>
        /// <param name="resource">The resource to be extracted or decompiled.</param>
        public static ContentFile Extract(Resource resource, IFileLoader fileLoader)
        {
            var contentFile = new ContentFile();

            switch (resource.ResourceType)
            {
                case ResourceType.Map:
                    contentFile = new MapExtract(resource, fileLoader).ToContentFile();
                    break;

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
                        if (IsChildResource(resource))
                        {
                            using var bitmap = ((Texture)resource.DataBlock).GenerateBitmap();
                            contentFile.Data = TextureExtract.ToPngImage(bitmap);
                            break;
                        }

                        var textureExtract = new TextureExtract(resource);
                        contentFile = textureExtract.ToContentFile();
                        break;
                    }

                case ResourceType.Particle:
                    contentFile.Data = Encoding.UTF8.GetBytes(((ParticleSystem)resource.DataBlock).ToString());
                    break;

                case ResourceType.Snap:
                    contentFile = new SnapshotExtract(resource).ToContentFile();
                    break;

                case ResourceType.Material:
                    contentFile = new MaterialExtract(resource, fileLoader).ToContentFile();
                    break;

                case ResourceType.Shader:
                    contentFile = new ShaderExtract(resource).ToContentFile();
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
                            fileName: Path.GetFileName(lutFileName),
                            extractMethod: () => ((PostProcessing)resource.DataBlock).GetRAWData()
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
            return extraIntData.List.FirstOrDefault(x => x.Name == "IsChildResource")?.Value == 1;
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

            return GetExtension(resource.ResourceType);
        }

        public static string GetExtension(ResourceType resourceType)
        {
            if (resourceType != ResourceType.Unknown)
            {
                var type = typeof(ResourceType).GetMember(resourceType.ToString())[0];
                return ((ExtensionAttribute)type.GetCustomAttributes(typeof(ExtensionAttribute), false)[0]).Extension;
            }

            return null;
        }
    }
}
