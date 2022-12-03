using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using ZstdSharp;
using LzmaDecoder = SevenZip.Compression.LZMA.Decoder;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;

namespace ValveResourceFormat.CompiledShader
{
    public class ShaderFile : IDisposable
    {
        public const int MAGIC = 0x32736376; // "vcs2"
        public const uint ZSTD_DELIM = 0xFFFFFFFD;
        public const uint LZMA_DELIM = 0x414D5A4C;
        public const int UNCOMPRESSED = 0;
        public const int ZSTD_COMPRESSION = 1;
        public const int LZMA_COMPRESSION = 2;
        public const uint PI_MURMURSEED = 0x31415926;
        public ShaderDataReader DataReader { get; set; }
        private FileStream FileStream;

        public string FilenamePath { get; private set; }
        public VcsProgramType VcsProgramType { get; private set; }
        public VcsPlatformType VcsPlatformType { get; private set; }
        public VcsShaderModelType VcsShaderModelType { get; private set; }
        public FeaturesHeaderBlock FeaturesHeader { get; private set; }
        public VsPsHeaderBlock VspsHeader { get; private set; }
        public int VcsVersion { get; private set; }
        public int PossibleEditorDescription { get; private set; } // 17 for all up to date files. 14 seen in old test files
        public List<SfBlock> SfBlocks { get; private set; } = new();
        public List<SfConstraintsBlock> SfConstraintsBlocks { get; private set; } = new();
        public List<DBlock> DBlocks { get; private set; } = new();
        public List<DConstraintsBlock> DConstraintsBlocks { get; private set; } = new();
        public List<ParamBlock> ParamBlocks { get; private set; } = new();
        public List<MipmapBlock> MipmapBlocks { get; private set; } = new();
        public List<BufferBlock> BufferBlocks { get; private set; } = new();
        public List<VertexSymbolsBlock> SymbolBlocks { get; private set; } = new();

        // Zframe data assigned to the ZFrameDataDescription class are key pieces of
        // information needed to decompress and retrieve zframes (to save processing zframes are only
        // decompressed on request). This information is organised in zframesLookup by their zframeId's.
        // Because the zframes appear in the file in ascending order, storing their data in a
        // sorted dictionary enables retrieval based on the order they are seen; by calling
        // zframesLookup.ElementAt(zframeIndex). We also retrieve them based on their id using
        // zframesLookup[zframeId]. Both methods are useful in different contexts (be aware not to mix them up).
        public SortedDictionary<long, ZFrameDataDescription> ZframesLookup { get; } = new();
        private ConfigMappingDParams dBlockConfigGen;

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

                if (DataReader != null)
                {
                    DataReader.Dispose();
                    DataReader = null;
                }
            }
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filenamepath">The file to open and read.</param>
        public void Read(string filenamepath)
        {
            FileStream = new FileStream(filenamepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Read(filenamepath, FileStream);
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="filenamepath">The filename <see cref="string"/>.</param>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(string filenamepath, Stream input)
        {
            DataReader = new ShaderDataReader(input);
            FilenamePath = filenamepath;
            ParseFile();
        }

        public void PrintSummary(HandleOutputWrite OutputWriter = null, bool showRichTextBoxLinks = false, List<string> relatedfiles = null)
        {
            var fileSummary = new PrintVcsFileSummary(this, OutputWriter, showRichTextBoxLinks, relatedfiles);
        }

        private void ParseFile()
        {
            var vcsFileProperties = ComputeVCSFileName(FilenamePath);
            VcsProgramType = vcsFileProperties.Item1;
            VcsPlatformType = vcsFileProperties.Item2;
            VcsShaderModelType = vcsFileProperties.Item3;
            // There's a chance HullShader, DomainShader and RaytracingShader work but they haven't been tested
            if (VcsProgramType == VcsProgramType.Features)
            {
                FeaturesHeader = new FeaturesHeaderBlock(DataReader);
                VcsVersion = FeaturesHeader.VcsFileVersion;
            }
            else if (VcsProgramType == VcsProgramType.VertexShader || VcsProgramType == VcsProgramType.PixelShader
                 || VcsProgramType == VcsProgramType.GeometryShader || VcsProgramType == VcsProgramType.PixelShaderRenderState
                 || VcsProgramType == VcsProgramType.ComputeShader || VcsProgramType == VcsProgramType.HullShader
                 || VcsProgramType == VcsProgramType.DomainShader || VcsProgramType == VcsProgramType.RaytracingShader)
            {
                VspsHeader = new VsPsHeaderBlock(DataReader);
                VcsVersion = VspsHeader.VcsFileVersion;
            }
            else
            {
                throw new ShaderParserException($"Can't parse this filetype: {VcsProgramType}");
            }
            PossibleEditorDescription = DataReader.ReadInt32();
            var sfBlockCount = DataReader.ReadInt32();
            for (var i = 0; i < sfBlockCount; i++)
            {
                SfBlock nextSfBlock = new(DataReader, i);
                SfBlocks.Add(nextSfBlock);
            }
            var sfConstraintsBlockCount = DataReader.ReadInt32();
            for (var i = 0; i < sfConstraintsBlockCount; i++)
            {
                SfConstraintsBlock nextSfConstraintsBlock = new(DataReader, i);
                SfConstraintsBlocks.Add(nextSfConstraintsBlock);
            }
            var dBlockCount = DataReader.ReadInt32();
            for (var i = 0; i < dBlockCount; i++)
            {
                DBlock nextDBlock = new(DataReader, i);
                DBlocks.Add(nextDBlock);
            }
            var dConstraintsBlockCount = DataReader.ReadInt32();
            for (var i = 0; i < dConstraintsBlockCount; i++)
            {
                DConstraintsBlock nextDConstraintsBlock = new(DataReader, i);
                DConstraintsBlocks.Add(nextDConstraintsBlock);
            }

            // This is needed for the zframes to determine their source mapping
            // it must be instantiated after the D-blocks have been read
            dBlockConfigGen = new ConfigMappingDParams(this);

            var paramBlockCount = DataReader.ReadInt32();
            for (var i = 0; i < paramBlockCount; i++)
            {
                ParamBlock nextParamBlock = new(DataReader, i, VcsVersion);
                ParamBlocks.Add(nextParamBlock);
            }
            var mipmapBlockCount = DataReader.ReadInt32();
            for (var i = 0; i < mipmapBlockCount; i++)
            {
                MipmapBlock nextMipmapBlock = new(DataReader, i);
                MipmapBlocks.Add(nextMipmapBlock);
            }
            var bufferBlockCount = DataReader.ReadInt32();
            for (var i = 0; i < bufferBlockCount; i++)
            {
                BufferBlock nextBufferBlock = new(DataReader, i);
                BufferBlocks.Add(nextBufferBlock);
            }
            if (VcsProgramType == VcsProgramType.Features || VcsProgramType == VcsProgramType.VertexShader)
            {
                var symbolsBlockCount = DataReader.ReadInt32();
                for (var i = 0; i < symbolsBlockCount; i++)
                {
                    VertexSymbolsBlock nextSymbolsBlock = new(DataReader, i);
                    SymbolBlocks.Add(nextSymbolsBlock);
                }
            }

            List<long> zframeIds = new();
            var zframesCount = DataReader.ReadInt32();
            if (zframesCount == 0)
            {
                // if zframes = 0 there's nothing more to do
                if (DataReader.BaseStream.Position != DataReader.BaseStream.Length)
                {
                    throw new ShaderParserException($"Reader contains more data, but EOF expected");
                }
                return;
            }
            for (var i = 0; i < zframesCount; i++)
            {
                zframeIds.Add(DataReader.ReadInt64());
            }

            List<(long, int)> zframeIdsAndOffsets = new();
            foreach (var zframeId in zframeIds)
            {
                zframeIdsAndOffsets.Add((zframeId, DataReader.ReadInt32()));
            }

            var offsetToEndOffile = DataReader.ReadInt32();
            if (offsetToEndOffile != (int)DataReader.BaseStream.Length)
            {
                throw new ShaderParserException($"Pointer to end of file expected, value read = {offsetToEndOffile}");
            }

            foreach (var item in zframeIdsAndOffsets)
            {
                var zframeId = item.Item1;
                var offsetToZframeHeader = item.Item2;
                DataReader.BaseStream.Position = offsetToZframeHeader;
                var chunkSizeOrZframeDelim = DataReader.ReadUInt32();
                var compressionType = chunkSizeOrZframeDelim == ZSTD_DELIM ? ZSTD_COMPRESSION : LZMA_COMPRESSION;

                var uncompressedLength = 0;
                var compressedLength = 0;
                if (chunkSizeOrZframeDelim != ZSTD_DELIM)
                {
                    var lzmaDelimOrStartOfData = DataReader.ReadUInt32();
                    if (lzmaDelimOrStartOfData != LZMA_DELIM)
                    {
                        uncompressedLength = (int)chunkSizeOrZframeDelim;
                        compressionType = UNCOMPRESSED;
                    }
                }

                if (compressionType == ZSTD_COMPRESSION || compressionType == LZMA_COMPRESSION)
                {
                    uncompressedLength = DataReader.ReadInt32();
                    compressedLength = DataReader.ReadInt32();
                }

                var zframeDataDesc = new ZFrameDataDescription(zframeId, offsetToZframeHeader,
                    compressionType, uncompressedLength, compressedLength, DataReader);
                ZframesLookup.Add(zframeId, zframeDataDesc);
            }
        }

#pragma warning disable CA1024 // Use properties where appropriate
        public int GetZFrameCount()
        {
            return ZframesLookup.Count;
        }

        public long GetZFrameIdByIndex(int zframeIndex)
        {
            return ZframesLookup.ElementAt(zframeIndex).Key;
        }

        public byte[] GetCompressedZFrameData(long zframeId)
        {
            return ZframesLookup[zframeId].GetCompressedZFrameData();
        }

        public byte[] GetDecompressedZFrameByIndex(int zframeIndex)
        {
            return ZframesLookup.ElementAt(zframeIndex).Value.GetDecompressedZFrame();
        }

        public byte[] GetDecompressedZFrame(long zframeId)
        {
            return ZframesLookup[zframeId].GetDecompressedZFrame();
        }

        public ZFrameFile GetZFrameFile(long zframeId, HandleOutputWrite outputWriter = null, bool omitParsing = false)
        {
            return new ZFrameFile(GetDecompressedZFrame(zframeId), FilenamePath, zframeId,
                VcsProgramType, VcsPlatformType, VcsShaderModelType, VcsVersion, omitParsing, outputWriter);
        }

        public ZFrameFile GetZFrameFileByIndex(int zframeIndex, HandleOutputWrite outputWriter = null, bool omitParsing = false)
        {
            return GetZFrameFile(ZframesLookup.ElementAt(zframeIndex).Key, outputWriter, omitParsing);
        }
#pragma warning restore CA1024

        public void PrintByteDetail(bool shortenOutput = true, HandleOutputWrite outputWriter = null)
        {
            DataReader.OutputWriter = outputWriter ?? ((x) => { Console.Write(x); });
            DataReader.BaseStream.Position = 0;
            if (VcsProgramType == VcsProgramType.Features)
            {
                FeaturesHeader.PrintByteDetail();
            }
            else if (VcsProgramType == VcsProgramType.VertexShader || VcsProgramType == VcsProgramType.PixelShader
                 || VcsProgramType == VcsProgramType.GeometryShader || VcsProgramType == VcsProgramType.PixelShaderRenderState
                 || VcsProgramType == VcsProgramType.ComputeShader || VcsProgramType == VcsProgramType.HullShader
                 || VcsProgramType == VcsProgramType.DomainShader || VcsProgramType == VcsProgramType.RaytracingShader)
            {
                VspsHeader.PrintByteDetail();
            }
            DataReader.ShowByteCount();
            var possible_editor_desc = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"({possible_editor_desc}) possible editor description");
            var lastEditorRef = VcsProgramType == VcsProgramType.Features ? FeaturesHeader.EditorIDs.Count - 1 : 1;
            DataReader.TabComment($"value appears to be linked to the last Editor reference (Editor ref. ID{lastEditorRef})", 15);
            DataReader.ShowByteCount();
            var sfBlockCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{sfBlockCount} SF blocks (usually 152 bytes each)");
            DataReader.BreakLine();
            foreach (var sfBlock in SfBlocks)
            {
                sfBlock.PrintByteDetail();
            }
            DataReader.ShowByteCount();
            var sfConstraintsBlockCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{sfConstraintsBlockCount} S-configuration constraint blocks (472 bytes each)");
            DataReader.BreakLine();
            foreach (var sfConstraintsBlock in SfConstraintsBlocks)
            {
                sfConstraintsBlock.PrintByteDetail();
            }
            DataReader.ShowByteCount();
            var dBlockCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{dBlockCount} D-blocks (152 bytes each)");
            DataReader.BreakLine();
            foreach (var dBlock in DBlocks)
            {
                dBlock.PrintByteDetail();
            }
            DataReader.ShowByteCount();
            var dConstraintsBlockCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{dConstraintsBlockCount} D-configuration constraint blocks (472 bytes each)");
            DataReader.BreakLine();
            foreach (var dConstraintBlock in DConstraintsBlocks)
            {
                dConstraintBlock.PrintByteDetail();
            }
            DataReader.ShowByteCount();
            var paramBlockCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{paramBlockCount} Param-Blocks");
            DataReader.BreakLine();
            foreach (var paramBlock in ParamBlocks)
            {
                paramBlock.PrintByteDetail(VcsVersion);
            }
            DataReader.ShowByteCount();
            var mipmapBlockCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{mipmapBlockCount} Mipmap blocks (280 bytes each)");
            DataReader.BreakLine();
            foreach (var mipmapBlock in MipmapBlocks)
            {
                mipmapBlock.PrintByteDetail();
            }
            DataReader.ShowByteCount();
            var bufferBlockCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{bufferBlockCount} Buffer blocks (variable length)");
            DataReader.BreakLine();
            foreach (var bufferBlock in BufferBlocks)
            {
                bufferBlock.PrintByteDetail();
            }
            if (VcsProgramType == VcsProgramType.Features || VcsProgramType == VcsProgramType.VertexShader)
            {
                DataReader.ShowByteCount();
                var symbolBlockCount = DataReader.ReadUInt32AtPosition();
                DataReader.ShowBytes(4, $"{symbolBlockCount} symbol/names blocks");
                foreach (var symbolBlock in SymbolBlocks)
                {
                    DataReader.BreakLine();
                    symbolBlock.PrintByteDetail();
                }
                DataReader.BreakLine();
            }

            PrintZframes(shortenOutput, out var zFrameCount);
            if (shortenOutput && zFrameCount > SKIP_ZFRAMES_IF_MORE_THAN)
            {
                DataReader.Comment("rest of data contains compressed zframes");
                DataReader.BreakLine();
            }

            DataReader.ShowEndOfFile();
        }

        public int[] GetDBlockConfig(int blockId)
        {
            return dBlockConfigGen.GetConfigState(blockId);
        }

        private const int SKIP_ZFRAMES_IF_MORE_THAN = 10;
        private const int MAX_ZFRAME_BYTES_TO_SHOW = 96;

        private void PrintZframes(bool shortenOutput, out uint zFrameCount)
        {
            //
            // The zFrameIds and zFrameDataOffsets are read as two separate lists before the data section starts
            // (Normally parsing is expected to stop when the ids and offsets are collected - sufficient to
            // retrieve any future data as-needed; the data section is always at the end of the file for this reason)
            //
            // For testing:
            // if `shortenOutput = false` all zframes and their data are shown.
            // if `shortenOutput = true` the parser returns after, or shortly after, showing the listings only
            //
            // Parsing correctness is always verified because an end-of-file pointer is provided right after the zframe
            // listing, the datareader must either reach or be assigned to the end of file to complete parsing; checked in
            // the call to datareader.ShowEndOfFile()
            //
            //
            List<uint> zFrameIds = new();
            List<long> zFrameDataOffsets = new();

            DataReader.ShowByteCount();
            zFrameCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{zFrameCount} zframes");
            DataReader.BreakLine();
            if (zFrameCount == 0)
            {
                return;
            }
            DataReader.ShowByteCount("zFrame IDs");
            for (var i = 0; i < zFrameCount; i++)
            {
                var zframeId = DataReader.ReadUInt32AtPosition();
                DataReader.ShowBytes(8, breakLine: false);
                DataReader.TabComment($"zframe[0x{zframeId:x08}]    {Convert.ToString(zframeId, 2).PadLeft(20, '0')} (bin.)");
                zFrameIds.Add(zframeId);
            }

            DataReader.BreakLine();
            DataReader.ShowByteCount("zFrame file offsets");
            foreach (var zframeId in zFrameIds)
            {
                var zframe_offset = DataReader.ReadUInt32AtPosition();
                zFrameDataOffsets.Add(zframe_offset);
                DataReader.ShowBytes(4, $"{zframe_offset} offset of zframe[0x{zframeId:x08}]");
            }
            var endOfFilePointer = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{endOfFilePointer} - end of file");
            DataReader.BreakLine();

            if (shortenOutput && zFrameCount > SKIP_ZFRAMES_IF_MORE_THAN)
            {
                DataReader.BaseStream.Position = endOfFilePointer;
                return;
            }
            for (var i = 0; i < zFrameCount; i++)
            {
                DataReader.BaseStream.Position = zFrameDataOffsets[i];
                PrintCompressedZFrame(zFrameIds[i]);
            }
            // in v62 the last zframe doesn't always finish at the end of file; it's necessary to assign
            // the end-pointer here (read above at the end of zframe listings) to confirm correct parsing.
            if (VcsVersion == 62)
            {
                DataReader.BaseStream.Position = endOfFilePointer;
            }
        }


        public void PrintCompressedZFrame(uint zframeId)
        {
            DataReader.OutputWriteLine($"[{DataReader.BaseStream.Position}] zframe[0x{zframeId:x08}]");
            var isLzma = false;
            var zstdDelimOrChunkSize = DataReader.ReadUInt32AtPosition();
            if (zstdDelimOrChunkSize == ZSTD_DELIM)
            {
                DataReader.ShowBytes(4, $"Zstd delim (0x{ZSTD_DELIM:x08})");
            }
            else
            {
                DataReader.ShowBytes(4, $"Chunk size {zstdDelimOrChunkSize}");
                var lzmaDelim = DataReader.ReadUInt32AtPosition();
                if (lzmaDelim != LZMA_DELIM)
                {
                    DataReader.Comment($"neither ZStd or Lzma found (frame appears to be uncompressed)");
                    DataReader.ShowBytes((int)zstdDelimOrChunkSize);
                    DataReader.BreakLine();
                    return;
                }
                isLzma = true;
                DataReader.ShowBytes(4, $"Lzma delim (0x{LZMA_DELIM:x08})");
            }
            var uncompressed_length = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"{uncompressed_length,-8} uncompressed length");
            var compressed_length = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"{compressed_length,-8} compressed length");
            if (isLzma)
            {
                DataReader.ShowBytes(5, "Decoder properties");
            }
            DataReader.ShowBytesAtPosition(0, compressed_length > MAX_ZFRAME_BYTES_TO_SHOW ? MAX_ZFRAME_BYTES_TO_SHOW : compressed_length);
            if (compressed_length > MAX_ZFRAME_BYTES_TO_SHOW)
            {
                DataReader.Comment($"... ({compressed_length - MAX_ZFRAME_BYTES_TO_SHOW} bytes not shown)");
            }
            DataReader.BaseStream.Position += compressed_length;
            DataReader.BreakLine();
        }
    }

    // Lzma also comes with a 'chunk-size' field, which is not needed
    public class ZFrameDataDescription
    {
        public long ZframeId { get; }
        public int OffsetToZFrameHeader { get; }
        public int CompressionType { get; }
        public int CompressedLength { get; }
        public int UncompressedLength { get; }
        private ShaderDataReader DataReader { get; }
        public ZFrameDataDescription(long zframeId, int offsetToZFrameHeader, int compressionType,
            int uncompressedLength, int compressedLength, ShaderDataReader datareader)
        {
            ZframeId = zframeId;
            OffsetToZFrameHeader = offsetToZFrameHeader;
            CompressionType = compressionType;
            UncompressedLength = uncompressedLength;
            CompressedLength = compressedLength;
            DataReader = datareader;
        }

        public byte[] GetCompressedZFrameData()
        {
            DataReader.BaseStream.Position = OffsetToZFrameHeader;
            switch (CompressionType)
            {
                case ShaderFile.UNCOMPRESSED:
                    DataReader.BaseStream.Position += 4;
                    return DataReader.ReadBytes(UncompressedLength);

                case ShaderFile.ZSTD_COMPRESSION:
                    DataReader.BaseStream.Position += 12;
                    return DataReader.ReadBytes(CompressedLength);

                case ShaderFile.LZMA_COMPRESSION:
                    DataReader.BaseStream.Position += 21;
                    return DataReader.ReadBytes(CompressedLength);

                default:
                    throw new ShaderParserException($"Unknown compression type or compression type not determined {CompressionType}");
            }
        }

        public byte[] GetDecompressedZFrame()
        {
            DataReader.BaseStream.Position = OffsetToZFrameHeader;
            switch (CompressionType)
            {
                case ShaderFile.UNCOMPRESSED:
                    DataReader.BaseStream.Position += 4;
                    return DataReader.ReadBytes(UncompressedLength);

                case ShaderFile.ZSTD_COMPRESSION:
                    using (var zstdDecoder = new Decompressor())
                    {
                        DataReader.BaseStream.Position += 12;
                        var compressedZframe = DataReader.ReadBytes(CompressedLength);
                        zstdDecoder.LoadDictionary(ZstdDictionary.GetDictionary());
                        var zframeUncompressed = zstdDecoder.Unwrap(compressedZframe);
                        if (zframeUncompressed.Length != UncompressedLength)
                        {
                            throw new ShaderParserException("Decompressed zframe doesn't match expected size");
                        }
                        zstdDecoder.Dispose();
                        return zframeUncompressed.ToArray();
                    }

                case ShaderFile.LZMA_COMPRESSION:
                    var lzmaDecoder = new LzmaDecoder();
                    DataReader.BaseStream.Position += 16;
                    lzmaDecoder.SetDecoderProperties(DataReader.ReadBytes(5));
                    var compressedBuffer = DataReader.ReadBytes(CompressedLength);
                    using (var inputStream = new MemoryStream(compressedBuffer))
                    using (var outStream = new MemoryStream((int)UncompressedLength))
                    {
                        lzmaDecoder.Code(inputStream, outStream, compressedBuffer.Length, UncompressedLength, null);
                        return outStream.ToArray();
                    }

                default:
                    throw new ShaderParserException($"Unknown compression type or compression type not determined {CompressionType}");
            }
        }

        public override string ToString()
        {
            var comprDesc = CompressionType switch
            {
                ShaderFile.UNCOMPRESSED => "uncompressed",
                ShaderFile.ZSTD_COMPRESSION => "ZSTD",
                ShaderFile.LZMA_COMPRESSION => "LZMA",
                _ => "undetermined"
            };
            return $"zframeId[0x{ZframeId:x08}] {comprDesc} offset={OffsetToZFrameHeader,8} " +
                $"compressedLength={CompressedLength,7} uncompressedLength={UncompressedLength,9}";
        }
    }
}
