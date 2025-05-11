using System.IO;
using System.Linq;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    public class VfxProgramData : IDisposable
    {
        public const int MAGIC = 0x32736376; // "vcs2"

        public ShaderDataReader DataReader { get; set; }
        private Stream BaseStream;

        public string FilenamePath { get; private set; }
        public string ShaderName { get; private set; }
        public VcsProgramType VcsProgramType { get; private set; } = VcsProgramType.Undetermined;
        public VcsPlatformType VcsPlatformType { get; private set; } = VcsPlatformType.Undetermined;
        public VcsShaderModelType VcsShaderModelType { get; private set; } = VcsShaderModelType.Undetermined;
        public FeaturesHeaderBlock FeaturesHeader { get; private set; }
        public int VcsVersion { get; private set; }
        public Guid FileHash { get; private set; }
        public VcsAdditionalFiles AdditionalFiles { get; private set; }
        public bool IsSbox { get; init; }
        public int VariableSourceMax { get; private set; } // 17 for all up to date files. 14 seen in old test files
        public List<(Guid, string)> EditorIDs { get; } = [];
        public List<VfxCombo> StaticCombos { get; private set; } = [];
        public List<VfxRule> StaticComboRules { get; private set; } = [];
        public List<VfxCombo> DynamicCombos { get; private set; } = [];
        public List<VfxRule> DynamicComboRules { get; private set; } = [];
        public List<VfxVariableDescription> VariableDescriptions { get; private set; } = [];
        public List<VfxTextureChannelProcessor> TextureChannelProcessors { get; private set; } = [];
        public List<ConstantBufferVariable> ExtConstantBufferDescriptions { get; private set; } = [];
        public List<VsInputSignatureElement> VSInputSignatures { get; private set; } = [];

        // Zframe data assigned to the ZFrameDataDescription class are key pieces of
        // information needed to decompress and retrieve zframes (to save processing zframes are only
        // decompressed on request). This information is organised in zframesLookup by their zframeId's.
        // Because the zframes appear in the file in ascending order, storing their data in a
        // sorted dictionary enables retrieval based on the order they are seen; by calling
        // zframesLookup.ElementAt(zframeIndex). We also retrieve them based on their id using
        // zframesLookup[zframeId]. Both methods are useful in different contexts (be aware not to mix them up).
        public SortedDictionary<long, VfxStaticComboVcsEntry> ZframesLookup { get; } = [];
        public StaticCache ZFrameCache { get; private set; }
        private ConfigMappingDParams dBlockConfigGen;

        public int AdditionalFileCount => AdditionalFiles == VcsAdditionalFiles.Ms ? 3 : AdditionalFiles == VcsAdditionalFiles.PsrsAndRtx ? 2 : (int)AdditionalFiles;

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

                ZFrameCache?.Dispose();
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
            Read(filenamepath, stream);
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="filenamepath">The filename <see cref="string"/>.</param>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(string filenamepath, Stream input)
        {
            BaseStream = input;
            DataReader = new ShaderDataReader(input);
            FilenamePath = filenamepath;
            VfxCreateFromVcs();
            ZFrameCache = new StaticCache(this);
        }

        public void PrintSummary(HandleOutputWrite OutputWriter = null, bool showRichTextBoxLinks = false, List<string> relatedfiles = null)
        {
            var fileSummary = new PrintVcsFileSummary(this, OutputWriter, showRichTextBoxLinks, relatedfiles);
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

            if (VcsVersion >= 64)
            {
                AdditionalFiles = (VcsAdditionalFiles)DataReader.ReadInt32();
            }

            if (!Enum.IsDefined(AdditionalFiles))
            {
                throw new UnexpectedMagicException("Unexpected additional files", (int)AdditionalFiles, nameof(AdditionalFiles));
            }
            else if (IsSbox && AdditionalFiles == VcsAdditionalFiles.Rtx)
            {
                DataReader.BaseStream.Position += 4;
                AdditionalFiles = VcsAdditionalFiles.None;
                VcsVersion--;
            }

            UnserializeVfxProgramData();
        }

        private void UnserializeVfxProgramData()
        {
            // There's a chance HullShader, DomainShader and RaytracingShader work but they haven't been tested
            if (VcsProgramType == VcsProgramType.Features)
            {
                FeaturesHeader = new FeaturesHeaderBlock(VcsVersion, DataReader, AdditionalFileCount);

                // EditorIDs is probably MD5 hashes
                foreach (var programType in ProgramTypeIterator())
                {
                    EditorIDs.Add((new Guid(DataReader.ReadBytes(16)), $"// {programType}"));
                }
            }
            else
            {
                EditorIDs.Add((new Guid(DataReader.ReadBytes(16)), string.Empty));
            }

            FileHash = new Guid(DataReader.ReadBytes(16));

            VariableSourceMax = DataReader.ReadInt32();

            var staticCombosCount = DataReader.ReadInt32();
            for (var i = 0; i < staticCombosCount; i++)
            {
                VfxCombo nextSfBlock = new(DataReader, i);
                StaticCombos.Add(nextSfBlock);
            }

            var staticComboRulesCount = DataReader.ReadInt32();
            for (var i = 0; i < staticComboRulesCount; i++)
            {
                VfxRule nextSfConstraintBlock = VcsProgramType == VcsProgramType.Features
                    ? new(DataReader, i, ConditionalType.Feature)
                    : new(DataReader, i, ConditionalType.Static);

                StaticComboRules.Add(nextSfConstraintBlock);
            }

            var dynamicCombosCount = DataReader.ReadInt32();
            for (var i = 0; i < dynamicCombosCount; i++)
            {
                VfxCombo nextDBlock = new(DataReader, i);
                DynamicCombos.Add(nextDBlock);
            }

            var dynamicComboRulesCount = DataReader.ReadInt32();
            for (var i = 0; i < dynamicComboRulesCount; i++)
            {
                VfxRule nextDConstraintsBlock = new(DataReader, i, ConditionalType.Dynamic);
                DynamicComboRules.Add(nextDConstraintsBlock);
            }

            // This is needed for the zframes to determine their source mapping
            // it must be instantiated after the D-blocks have been read
            dBlockConfigGen = new ConfigMappingDParams(this);

            var variableDescriptionsCount = DataReader.ReadInt32();
            for (var i = 0; i < variableDescriptionsCount; i++)
            {
                VfxVariableDescription nextParamBlock = new(DataReader, i, VcsVersion);
                VariableDescriptions.Add(nextParamBlock);
            }

            var textureChannelProcessorsCount = DataReader.ReadInt32();
            for (var i = 0; i < textureChannelProcessorsCount; i++)
            {
                VfxTextureChannelProcessor nextChannelBlock = new(DataReader, i);
                TextureChannelProcessors.Add(nextChannelBlock);
            }

            var extConstantBufferDescriptionsCount = DataReader.ReadInt32();
            for (var i = 0; i < extConstantBufferDescriptionsCount; i++)
            {
                ConstantBufferVariable nextBufferBlock = new(DataReader, i);
                ExtConstantBufferDescriptions.Add(nextBufferBlock);
            }

            if (VcsProgramType == VcsProgramType.Features || VcsProgramType == VcsProgramType.VertexShader)
            {
                var vsInputSignaturesCount = DataReader.ReadInt32();
                for (var i = 0; i < vsInputSignaturesCount; i++)
                {
                    VsInputSignatureElement nextSymbolsBlock = new(DataReader, i);
                    VSInputSignatures.Add(nextSymbolsBlock);
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

            var zframeIdsAndOffsets = new (long Id, int Offset)[combosCount];

            for (var i = 0; i < combosCount; i++)
            {
                zframeIdsAndOffsets[i].Id = DataReader.ReadInt64();
            }

            for (var i = 0; i < combosCount; i++)
            {
                // CVfxStaticComboVcsEntry::Unserialize
                // This is a separate function because Valve has a flag to skip actually parsing the entries,
                // so if it's not requested, it just creates empty VfxStaticComboVcsEntry with the offset.
                zframeIdsAndOffsets[i].Offset = DataReader.ReadInt32();
            }

            var offsetToEndOffile = DataReader.ReadInt32();
            if (offsetToEndOffile != (int)DataReader.BaseStream.Length)
            {
                throw new ShaderParserException($"Pointer to end of file expected, value read = {offsetToEndOffile}");
            }

            // CVfxProgramData::UnserializeStaticComboDataCache
            foreach (var zFrame in zframeIdsAndOffsets)
            {
                ZframesLookup.Add(zFrame.Id, new VfxStaticComboVcsEntry
                {
                    ParentProgramData = this,
                    ZframeId = zFrame.Id,
                    OffsetToZFrameHeader = zFrame.Offset,
                });
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

        public IEnumerable<VcsProgramType> ProgramTypeIterator()
        {
            var programTypeLast = (int)VcsProgramType.ComputeShader + AdditionalFileCount;

            for (var i = 0; i <= programTypeLast; i++)
            {
                var programType = (VcsProgramType)i;

                // Version 63 adds compute shaders
                if (VcsVersion < 63 && programType is VcsProgramType.ComputeShader)
                {
                    continue;
                }

                // Version 68 removes hull and domain shaders
                if (VcsVersion >= 68 && programType is VcsProgramType.HullShader or VcsProgramType.DomainShader)
                {
                    continue;
                }

                yield return programType;
            }
        }

#pragma warning disable CA1024 // Use properties where appropriate
        public int GetZFrameCount()
        {
            return ZframesLookup.Count;
        }

        public VfxStaticComboData GetZFrameFile(long zframeId)
        {
            var entry = ZframesLookup[zframeId];

            var decompressed = entry.GetDecompressedZFrame();
            using var stream = new MemoryStream(decompressed);

            return new VfxStaticComboData(stream, zframeId, this);
        }

        public VfxStaticComboData GetZFrameFileByIndex(int zframeIndex)
        {
            return GetZFrameFile(ZframesLookup.ElementAt(zframeIndex).Key);
        }

        public int[] GetDBlockConfig(long blockId)
        {
            return dBlockConfigGen.GetConfigState(blockId);
        }
    }
}
