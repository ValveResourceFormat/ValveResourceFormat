using System.IO;
using System.Text;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;

#nullable disable

namespace ValveResourceFormat.IO
{
    public class ContentFile : IDisposable
    {
        /// <summary>
        /// Data can be null if the file is not meant to be written out.
        /// However it can still contain subfiles.
        /// </summary>
        public byte[] Data { get; set; }

        private string outFileName;

        /// <summary>
        /// Suggested output file name. Based on the resource name.
        /// </summary>
        public string FileName
        {
            get => outFileName;
            set
            {
                outFileName = value.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.InvariantCultureIgnoreCase)
                ? value[..^2]
                : value;
            }
        }

        /// <summary>
        /// Additional files that make up this content file. E.g. for a vtex, this would be the PNG files.
        /// </summary>
        public List<SubFile> SubFiles { get; init; } = [];

        /// <summary>
        /// Additional extracted resources. E.g. for a vmat, this would be the vtex files.
        /// You will want to extract the files if data is non null, and also their respective subfiles.
        /// You might want to ignore further extracts on these files—especially lone extracts,
        /// since this is most likely their most optimal extract context.
        /// </summary>
        public List<ContentFile> AdditionalFiles { get; init; } = [];

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
                foreach (var externalRef in AdditionalFiles)
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
        public HashSet<string> LoadedFilePaths { get; } = [];
        private readonly IFileLoader fileLoader;

        public Resource LoadFile(string file)
        {
            var resource = fileLoader.LoadFile(file);
            if (resource is not null)
            {
                lock (LoadedFilePaths)
                {
                    LoadedFilePaths.Add(file.Replace('\\', '/'));
                }
            }

            return resource;
        }

        public Resource LoadFileCompiled(string file) => LoadFile(string.Concat(file, GameFileLoader.CompiledFileSuffix));

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
        public static ContentFile Extract(Resource resource, IFileLoader fileLoader, IProgress<string> progress = null)
        {
            var contentFile = new ContentFile();

            switch (resource.ResourceType)
            {
                case ResourceType.Map:
                case ResourceType.World:
                    contentFile = new MapExtract(resource, fileLoader) { ProgressReporter = progress }.ToContentFile();
                    break;

                case ResourceType.Model:
                    contentFile = new ModelExtract(resource, fileLoader).ToContentFile();
                    break;

                case ResourceType.AnimationGraph:
                    contentFile = new AnimationGraphExtract(resource).ToContentFile();
                    break;

                case ResourceType.Panorama:
                case ResourceType.PanoramaScript:
                case ResourceType.PanoramaTypescript:
                case ResourceType.PanoramaVectorGraphic:
                    if (resource.DataBlock == null)
                    {
                        contentFile.Data = [];
                        break;
                    }

                    contentFile.Data = ((Panorama)resource.DataBlock).Data;
                    break;

                case ResourceType.Sound:
                    {
                        using var soundStream = ((Sound)resource.DataBlock).GetSoundStream();
                        soundStream.TryGetBuffer(out var buffer);
                        contentFile.Data = [.. buffer];

                        break;
                    }

                case ResourceType.Texture:
                    contentFile = new TextureExtract(resource).ToContentFile();
                    break;

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

                case ResourceType.NmClip:
                    {
                        var clip = (ResourceTypes.ModelAnimation2.AnimationClip)resource.DataBlock;

                        // todo: improve
                        var kv = new Serialization.KeyValues.KVObject(null);
                        var sourceFileName = Path.ChangeExtension(resource.FileName, ".dmx");
                        kv.AddProperty("m_sourceFilename", sourceFileName);
                        kv.AddProperty("m_animationSkeletonName ", clip.SkeletonName);
                        contentFile.Data = Encoding.UTF8.GetBytes(new Serialization.KeyValues.KV3File(kv).ToString());

                        contentFile.AddSubFile(sourceFileName, () =>
                        {
                            var skeleton = ResourceTypes.ModelAnimation.Skeleton.FromSkeletonData(((BinaryKV3)fileLoader.LoadFileCompiled(clip.SkeletonName).DataBlock!).Data);

                            return ModelExtract.ToDmxAnim(skeleton, [], new ResourceTypes.ModelAnimation.Animation(clip));
                        });

                        break;
                    }

                // These all just use ToString() and WriteText() to do the job
                case ResourceType.PanoramaStyle:
                case ResourceType.PanoramaLayout:
                case ResourceType.SoundEventScript:
                case ResourceType.SoundStackScript:
                    contentFile.Data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    break;

                case ResourceType.ChoreoSceneFileData:
                    contentFile = new ChoreoExtract(resource).ToContentFile();
                    break;

                default:
                    contentFile.Data = Encoding.UTF8.GetBytes(resource.DataBlock.ToString());
                    break;
            }

            return contentFile;
        }

        /// <summary>
        /// Extract content file from a non-resource stream.
        /// </summary>
        /// <param name="stream">Stream to be extracted or decompiled.</param>
        public static ContentFile ExtractNonResource(Stream stream, string fileName)
        {
            Span<byte> buffer = stackalloc byte[4];
            var read = stream.Read(buffer);
            stream.Seek(-read, SeekOrigin.Current);
            if (read != 4)
            {
                return null;
            }

            var magic = BitConverter.ToUInt32(buffer);

            return magic switch
            {
                FlexSceneFile.FlexSceneFile.MAGIC => new FlexSceneExtract(stream).ToContentFile(),
                ClosedCaptions.ClosedCaptions.MAGIC => new ClosedCaptionsExtract(stream, fileName).ToContentFile(),
                _ => null,
            };
        }

        public static bool TryExtractNonResource(Stream stream, string fileName, out ContentFile contentFile)
        {
            contentFile = ExtractNonResource(stream, fileName);
            return contentFile != null;
        }

        public static bool IsChildResource(Resource resource)
            => resource.EditInfo.SearchableUserData.GetProperty<long>("IsChildResource") == 1;

        public static string GetExtension(Resource resource)
        {
            // When updating this, don't forget to update ExtractProgressForm
            switch (resource.ResourceType)
            {
                case ResourceType.PanoramaLayout: return "xml";
                case ResourceType.PanoramaScript: return "js";
                case ResourceType.PanoramaTypescript: return "js";
                case ResourceType.PanoramaStyle: return "css";
                case ResourceType.PanoramaVectorGraphic: return "svg";

                case ResourceType.Texture:
                    {
                        if (IsChildResource(resource))
                        {
                            var texture = (Texture)resource.DataBlock;
                            return TextureExtract.GetImageOutputExtension(texture);
                        }

                        return "vtex";
                    }

                case ResourceType.Sound:
                    switch (((Sound)resource.DataBlock).SoundType)
                    {
                        case Sound.AudioFileType.MP3: return "mp3";
                        case Sound.AudioFileType.WAV: return "wav";
                    }

                    break;
            }

            return resource.ResourceType.GetExtension();
        }

        internal static void EnsurePopulatedStringToken(IFileLoader fileLoader)
        {
            if (fileLoader is GameFileLoader gameFileLoader)
            {
                gameFileLoader.EnsureStringTokenGameKeys();
            }
        }
    }
}
