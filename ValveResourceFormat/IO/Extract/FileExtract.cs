using System.IO;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.TextureDecoders;

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
        /// Content path of the file, such as <c>models/props/crate.vmdl</c>. A trailing <c>_c</c> is removed.
        /// </summary>
        public string FileName
        {
            get => outFileName;
            set => outFileName = ContentPaths.TrimCompiledSuffix(value);
        }

        /// <summary>
        /// Files that make up this content file, such as the images of a vtex.
        /// </summary>
        public List<SubFile> SubFiles { get; init; } = [];

        /// <summary>
        /// Other resources extracted along with this one, such as the vtex files of a vmat. An entry without
        /// <see cref="Data"/> or sub files only marks its resource as handled, so an export skips it.
        /// </summary>
        public List<ContentFile> AdditionalFiles { get; init; } = [];

        /// <summary>
        /// Gets the sub file that stands for this content file when it has no <see cref="Data"/> of its own and one sub
        /// file, such as the image of a texture. It is written to the target file of an export, and printed in place of
        /// this content file.
        /// </summary>
        public SubFile? MainSubFile => Data == null && SubFiles.Count == 1 ? SubFiles[0] : null;

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
        /// Gets or sets the content path of the file, such as <c>models/props/crate_body.dmx</c>.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the extraction function that returns the file data.
        /// </summary>
        public virtual Func<byte[]>? Extract { get; set; }

        /// <summary>
        /// Gets or sets the pixel count of an image decoded from a compiled texture, or 0 for other files. Such images
        /// are named after their source image, so <see cref="ContentExporter"/> keeps the one with the most pixels when
        /// several claim the same path instead of renaming them.
        /// </summary>
        public long ImagePixelCount { get; set; }
    }

    /// <summary>
    /// Just a way to track previously loaded (and thus extracted) files.
    /// </summary>
    public class TrackingFileLoader : IFileLoader
    {
        /// <summary>
        /// Gets the set of file paths that have been loaded.
        /// </summary>
        public HashSet<string> LoadedFilePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly IFileLoader fileLoader;

        /// <inheritdoc/>
        public Resource? LoadFile(string file)
        {
            var resource = fileLoader.LoadFile(file);
            if (resource is not null)
            {
                lock (LoadedFilePaths)
                {
                    LoadedFilePaths.Add(ContentPaths.Normalize(file));
                }
            }

            return resource;
        }

        /// <inheritdoc/>
        public Resource? LoadFileCompiled(string file) => LoadFile(string.Concat(file, GameFileLoader.CompiledFileSuffix));

        /// <inheritdoc/>
        public ShaderCollection? LoadShader(string shaderName) => fileLoader.LoadShader(shaderName);

        /// <inheritdoc/>
        public Stream? GetFileStream(string file) => fileLoader.GetFileStream(file);

        /// <inheritdoc/>
        public IEnumerable<string> FindFilesByName(string fileName) => fileLoader.FindFilesByName(fileName);

        /// <summary>
        /// Initializes a new instance of the <see cref="TrackingFileLoader"/> class.
        /// </summary>
        public TrackingFileLoader(IFileLoader fileLoader)
        {
            this.fileLoader = fileLoader;
        }
    }

    /// <summary>
    /// Options for <see cref="FileExtract.Extract(Resource, IFileLoader, IProgress{string}?, FileExtractOptions)"/>.
    /// </summary>
    public sealed record FileExtractOptions
    {
        /// <summary>
        /// Gets whether models, and the models a map extracts, reconstruct their soft-body (cloth) physics. Defaults to true.
        /// </summary>
        public bool ReconstructSoftbody { get; init; } = true;

        /// <summary>
        /// Gets the flags textures are decoded with. Defaults to <see cref="TextureCodec.Auto"/>.
        /// </summary>
        public TextureCodec TextureDecodeFlags { get; init; } = TextureCodec.Auto;

        /// <summary>
        /// Gets whether tools asset info files list only the paths of their assets, without their dependencies.
        /// </summary>
        public bool ToolsAssetInfoPathsOnly { get; init; }
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
            => Extract(resource, fileLoader, progress, new FileExtractOptions());

        /// <summary>
        /// Extract content file from a compiled resource.
        /// </summary>
        /// <param name="resource">The resource to be extracted or decompiled.</param>
        /// <param name="fileLoader">The file loader for resolving dependencies.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="options">Options that control how resources are extracted.</param>
        public static ContentFile Extract(Resource resource, IFileLoader fileLoader, IProgress<string>? progress, FileExtractOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var contentFile = new ContentFile();

            switch (resource.ResourceType)
            {
                case ResourceType.Map:
                case ResourceType.World:
                    contentFile = new MapExtract(resource, fileLoader) { ProgressReporter = progress, ReconstructSoftbody = options.ReconstructSoftbody }.ToContentFile();
                    break;

                case ResourceType.Model:
                    contentFile = new ModelExtract(resource, fileLoader) { ProgressReporter = progress, ReconstructSoftbody = options.ReconstructSoftbody }.ToContentFile();
                    break;

                case ResourceType.AnimationGraph:
                {
                    using var animGraphExtract = new AnimationGraphExtract(resource, fileLoader);
                    contentFile = animGraphExtract.ToContentFile();
                }
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
                    if (resource.DataBlock is Sound { StreamingDataSize: > 0 } soundData)
                    {
                        contentFile.Data = soundData.GetSound();

                        // Lip-sync phoneme data; the compiler bakes this back in when the txt sits next to the source sound.
                        if (soundData.Sentence != null)
                        {
                            contentFile.AdditionalFiles.Add(new ContentFile
                            {
                                FileName = Path.ChangeExtension(resource.FileName, "txt") ?? "exported.txt",
                                Data = Encoding.UTF8.GetBytes(soundData.Sentence.ToValveSentence())
                            });
                        }

                        // TODO: Refactor this into a SoundExtract?
                        if (resource.GetBlockByType(BlockType.CTRL) is BinaryKV3 ctrlData)
                        {
                            var wrappedData = KVObject.Collection();
                            wrappedData.Add("VrfExportedSound", ctrlData.Data);
                            contentFile.AdditionalFiles.Add(new ContentFile
                            {
                                FileName = Path.ChangeExtension(resource.FileName, "vsnd") ?? "exported.vsnd",
                                Data = Encoding.UTF8.GetBytes(wrappedData.ToKV3String())
                            });
                        }
                    }
                    else if (resource.GetBlockByType(BlockType.CTRL) is BinaryKV3 ctrlData)
                    {
                        // TODO: We may want to cleanup m_vSound (recursively) since it contains random garbage if not actually used
                        var wrappedData = KVObject.Collection();
                        wrappedData.Add("VrfExportedSound", ctrlData.Data);
                        contentFile.Data = Encoding.UTF8.GetBytes(wrappedData.ToKV3String());
                    }

                    break;

                case ResourceType.Texture:
                    contentFile = new TextureExtract(resource) { DecodeFlags = options.TextureDecodeFlags }.ToContentFile();
                    TextureExtract.SetImagePixelCount(contentFile, (Texture)resource.DataBlock!);
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

                case ResourceType.EntityLump:
                    contentFile.Data = Encoding.UTF8.GetBytes(((EntityLump)resource.DataBlock!).ToEntityDumpString());
                    break;

                case ResourceType.PostProcessing:
                {
                    var lutFileName = ContentPaths.ChangeExtension(resource.FileName!, "raw");
                    contentFile.Data = Encoding.UTF8.GetBytes(
                        ((PostProcessing)resource.DataBlock!).ToValvePostProcessing(preloadLookupTable: true, lutFileName: lutFileName)
                    );

                    contentFile.AddSubFile(
                        fileName: lutFileName,
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

                // A graph variation holds no nodes, only the graph it binds and the skeleton and
                // clips bound to it; the base graph's document already carries every variation.
                case ResourceType.NmGraph:
                    using (var nmGraphExtract = new NmGraphExtract(resource, fileLoader))
                    {
                        contentFile = nmGraphExtract.ToContentFile();
                    }
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

            if (contentFile.FileName.Length == 0 && resource.FileName != null)
            {
                contentFile.FileName = ContentPaths.ChangeExtension(resource.FileName, GetExtension(resource));
            }

            return contentFile;
        }

        /// <summary>
        /// Extract content file from a non-resource stream.
        /// </summary>
        /// <param name="stream">Stream to be extracted or decompiled.</param>
        /// <param name="fileName">The file name for context.</param>
        public static ContentFile? ExtractNonResource(Stream stream, string fileName)
            => ExtractNonResource(stream, fileName, new FileExtractOptions());

        /// <summary>
        /// Extract content file from a non-resource stream.
        /// </summary>
        /// <param name="stream">Stream to be extracted or decompiled.</param>
        /// <param name="fileName">The file name for context.</param>
        /// <param name="options">Options that control how files are extracted.</param>
        /// <returns>The content file, or null when the file is not of a format that is extracted.</returns>
        public static ContentFile? ExtractNonResource(Stream stream, string fileName, FileExtractOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            // Fonts have no magic at the start of the file
            var extension = Path.GetExtension(fileName);

            if (extension.Equals(".vfont", StringComparison.OrdinalIgnoreCase))
            {
                return new ContentFile
                {
                    Data = new ValveFont.ValveFont().Read(stream),
                    FileName = ContentPaths.ChangeExtension(fileName, ".ttf"),
                };
            }

            if (extension.Equals(".uifont", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractUIFont(stream, fileName);
            }

            return PeekMagic(stream) switch
            {
                FlexSceneFile.FlexSceneFile.MAGIC => ExtractFlexScene(stream, fileName),
                ClosedCaptions.ClosedCaptions.MAGIC => new ClosedCaptionsExtract(stream, fileName).ToContentFile(),
                NavMesh.NavMeshFile.MAGIC => ExtractNavMesh(stream, fileName),
                NavMesh.NavSpaceFile.MAGIC => ExtractNavSpace(stream, fileName),
                MapFormats.GridNavFile.MAGIC => ExtractGridNav(stream, fileName),
                ToolsAssetInfo.ToolsAssetInfo.MAGIC or ToolsAssetInfo.ToolsAssetInfo.MAGIC2 => ExtractToolsAssetInfo(stream, fileName, options),
                _ => null,
            };
        }

        /// <summary>
        /// Whether <see cref="ExtractNonResource(Stream, string, FileExtractOptions)"/> extracts a file, from its name and
        /// the start of its contents, without extracting it.
        /// </summary>
        /// <param name="stream">The file's contents, its position is kept.</param>
        /// <param name="fileName">The file name.</param>
        public static bool CanExtractNonResource(Stream stream, string fileName)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(fileName);

            var extension = Path.GetExtension(fileName);

            if (extension.Equals(".vfont", StringComparison.OrdinalIgnoreCase) || extension.Equals(".uifont", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return PeekMagic(stream) is FlexSceneFile.FlexSceneFile.MAGIC
                or ClosedCaptions.ClosedCaptions.MAGIC
                or NavMesh.NavMeshFile.MAGIC
                or NavMesh.NavSpaceFile.MAGIC
                or MapFormats.GridNavFile.MAGIC
                or ToolsAssetInfo.ToolsAssetInfo.MAGIC
                or ToolsAssetInfo.ToolsAssetInfo.MAGIC2;
        }

        /// <summary>
        /// Reads the first four bytes of a stream as a little-endian magic, keeping its position.
        /// </summary>
        /// <returns>The magic, or 0 when the stream is shorter.</returns>
        internal static uint PeekMagic(Stream stream)
        {
            Span<byte> magic = stackalloc byte[4];
            var read = stream.Read(magic);
            stream.Seek(-read, SeekOrigin.Current);

            return read == magic.Length ? BitConverter.ToUInt32(magic) : 0;
        }

        private static ContentFile ExtractFlexScene(Stream stream, string fileName)
        {
            var contentFile = new FlexSceneExtract(stream).ToContentFile();
            contentFile.FileName = ContentPaths.ChangeExtension(fileName, ".txt");
            return contentFile;
        }

        private static ContentFile ExtractNavSpace(Stream stream, string fileName)
        {
            var navSpace = new NavMesh.NavSpaceFile();
            navSpace.Read(stream);

            return new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(navSpace.ToString()),
                FileName = ContentPaths.ChangeExtension(fileName, ".txt"),
            };
        }

        private static ContentFile ExtractToolsAssetInfo(Stream stream, string fileName, FileExtractOptions options)
        {
            var toolsAssetInfo = new ToolsAssetInfo.ToolsAssetInfo();
            toolsAssetInfo.Read(stream);

            return new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(toolsAssetInfo.ToDependencyText(options.ToolsAssetInfoPathsOnly)),
                FileName = ContentPaths.ChangeExtension(fileName, ".txt"),
            };
        }

        private static ContentFile ExtractGridNav(Stream stream, string fileName)
        {
            var gridNav = new MapFormats.GridNavFile();
            gridNav.Read(stream);

            return new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(gridNav.ToString()),
                FileName = ContentPaths.ChangeExtension(fileName, ".txt"),
            };
        }

        private static ContentFile ExtractUIFont(Stream stream, string fileName)
        {
            var data = new byte[stream.Length - stream.Position];
            stream.ReadExactly(data);

            var fontPackage = new ValveFont.UIFontFilePackage();
            fontPackage.Read(data);

            if (fontPackage.FontFiles.Count == 1)
            {
                var fontFile = fontPackage.FontFiles[0];

                return new ContentFile
                {
                    Data = fontFile.OpenTypeFontData,
                    FileName = ContentPaths.ChangeExtension(fileName, Path.GetExtension(fontFile.FileName)),
                };
            }

            var contentFile = new ContentFile
            {
                FileName = fileName,
            };

            var folder = Path.GetDirectoryName(fileName);

            foreach (var fontFile in fontPackage.FontFiles)
            {
                contentFile.AddSubFile(ContentPaths.Join(folder, fontFile.FileName), () => fontFile.OpenTypeFontData);
            }

            return contentFile;
        }

        private static ContentFile ExtractNavMesh(Stream stream, string fileName)
        {
            var navMesh = new NavMesh.NavMeshFile();
            navMesh.Read(stream);

            var exporter = new GltfModelExporter(new NullFileLoader());
            var glbStream = new MemoryStream();
            var resourceName = Path.GetFileNameWithoutExtension(fileName);
            exporter.Export(navMesh, resourceName, glbStream);

            return new ContentFile
            {
                Data = glbStream.ToArray(),
                FileName = ContentPaths.ChangeExtension(fileName, ".glb"),
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
            => resource.EditInfo?.SearchableUserData?.GetIntegerProperty("IsChildResource") == 1;

        /// <summary>
        /// Gets the appropriate file extension for the extracted resource.
        /// </summary>
        public static string GetExtension(Resource resource)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Texture:
                {
                    var texture = (Texture)resource.DataBlock!;

                    if (IsChildResource(resource) || texture.IsRawAnyImage)
                    {
                        return TextureExtract.GetImageOutputExtension(texture);
                    }

                    return "vtex";
                }

                case ResourceType.Sound:
                    if (resource.DataBlock is Sound { StreamingDataSize: > 0 } soundData)
                    {
                        switch (soundData.SoundType)
                        {
                            case Sound.AudioFileType.MP3: return "mp3";
                            case Sound.AudioFileType.WAV: return "wav";
                            case Sound.AudioFileType.AAC: return "aac";
                        }
                    }
                    else
                    {
                        return "vsnd";
                    }

                    break;
            }

            return GetExtension(resource.ResourceType);
        }

        /// <summary>
        /// Gets the file extension an extracted resource of the given type has, without inspecting its data.
        /// Textures and sounds report their source extension, use <see cref="GetExtension(Resource)"/> for the decoded one.
        /// </summary>
        public static string GetExtension(ResourceType resourceType)
        {
            return resourceType switch
            {
                ResourceType.PanoramaLayout => "xml",
                ResourceType.PanoramaScript => "js",
                ResourceType.PanoramaTypescript => "js",
                ResourceType.PanoramaStyle => "css",
                ResourceType.PanoramaVectorGraphic => "svg",
                _ => resourceType.GetExtension() ?? "dat",
            };
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
