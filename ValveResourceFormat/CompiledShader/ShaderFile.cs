using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using ZstdSharp;
using LzmaDecoder = SevenZip.Compression.LZMA.Decoder;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    public class ShaderFile
    {
        public ShaderDataReader datareader { get; }
        public string filenamepath { get; }
        public VcsFileType vcsFileType { get; }
        public VcsPlatformType vcsPlatformType { get; }
        public VcsModelType vcsModelType { get; }
        public FeaturesHeaderBlock featuresHeader { get; }
        public VsPsHeaderBlock vspsHeader { get; }
        public int possibleMinorVersion { get; } // 17 for all up to date files. 14 seen in old test files
        public List<SfBlock> sfBlocks { get; } = new();
        public List<SfConstraintsBlock> sfConstraintsBlocks { get; } = new();
        public List<DBlock> dBlocks { get; } = new();
        public List<DConstraintsBlock> dConstraintsBlocks { get; } = new();
        public List<ParamBlock> paramBlocks { get; } = new();
        public List<MipmapBlock> mipmapBlocks { get; } = new();
        public List<BufferBlock> bufferBlocks { get; } = new();
        public List<VertexSymbolsBlock> symbolBlocks { get; } = new();

        // Zframe data assigned to the ZFrameDataDescription class are key pieces of
        // information needed to decompress and retrieve zframes (to save processing zframes are only
        // decompressed on request). This information is organised in zframesLookup by their zframeId's.
        // Because the zframes appear in the file in ascending order, storing their data in a
        // sorted dictionary enables retrieval based on the order they are seen; by calling
        // zframesLookup.ElementAt(zframeIndex). We also retrieve them based on their id using
        // zframesLookup[zframeId]. Both methods are useful in different contexts (be aware not to mix them up).
        public SortedDictionary<long, ZFrameDataDescription> zframesLookup { get; } = new();
        private ConfigMappingDParams dBlockConfigGen;

        public ShaderFile(string filenamepath, ShaderDataReader datareader)
        {
            this.filenamepath = filenamepath;
            var vcsFileProperties = ComputeVCSFileName(filenamepath);
            vcsFileType = vcsFileProperties.Item1;
            vcsPlatformType = vcsFileProperties.Item2;
            vcsModelType = vcsFileProperties.Item3;
            this.datareader = datareader;
            // There's a chance HullShader, DomainShader and RaytracingShader work but they haven't been tested
            if (vcsFileType == VcsFileType.Features)
            {
                featuresHeader = new FeaturesHeaderBlock(datareader, datareader.GetOffset());
            } else if (vcsFileType == VcsFileType.VertexShader || vcsFileType == VcsFileType.PixelShader
                   || vcsFileType == VcsFileType.GeometryShader || vcsFileType == VcsFileType.PixelShaderRenderState
                   || vcsFileType == VcsFileType.ComputeShader || vcsFileType == VcsFileType.HullShader
                   || vcsFileType == VcsFileType.DomainShader || vcsFileType == VcsFileType.RaytracingShader)
            {
                vspsHeader = new VsPsHeaderBlock(datareader, datareader.GetOffset());
            } else
            {
                throw new ShaderParserException($"Can't parse this filetype: {vcsFileType}");
            }
            possibleMinorVersion = datareader.ReadInt();
            int sfBlockCount = datareader.ReadInt();
            for (int i = 0; i < sfBlockCount; i++)
            {
                SfBlock nextSfBlock = new(datareader, datareader.GetOffset(), i);
                sfBlocks.Add(nextSfBlock);
            }
            int sfConstraintsBlockCount = datareader.ReadInt();
            for (int i = 0; i < sfConstraintsBlockCount; i++)
            {
                SfConstraintsBlock nextSfConstraintsBlock = new(datareader, datareader.GetOffset(), i);
                sfConstraintsBlocks.Add(nextSfConstraintsBlock);
            }
            int dBlockCount = datareader.ReadInt();
            for (int i = 0; i < dBlockCount; i++)
            {
                DBlock nextDBlock = new(datareader, datareader.GetOffset(), i);
                dBlocks.Add(nextDBlock);
            }
            int dConstraintsBlockCount = datareader.ReadInt();
            for (int i = 0; i < dConstraintsBlockCount; i++)
            {
                DConstraintsBlock nextDConstraintsBlock = new(datareader, datareader.GetOffset(), i);
                dConstraintsBlocks.Add(nextDConstraintsBlock);
            }

            // This is needed for the zframes to determine their source mapping
            // it must be instantiated after the D-blocks have been read
            dBlockConfigGen = new ConfigMappingDParams(this);

            int paramBlockCount = datareader.ReadInt();
            for (int i = 0; i < paramBlockCount; i++)
            {
                ParamBlock nextParamBlock = new(datareader, datareader.GetOffset(), i);
                paramBlocks.Add(nextParamBlock);
            }
            int mipmapBlockCount = datareader.ReadInt();
            for (int i = 0; i < mipmapBlockCount; i++)
            {
                MipmapBlock nextMipmapBlock = new(datareader, datareader.GetOffset(), i);
                mipmapBlocks.Add(nextMipmapBlock);
            }
            int bufferBlockCount = datareader.ReadInt();
            for (int i = 0; i < bufferBlockCount; i++)
            {
                BufferBlock nextBufferBlock = new(datareader, datareader.GetOffset(), i);
                bufferBlocks.Add(nextBufferBlock);
            }
            if (vcsFileType == VcsFileType.Features || vcsFileType == VcsFileType.VertexShader)
            {
                int sybmolsBlockCount = datareader.ReadInt();
                for (int i = 0; i < sybmolsBlockCount; i++)
                {
                    VertexSymbolsBlock nextSymbolsBlock = new(datareader, datareader.GetOffset(), i);
                    symbolBlocks.Add(nextSymbolsBlock);
                }
            }

            List<long> zframeIds = new();
            int zframesCount = datareader.ReadInt();
            if (zframesCount == 0)
            {
                // if zframes = 0 there's nothing more to do
                if (!datareader.CheckPositionIsAtEOF())
                {
                    throw new ShaderParserException($"Reader contains more data, but EOF expected");
                }
                return;
            }
            for (int i = 0; i < zframesCount; i++)
            {
                zframeIds.Add(datareader.ReadLong());
            }

            List<(long, int)> zframeIdsAndOffsets = new();
            foreach (long zframeId in zframeIds)
            {
                zframeIdsAndOffsets.Add((zframeId, datareader.ReadInt()));
            }

            int offsetToEndOffile = datareader.ReadInt();
            if (offsetToEndOffile != datareader.GetFileSize())
            {
                throw new ShaderParserException($"Pointer to end of file expected, value read = {offsetToEndOffile}");
            }

            foreach (var item in zframeIdsAndOffsets)
            {
                long zframeId = item.Item1;
                int offsetToZframeHeader = item.Item2;
                datareader.SetOffset(offsetToZframeHeader);
                uint chunkSizeOrZframeDelim = datareader.ReadUInt();
                int compressionType = chunkSizeOrZframeDelim == ShaderCollection.ZSTD_DELIM ?
                    ShaderCollection.ZSTD_COMPRESSION : ShaderCollection.LZMA_COMPRESSION;
                if (chunkSizeOrZframeDelim != ShaderCollection.ZSTD_DELIM)
                {
                    if (datareader.ReadUInt() != ShaderCollection.LZMA_DELIM)
                    {
                        throw new ShaderParserException("Unknown compression, neither ZStd nor Lzma found");
                    }
                }
                int uncompressedLength = datareader.ReadInt();
                int compressedLength = datareader.ReadInt();

                ZFrameDataDescription zframeDataDesc = new ZFrameDataDescription(zframeId, offsetToZframeHeader,
                    compressionType, uncompressedLength, compressedLength, datareader);
                zframesLookup.Add(zframeId, zframeDataDesc);
            }
        }

#pragma warning disable CA1024 // Use properties where appropriate
        public int GetZFrameCount()
        {
            return zframesLookup.Count;
        }

        public long GetZFrameIdByIndex(int zframeIndex)
        {
            return zframesLookup.ElementAt(zframeIndex).Key;
        }

        public byte[] GetDecompressedZFrameByIndex(int zframeIndex)
        {
            return zframesLookup.ElementAt(zframeIndex).Value.GetDecompressedZFrame();
        }

        public byte[] GetDecompressedZFrame(long zframeId)
        {
            return zframesLookup[zframeId].GetDecompressedZFrame();
        }

        public ZFrameFile GetZFrameFile(long zframeId, bool omitParsing = false)
        {
            return new ZFrameFile(GetDecompressedZFrame(zframeId), filenamepath, zframeId,
                vcsFileType, vcsPlatformType, vcsModelType, omitParsing);
        }

        public ZFrameFile GetZFrameFileByIndex(int zframeIndex)
        {
            long zframeId = zframesLookup.ElementAt(zframeIndex).Key;
            return GetZFrameFile(zframeId);
        }
#pragma warning restore CA1024


        private uint zFrameCount;
        const int SKIP_ZFRAMES_IF_MORE_THAN = 10;

        public void PrintByteAnalysis(bool shortenOutput = true)
        {
            datareader.SetOffset(0);
            if (vcsFileType == VcsFileType.Features)
            {
                featuresHeader.PrintAnnotatedBytestream();
            } else if (vcsFileType == VcsFileType.VertexShader || vcsFileType == VcsFileType.PixelShader
                   || vcsFileType == VcsFileType.GeometryShader || vcsFileType == VcsFileType.PixelShaderRenderState
                   || vcsFileType == VcsFileType.ComputeShader || vcsFileType == VcsFileType.HullShader
                   || vcsFileType == VcsFileType.DomainShader || vcsFileType == VcsFileType.RaytracingShader)
            {
                vspsHeader.PrintAnnotatedBytestream();
            }
            datareader.ShowByteCount();
            int unknown_val = datareader.ReadIntAtPosition();
            datareader.ShowBytes(4, $"({unknown_val}) unknown significance, possibly a minor-version");
            int lastEditorRef = vcsFileType == VcsFileType.Features ? featuresHeader.editorIDs.Count - 1 : 1;
            datareader.TabComment($"the value appears to be linked to the last Editor reference (Editor ref. ID{lastEditorRef})", 15);
            datareader.ShowByteCount();
            uint sfBlockCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{sfBlockCount} SF blocks (usually 152 bytes each)");
            datareader.BreakLine();
            foreach (var sfBlock in sfBlocks)
            {
                sfBlock.PrintAnnotatedBytestream();
            }
            datareader.ShowByteCount();
            uint sfConstraintsBlockCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{sfConstraintsBlockCount} S-configuration constraint blocks (472 bytes each)");
            datareader.BreakLine();
            foreach (var sfConstraintsBlock in sfConstraintsBlocks)
            {
                sfConstraintsBlock.PrintAnnotatedBytestream();
            }
            datareader.ShowByteCount();
            uint dBlockCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{dBlockCount} D-blocks (152 bytes each)");
            datareader.BreakLine();
            foreach (var dBlock in dBlocks)
            {
                dBlock.PrintAnnotatedBytestream();
            }
            datareader.ShowByteCount();
            uint dConstraintsBlockCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{dConstraintsBlockCount} D-configuration constraint blocks (472 bytes each)");
            datareader.BreakLine();
            foreach (var dConstraintBlock in dConstraintsBlocks)
            {
                dConstraintBlock.PrintAnnotatedBytestream();
            }
            datareader.ShowByteCount();
            uint paramBlockCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{paramBlockCount} Param-Blocks");
            datareader.BreakLine();
            foreach (var paramBlock in paramBlocks)
            {
                paramBlock.PrintAnnotatedBytestream();
            }
            datareader.ShowByteCount();
            uint mipmapBlockCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{mipmapBlockCount} Mipmap blocks (280 bytes each)");
            datareader.BreakLine();
            foreach (var mipmapBlock in mipmapBlocks)
            {
                mipmapBlock.PrintAnnotatedBytestream();
            }
            datareader.ShowByteCount();
            uint bufferBlockCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{bufferBlockCount} Buffer blocks (variable length)");
            datareader.BreakLine();
            foreach (var bufferBlock in bufferBlocks)
            {
                bufferBlock.PrintAnnotatedBytestream();
            }
            if (vcsFileType == VcsFileType.Features || vcsFileType == VcsFileType.VertexShader)
            {
                datareader.ShowByteCount();
                uint symbolBlockCount = datareader.ReadUIntAtPosition();
                datareader.ShowBytes(4, $"{symbolBlockCount} symbol/names blocks");
                foreach (var symbolBlock in symbolBlocks)
                {
                    datareader.BreakLine();
                    symbolBlock.PrintAnnotatedBytestream();
                }
                datareader.BreakLine();
            }

            PrintZframes(shortenOutput);
            if (shortenOutput && zFrameCount > SKIP_ZFRAMES_IF_MORE_THAN)
            {
                datareader.Comment("rest of data contains compressed zframes");
                datareader.BreakLine();
                return;
            }

            datareader.ShowEndOfFile();
        }

        public int[] GetDBlockConfig(int blockId)
        {
            return dBlockConfigGen.GetConfigState(blockId);
        }

        private void PrintZframes(bool shortenOutput)
        {
            datareader.ShowByteCount();
            zFrameCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{zFrameCount} zframes");
            datareader.BreakLine();
            if (zFrameCount == 0)
            {
                return;
            }
            List<uint> zFrameIndexes = new();
            datareader.ShowByteCount("zFrame IDs");
            for (int i = 0; i < zFrameCount; i++)
            {
                uint zframeId = datareader.ReadUIntAtPosition();
                datareader.ShowBytes(8, breakLine: false);
                datareader.TabComment($"{getZFrameIdString(zframeId)}    {Convert.ToString(zframeId, 2).PadLeft(20, '0')}");
                zFrameIndexes.Add(zframeId);
            }
            datareader.BreakLine();
            if (shortenOutput && zFrameCount > SKIP_ZFRAMES_IF_MORE_THAN)
            {
                return;
            }
            datareader.ShowByteCount("zFrame file offsets");
            foreach (uint zframeId in zFrameIndexes)
            {
                uint zframe_offset = datareader.ReadUIntAtPosition();
                datareader.ShowBytes(4, $"{zframe_offset} offset of {getZFrameIdString(zframeId)}");
            }
            uint total_size = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{total_size} - end of file");
            datareader.OutputWriteLine("");
            foreach (uint zframeId in zFrameIndexes)
            {
                PrintCompressedZFrame(zframeId);
            }
        }

        int MAX_ZFRAME_BYTES_TO_SHOW = 96;
        public void PrintCompressedZFrame(uint zframeId)
        {
            datareader.OutputWriteLine($"[{datareader.GetOffset()}] {getZFrameIdString(zframeId)}");
            bool isLzma = false;
            uint zstdDelimOrChunkSize = datareader.ReadUIntAtPosition();
            if (zstdDelimOrChunkSize == ShaderCollection.ZSTD_DELIM)
            {
                datareader.ShowBytes(4, $"Zstd delim (0x{ShaderCollection.ZSTD_DELIM:x08})");
            } else
            {
                datareader.ShowBytes(4, $"Lzma chunk size {zstdDelimOrChunkSize}");
                uint lzmaDelim = datareader.ReadUIntAtPosition();
                if (lzmaDelim != ShaderCollection.LZMA_DELIM)
                {
                    throw new ShaderParserException("Unknown compression, neither ZStd nor Lzma found");
                }
                isLzma = true;
                datareader.ShowBytes(4, $"Lzma delim (0x{ShaderCollection.LZMA_DELIM:x08})");
            }
            int uncompressed_length = datareader.ReadIntAtPosition();
            datareader.ShowBytes(4, $"{uncompressed_length,-8} uncompressed length");
            int compressed_length = datareader.ReadIntAtPosition();
            datareader.ShowBytes(4, $"{compressed_length,-8} compressed length");
            if (isLzma)
            {
                datareader.ShowBytes(5, "Decoder properties");
            }
            datareader.ShowBytesAtPosition(0, compressed_length > MAX_ZFRAME_BYTES_TO_SHOW ? MAX_ZFRAME_BYTES_TO_SHOW : compressed_length);
            if (compressed_length > MAX_ZFRAME_BYTES_TO_SHOW)
            {
                datareader.Comment($"... ({compressed_length - MAX_ZFRAME_BYTES_TO_SHOW} bytes not shown)");
            }
            datareader.MoveOffset(compressed_length);
            datareader.BreakLine();
        }

        private static string getZFrameIdString(uint zframeId)
        {
            return $"zframe[0x{zframeId:x08}]";
        }
    }


    // Lzma also comes with a 'chunk-size' field, which is not needed
    public class ZFrameDataDescription
    {
        public long zframeId { get; }
        public int offsetToZFrameHeader { get; }
        public int compressionType { get; }
        public int compressedLength { get; }
        public int uncompressedLength { get; }
        private ShaderDataReader datareader { get; }
        public ZFrameDataDescription(long zframeId, int offsetToZFrameHeader, int compressionType,
            int uncompressedLength, int compressedLength, ShaderDataReader datareader)
        {
            this.zframeId = zframeId;
            this.offsetToZFrameHeader = offsetToZFrameHeader;
            this.compressionType = compressionType;
            this.uncompressedLength = uncompressedLength;
            this.compressedLength = compressedLength;
            this.datareader = datareader;
        }
        public byte[] GetDecompressedZFrame()
        {
            datareader.SetOffset(offsetToZFrameHeader);
            if (compressionType == ShaderCollection.ZSTD_COMPRESSION)
            {
                datareader.MoveOffset(12);
                byte[] compressedZframe = datareader.ReadBytes(compressedLength);
                using var zstdDecoder = new Decompressor();
                zstdDecoder.LoadDictionary(ZstdDictionary.GetDictionary());
                Span<byte> zframeUncompressed = zstdDecoder.Unwrap(compressedZframe);
                if (zframeUncompressed.Length != uncompressedLength)
                {
                    throw new ShaderParserException("Decompressed zframe doesn't match expected size");
                }
                zstdDecoder.Dispose();
                return zframeUncompressed.ToArray();
            }

            if (compressionType == ShaderCollection.LZMA_COMPRESSION)
            {
                var lzmaDecoder = new LzmaDecoder();
                datareader.MoveOffset(16);
                lzmaDecoder.SetDecoderProperties(datareader.ReadBytes(5));
                var compressedBuffer = datareader.ReadBytes(compressedLength);
                using (var inputStream = new MemoryStream(compressedBuffer))
                using (var outStream = new MemoryStream((int)uncompressedLength))
                {
                    lzmaDecoder.Code(inputStream, outStream, compressedBuffer.Length, uncompressedLength, null);
                    return outStream.ToArray();
                }
            }

            throw new ShaderParserException("This point cannot be reached, compressionType should be either ZSTD or LZMA");
        }

        public override string ToString()
        {
            string comprDesc = compressionType == ShaderCollection.ZSTD_COMPRESSION ? "ZSTD" : "LZMA";
            return $"zframeId[0x{zframeId:x08}] {comprDesc} offset={offsetToZFrameHeader,8} " +
                $"compressedLength={compressedLength,7} uncompressedLength={uncompressedLength,9}";
        }


    }
}
