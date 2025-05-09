using System.Buffers;
using System.IO;

namespace ValveResourceFormat.CompiledShader;

public class VfxStaticComboVcsEntry
{
    private const int LZMA_MAGIC = 0x414D5A4C;

    public long ZframeId { get; init; }
    public int OffsetToZFrameHeader { get; init; }

    public byte[] GetDecompressedZFrame(ShaderDataReader dataReader, int version, VcsProgramType programType)
    {
        dataReader.BaseStream.Position = OffsetToZFrameHeader;

        var compressionTypeOrSize = dataReader.ReadInt32();

        if (version <= 64)
        {
            if (programType == VcsProgramType.Features)
            {
                // features are uncompressed
                return dataReader.ReadBytes(compressionTypeOrSize);
            }

            var shouldBeLzmaMagic = dataReader.ReadInt32();

            UnexpectedMagicException.Assert(shouldBeLzmaMagic == LZMA_MAGIC, shouldBeLzmaMagic);

            var uncompressedSize2 = dataReader.ReadInt32();
            var compressedSize2 = dataReader.ReadInt32();

            var lzmaDecoder = new SevenZip.Compression.LZMA.Decoder();
            lzmaDecoder.SetDecoderProperties(dataReader.ReadBytes(5));
            using var outStream = new MemoryStream(uncompressedSize2);
            lzmaDecoder.Code(dataReader.BaseStream, outStream, compressedSize2, uncompressedSize2, null);
            return outStream.ToArray();
        }

        var compressionType = -compressionTypeOrSize; // it's negative

        var uncompressedSize = dataReader.ReadInt32();
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

                        var output = new byte[uncompressedSize];

                        if (!zstdDecompressor.TryUnwrap(input, output, out var written) || output.Length != written)
                        {
                            throw new InvalidDataException($"Failed to decompress ZSTD (expected {output.Length} bytes, got {written})");
                        }

                        return output;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(inputBuf);
                    }
                }

            case 4:
                throw new NotImplementedException("LZ4 compressed");

            default:
                throw new UnexpectedMagicException("Unknown compression", compressionType);
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
