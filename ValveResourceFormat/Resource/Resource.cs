using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat
{
    /// <summary>
    /// Represents a Valve resource.
    /// </summary>
    public class Resource : IDisposable
    {
        public const ushort KnownHeaderVersion = 12;

        private FileStream FileStream;

        /// <summary>
        /// Gets the binary reader. USE AT YOUR OWN RISK!
        /// It is exposed publicly to ease of reading the same file.
        /// </summary>
        /// <value>The binary reader.</value>
        public BinaryReader Reader { get; private set; }

        /// <summary>
        /// Gets or sets the file name this resource was parsed from.
        /// </summary>
        public string FileName { get; set; }

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
        public ResourceType ResourceType { get; set; }

        /// <summary>
        /// Gets the ResourceExtRefList block.
        /// </summary>
        public ResourceExtRefList ExternalReferences
        {
            get
            {
                return (ResourceExtRefList)GetBlockByType(BlockType.RERL);
            }
        }

        /// <summary>
        /// Gets the ResourceEditInfo block.
        /// </summary>
        public ResourceEditInfo EditInfo { get; private set; }

        /// <summary>
        /// Gets the ResourceIntrospectionManifest block.
        /// </summary>
        public ResourceIntrospectionManifest IntrospectionManifest
        {
            get
            {
                return (ResourceIntrospectionManifest)GetBlockByType(BlockType.NTRO);
            }
        }

        /// <summary>
        /// Gets the Vertex and Index Buffer block.
        /// </summary>
        public VBIB VBIB
        {
            get
            {
                return (VBIB)GetBlockByType(BlockType.VBIB);
            }
        }

        /// <summary>
        /// Gets the generic DATA block.
        /// </summary>
        public ResourceData DataBlock
        {
            get
            {
                return (ResourceData)GetBlockByType(BlockType.DATA);
            }
        }

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

                if (ResourceType == ResourceType.Sound)
                {
                    var data = (Sound)DataBlock;
                    size += data.StreamingDataSize;
                }
                else if (ResourceType == ResourceType.Texture)
                {
                    var data = (Texture)DataBlock;
                    size += (uint)data.CalculateTextureDataSize();
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

            if (FileSize == 0x55AA1234)
            {
                throw new InvalidDataException("Use ValvePak library to parse VPK files.\nSee https://github.com/ValveResourceFormat/ValvePak");
            }

            if (FileSize == ShaderFile.MAGIC)
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
                var blockType = Encoding.UTF8.GetString(Reader.ReadBytes(4));

                var position = Reader.BaseStream.Position;
                var offset = (uint)position + Reader.ReadUInt32();
                var size = Reader.ReadUInt32();
                Block block = null;

                if (size == 0)
                {
                    continue;
                }

                // Peek data to detect VKV3
                // Valve has deprecated NTRO as reported by resourceinfo.exe
                // TODO: Find a better way without checking against resource type
                if (size >= 4 && blockType == nameof(BlockType.DATA) && !IsHandledResourceType(ResourceType))
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

                Blocks.Add(block);

                switch (block.Type)
                {
                    case BlockType.REDI:
                    case BlockType.RED2:
                        block.Read(Reader, this);

                        EditInfo = (ResourceEditInfo)block;

                        // Try to determine resource type by looking at the compiler indentifiers
                        if (ResourceType == ResourceType.Unknown && EditInfo.Structs.TryGetValue(ResourceEditInfo.REDIStruct.SpecialDependencies, out var specialBlock))
                        {
                            var specialDeps = (SpecialDependencies)specialBlock;

                            foreach (var specialDep in specialDeps.List)
                            {
                                ResourceType = DetermineResourceTypeByCompilerIdentifier(specialDep);

                                if (ResourceType != ResourceType.Unknown)
                                {
                                    break;
                                }
                            }
                        }

                        // Try to determine resource type by looking at the input dependency if there is only one
                        if (ResourceType == ResourceType.Unknown && EditInfo.Structs.TryGetValue(ResourceEditInfo.REDIStruct.InputDependencies, out var inputBlock))
                        {
                            var inputDeps = (InputDependencies)inputBlock;

                            if (inputDeps.List.Count == 1)
                            {
                                ResourceType = ResourceTypeExtensions.DetermineByFileExtension(Path.GetExtension(inputDeps.List[0].ContentRelativeFilename));
                            }
                        }

                        break;

                    case BlockType.NTRO:
                        block.Read(Reader, this);
                        break;
                }

                Reader.BaseStream.Position = position + 8;
            }

            foreach (var block in Blocks)
            {
                if (block.Type is not BlockType.REDI and not BlockType.RED2 and not BlockType.NTRO)
                {
                    block.Read(Reader, this);
                }
            }

            if (ResourceType == ResourceType.Sound && ContainsBlockType(BlockType.CTRL)) // Version >= 5, but other ctrl-type sounds have version 0
            {
                var block = new Sound();
                block.ConstructFromCtrl(Reader, this);
                Blocks.Add(block);
            }

            var fullFileSize = FullFileSize;

            if (verifyFileSize && Reader.BaseStream.Length != fullFileSize)
            {
                if (ResourceType == ResourceType.Texture)
                {
                    var data = (Texture)DataBlock;

                    // TODO: We do not currently have a way of calculating buffer size for these types
                    // Texture.GenerateBitmap also just reads until end of the buffer
                    if (data.IsRawJpeg)
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
            return Blocks[index];
        }

        public Block GetBlockByType(BlockType type)
        {
            return Blocks.Find(b => b.Type == type);
        }

        public bool ContainsBlockType(BlockType type)
        {
            return Blocks.Exists(b => b.Type == type);
        }

        private Block ConstructFromType(string input)
        {
            return input switch
            {
                nameof(BlockType.DATA) => ConstructResourceType(),
                nameof(BlockType.REDI) => new ResourceEditInfo(),
                nameof(BlockType.RED2) => new ResourceEditInfo2(),
                nameof(BlockType.RERL) => new ResourceExtRefList(),
                nameof(BlockType.NTRO) => new ResourceIntrospectionManifest(),
                nameof(BlockType.VBIB) => new VBIB(),
                nameof(BlockType.VXVS) => new VXVS(),
                nameof(BlockType.SNAP) => new SNAP(),
                nameof(BlockType.MBUF) => new MBUF(),
                nameof(BlockType.CTRL) => new BinaryKV3(BlockType.CTRL),
                nameof(BlockType.MDAT) => new Mesh(BlockType.MDAT),
                nameof(BlockType.INSG) => new BinaryKV3(BlockType.INSG),
                nameof(BlockType.SrMa) => new BinaryKV3(BlockType.SrMa), // SourceMap
                nameof(BlockType.LaCo) => new BinaryKV3(BlockType.LaCo), // vxml ast
                nameof(BlockType.STAT) => new BinaryKV3(BlockType.STAT),
                nameof(BlockType.FLCI) => new BinaryKV3(BlockType.FLCI),
                nameof(BlockType.DSTF) => new BinaryKV3(BlockType.DSTF),
                nameof(BlockType.MRPH) => new Morph(BlockType.MRPH),
                nameof(BlockType.ANIM) => new KeyValuesOrNTRO(BlockType.ANIM, "AnimationResourceData_t"),
                nameof(BlockType.ASEQ) => new KeyValuesOrNTRO(BlockType.ASEQ, "SequenceGroupResourceData_t"),
                nameof(BlockType.AGRP) => new KeyValuesOrNTRO(BlockType.AGRP, "AnimationGroupResourceData_t"),
                nameof(BlockType.PHYS) => new PhysAggregateData(BlockType.PHYS),
                nameof(BlockType.DXBC) => new SboxShader(BlockType.DXBC),
                nameof(BlockType.SPRV) => new SboxShader(BlockType.SPRV),
                _ => throw new ArgumentException($"Unrecognized block type '{input}'"),
            };
        }

        private ResourceData ConstructResourceType()
        {
            switch (ResourceType)
            {
                case ResourceType.Panorama:
                case ResourceType.PanoramaScript:
                case ResourceType.PanoramaTypescript:
                case ResourceType.PanoramaVectorGraphic:
                    return new Panorama();

                case ResourceType.PanoramaStyle:
                    return new PanoramaStyle();

                case ResourceType.PanoramaLayout:
                    return new PanoramaLayout();

                case ResourceType.PanoramaDynamicImages:
                    return new PanoramaDynamicImages();

                case ResourceType.Sound:
                    return new Sound();

                case ResourceType.Texture:
                    return new Texture();

                case ResourceType.Model:
                    return new Model();

                case ResourceType.Morph:
                    return new Morph(BlockType.DATA);

                case ResourceType.World:
                    return new World();

                case ResourceType.WorldNode:
                    return new WorldNode();

                case ResourceType.EntityLump:
                    return new EntityLump();

                case ResourceType.Map:
                    return new Map();

                case ResourceType.Material:
                    return new Material();

                case ResourceType.SoundStackScript:
                    return new SoundStackScript();

                case ResourceType.Particle:
                    return new ParticleSystem();

                case ResourceType.PostProcessing:
                    return new PostProcessing();

                case ResourceType.ResourceManifest:
                    return new ResourceManifest();

                case ResourceType.ResponseRules:
                    return new ResponseRules();

                case ResourceType.SboxManagedResource:
                case ResourceType.ArtifactItem:
                case ResourceType.DotaHeroList:
                    return new Plaintext();

                case ResourceType.Shader:
                    return new SboxShader();

                case ResourceType.PhysicsCollisionMesh:
                    return new PhysAggregateData();

                case ResourceType.SmartProp:
                    return new SmartProp();

                case ResourceType.AnimationGraph:
                    return new AnimGraph();

                case ResourceType.Mesh:
                    return new Mesh(BlockType.DATA);

                case ResourceType.ChoreoSceneFileData:
                    return new ChoreoSceneFileData();
            }

            if (ContainsBlockType(BlockType.NTRO))
            {
                return new NTRO();
            }

            return new ResourceData();
        }

        private static bool IsHandledResourceType(ResourceType type)
        {
            return type == ResourceType.Model
                   || type == ResourceType.Mesh
                   || type == ResourceType.World
                   || type == ResourceType.WorldNode
                   || type == ResourceType.Particle
                   || type == ResourceType.Material
                   || type == ResourceType.EntityLump
                   || type == ResourceType.PhysicsCollisionMesh
                   || type == ResourceType.Morph
                   || type == ResourceType.SmartProp
                   || type == ResourceType.AnimationGraph
                   || type == ResourceType.PostProcessing;
        }

        private static ResourceType DetermineResourceTypeByCompilerIdentifier(SpecialDependencies.SpecialDependency input)
        {
            var identifier = input.CompilerIdentifier;

            if (identifier.StartsWith("Compile", StringComparison.Ordinal))
            {
                identifier = identifier.Remove(0, "Compile".Length);
            }

            // Special mappings and otherwise different identifiers
            switch (identifier)
            {
                case "Psf":
                    return ResourceType.ParticleSnapshot;
                case "AnimGroup":
                    return ResourceType.AnimationGroup;
                case "Animgraph":
                    return ResourceType.AnimationGraph;
                case "NmGraph":
                    return ResourceType.NmGraph;
                case "NmGraphVariation":
                    return ResourceType.NmGraphVariation;
                case "NmSkeleton":
                    return ResourceType.NmSkeleton;
                case "NmClip":
                    return ResourceType.NmClip;
                case "VPhysXData":
                    return ResourceType.PhysicsCollisionMesh;
                case "Font":
                    return ResourceType.BitmapFont;
                case "RenderMesh":
                    return ResourceType.Mesh;
                case "ChoreoSceneFileData":
                    return ResourceType.ChoreoSceneFileData;
                case "Panorama":
                    return input.String switch
                    {
                        "Panorama Style Compiler Version" => ResourceType.PanoramaStyle,
                        "Panorama Script Compiler Version" => ResourceType.PanoramaScript,
                        "Panorama Layout Compiler Version" => ResourceType.PanoramaLayout,
                        "Panorama Dynamic Images Compiler Version" => ResourceType.PanoramaDynamicImages,
                        _ => ResourceType.Panorama,
                    };
                case "VectorGraphic":
                    return ResourceType.PanoramaVectorGraphic;
                case "VCompMat":
                    return ResourceType.CompositeMaterial;
                case "VData":
                    return ResourceType.VData;
                case "ResponseRules":
                    return ResourceType.ResponseRules;
                case "DotaItem":
                    return ResourceType.ArtifactItem;
                case "CSGOItem":
                    return ResourceType.CSGOItem;
                case "CSGOEconItem":
                    return ResourceType.CSGOEconItem;
                case "PulseGraphDef":
                    return ResourceType.PulseGraphDef;
                case "SmartProp":
                    return ResourceType.SmartProp;
                case "GraphInstance":
                    return ResourceType.ProcessingGraphInstance;
                case "DotaHeroList":
                    return ResourceType.DotaHeroList;
                case "DotaPatchNotes":
                    return ResourceType.DotaPatchNotes;
                case "DotaVisualNovels":
                    return ResourceType.DotaVisualNovels;
                case "SBData":
                case "ManagedResourceCompiler": // This is without the "Compile" prefix
                    return ResourceType.SboxManagedResource;
            }

            if (Enum.TryParse(identifier, false, out ResourceType resourceType))
            {
                return resourceType;
            }

            return ResourceType.Unknown;
        }
    }
}
