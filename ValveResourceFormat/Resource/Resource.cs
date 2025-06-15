using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat
{
    /// <summary>
    /// Represents a Valve resource.
    /// </summary>
    public class Resource : IDisposable
    {
        public const ushort KnownHeaderVersion = 12;

        private FileStream? FileStream;

        /// <summary>
        /// Gets the binary reader. USE AT YOUR OWN RISK!
        /// It is exposed publicly to ease of reading the same file.
        /// </summary>
        /// <value>The binary reader.</value>
        public BinaryReader? Reader { get; private set; }

        /// <summary>
        /// Gets or sets the file name this resource was parsed from.
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// Gets the resource size.
        /// </summary>
        public uint FileSize { get; private set; }

        /// <summary>
        /// Gets the version of this resource, should be 12.
        /// </summary>
        public ushort HeaderVersion { get; private set; }

        /// <summary>
        /// Gets the file type version.
        /// </summary>
        public ushort Version { get; private set; }

        /// <summary>
        /// Gets the list of blocks this resource contains.
        /// </summary>
        public List<Block> Blocks { get; }

        /// <summary>
        /// Gets or sets the type of the resource.
        /// </summary>
        /// <value>The type of the resource.</value>
        public ResourceType ResourceType { get; private set; }

        /// <summary>
        /// Gets the ResourceEditInfo block.
        /// </summary>
        public ResourceEditInfo? EditInfo { get; private set; }

        /// <summary>
        /// Gets the ResourceExtRefList block.
        /// </summary>
        public ResourceExtRefList? ExternalReferences => (ResourceExtRefList?)GetBlockByType(BlockType.RERL);

        /// <summary>
        /// Gets the generic DATA block.
        /// </summary>
        public ResourceData? DataBlock => (ResourceData?)GetBlockByType(BlockType.DATA);

        /// <summary>
        /// Resource files have a FileSize in the metadata, however
        /// certain file types such as sounds have streaming audio data come
        /// after the resource file, and the size is specified within the DATA block.
        /// This property attemps to return the correct size.
        /// </summary>
        public uint FullFileSize
        {
            get
            {
                var size = FileSize;

                if (DataBlock == null)
                {
                    return size;
                }

                if (ResourceType == ResourceType.Sound && DataBlock is Sound dataSound)
                {
                    size += dataSound.StreamingDataSize;
                }
                else if (ResourceType == ResourceType.Texture && DataBlock is Texture dataTexture)
                {
                    size += (uint)dataTexture.CalculateTextureDataSize();
                }

                return size;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Resource"/> class.
        /// </summary>
        public Resource()
        {
            ResourceType = ResourceType.Unknown;
            Blocks = [];
        }

        /// <summary>
        /// Releases binary reader.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (FileStream != null)
                {
                    FileStream.Dispose();
                    FileStream = null;
                }

                if (Reader != null)
                {
                    Reader.Dispose();
                    Reader = null;
                }
            }
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            FileName = filename;
            FileStream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            Read(FileStream);
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        /// <param name="verifyFileSize">Whether to verify that the stream was correctly consumed.</param>
        public void Read(Stream input, bool verifyFileSize = true)
        {
            Reader = new BinaryReader(input);

            FileSize = Reader.ReadUInt32();

            if (FileSize == SteamDatabase.ValvePak.Package.MAGIC)
            {
                throw new InvalidDataException("Use ValvePak library to parse VPK files.\nSee https://github.com/ValveResourceFormat/ValvePak");
            }

            if (FileSize == VfxProgramData.MAGIC)
            {
                throw new InvalidDataException("Use ShaderFile() class to parse compiled shader files.");
            }

            HeaderVersion = Reader.ReadUInt16();

            if (HeaderVersion != KnownHeaderVersion)
            {
                throw new UnexpectedMagicException($"Unexpected header (expected {KnownHeaderVersion})", HeaderVersion, nameof(HeaderVersion));
            }

            if (FileName != null)
            {
                ResourceType = ResourceTypeExtensions.DetermineByFileExtension(Path.GetExtension(FileName));
            }

            Version = Reader.ReadUInt16();

            var blockOffset = Reader.ReadUInt32();
            var blockCount = Reader.ReadUInt32();

            Reader.BaseStream.Position += blockOffset - 8; // 8 is 2 uint32s we just read

            Blocks.EnsureCapacity((int)blockCount);

            for (var i = 0; i < blockCount; i++)
            {
                var blockType = (BlockType)Reader.ReadUInt32();

                var position = Reader.BaseStream.Position;
                var offset = (uint)position + Reader.ReadUInt32();
                var size = Reader.ReadUInt32();
                Block? block = null;

                if (size == 0)
                {
                    continue;
                }

                // Peek data to detect VKV3
                // Valve has deprecated NTRO as reported by resourceinfo.exe
                // TODO: Find a better way without checking against resource type
                if (size >= 4 && blockType == BlockType.DATA && !IsHandledResourceType(ResourceType))
                {
                    Reader.BaseStream.Position = offset;

                    var magic = Reader.ReadUInt32();

                    if (BinaryKV3.IsBinaryKV3(magic))
                    {
                        block = new BinaryKV3();
                    }
                    else if (magic == BinaryKV1.MAGIC)
                    {
                        block = new BinaryKV1();
                    }

                    Reader.BaseStream.Position = position;
                }

                block ??= ConstructFromType(blockType);

                block.Offset = offset;
                block.Size = size;
                block.Resource = this;

                Blocks.Add(block);

                if (block.Type is BlockType.NTRO)
                {
                    block.Read(Reader);
                }

                if (block.Type is BlockType.RED2 or BlockType.REDI)
                {
                    block.Read(Reader);
                    EditInfo = (ResourceEditInfo)block;

                    // Try to determine resource type by looking at the compiler indentifiers
                    // This must be done right after reading EditInfo because future DATA block
                    // will depend on knowing the resource type to construct the correct block in ConstructResourceType()
                    if (ResourceType == ResourceType.Unknown)
                    {
                        foreach (var specialDep in EditInfo.SpecialDependencies)
                        {
                            ResourceType = DetermineResourceTypeByCompilerIdentifier(specialDep);

                            if (ResourceType != ResourceType.Unknown)
                            {
                                break;
                            }
                        }

                        // Try to determine resource type by looking at the input dependency if there is only one
                        if (ResourceType == ResourceType.Unknown && EditInfo.InputDependencies.Count == 1)
                        {
                            ResourceType = ResourceTypeExtensions.DetermineByFileExtension(Path.GetExtension(EditInfo.InputDependencies[0].ContentRelativeFilename));
                        }
                    }
                }

                Reader.BaseStream.Position = position + 8;
            }

            foreach (var block in Blocks)
            {
                if (block.Type is not BlockType.REDI and not BlockType.RED2 and not BlockType.NTRO)
                {
                    block.Read(Reader);
                }
            }

            if (ResourceType == ResourceType.Sound && ContainsBlockType(BlockType.CTRL)) // Version >= 5, but other ctrl-type sounds have version 0
            {
                var block = new Sound
                {
                    Resource = this,
                };

                if (block.ConstructFromCtrl())
                {
                    Blocks.Add(block);
                }
            }

            var fullFileSize = FullFileSize;

            if (verifyFileSize && Reader.BaseStream.Length != fullFileSize)
            {
                if (ResourceType == ResourceType.Texture)
                {
                    var data = (Texture?)DataBlock;

                    // TODO: We do not currently have a way of calculating buffer size for these types
                    // Texture.GenerateBitmap also just reads until end of the buffer
                    if (data == null || data.IsRawJpeg)
                    {
                        return;
                    }

                    // TODO: Valve added null bytes after the png for whatever reason,
                    // so assume we have the full file if the buffer is bigger than the size we calculated
                    if (data.IsRawPng)
                    {
                        if (Reader.BaseStream.Length > fullFileSize)
                        {
                            return;
                        }
                    }
                }

                throw new InvalidDataException($"File size ({Reader.BaseStream.Length}) does not match size specified in file ({fullFileSize}) ({ResourceType}).");
            }
        }

        public Block GetBlockByIndex(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Blocks.Count);

            return Blocks[index];
        }

        public Block? GetBlockByType(BlockType type)
        {
            foreach (var block in Blocks)
            {
                if (block.Type == type)
                {
                    return block;
                }
            }

            return null;
        }

        public bool ContainsBlockType(BlockType type)
        {
            foreach (var block in Blocks)
            {
                if (block.Type == type)
                {
                    return true;
                }
            }
            return false;
        }

        private Block ConstructFromType(BlockType blockType)
        {
            return blockType switch
            {
                BlockType.DATA => ConstructResourceType(),
                BlockType.REDI => new ResourceEditInfo(),
                BlockType.RED2 => new ResourceEditInfo2(),
                BlockType.RERL => new ResourceExtRefList(),
                BlockType.NTRO => new ResourceIntrospectionManifest(),
                BlockType.VBIB => new VBIB(),
                BlockType.VXVS => new VoxelVisibility(),
                BlockType.SNAP => new ParticleSnapshot(),
                BlockType.MBUF => new MBUF(),
                BlockType.TBUF => new TBUF(),
                BlockType.MVTX => new MeshVertexBuffer(),
                BlockType.MIDX => new MeshIndexBuffer(),
                BlockType.CTRL => new BinaryKV3(BlockType.CTRL),
                BlockType.MDAT => new Mesh(BlockType.MDAT),
                BlockType.INSG => new BinaryKV3(BlockType.INSG),
                BlockType.SrMa => new BinaryKV3(BlockType.SrMa), // SourceMap
                BlockType.LaCo => new BinaryKV3(BlockType.LaCo), // vxml ast
                BlockType.STAT => new BinaryKV3(BlockType.STAT),
                BlockType.FLCI => new BinaryKV3(BlockType.FLCI),
                BlockType.DSTF => new BinaryKV3(BlockType.DSTF),
                BlockType.MRPH => new Morph(BlockType.MRPH),
                BlockType.ANIM => new KeyValuesOrNTRO(BlockType.ANIM, "AnimationResourceData_t"),
                BlockType.ASEQ => new KeyValuesOrNTRO(BlockType.ASEQ, "SequenceGroupResourceData_t"),
                BlockType.AGRP => new KeyValuesOrNTRO(BlockType.AGRP, "AnimationGroupResourceData_t"),
                BlockType.PHYS => new PhysAggregateData(BlockType.PHYS),
                BlockType.SPRV => new SboxShader(BlockType.SPRV),
                _ => throw new ArgumentException($"Unrecognized block type '{Encoding.ASCII.GetString(BitConverter.GetBytes((uint)blockType))}'"),
            };
        }

        private ResourceData ConstructResourceType()
        {
            return ResourceType switch
            {
                ResourceType.AnimationGraph => new AnimGraph(),
                ResourceType.ChoreoSceneFileData => new ChoreoSceneFileData(),
                ResourceType.EntityLump => new EntityLump(),
                ResourceType.Map => new Map(),
                ResourceType.Material => new Material(),
                ResourceType.Mesh => new Mesh(BlockType.DATA),
                ResourceType.Model => new Model(),
                ResourceType.Morph => new Morph(BlockType.DATA),
                ResourceType.Panorama or ResourceType.PanoramaScript or ResourceType.PanoramaTypescript or ResourceType.PanoramaVectorGraphic => new Panorama(),
                ResourceType.PanoramaDynamicImages => new PanoramaDynamicImages(),
                ResourceType.PanoramaLayout => new PanoramaLayout(),
                ResourceType.PanoramaStyle => new PanoramaStyle(),
                ResourceType.Particle => new ParticleSystem(),
                ResourceType.PhysicsCollisionMesh => new PhysAggregateData(),
                ResourceType.PostProcessing => new PostProcessing(),
                ResourceType.ResourceManifest => new ResourceManifest(),
                ResourceType.ResponseRules => new ResponseRules(),
                ResourceType.SboxManagedResource or ResourceType.ArtifactItem or ResourceType.DotaHeroList => new Plaintext(),
                ResourceType.Shader => new SboxShader(),
                ResourceType.SmartProp => new SmartProp(),
                ResourceType.Sound => new Sound(),
                ResourceType.SoundStackScript => new SoundStackScript(),
                ResourceType.Texture => new Texture(),
                ResourceType.World => new World(),
                ResourceType.WorldNode => new WorldNode(),
                _ => ContainsBlockType(BlockType.NTRO) ? new NTRO() : new ResourceData(),
            };
        }

        private static bool IsHandledResourceType(ResourceType type)
        {
            return type
                is ResourceType.Model
                or ResourceType.Mesh
                or ResourceType.World
                or ResourceType.WorldNode
                or ResourceType.Particle
                or ResourceType.Material
                or ResourceType.EntityLump
                or ResourceType.PhysicsCollisionMesh
                or ResourceType.Morph
                or ResourceType.SmartProp
                or ResourceType.AnimationGraph
                or ResourceType.PostProcessing;
        }

        private static ResourceType DetermineResourceTypeByCompilerIdentifier(SpecialDependency input)
        {
            var identifier = input.CompilerIdentifier.AsSpan();

            if (identifier.StartsWith("Compile", StringComparison.Ordinal))
            {
                identifier = identifier["Compile".Length..];
            }

            // Special mappings and otherwise different identifiers
            var resourceType = identifier switch
            {
                "Animgraph" => ResourceType.AnimationGraph,
                "AnimGroup" => ResourceType.AnimationGroup,
                "ChoreoSceneFileData" => ResourceType.ChoreoSceneFileData,
                "CSGOEconItem" => ResourceType.CSGOEconItem,
                "CSGOItem" => ResourceType.CSGOItem,
                "DotaHeroList" => ResourceType.DotaHeroList,
                "DotaItem" => ResourceType.ArtifactItem,
                "DotaPatchNotes" => ResourceType.DotaPatchNotes,
                "DotaVisualNovels" => ResourceType.DotaVisualNovels,
                "Font" => ResourceType.BitmapFont,
                "GraphInstance" => ResourceType.ProcessingGraphInstance,
                "NmClip" => ResourceType.NmClip,
                "NmGraph" => ResourceType.NmGraph,
                "NmGraphVariation" => ResourceType.NmGraphVariation,
                "NmSkeleton" => ResourceType.NmSkeleton,
                "Panorama" => input.String switch
                {
                    "Panorama Style Compiler Version" => ResourceType.PanoramaStyle,
                    "Panorama Script Compiler Version" => ResourceType.PanoramaScript,
                    "Panorama Layout Compiler Version" => ResourceType.PanoramaLayout,
                    "Panorama Dynamic Images Compiler Version" => ResourceType.PanoramaDynamicImages,
                    _ => ResourceType.Panorama,
                },
                "Psf" => ResourceType.ParticleSnapshot,
                "PulseGraphDef" => ResourceType.PulseGraphDef,
                "RenderMesh" => ResourceType.Mesh,
                "ResponseRules" => ResourceType.ResponseRules,
                "SBData" or "ManagedResourceCompiler" => ResourceType.SboxManagedResource,
                "SmartProp" => ResourceType.SmartProp,
                "TypeScript" => ResourceType.PanoramaTypescript,
                "VCompMat" => ResourceType.CompositeMaterial,
                "VData" => ResourceType.VData,
                "VectorGraphic" => ResourceType.PanoramaVectorGraphic,
                "VPhysXData" => ResourceType.PhysicsCollisionMesh,
                _ => ResourceType.Unknown,
            };

            if (resourceType == ResourceType.Unknown && Enum.TryParse(identifier, false, out ResourceType resourceTypeParsed))
            {
                return resourceTypeParsed;
            }

            return resourceType;
        }
    }
}
