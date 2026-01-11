using System.IO;
using System.Text;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Represents a content file extracted from a compiled resource.
    /// </summary>
    public class ContentFile : IDisposable
    {
        /// <summary>
        /// Data can be null if the file is not meant to be written out.
        /// However it can still contain subfiles.
        /// </summary>
        public byte[]? Data { get; set; }

        private string outFileName = string.Empty;

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

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        protected bool Disposed { get; private set; }

        /// <summary>
        /// Adds a sub-file to be extracted alongside the main content file.
        /// </summary>
        public void AddSubFile(string fileName, Func<byte[]> extractMethod)
        {
            var subFile = new SubFile
            {
                FileName = fileName,
                Extract = extractMethod
            };

            SubFiles.Add(subFile);
        }

        /// <summary>
        /// Releases resources used by this instance.
        /// </summary>
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

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Represents a sub-file that is part of a content file extraction.
    /// </summary>
    public class SubFile
    {
        /// <summary>
        /// Gets or sets the file name (relative to the content file).
        /// </summary>
        /// <remarks>
        /// This is relative to the content file.
        /// </remarks>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the extraction function that returns the file data.
        /// </summary>
        public virtual Func<byte[]>? Extract { get; set; }
    }

    /// <summary>
    /// Just a way to track previously loaded (and thus extracted) files.
    /// </summary>
    public class TrackingFileLoader : IFileLoader
    {
        /// <summary>
        /// Gets the set of file paths that have been loaded.
        /// </summary>
        public HashSet<string> LoadedFilePaths { get; } = [];
        private readonly IFileLoader fileLoader;

        /// <inheritdoc/>
        public Resource? LoadFile(string file)
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

        /// <inheritdoc/>
        public Resource? LoadFileCompiled(string file) => LoadFile(string.Concat(file, GameFileLoader.CompiledFileSuffix));

        /// <inheritdoc/>
        public ShaderCollection? LoadShader(string shaderName) => fileLoader.LoadShader(shaderName);

        /// <summary>
        /// Initializes a new instance of the <see cref="TrackingFileLoader"/> class.
        /// </summary>
        public TrackingFileLoader(IFileLoader fileLoader)
        {
            this.fileLoader = fileLoader;
        }
    }

    /// <summary>
    /// Provides methods for extracting content files from compiled resources.
    /// </summary>
    public static class FileExtract
    {
        /// <summary>
        /// Extract content file from a compiled resource.
        /// </summary>
        /// <param name="resource">The resource to be extracted or decompiled.</param>
        /// <param name="fileLoader">The file loader for resolving dependencies.</param>
        /// <param name="progress">Optional progress reporter.</param>
        public static ContentFile Extract(Resource resource, IFileLoader fileLoader, IProgress<string>? progress = null)
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
                    if (resource.DataBlock is Sound soundData)
                    {
                        using var soundStream = soundData.GetSoundStream();
                        soundStream.TryGetBuffer(out var buffer);
                        contentFile.Data = [.. buffer];

                        // TODO: Refactor this into a SoundExtract?
                        if (resource.GetBlockByType(BlockType.CTRL) is BinaryKV3 ctrlData)
                        {
                            var wrappedData = new KVObject("root");
                            wrappedData.AddProperty("VrfExportedSound", ctrlData.Data);
                            contentFile.AdditionalFiles.Add(new ContentFile
                            {
                                FileName = Path.GetFileNameWithoutExtension(resource.FileName) + ".vsnd",
                                Data = Encoding.UTF8.GetBytes(new KV3File(wrappedData).ToString())
                            });
                        }
                    }
                    else if (resource.GetBlockByType(BlockType.CTRL) is BinaryKV3 ctrlData)
                    {
                        // TODO: We may want to cleanup m_vSound (recursively) since it contains random garbage if not actually used
                        var wrappedData = new KVObject("root");
                        wrappedData.AddProperty("VrfExportedSound", ctrlData.Data);
                        contentFile.Data = Encoding.UTF8.GetBytes(new KV3File(wrappedData).ToString());
                    }

                    break;

                case ResourceType.Texture:
                    contentFile = new TextureExtract(resource).ToContentFile();
                    break;

                case ResourceType.Particle:
                    contentFile.Data = Encoding.UTF8.GetBytes(((ParticleSystem)resource.DataBlock!).ToString()!);
                    break;

                case ResourceType.ParticleSnapshot:
                    contentFile = new SnapshotExtract(resource).ToContentFile();
                    break;

                case ResourceType.Material:
                    contentFile = new MaterialExtract(resource, fileLoader).ToContentFile();
                    break;

                case ResourceType.SboxShader:
                    contentFile = new ShaderExtract(resource).ToContentFile();
                    break;

                case ResourceType.EntityLump:
                    contentFile.Data = Encoding.UTF8.GetBytes(((EntityLump)resource.DataBlock!).ToEntityDumpString());
                    break;

                case ResourceType.PostProcessing:
                    {
                        var lutFileName = Path.ChangeExtension(resource.FileName, "raw")!;
                        contentFile.Data = Encoding.UTF8.GetBytes(
                            ((PostProcessing)resource.DataBlock!).ToValvePostProcessing(preloadLookupTable: true, lutFileName: lutFileName.Replace(Path.DirectorySeparatorChar, '/'))
                        );

                        contentFile.AddSubFile(
                            fileName: Path.GetFileName(lutFileName)!,
                            extractMethod: () => ((PostProcessing)resource.DataBlock!).GetRAWData()
                        );

                        break;
                    }

                case ResourceType.NmSkeleton:
                    contentFile = new NmSkeletonExtract(resource).ToContentFile();
                    break;

                case ResourceType.NmClip:
                    contentFile = new NmClipExtract(resource, fileLoader).ToContentFile();
                    break;

                // These all just use ToString() and WriteText() to do the job
                case ResourceType.PanoramaStyle:
                case ResourceType.PanoramaLayout:
                case ResourceType.SoundEventScript:
                case ResourceType.SoundStackScript:
                    contentFile.Data = Encoding.UTF8.GetBytes(resource.DataBlock!.ToString()!);
                    break;

                case ResourceType.ChoreoSceneFileData:
                    contentFile = new ChoreoExtract(resource).ToContentFile();
                    break;

                default:
                    {
                        var dataBlock = resource.DataBlock;

                        if (dataBlock != null)
                        {
                            contentFile.Data = Encoding.UTF8.GetBytes(dataBlock.ToString()!);
                        }

                        break;
                    }
            }

            return contentFile;
        }

        /// <summary>
        /// Extract content file from a non-resource stream.
        /// </summary>
        /// <param name="stream">Stream to be extracted or decompiled.</param>
        /// <param name="fileName">The file name for context.</param>
        public static ContentFile? ExtractNonResource(Stream stream, string fileName)
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

        /// <summary>
        /// Attempts to extract content from a non-resource stream.
        /// </summary>
        public static bool TryExtractNonResource(Stream stream, string fileName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ContentFile? contentFile)
        {
            contentFile = ExtractNonResource(stream, fileName);
            return contentFile != null;
        }

        /// <summary>
        /// Determines whether the resource is a child resource.
        /// </summary>
        public static bool IsChildResource(Resource resource)
            => resource.EditInfo?.SearchableUserData?.GetProperty<long>("IsChildResource") == 1;

        /// <summary>
        /// Gets the appropriate file extension for the extracted resource.
        /// </summary>
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
                            var texture = (Texture)resource.DataBlock!;
                            return TextureExtract.GetImageOutputExtension(texture);
                        }

                        return "vtex";
                    }

                case ResourceType.Sound:
                    if (resource.DataBlock is Sound soundData)
                    {
                        switch (soundData.SoundType)
                        {
                            case Sound.AudioFileType.MP3: return "mp3";
                            case Sound.AudioFileType.WAV: return "wav";
                        }
                    }
                    else
                    {
                        return "vsnd";
                    }

                    break;
            }

            return resource.ResourceType.GetExtension() ?? "dat";
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
