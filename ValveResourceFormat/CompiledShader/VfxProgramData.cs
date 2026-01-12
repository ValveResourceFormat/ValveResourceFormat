using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Represents a compiled VFX shader program with all its associated data.
    /// </summary>
    public class VfxProgramData : IDisposable
    {
        /// <summary>
        /// Magic number for VCS2 files ("vcs2").
        /// </summary>
        public const int MAGIC = 0x32736376; // "vcs2"

        /// <summary>
        /// Gets or sets the binary reader for this shader data.
        /// </summary>
        public BinaryReader? DataReader { get; set; }

        private Stream? BaseStream;

        /// <summary>
        /// Gets the file path of the shader.
        /// </summary>
        public string? FilenamePath { get; private set; }

        /// <summary>
        /// Gets the shader name.
        /// </summary>
        public string? ShaderName { get; private set; }

        /// <summary>
        /// The resource this VfxProgramData was read from.
        /// Starting from VCS version 70.
        /// </summary>
        public Resource? Resource { get; private set; }

        /// <summary>
        /// Gets the VCS program type (e.g., vertex shader, pixel shader).
        /// </summary>
        public VcsProgramType VcsProgramType { get; private set; } = VcsProgramType.Undetermined;

        /// <summary>
        /// Gets the VCS platform type (e.g., PC, Vulkan).
        /// </summary>
        public VcsPlatformType VcsPlatformType { get; private set; } = VcsPlatformType.Undetermined;

        /// <summary>
        /// Gets the VCS shader model type (e.g., 4.0, 5.0, 6.0).
        /// </summary>
        public VcsShaderModelType VcsShaderModelType { get; private set; } = VcsShaderModelType.Undetermined;

        /// <summary>
        /// Gets the features header block for feature files.
        /// </summary>
        public FeaturesHeaderBlock? FeaturesHeader { get; private set; }

        /// <summary>
        /// Gets the VCS file format version.
        /// </summary>
        public int VcsVersion { get; private set; }

        /// <summary>
        /// Gets the file hash GUID.
        /// </summary>
        public Guid FileHash { get; private set; }

        /// <summary>
        /// Gets flags indicating which additional files are present.
        /// </summary>
        public VcsAdditionalFileFlags AdditionalFiles { get; private set; }

        /// <summary>
        /// Gets whether this is an S&amp;box shader.
        /// </summary>
        public bool IsSbox { get; init; }

        /// <summary>
        /// Gets the maximum variable source value (17 for up-to-date files, 14 for older files).
        /// </summary>
        public int VariableSourceMax { get; private set; }

        /// <summary>
        /// Gets the list of MD5 hashes.
        /// </summary>
        public List<Guid> HashesMD5 { get; } = [];

        /// <summary>
        /// Gets the static combo configuration array.
        /// </summary>
        public VfxCombo[] StaticComboArray { get; private set; } = [];

        /// <summary>
        /// Gets the static combo constraint rules.
        /// </summary>
        public VfxRule[] StaticComboRules { get; private set; } = [];

        /// <summary>
        /// Gets the dynamic combo configuration array.
        /// </summary>
        public VfxCombo[] DynamicComboArray { get; private set; } = [];

        /// <summary>
        /// Gets the dynamic combo constraint rules.
        /// </summary>
        public VfxRule[] DynamicComboRules { get; private set; } = [];

        /// <summary>
        /// Gets the variable descriptions array.
        /// </summary>
        public VfxVariableDescription[] VariableDescriptions { get; private set; } = [];

        /// <summary>
        /// Gets the texture channel processor configurations.
        /// </summary>
        public VfxTextureChannelProcessor[] TextureChannelProcessors { get; private set; } = [];

        /// <summary>
        /// Gets the external constant buffer descriptions.
        /// </summary>
        public ConstantBufferDescription[] ExtConstantBufferDescriptions { get; private set; } = [];

        /// <summary>
        /// Gets the vertex shader input signature elements.
        /// </summary>
        public VsInputSignatureElement[] VSInputSignatures { get; private set; } = [];

        /// <summary>
        /// Gets the static combo entries dictionary, organized by zframe ID.
        /// Zframe data contains key information needed to decompress and retrieve zframes.
        /// The sorted dictionary enables retrieval both by order (using ElementAt) and by ID (using indexer).
        /// </summary>
        /// <remarks>
        /// Zframe data assigned to the ZFrameDataDescription class are key pieces of
        /// information needed to decompress and retrieve zframes (to save processing zframes are only
        /// decompressed on request). This information is organised in zframesLookup by their zframeId's.
        /// Because the zframes appear in the file in ascending order, storing their data in a
        /// sorted dictionary enables retrieval based on the order they are seen; by calling
        /// zframesLookup.ElementAt(zframeIndex). We also retrieve them based on their id using
        /// zframesLookup[zframeId]. Both methods are useful in different contexts (be aware not to mix them up).
        /// </remarks>
        public SortedDictionary<long, VfxStaticComboVcsEntry> StaticComboEntries { get; } = [];

        /// <summary>
        /// Gets the static combo cache for efficiently retrieving parsed static combos.
        /// </summary>
        public StaticCache StaticComboCache { get; private set; }

        private ConfigMappingParams? dBlockConfigGen;

        /// <summary>
        /// Initializes a new instance of the <see cref="VfxProgramData"/> class.
        /// </summary>
        public VfxProgramData()
        {
            StaticComboCache = new StaticCache(this);
        }

        /// <summary>
        /// Releases streams, readers, and any cached combo data.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="VfxProgramData"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                BaseStream?.Dispose();
                BaseStream = null;

                DataReader?.Dispose();
                DataReader = null;

                Resource?.Dispose();
                Resource = null;

                StaticComboCache.Dispose();
            }
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filenamepath">The file to open and read.</param>
        public void Read(string filenamepath)
        {
            var stream = new FileStream(filenamepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            try
            {
                Read(filenamepath, stream);
                stream = null;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="filenamepath">The filename <see cref="string"/>.</param>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(string filenamepath, Stream input)
        {
            BaseStream = input;
            DataReader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            FilenamePath = filenamepath;

            var vcsMagicId = DataReader.ReadInt32();
            if (vcsMagicId == MAGIC)
            {
                VfxCreateFromVcs();
            }
            else
            {
                var resource = new Resource
                {
                    FileName = filenamepath
                };

                input.Position -= 4;
                resource.Read(input, false, leaveOpen: true);

                VfxCreateFromResource(resource);
            }
        }

        /// <summary>
        /// Prints a summary of the shader program data to the console or a provided writer.
        /// </summary>
        /// <param name="outputWriter">Optional indented text writer for output.</param>
        public void PrintSummary(IndentedTextWriter? outputWriter = null)
        {
            if (outputWriter == null)
            {
                using var output = new IndentedTextWriter();
                var consoleOutput = new PrintVcsFileSummary(this, output);
                Console.Write(output.ToString());
                return;
            }

            var fileSummary = new PrintVcsFileSummary(this, outputWriter);
        }

        private void VfxCreateFromVcs()
        {
            Debug.Assert(DataReader != null);
            Debug.Assert(FilenamePath != null);

            SetFileNameDerivedProperties(FilenamePath);

            VcsVersion = DataReader.ReadInt32();
            ThrowIfNotSupported(VcsVersion);

            var programTypesCount = 1 + (int)VcsProgramType.ComputeShader;

            if (VcsVersion >= 68) // Version 68 removed hull and domain shaders
            {
                programTypesCount -= 2;
            }

            if (VcsVersion < 63) // Version 63 added compute shaders
            {
                programTypesCount -= 1;
            }

            if (IsSbox)
            {
                var abiCurrentVersion = DataReader.ReadInt32();
                Debug.Assert(VcsVersion == 65);
                VcsVersion = 64;
            }

            // I guess the idea with this change is that they only store a flag for each shader type that is present
            // but they should have just changed all program types to be flags, instead of only the new ones
            if (VcsVersion >= 64)
            {
                AdditionalFiles = (VcsAdditionalFileFlags)DataReader.ReadUInt32();

                if ((AdditionalFiles & VcsAdditionalFileFlags.HasMeshShader) != 0)
                {
                    programTypesCount += 3;
                }
                else if ((AdditionalFiles & VcsAdditionalFileFlags.HasRaytracing) != 0)
                {
                    programTypesCount += 2;
                }
                else if ((AdditionalFiles & VcsAdditionalFileFlags.HasPixelShaderRenderState) != 0)
                {
                    programTypesCount += 1;
                }

                if (AdditionalFiles > VcsAdditionalFileFlags.HasMeshShader)
                {
                    throw new UnexpectedMagicException("Unexpected additional files", (int)AdditionalFiles, nameof(AdditionalFiles));
                }
            }

            UnserializeVfxProgramData(programTypesCount);
        }

        private void UnserializeVfxProgramData(int programTypesCount)
        {
            Debug.Assert(DataReader != null);

            if (VcsProgramType == VcsProgramType.Features)
            {
                FeaturesHeader = new FeaturesHeaderBlock(DataReader, programTypesCount);

                for (var i = 0; i < programTypesCount; i++)
                {
                    HashesMD5.Add(new Guid(DataReader.ReadBytes(16)));
                }
            }
            else
            {
                HashesMD5.Add(new Guid(DataReader.ReadBytes(16)));
            }

            FileHash = new Guid(DataReader.ReadBytes(16));

            VariableSourceMax = DataReader.ReadInt32();

            var staticCombosCount = DataReader.ReadInt32();
            StaticComboArray = new VfxCombo[staticCombosCount];
            for (var i = 0; i < staticCombosCount; i++)
            {
                VfxCombo nextSfBlock = new(DataReader, i);
                StaticComboArray[i] = nextSfBlock;
            }

            CalculateComboIds(StaticComboArray);

            var staticComboRulesCount = DataReader.ReadInt32();
            StaticComboRules = new VfxRule[staticComboRulesCount];
            for (var i = 0; i < staticComboRulesCount; i++)
            {
                VfxRule nextSfConstraintBlock = new(DataReader, i);
                StaticComboRules[i] = nextSfConstraintBlock;
            }

            var dynamicCombosCount = DataReader.ReadInt32();
            DynamicComboArray = new VfxCombo[dynamicCombosCount];
            for (var i = 0; i < dynamicCombosCount; i++)
            {
                VfxCombo nextDBlock = new(DataReader, i);
                DynamicComboArray[i] = nextDBlock;
            }

            CalculateComboIds(DynamicComboArray);

            var dynamicComboRulesCount = DataReader.ReadInt32();
            DynamicComboRules = new VfxRule[dynamicComboRulesCount];
            for (var i = 0; i < dynamicComboRulesCount; i++)
            {
                VfxRule nextDConstraintsBlock = new(DataReader, i);
                DynamicComboRules[i] = nextDConstraintsBlock;
            }

            // This is needed for the zframes to determine their source mapping
            // it must be instantiated after the D-blocks have been read
            dBlockConfigGen = new ConfigMappingParams(this, isDynamic: true);

            var variableDescriptionsCount = DataReader.ReadInt32();
            VariableDescriptions = new VfxVariableDescription[variableDescriptionsCount];
            for (var i = 0; i < variableDescriptionsCount; i++)
            {
                VfxVariableDescription nextParamBlock = new(DataReader, i, VcsVersion);
                VariableDescriptions[i] = nextParamBlock;
            }

            var textureChannelProcessorsCount = DataReader.ReadInt32();
            TextureChannelProcessors = new VfxTextureChannelProcessor[textureChannelProcessorsCount];
            for (var i = 0; i < textureChannelProcessorsCount; i++)
            {
                VfxTextureChannelProcessor nextChannelBlock = new(DataReader, i);
                TextureChannelProcessors[i] = nextChannelBlock;
            }

            var extConstantBufferDescriptionsCount = DataReader.ReadInt32();
            ExtConstantBufferDescriptions = new ConstantBufferDescription[extConstantBufferDescriptionsCount];
            for (var i = 0; i < extConstantBufferDescriptionsCount; i++)
            {
                ConstantBufferDescription nextBufferBlock = new(DataReader, i);
                ExtConstantBufferDescriptions[i] = nextBufferBlock;
            }

            if (VcsProgramType == VcsProgramType.Features || VcsProgramType == VcsProgramType.VertexShader)
            {
                var vsInputSignaturesCount = DataReader.ReadInt32();
                VSInputSignatures = new VsInputSignatureElement[vsInputSignaturesCount];
                for (var i = 0; i < vsInputSignaturesCount; i++)
                {
                    VsInputSignatureElement nextSymbolsBlock = new(DataReader, i);
                    VSInputSignatures[i] = nextSymbolsBlock;
                }
            }

            var combosCount = DataReader.ReadInt32();
            if (combosCount == 0)
            {
                // if zframes = 0 there's nothing more to do
                if (DataReader.BaseStream.Position != DataReader.BaseStream.Length)
                {
                    throw new ShaderParserException($"Reader contains more data, but EOF expected");
                }
                return;
            }

            var staticComboIds = new long[combosCount];

            for (var i = 0; i < combosCount; i++)
            {
                staticComboIds[i] = DataReader.ReadInt64();
            }

            for (var i = 0; i < combosCount; i++)
            {
                // CVfxStaticComboVcsEntry::Unserialize
                // This is a separate function because Valve has a flag to skip actually parsing the entries,
                // so if it's not requested, it just creates empty VfxStaticComboVcsEntry with the offset.
                var offset = DataReader.ReadInt32();

                var staticComboId = staticComboIds[i];
                StaticComboEntries.Add(staticComboId, new VfxStaticComboVcsEntry
                {
                    ParentProgramData = this,
                    StaticComboId = staticComboId,
                    FileOffset = offset,
                });
            }

            var offsetToEndOffile = DataReader.ReadInt32();
            if (offsetToEndOffile != (int)DataReader.BaseStream.Length)
            {
                throw new ShaderParserException($"Pointer to end of file expected, value read = {offsetToEndOffile}");
            }
        }

        internal void VfxCreateFromResource(Resource resource)
        {
            Resource = resource;
            VcsVersion = resource.Version;
            DataReader = resource.Reader;

            SetFileNameDerivedProperties(resource.FileName!);
            ThrowIfNotSupported(VcsVersion);

            var data = ((BinaryKV3)resource.DataBlock!).Data;

            if (VcsProgramType is VcsProgramType.Features)
            {
                FeaturesHeader = new FeaturesHeaderBlock(data);
                var programData = data.GetProperty<KVObject>("m_programData")!;
                UnserializeKV3ProgramData(programData);
                return;
            }

            UnserializeKV3ProgramData(data);
        }

        private void UnserializeKV3ProgramData(KVObject data)
        {
            var programHashes = data.GetArray("m_programHashes");
            foreach (var hashObject in programHashes)
            {
                var hashBytes = hashObject.GetProperty<byte[]>("m_nHashChar")!;
                Debug.Assert(hashBytes.Length == 16);
                HashesMD5.Add(new Guid(hashBytes));
            }

            FileHash = new Guid(data.GetProperty<KVObject>("m_variableDescriptionVersionHash")!.GetProperty<byte[]>("m_nHashChar")!);
            VariableSourceMax = data.GetInt32Property("m_nVariableSourceMax");

            var staticCombos = data.GetArray("m_staticComboArray");
            StaticComboArray = new VfxCombo[staticCombos.Length];
            for (var i = 0; i < staticCombos.Length; i++)
            {
                StaticComboArray[i] = new VfxCombo(staticCombos[i], i);
            }

            // CalculateComboIds(StaticComboArray);

            var staticComboRules = data.GetArray("m_staticComboRuleArray");
            StaticComboRules = new VfxRule[staticComboRules.Length];
            for (var i = 0; i < staticComboRules.Length; i++)
            {
                StaticComboRules[i] = new VfxRule(staticComboRules[i], i);
            }

            var dynamicCombos = data.GetArray("m_dynamicComboArray");
            DynamicComboArray = new VfxCombo[dynamicCombos.Length];
            for (var i = 0; i < dynamicCombos.Length; i++)
            {
                DynamicComboArray[i] = new VfxCombo(dynamicCombos[i], i);
            }

            // CalculateComboIds(DynamicComboArray);

            var dynamicComboRules = data.GetArray("m_dynamicComboRuleArray");
            DynamicComboRules = new VfxRule[dynamicComboRules.Length];
            for (var i = 0; i < dynamicComboRules.Length; i++)
            {
                DynamicComboRules[i] = new VfxRule(dynamicComboRules[i], i);
            }

            // This is needed for the zframes to determine their source mapping
            // it must be instantiated after the D-blocks have been read
            dBlockConfigGen = new ConfigMappingParams(this, isDynamic: true);

            var variableDescriptions = data.GetArray("m_variableDescriptionArray");
            VariableDescriptions = new VfxVariableDescription[variableDescriptions.Length];
            for (var i = 0; i < variableDescriptions.Length; i++)
            {
                VariableDescriptions[i] = new VfxVariableDescription(variableDescriptions[i], i);
            }

            var textureProcessors = data.GetArray("m_textureChannelProcessorArray");
            TextureChannelProcessors = new VfxTextureChannelProcessor[textureProcessors.Length];
            for (var i = 0; i < textureProcessors.Length; i++)
            {
                TextureChannelProcessors[i] = new VfxTextureChannelProcessor(textureProcessors[i], i);
            }

            var vsInputSignatureArray = data.GetArray("m_vsInputSignatureArray");
            VSInputSignatures = new VsInputSignatureElement[vsInputSignatureArray.Length];
            for (var i = 0; i < vsInputSignatureArray.Length; i++)
            {
                VSInputSignatures[i] = new VsInputSignatureElement(vsInputSignatureArray[i], i);
            }

            var staticComboData = data.GetArray("m_staticComboData");
            var staticComboIDs = data.GetIntegerArray("m_staticComboIDs");
            var byteCodeData = data.GetArray("m_byteCodeData");
            var attributes = data.GetArray("m_attributes").Select(a => new VfxShaderAttribute(a)).ToArray();

            for (var i = 0; i < staticComboData.Length; i++)
            {
                var staticComboId = staticComboIDs[i];
                var comboData = staticComboData[i];

                var entry = new VfxStaticComboVcsEntry
                {
                    ParentProgramData = this,
                    StaticComboId = staticComboId,
                    FileOffset = -1,
                    KVEntry = new(comboData, attributes, byteCodeData),
                };

                StaticComboEntries.Add(staticComboId, entry);
            }
        }

        private static void ThrowIfNotSupported(int vcsFileVersion)
        {
            const int earliest = 59;
            const int latest = 70;

            if (vcsFileVersion < earliest || vcsFileVersion > latest)
            {
                throw new UnexpectedMagicException($"Only VCS file versions {earliest} through {latest} are supported",
                    vcsFileVersion, nameof(vcsFileVersion));
            }
        }

        private void SetFileNameDerivedProperties(string fileName)
        {
            FilenamePath = fileName;
            var vcsFileProperties = ComputeVCSFileName(fileName);
            ShaderName = vcsFileProperties.ShaderName;
            VcsProgramType = vcsFileProperties.ProgramType;
            VcsPlatformType = vcsFileProperties.PlatformType;
            VcsShaderModelType = vcsFileProperties.ShaderModelType;
        }

        /// <summary>
        /// Retrieves and unserializes a static combo by its ID.
        /// </summary>
        /// <param name="id">The static combo ID.</param>
        /// <returns>The unserialized static combo data.</returns>
        public VfxStaticComboData GetStaticCombo(long id)
        {
            return StaticComboEntries[id].Unserialize();
        }

        /// <summary>
        /// Gets the configuration state for a dynamic block.
        /// </summary>
        /// <param name="blockId">The block ID.</param>
        /// <returns>The configuration state array.</returns>
        public int[] GetDBlockConfig(long blockId)
        {
            if (dBlockConfigGen == null)
            {
                throw new InvalidOperationException("DBlock configuration generator is not initialized.");
            }

            return dBlockConfigGen.GetConfigState(blockId);
        }

        /*
        public long CalcStaticComboIdFromValues(int[] configState)
        {
            Debug.Assert(configState.Length == StaticComboArray.Length);

            var comboId = 0L;
            var combos = StaticComboArray;
            var i = 0;

            for (; i < combos.Length - (combos.Length % 2); i += 2)
            {
                var v1 = configState[i] - combos[i].RangeMin;
                comboId += combos[i].CalculatedComboId * v1;

                var v2 = configState[i + 1] - combos[i + 1].RangeMin;
                comboId += combos[i + 1].CalculatedComboId * v2;
            }

            if (i < combos.Length)
            {
                var v = configState[i] - combos[i].RangeMin;
                comboId += combos[i].CalculatedComboId * v;
            }

            return comboId;
        }
        */

        private static void CalculateComboIds(VfxCombo[] combos)
        {
            if (combos.Length == 0)
            {
                return;
            }

            var comboPrev = combos[0];
            comboPrev.CalculatedComboId = 1;

            for (var i = 1; i < combos.Length; i++)
            {
                var combo = combos[i];
                combo.CalculatedComboId = comboPrev.CalculatedComboId;
                combo.CalculatedComboId *= comboPrev.RangeMax - comboPrev.RangeMin + 1;
                comboPrev = combo;
            }
        }
    }
}
