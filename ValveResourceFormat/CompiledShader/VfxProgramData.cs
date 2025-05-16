using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    public class VfxProgramData : IDisposable
    {
        public const int MAGIC = 0x32736376; // "vcs2"

        public BinaryReader DataReader { get; set; }
        private Stream BaseStream;

        public string FilenamePath { get; private set; }
        public string ShaderName { get; private set; }
        public VcsProgramType VcsProgramType { get; private set; } = VcsProgramType.Undetermined;
        public VcsPlatformType VcsPlatformType { get; private set; } = VcsPlatformType.Undetermined;
        public VcsShaderModelType VcsShaderModelType { get; private set; } = VcsShaderModelType.Undetermined;
        public FeaturesHeaderBlock FeaturesHeader { get; private set; }
        public int VcsVersion { get; private set; }
        public Guid FileHash { get; private set; }
        public VcsAdditionalFileFlags AdditionalFiles { get; private set; }
        public bool IsSbox { get; init; }
        public int VariableSourceMax { get; private set; } // 17 for all up to date files. 14 seen in old test files
        public List<(Guid, string)> HashesMD5 { get; } = [];
        public VfxCombo[] StaticComboArray { get; private set; } = [];
        public VfxRule[] StaticComboRules { get; private set; } = [];
        public VfxCombo[] DynamicComboArray { get; private set; } = [];
        public VfxRule[] DynamicComboRules { get; private set; } = [];
        public VfxVariableDescription[] VariableDescriptions { get; private set; } = [];
        public VfxTextureChannelProcessor[] TextureChannelProcessors { get; private set; } = [];
        public ConstantBufferDescription[] ExtConstantBufferDescriptions { get; private set; } = [];
        public VsInputSignatureElement[] VSInputSignatures { get; private set; } = [];

        // Zframe data assigned to the ZFrameDataDescription class are key pieces of
        // information needed to decompress and retrieve zframes (to save processing zframes are only
        // decompressed on request). This information is organised in zframesLookup by their zframeId's.
        // Because the zframes appear in the file in ascending order, storing their data in a
        // sorted dictionary enables retrieval based on the order they are seen; by calling
        // zframesLookup.ElementAt(zframeIndex). We also retrieve them based on their id using
        // zframesLookup[zframeId]. Both methods are useful in different contexts (be aware not to mix them up).
        public SortedDictionary<long, VfxStaticComboVcsEntry> StaticComboEntries { get; } = [];
        public StaticCache StaticComboCache { get; private set; }
        private ConfigMappingParams dBlockConfigGen;

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
                if (BaseStream != null)
                {
                    BaseStream.Dispose();
                    BaseStream = null;
                }

                if (DataReader != null)
                {
                    DataReader.Dispose();
                    DataReader = null;
                }

                StaticComboCache?.Dispose();
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
            VfxCreateFromVcs();
            StaticComboCache = new StaticCache(this);
        }

        public void PrintSummary(PrintVcsFileSummary.HandleOutputWrite OutputWriter = null)
        {
            var fileSummary = new PrintVcsFileSummary(this, OutputWriter);
        }

        private void VfxCreateFromVcs()
        {
            var vcsFileProperties = ComputeVCSFileName(FilenamePath);
            ShaderName = vcsFileProperties.ShaderName;
            VcsProgramType = vcsFileProperties.ProgramType;
            VcsPlatformType = vcsFileProperties.PlatformType;
            VcsShaderModelType = vcsFileProperties.ShaderModelType;

            var vcsMagicId = DataReader.ReadInt32();
            UnexpectedMagicException.Assert(vcsMagicId == MAGIC, vcsMagicId);

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
            if (VcsProgramType == VcsProgramType.Features)
            {
                FeaturesHeader = new FeaturesHeaderBlock(DataReader, programTypesCount);

                for (var i = 0; i < programTypesCount; i++)
                {
                    HashesMD5.Add((new Guid(DataReader.ReadBytes(16)), $"// {i}"));
                }
            }
            else
            {
                HashesMD5.Add((new Guid(DataReader.ReadBytes(16)), string.Empty));
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

        private static void ThrowIfNotSupported(int vcsFileVersion)
        {
            const int earliest = 62;
            const int latest = 68;

            if (vcsFileVersion < earliest || vcsFileVersion > latest)
            {
                throw new UnexpectedMagicException($"Only VCS file versions {earliest} through {latest} are supported",
                    vcsFileVersion, nameof(vcsFileVersion));
            }
        }

        public VfxStaticComboData GetStaticCombo(long id)
        {
            return StaticComboEntries[id].Unserialize();
        }

        public int[] GetDBlockConfig(long blockId)
        {
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
