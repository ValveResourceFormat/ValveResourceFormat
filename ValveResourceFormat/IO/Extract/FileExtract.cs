using System.Buffers.Binary;
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

        private readonly List<Resource> OwnedResources = [];

        /// <summary>
        /// Disposes <paramref name="resource"/> along with this content file, for a resource its sub files read from.
        /// </summary>
        internal void TakeOwnership(Resource resource)
        {
            OwnedResources.Add(resource);
        }

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

                foreach (var resource in OwnedResources)
                {
                    resource.Dispose();
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
        public GameInfo GameInfo => fileLoader.GameInfo;

        /// <inheritdoc/>
        public IEnumerable<string> FindFilesByName(string fileName) => fileLoader.FindFilesByName(fileName);

        /// <inheritdoc/>
        public IReadOnlyDictionary<uint, string> GetSurfacePropertyNames() => fileLoader.GetSurfacePropertyNames();

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
        /// Gets whether a decompiled model leaves out its animations, see <see cref="ModelExtract.SkipAnimations"/>.
        /// </summary>
        public bool ModelSkipAnimations { get; init; }

        /// <summary>
        /// Gets whether a decompiled model leaves out its physics, see <see cref="ModelExtract.SkipPhysics"/>.
        /// </summary>
        public bool ModelSkipPhysics { get; init; }

        /// <summary>
        /// Gets whether a decompiled model keeps only its most detailed level of detail, see <see cref="ModelExtract.HighestLodOnly"/>.
        /// </summary>
        public bool ModelHighestLodOnly { get; init; }

        /// <summary>
        /// Gets the flags textures are decoded with. Defaults to <see cref="TextureCodec.Auto"/>.
        /// </summary>
        public TextureCodec TextureDecodeFlags { get; init; } = TextureCodec.Auto;

        /// <summary>
        /// Gets whether textures are extracted as their images alone, without a vtex file.
        /// </summary>
        public bool TextureImagesOnly { get; init; }

        /// <summary>
        /// Gets whether HDR cubemaps are written as one lat-long image, see <see cref="TextureExtract.LatLongCombineCubemap"/>. Defaults to true.
        /// </summary>
        public bool TextureLatLongCombineCubemap { get; init; } = true;

        /// <summary>
        /// Gets whether tools asset info files list only the paths of their assets, without their dependencies.
        /// </summary>
        public bool ToolsAssetInfoPathsOnly { get; init; }

        /// <summary>
        /// Gets the parameters compiled shaders are decompiled with. Defaults to <see cref="ShaderExtract.ShaderExtractParams.Export"/>.
        /// </summary>
        public ShaderExtract.ShaderExtractParams ShaderParams { get; init; } = ShaderExtract.ShaderExtractParams.Export;

        /// <summary>
        /// Gets the map of the content paths of the files an export writes somewhere else to the paths they are
        /// written at. References to them are rewritten, and so are the paths of the files written along with a
        /// resource. Null writes both as they are.
        /// </summary>
        public ContentPathMap? ReferenceMap { get; init; }
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

            var contentFile = ExtractResource(resource, fileLoader, progress, options);

            if (contentFile.FileName.Length == 0 && resource.FileName != null)
            {
                contentFile.FileName = ContentPaths.ChangeExtension(resource.FileName, GetExtension(resource));
            }

            if (options.ReferenceMap != null)
            {
                MapFileNames(contentFile, options.ReferenceMap);
            }

            return contentFile;
        }

        private static void MapFileNames(ContentFile contentFile, ContentPathMap referenceMap)
        {
            contentFile.FileName = referenceMap.Map(contentFile.FileName);

            foreach (var subFile in contentFile.SubFiles)
            {
                subFile.FileName = referenceMap.Map(subFile.FileName);
            }

            foreach (var additionalFile in contentFile.AdditionalFiles)
            {
                MapFileNames(additionalFile, referenceMap);
            }
        }

        private static ContentFile ExtractResource(Resource resource, IFileLoader fileLoader, IProgress<string>? progress, FileExtractOptions options)
        {
            var referenceMap = options.ReferenceMap;

            switch (resource.ResourceType)
            {
                case ResourceType.Map:
                case ResourceType.World:
                    return new MapExtract(resource, fileLoader) { ProgressReporter = progress, ReconstructSoftbody = options.ReconstructSoftbody, ReferenceMap = referenceMap }.ToContentFile();

                case ResourceType.Model:
                    return new ModelExtract(resource, fileLoader, referenceMap)
                    {
                        ProgressReporter = progress,
                        ReconstructSoftbody = options.ReconstructSoftbody,
                        SkipAnimations = options.ModelSkipAnimations,
                        SkipPhysics = options.ModelSkipPhysics,
                        HighestLodOnly = options.ModelHighestLodOnly,
                    }.ToContentFile();

                case ResourceType.AnimationGraph:
                {
                    using var animGraphExtract = new AnimationGraphExtract(resource, fileLoader) { ReferenceMap = referenceMap };
                    return animGraphExtract.ToContentFile();
                }

                case ResourceType.Panorama:
                case ResourceType.PanoramaScript:
                case ResourceType.PanoramaTypescript:
                case ResourceType.PanoramaVectorGraphic:
                    return ExtractPanorama(resource);

                case ResourceType.VMix:
                    return new VMixGraphExtract(resource, fileLoader) { ReferenceMap = referenceMap }.ToContentFile();

                case ResourceType.Sound when VMixGraphExtract.GetGraphSound(resource) != null:
                    return new VMixGraphExtract(resource, fileLoader) { ReferenceMap = referenceMap }.ToContentFile();

                case ResourceType.Sound:
                    return ExtractSound(resource, referenceMap);

                case ResourceType.Texture:
                    return new TextureExtract(resource)
                    {
                        DecodeFlags = options.TextureDecodeFlags,
                        IgnoreVtexFile = options.TextureImagesOnly,
                        LatLongCombineCubemap = options.TextureLatLongCombineCubemap,
                    }.ToContentFile();

                case ResourceType.Particle:
                    return CreateTextFile(resource.DataBlock!, referenceMap);

                case ResourceType.ParticleSnapshot:
                    return new SnapshotExtract(resource).ToContentFile();

                case ResourceType.Material:
                    return new MaterialExtract(resource, fileLoader) { ReferenceMap = referenceMap }.ToContentFile();

                case ResourceType.EntityLump:
                    return CreateTextFile(((EntityLump)resource.DataBlock!).ToEntityDumpString());

                case ResourceType.PostProcessing:
                    return ExtractPostProcessing(resource);

                case ResourceType.NmSkeleton:
                    return new NmSkeletonExtract(resource) { ReferenceMap = referenceMap }.ToContentFile();

                case ResourceType.NmClip:
                    return new NmClipExtract(resource, fileLoader) { ReferenceMap = referenceMap }.ToContentFile();

                // A graph variation holds no nodes, only the graph it binds and the skeleton and
                // clips bound to it; the base graph's document already carries every variation.
                case ResourceType.NmGraph:
                {
                    using var nmGraphExtract = new NmGraphExtract(resource, fileLoader) { ReferenceMap = referenceMap };
                    return nmGraphExtract.ToContentFile();
                }

                // These all just use ToString() and WriteText() to do the job
                case ResourceType.PanoramaStyle:
                case ResourceType.PanoramaLayout:
                case ResourceType.SoundEventScript:
                case ResourceType.SoundStackScript:
                    return CreateTextFile(resource.DataBlock!, referenceMap);

                case ResourceType.ChoreoSceneFileData:
                    return new ChoreoExtract(resource) { ReferenceMap = referenceMap }.ToContentFile();

                case ResourceType.PulseGraphDef:
                    return new PulseGraphExtract(resource) { ProgressReporter = progress, ReferenceMap = referenceMap }.ToContentFile();

                default:
                    if (resource.DataBlock is { } dataBlock)
                    {
                        return CreateTextFile(dataBlock, referenceMap);
                    }

                    return new ContentFile();
            }
        }

        private static ContentFile CreateTextFile(string text)
        {
            return new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(text),
            };
        }

        private static ContentFile CreateTextFile(Block block, ContentPathMap? referenceMap)
        {
            return new ContentFile
            {
                Data = block.ToUtf8Text(referenceMap),
            };
        }

        private static ContentFile ExtractPanorama(Resource resource)
        {
            var data = resource.DataBlock == null ? [] : ((Panorama)resource.DataBlock).Data;

            return new ContentFile
            {
                Data = data,
            };
        }

        private static ContentFile ExtractSound(Resource resource, ContentPathMap? referenceMap)
        {
            var contentFile = new ContentFile();

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
                    wrappedData.Add("VrfExportedSound", referenceMap.MapReferences(ctrlData.Data));
                    contentFile.AdditionalFiles.Add(new ContentFile
                    {
                        FileName = Path.ChangeExtension(resource.FileName, "vsnd") ?? "exported.vsnd",
                        Data = wrappedData.ToKV3Bytes()
                    });
                }
            }
            else if (resource.GetBlockByType(BlockType.CTRL) is BinaryKV3 ctrlData)
            {
                // TODO: We may want to cleanup m_vSound (recursively) since it contains random garbage if not actually used
                var wrappedData = KVObject.Collection();
                wrappedData.Add("VrfExportedSound", referenceMap.MapReferences(ctrlData.Data));
                contentFile.Data = wrappedData.ToKV3Bytes();
            }

            return contentFile;
        }

        private static ContentFile ExtractPostProcessing(Resource resource)
        {
            var postProcessing = (PostProcessing)resource.DataBlock!;
            var lutFileName = ContentPaths.ChangeExtension(resource.FileName!, "raw");
            var contentFile = new ContentFile
            {
                Data = postProcessing.ToValvePostProcessingBytes(preloadLookupTable: true, lutFileName: lutFileName),
            };

            if (postProcessing.HasColorCorrection())
            {
                contentFile.AddSubFile(
                    fileName: lutFileName,
                    extractMethod: postProcessing.GetRAWData
                );
            }

            return contentFile;
        }

        /// <summary>
        /// Extract content file from a non-resource stream.
        /// </summary>
        /// <param name="stream">Stream to be extracted or decompiled.</param>
        /// <param name="fileName">The file name for context.</param>
        /// <param name="options">Options that control how files are extracted.</param>
        /// <returns>The content file, or null when the file is not of a format that is extracted.</returns>
        public static ContentFile? ExtractNonResource(Stream stream, string fileName, FileExtractOptions? options = null)
        {
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
                ToolsAssetInfo.ToolsAssetInfo.MAGIC or ToolsAssetInfo.ToolsAssetInfo.MAGIC2 => ExtractToolsAssetInfo(stream, fileName, options?.ToolsAssetInfoPathsOnly ?? false),
                _ => null,
            };
        }

        /// <summary>
        /// Reads the first four bytes of a stream as a little-endian magic, keeping its position.
        /// </summary>
        /// <returns>The magic, or 0 when the stream is shorter.</returns>
        internal static uint PeekMagic(Stream stream)
        {
            Span<byte> magic = stackalloc byte[4];
            var read = stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false);
            stream.Seek(-read, SeekOrigin.Current);

            return read == magic.Length ? BinaryPrimitives.ReadUInt32LittleEndian(magic) : 0;
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

        private static ContentFile ExtractToolsAssetInfo(Stream stream, string fileName, bool pathsOnly)
        {
            var toolsAssetInfo = new ToolsAssetInfo.ToolsAssetInfo();
            toolsAssetInfo.Read(stream);

            return new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(toolsAssetInfo.ToDependencyText(pathsOnly)),
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
            using var glbStream = new MemoryStream();
            var resourceName = Path.GetFileNameWithoutExtension(fileName);
            exporter.Export(navMesh, resourceName, glbStream);

            return new ContentFile
            {
                Data = glbStream.ToArray(),
                FileName = ContentPaths.ChangeExtension(fileName, ".glb"),
            };
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

                    if (texture.IsRawAnyImage)
                    {
                        return TextureExtract.GetImageOutputExtension(texture);
                    }

                    return "vtex";
                }

                case ResourceType.Sound:
                    if (VMixGraphExtract.GetGraphSound(resource) != null)
                    {
                        return "vmix";
                    }

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
    }
}
