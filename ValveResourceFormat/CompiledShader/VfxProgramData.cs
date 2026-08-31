using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
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
        /// The resource this <see cref="VfxProgramData"/> was read from.
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
        /// Gets the variable description version hash, shared by multiple different vcs files.
        /// </summary>
        public Guid VariableDescriptionVersionHash { get; private set; }

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
        /// Gets the MD5 hashes of each program.
        /// </summary>
        public List<Guid> ProgramHashes { get; } = [];

        /// <summary>
        /// Gets the static combo definitions. For <see cref="VcsProgramType.Features"/> programs
        /// this array holds the feature definitions instead.
        /// </summary>
        public VfxCombo[] StaticComboArray { get; private set; } = [];

        /// <summary>
        /// Gets the static combo constraint rules. For <see cref="VcsProgramType.Features"/> programs
        /// this array holds the feature rules instead.
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
        /// Gets the vertex shader input signatures.
        /// </summary>
        public VsInputSignature[] VsInputSignatures { get; private set; } = [];

        /// <summary>
        /// Gets the static combo entries, keyed by static combo ID.
        /// </summary>
        /// <remarks>
        /// Each entry holds the information needed to locate and decompress its static combo;
        /// to save processing, static combos are only decompressed on request.
        /// </remarks>
        public SortedDictionary<long, VfxStaticComboVcsEntry> StaticComboEntries { get; } = [];

        /// <summary>
        /// Gets the static combo cache for efficiently retrieving parsed static combos.
        /// </summary>
        public StaticComboCache StaticComboCache { get; private set; }

        private ComboConfigMapping? dynamicComboMapping;

        /// <summary>
        /// Initializes a new instance of the <see cref="VfxProgramData"/> class.
        /// </summary>
        public VfxProgramData()
        {
            StaticComboCache = new StaticComboCache(this);
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
        /// <param name="featuresProgram">Optional features program for resolving feature names.</param>
        public void PrintSummary(IndentedTextWriter? outputWriter = null, VfxProgramData? featuresProgram = null)
        {
            if (outputWriter == null)
            {
                using var output = new IndentedTextWriter();
                var consoleOutput = new PrintVcsFileSummary(this, output, featuresProgram);
                Console.Write(output.ToString());
                return;
            }

            var fileSummary = new PrintVcsFileSummary(this, outputWriter, featuresProgram);
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
                    ProgramHashes.Add(new Guid(DataReader.ReadBytes(16)));
                }
            }
            else
            {
                ProgramHashes.Add(new Guid(DataReader.ReadBytes(16)));
            }

            VariableDescriptionVersionHash = new Guid(DataReader.ReadBytes(16));

            VariableSourceMax = DataReader.ReadInt32();

            var staticCombosCount = DataReader.ReadInt32();
            StaticComboArray = new VfxCombo[staticCombosCount];
            for (var i = 0; i < staticCombosCount; i++)
            {
                StaticComboArray[i] = new VfxCombo(DataReader, i, VcsVersion);
            }

            CalculateComboIndexValues(StaticComboArray);

            var staticComboRulesCount = DataReader.ReadInt32();
            StaticComboRules = new VfxRule[staticComboRulesCount];
            for (var i = 0; i < staticComboRulesCount; i++)
            {
                StaticComboRules[i] = new VfxRule(DataReader, i);
            }

            var dynamicCombosCount = DataReader.ReadInt32();
            DynamicComboArray = new VfxCombo[dynamicCombosCount];
            for (var i = 0; i < dynamicCombosCount; i++)
            {
                DynamicComboArray[i] = new VfxCombo(DataReader, i, VcsVersion);
            }

            CalculateComboIndexValues(DynamicComboArray);

            var dynamicComboRulesCount = DataReader.ReadInt32();
            DynamicComboRules = new VfxRule[dynamicComboRulesCount];
            for (var i = 0; i < dynamicComboRulesCount; i++)
            {
                DynamicComboRules[i] = new VfxRule(DataReader, i);
            }

            // This is needed for the static combos to determine their source mapping
            // it must be instantiated after the dynamic combos have been read
            dynamicComboMapping = new ComboConfigMapping(this, isDynamic: true);

            var variableDescriptionsCount = DataReader.ReadInt32();
            VariableDescriptions = new VfxVariableDescription[variableDescriptionsCount];
            for (var i = 0; i < variableDescriptionsCount; i++)
            {
                VariableDescriptions[i] = new VfxVariableDescription(DataReader, i, VcsVersion);
            }

            var textureChannelProcessorsCount = DataReader.ReadInt32();
            TextureChannelProcessors = new VfxTextureChannelProcessor[textureChannelProcessorsCount];
            for (var i = 0; i < textureChannelProcessorsCount; i++)
            {
                TextureChannelProcessors[i] = new VfxTextureChannelProcessor(DataReader, i, VcsVersion);
            }

            var extConstantBufferDescriptionsCount = DataReader.ReadInt32();
            ExtConstantBufferDescriptions = new ConstantBufferDescription[extConstantBufferDescriptionsCount];
            for (var i = 0; i < extConstantBufferDescriptionsCount; i++)
            {
                ExtConstantBufferDescriptions[i] = new ConstantBufferDescription(DataReader, i);
            }

            if (VcsProgramType == VcsProgramType.Features || VcsProgramType == VcsProgramType.VertexShader)
            {
                var vsInputSignaturesCount = DataReader.ReadInt32();
                VsInputSignatures = new VsInputSignature[vsInputSignaturesCount];
                for (var i = 0; i < vsInputSignaturesCount; i++)
                {
                    VsInputSignatures[i] = new VsInputSignature(DataReader, i);
                }
            }

            var combosCount = DataReader.ReadInt32();
            if (combosCount == 0)
            {
                // if static combos = 0 there's nothing more to do
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

            var offsetToEndOfFile = DataReader.ReadInt32();
            if (offsetToEndOfFile != (int)DataReader.BaseStream.Length)
            {
                throw new ShaderParserException($"Pointer to end of file expected, value read = {offsetToEndOfFile}");
            }
        }

        internal void VfxCreateFromResource(Resource resource)
        {
            Resource = resource;
            VcsVersion = resource.Version;
            DataReader = resource.Reader;

            SetFileNameDerivedProperties(resource.FileName!);
            ThrowIfNotSupported(VcsVersion);

            KVObject data = ((BinaryKV3)resource.DataBlock!).Data;

            if (VcsProgramType is VcsProgramType.Features)
            {
                FeaturesHeader = new FeaturesHeaderBlock(data);
                var programData = data.GetSubCollection("m_programData")!;
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
                var hashBytes = hashObject.GetArray<byte>("m_nHashChar")!;
                Debug.Assert(hashBytes.Length == 16);
                ProgramHashes.Add(new Guid(hashBytes));
            }

            VariableDescriptionVersionHash = new Guid(data.GetSubCollection("m_variableDescriptionVersionHash")!.GetArray<byte>("m_nHashChar")!);
            VariableSourceMax = data.GetInt32Property("m_nVariableSourceMax");

            var staticCombos = data.GetArray("m_staticComboArray");
            StaticComboArray = new VfxCombo[staticCombos.Count];
            for (var i = 0; i < staticCombos.Count; i++)
            {
                StaticComboArray[i] = new VfxCombo(staticCombos[i], i, VcsVersion);
            }

            // CalculateComboIndexValues(StaticComboArray);

            var staticComboRules = data.GetArray("m_staticComboRuleArray");
            StaticComboRules = new VfxRule[staticComboRules.Count];
            for (var i = 0; i < staticComboRules.Count; i++)
            {
                StaticComboRules[i] = new VfxRule(staticComboRules[i], i);
            }

            var dynamicCombos = data.GetArray("m_dynamicComboArray");
            DynamicComboArray = new VfxCombo[dynamicCombos.Count];
            for (var i = 0; i < dynamicCombos.Count; i++)
            {
                DynamicComboArray[i] = new VfxCombo(dynamicCombos[i], i, VcsVersion);
            }

            // CalculateComboIndexValues(DynamicComboArray);

            var dynamicComboRules = data.GetArray("m_dynamicComboRuleArray");
            DynamicComboRules = new VfxRule[dynamicComboRules.Count];
            for (var i = 0; i < dynamicComboRules.Count; i++)
            {
                DynamicComboRules[i] = new VfxRule(dynamicComboRules[i], i);
            }

            // This is needed for the static combos to determine their source mapping
            // it must be instantiated after the dynamic combos have been read
            dynamicComboMapping = new ComboConfigMapping(this, isDynamic: true);

            var variableDescriptions = data.GetArray("m_variableDescriptionArray");
            VariableDescriptions = new VfxVariableDescription[variableDescriptions.Count];
            for (var i = 0; i < variableDescriptions.Count; i++)
            {
                VariableDescriptions[i] = new VfxVariableDescription(variableDescriptions[i], i);
            }

            var textureProcessors = data.GetArray("m_textureChannelProcessorArray");
            TextureChannelProcessors = new VfxTextureChannelProcessor[textureProcessors.Count];
            for (var i = 0; i < textureProcessors.Count; i++)
            {
                TextureChannelProcessors[i] = new VfxTextureChannelProcessor(textureProcessors[i], i);
            }

            var vsInputSignatureArray = data.GetArray("m_vsInputSignatureArray");
            VsInputSignatures = new VsInputSignature[vsInputSignatureArray.Count];
            for (var i = 0; i < vsInputSignatureArray.Count; i++)
            {
                VsInputSignatures[i] = new VsInputSignature(vsInputSignatureArray[i], i);
            }

            var staticComboData = data.GetArray("m_staticComboData");
            var staticComboIDs = data.GetIntegerArray("m_staticComboIDs");
            var byteCodeData = data.GetArray("m_byteCodeData");
            var attributes = data.GetArray("m_attributes").Select(a => new VfxShaderAttribute(a)).ToArray();

            for (var i = 0; i < staticComboData.Count; i++)
            {
                var staticComboId = staticComboIDs[i];
                var comboData = staticComboData[i];

                var entry = new VfxStaticComboVcsEntry
                {
                    ParentProgramData = this,
                    StaticComboId = staticComboId,
                    FileOffset = -1,
                    ResourceData = new(comboData, attributes, byteCodeData),
                };

                StaticComboEntries.Add(staticComboId, entry);
            }
        }

        private static void ThrowIfNotSupported(int vcsFileVersion)
        {
            const int earliest = 59;
            const int latest = 71;

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
        /// Retrieves and unserializes a static combo by its ID. This decompresses the combo on
        /// every call; use <see cref="StaticComboCache"/> for repeated access.
        /// </summary>
        /// <param name="id">The static combo ID.</param>
        /// <returns>The unserialized static combo data.</returns>
        public VfxStaticComboData GetStaticCombo(long id)
        {
            return StaticComboEntries[id].Unserialize();
        }

        /// <summary>
        /// Gets the configuration state for a dynamic combo.
        /// </summary>
        /// <param name="dynamicComboId">The dynamic combo ID.</param>
        /// <returns>The configuration state array.</returns>
        public int[] GetDynamicComboConfig(long dynamicComboId)
        {
            if (dynamicComboMapping == null)
            {
                throw new InvalidOperationException("Dynamic combo configuration mapping is not initialized.");
            }

            return dynamicComboMapping.GetConfigState(dynamicComboId);
        }

        /*
        public long CalcComboIdFromValues(int[] configState)
        {
            Debug.Assert(configState.Length == StaticComboArray.Length);

            var comboId = 0L;
            var combos = StaticComboArray;
            var i = 0;

            for (; i < combos.Length - (combos.Length % 2); i += 2)
            {
                var v1 = configState[i] - combos[i].RangeMin;
                comboId += combos[i].ComboIndexValue * v1;

                var v2 = configState[i + 1] - combos[i + 1].RangeMin;
                comboId += combos[i + 1].ComboIndexValue * v2;
            }

            if (i < combos.Length)
            {
                var v = configState[i] - combos[i].RangeMin;
                comboId += combos[i].ComboIndexValue * v;
            }

            return comboId;
        }
        */

        private static void CalculateComboIndexValues(VfxCombo[] combos)
        {
            if (combos.Length == 0)
            {
                return;
            }

            var comboPrev = combos[0];
            comboPrev.ComboIndexValue = 1;

            for (var i = 1; i < combos.Length; i++)
            {
                var combo = combos[i];
                combo.ComboIndexValue = comboPrev.ComboIndexValue;
                combo.ComboIndexValue *= comboPrev.RangeMax - comboPrev.RangeMin + 1;
                comboPrev = combo;
            }
        }
    }
}
