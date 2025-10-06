using System.Buffers;
using System.Diagnostics;
using System.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Entry point for a static combo in a VCS file.
/// </summary>
public class VfxStaticComboVcsEntry
{
    private const int LZMA_MAGIC = 0x414D5A4C;

    /// <summary>Gets or sets the parent program data.</summary>
    public required VfxProgramData ParentProgramData { get; init; }
    /// <summary>Gets or sets the static combo identifier.</summary>
    public long StaticComboId { get; init; }
    /// <summary>Gets or sets the file offset.</summary>
    public int FileOffset { get; init; }

    /// <summary>
    /// Resource entry for KeyValues-based files.
    /// </summary>
    public record ResourceEntry(KVObject ComboData, VfxShaderAttribute[] AllAttributes, KVObject[] ByteCodeDescArray);

    /// <summary>Gets or sets the KeyValues entry.</summary>
    public ResourceEntry? KVEntry { get; init; }

    /// <summary>
    /// Unserializes the static combo data.
    /// </summary>
    public VfxStaticComboData Unserialize()
    {
        if (KVEntry is not null)
        {
            return new VfxStaticComboData(
                KVEntry.ComboData,
                StaticComboId,
                KVEntry.AllAttributes,
                KVEntry.ByteCodeDescArray,
                ParentProgramData
            );
        }

        // CVfxStaticComboData::Unserialize
        var dataReader = ParentProgramData.DataReader;
        Debug.Assert(dataReader != null);

        dataReader.BaseStream.Position = FileOffset;

        using var pooledStream = GetUncompressedStaticComboDataStream(dataReader, ParentProgramData);
        pooledStream.Position = 0;
        return new VfxStaticComboData(pooledStream, StaticComboId, ParentProgramData);
    }

    /// <summary>
    /// Decompresses the static combo data stream.
    /// </summary>
    public static PooledMemoryStream GetUncompressedStaticComboDataStream(BinaryReader reader, VfxProgramData programData)
    {
        var compressionTypeOrSize = reader.ReadInt32();
        var uncompressedSize = 0;

        if (programData.VcsVersion < 64 && programData.VcsProgramType == VcsProgramType.Features)
        {
            var uncompressedStream = new PooledMemoryStream(compressionTypeOrSize);
            reader.Read(uncompressedStream.BufferSpan);
            return uncompressedStream;
        }

        uncompressedSize = reader.ReadInt32();

        if (uncompressedSize == LZMA_MAGIC)
        {
            // On PC v64 switched to using zstd, but on mobile builds they still kept the LZMA decompression.
            Debug.Assert(programData.VcsVersion <= 64);

            uncompressedSize = reader.ReadInt32();
            var compressedSize2 = reader.ReadInt32();

            var lzmaDecoder = new SevenZip.Compression.LZMA.Decoder();
            lzmaDecoder.SetDecoderProperties(reader.ReadBytes(5));

            var outStream = new PooledMemoryStream(uncompressedSize);
            lzmaDecoder.Code(reader.BaseStream, outStream, compressedSize2, uncompressedSize, null);
            return outStream;
        }

        var stream = new PooledMemoryStream(uncompressedSize);

        var compressionType = -compressionTypeOrSize; // it's negative
        var compressedSize = reader.ReadInt32();

        switch (compressionType)
        {
            case 1:
                throw new NotImplementedException("Uncompressed block");

            case 2:
                throw new NotImplementedException("ZSTD compresed without dict");

            case 3: // ZStd with dictionary 1
            case 5: // ZStd with dictionary 2
                using (var zstdDecompressor = new ZstdSharp.Decompressor())
                {
                    var dictionary = compressionType switch
                    {
                        3 => ZstdDictionary.GetDictionary_2bc2fa87(),
                        5 => ZstdDictionary.GetDictionary_255df362(),
                        _ => throw new NotImplementedException(),
                    };

                    zstdDecompressor.LoadDictionary(dictionary);

                    var inputBuf = ArrayPool<byte>.Shared.Rent(compressedSize);

                    try
                    {
                        var input = inputBuf.AsSpan(0, compressedSize);
                        reader.Read(input);

                        if (!zstdDecompressor.TryUnwrap(input, stream.BufferSpan, out var written) || uncompressedSize != written)
                        {
                            throw new InvalidDataException($"Failed to decompress ZSTD (expected {uncompressedSize} bytes, got {written} {stream.BufferSpan.Length})");
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

        return stream;
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
