using System.Buffers;
using System.Diagnostics;
using System.IO;

namespace ValveResourceFormat.CompiledShader;

public class VfxStaticComboVcsEntry
{
    private const int LZMA_MAGIC = 0x414D5A4C;

    public required VfxProgramData ParentProgramData { get; init; }
    public long ZframeId { get; init; }
    public int OffsetToZFrameHeader { get; init; }

    public VfxStaticComboData Unserialize()
    {
        var dataReader = ParentProgramData.DataReader;
        dataReader.BaseStream.Position = OffsetToZFrameHeader;

        var compressionTypeOrSize = dataReader.ReadInt32();
        var uncompressedSize = 0;

        if (ParentProgramData.VcsVersion < 64 && ParentProgramData.VcsProgramType == VcsProgramType.Features)
        {
            var data = dataReader.ReadBytes(compressionTypeOrSize); // not bothering to rent buffer for old versions
            using var outStream = new MemoryStream(data);
            return new VfxStaticComboData(outStream, ZframeId, ParentProgramData);
        }

        uncompressedSize = dataReader.ReadInt32();

        var uncompressedBuffer = ArrayPool<byte>.Shared.Rent(uncompressedSize);

        try
        {
            if (uncompressedSize == LZMA_MAGIC)
            {
                // On PC v64 switched to using zstd, but on mobile builds they still kept the LZMA decompression.
                Debug.Assert(ParentProgramData.VcsVersion <= 64);

                uncompressedSize = dataReader.ReadInt32();
                var compressedSize2 = dataReader.ReadInt32();

                var lzmaDecoder = new SevenZip.Compression.LZMA.Decoder();
                lzmaDecoder.SetDecoderProperties(dataReader.ReadBytes(5));

                using var outStream = new MemoryStream(uncompressedBuffer, 0, uncompressedSize);
                lzmaDecoder.Code(dataReader.BaseStream, outStream, compressedSize2, uncompressedSize, null);
                outStream.Position = 0;
                return new VfxStaticComboData(outStream, ZframeId, ParentProgramData);
            }

            var compressionType = -compressionTypeOrSize; // it's negative

            var compressedSize = dataReader.ReadInt32();

            switch (compressionType)
            {
                case 1:
                    throw new NotImplementedException("Uncompressed block");

                case 2:
                    throw new NotImplementedException("ZSTD compresed without dict");

                case 3:
                    using (var zstdDecompressor = new ZstdSharp.Decompressor())
                    {
                        zstdDecompressor.LoadDictionary(ZstdDictionary.GetDictionary());

                        var inputBuf = ArrayPool<byte>.Shared.Rent(compressedSize);

                        try
                        {
                            var input = inputBuf.AsSpan(0, compressedSize);
                            dataReader.Read(input);

                            if (!zstdDecompressor.TryUnwrap(input, uncompressedBuffer, out var written) || uncompressedSize != written)
                            {
                                throw new InvalidDataException($"Failed to decompress ZSTD (expected {uncompressedSize} bytes, got {written})");
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(inputBuf);
                        }
                    }
                    break;

                case 4:
                    throw new NotImplementedException("LZ4 compressed");

                default:
                    throw new UnexpectedMagicException("Unknown compression", compressionType);
            }

            using var stream = new MemoryStream(uncompressedBuffer, 0, uncompressedSize);
            return new VfxStaticComboData(stream, ZframeId, ParentProgramData);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(uncompressedBuffer);
        }
    }

#if false
    public override string ToString()
    {
        var comprDesc = CompressionType switch
        {
            UNCOMPRESSED => "uncompressed",
            ZSTD_COMPRESSION => "ZSTD",
            LZMA_COMPRESSION => "LZMA",
            _ => "undetermined"
        };
        return $"zframeId[0x{ZframeId:x08}] {comprDesc} offset={OffsetToZFrameHeader,8} " +
            $"compressedLength={CompressedLength,7} uncompressedLength={UncompressedLength,9}";
    }
#endif
}
