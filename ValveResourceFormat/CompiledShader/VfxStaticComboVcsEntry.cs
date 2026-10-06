using System.Diagnostics;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Directory entry for a static combo, holding what is needed to locate and lazily unserialize it.
/// </summary>
public class VfxStaticComboVcsEntry
{
    private const int LZMA_MAGIC = 0x414D5A4C;

    /// <summary>Gets the parent program data.</summary>
    public required VfxProgramData ParentProgramData { get; init; }
    /// <summary>Gets the static combo identifier.</summary>
    public long StaticComboId { get; init; }
    /// <summary>Gets the file offset.</summary>
    public int FileOffset { get; init; }

    /// <summary>
    /// Resource entry for KeyValues-based files.
    /// </summary>
    public record ResourceEntry(KVObject ComboData, VfxShaderAttribute[] AllAttributes, IReadOnlyList<KVObject> ByteCodeDescArray);

    /// <summary>Gets the entry data of KeyValues-based files.</summary>
    public ResourceEntry? ResourceData { get; init; }

    /// <summary>
    /// Unserializes the static combo data.
    /// </summary>
    public VfxStaticComboData Unserialize()
    {
        if (ResourceData is not null)
        {
            return new VfxStaticComboData(
                ResourceData.ComboData,
                StaticComboId,
                ResourceData.AllAttributes,
                ResourceData.ByteCodeDescArray,
                ParentProgramData
            );
        }

        // CVfxStaticComboData::Unserialize
        var dataReader = ParentProgramData.DataReader;
        Debug.Assert(dataReader != null);

        dataReader.BaseStream.Position = FileOffset;

        using var pooledStream = GetUncompressedStaticComboDataStream(dataReader);
        pooledStream.Position = 0;
        return new VfxStaticComboData(pooledStream, StaticComboId, ParentProgramData);
    }

    /// <summary>
    /// Decompresses the static combo data stream.
    /// </summary>
    internal static PooledMemoryStream GetUncompressedStaticComboDataStream(BinaryReader reader)
    {
        var compressionTypeOrSize = reader.ReadInt32();

        // Older files prefix each block with its size, followed by an LZMA stream, or by the raw body
        // for features. Whether a given version uses this framing differs between engine builds.
        if (compressionTypeOrSize >= 0)
        {
            if (reader.ReadInt32() != LZMA_MAGIC)
            {
                reader.BaseStream.Position -= 4;

                var uncompressedStream = new PooledMemoryStream(compressionTypeOrSize);
                reader.BaseStream.ReadExactly(uncompressedStream.BufferSpan);
                return uncompressedStream;
            }

            var lzmaUncompressedSize = reader.ReadInt32();
            var lzmaCompressedSize = reader.ReadInt32();

            var lzmaDecoder = new SevenZip.Compression.LZMA.Decoder();
            lzmaDecoder.SetDecoderProperties(reader.ReadBytes(5));

            var outStream = new PooledMemoryStream(lzmaUncompressedSize);
            lzmaDecoder.Code(reader.BaseStream, outStream, lzmaCompressedSize, lzmaUncompressedSize, null);
            return outStream;
        }

        var compressionType = -compressionTypeOrSize;
        var uncompressedSize = reader.ReadInt32();
        var compressedSize = reader.ReadInt32();

        var stream = new PooledMemoryStream(uncompressedSize);

        switch (compressionType)
        {
            case 1: // Uncompressed
                if (compressedSize != uncompressedSize)
                {
                    throw new InvalidDataException($"Uncompressed static combo block has {compressedSize} bytes, expected {uncompressedSize}");
                }

                reader.BaseStream.ReadExactly(stream.BufferSpan);
                break;

            case 2: // ZStd without dictionary
            case 3: // ZStd with dictionary 1
            case 5: // ZStd with dictionary 2
                using (var zstdDecompressor = new ZstdSharp.Decompressor())
                {
                    var dictionary = compressionType switch
                    {
                        3 => ZstdDictionary.GetDictionary_2bc2fa87(),
                        5 => ZstdDictionary.GetDictionary_255df362(),
                        _ => null,
                    };

                    if (dictionary != null)
                    {
                        zstdDecompressor.LoadDictionary(dictionary);
                    }

                    BinaryKV3.DecompressZSTD(zstdDecompressor, reader, stream.BufferSpan, compressedSize);
                }
                break;

            case 4: // Raw LZ4 block, not an LZ4 frame
                BinaryKV3.DecompressLZ4(reader, stream.BufferSpan, compressedSize);
                break;

            default:
                throw new UnexpectedMagicException("Unknown compression", compressionType);
        }

        return stream;
    }
}
