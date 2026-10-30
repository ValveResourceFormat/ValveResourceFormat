using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using ValveResourceFormat.Compression;

namespace ValveResourceFormat.Source1.Bsp
{
    /// <summary>
    /// Opens the pakfile lump of a map, whose entries can be LZMA compressed, which <see cref="ZipArchive"/> cannot read.
    /// </summary>
    internal static class BspPakFileReader
    {
        private const uint EndOfCentralDirectorySignature = 0x06054b50;
        private const uint CentralDirectorySignature = 0x02014b50;
        private const uint LocalHeaderSignature = 0x04034b50;
        private const ushort MethodStored = 0;
        private const ushort MethodDeflate = 8;
        private const ushort MethodLzma = 14;

        /// <summary>Reads the pakfile of a map into an archive whose entries are all readable.</summary>
        /// <param name="bsp">The map.</param>
        /// <returns>The archive, or null when the map has no pakfile.</returns>
        public static ZipArchive? Open(BspFile bsp)
        {
            ArgumentNullException.ThrowIfNull(bsp);

            var data = bsp.GetLumpData(LumpType.PakFile);

            if (data.Length == 0)
            {
                return null;
            }

            var span = data.Span;
            var endOffset = FindEndOfCentralDirectory(span);

            if (endOffset < 0)
            {
                return null;
            }

            var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(span[(endOffset + 10)..]);
            var directoryOffset = BinaryPrimitives.ReadInt32LittleEndian(span[(endOffset + 16)..]);

            var output = new MemoryStream();

            using (var writer = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var position = directoryOffset;

                for (var i = 0; i < entryCount; i++)
                {
                    if (position + 46 > span.Length || BinaryPrimitives.ReadUInt32LittleEndian(span[position..]) != CentralDirectorySignature)
                    {
                        break;
                    }

                    var method = BinaryPrimitives.ReadUInt16LittleEndian(span[(position + 10)..]);
                    var compressedSize = BinaryPrimitives.ReadInt32LittleEndian(span[(position + 20)..]);
                    var uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(span[(position + 24)..]);
                    var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(position + 28)..]);
                    var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(position + 30)..]);
                    var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(position + 32)..]);
                    var localOffset = BinaryPrimitives.ReadInt32LittleEndian(span[(position + 42)..]);
                    var name = System.Text.Encoding.ASCII.GetString(span.Slice(position + 46, nameLength));

                    position += 46 + nameLength + extraLength + commentLength;

                    if (name.EndsWith('/') || localOffset < 0 || localOffset + 30 > span.Length)
                    {
                        continue;
                    }

                    if (BinaryPrimitives.ReadUInt32LittleEndian(span[localOffset..]) != LocalHeaderSignature)
                    {
                        continue;
                    }

                    var localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(localOffset + 26)..]);
                    var localExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(localOffset + 28)..]);
                    var dataStart = localOffset + 30 + localNameLength + localExtraLength;

                    if (dataStart + compressedSize > span.Length)
                    {
                        continue;
                    }

                    var entryData = ReadEntry(span.Slice(dataStart, compressedSize), method, uncompressedSize);

                    if (entryData == null)
                    {
                        continue;
                    }

                    var entry = writer.CreateEntry(name, CompressionLevel.NoCompression);
                    using var entryStream = entry.Open();
                    entryStream.Write(entryData);
                }
            }

            output.Position = 0;
            return new ZipArchive(output, ZipArchiveMode.Read);
        }

        private static byte[]? ReadEntry(ReadOnlySpan<byte> compressed, ushort method, int uncompressedSize)
        {
            if (method == MethodStored)
            {
                return compressed.ToArray();
            }

            if (method == MethodDeflate)
            {
                using var input = new MemoryStream(compressed.ToArray());
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                var result = new byte[uncompressedSize];
                deflate.ReadExactly(result);
                return result;
            }

            if (method != MethodLzma || compressed.Length < 4 + LzmaDecoder.PropertiesSize)
            {
                return null;
            }

            var propertiesLength = BinaryPrimitives.ReadUInt16LittleEndian(compressed[2..]);

            if (propertiesLength < LzmaDecoder.PropertiesSize || 4 + propertiesLength > compressed.Length)
            {
                return null;
            }

            var properties = compressed.Slice(4, LzmaDecoder.PropertiesSize);
            var stream = compressed[(4 + propertiesLength)..];
            var output = new byte[uncompressedSize];

            try
            {
                LzmaDecoder.Decode(properties, stream, output);
            }
            catch (InvalidDataException)
            {
                return null;
            }

            return output;
        }

        private static int FindEndOfCentralDirectory(ReadOnlySpan<byte> span)
        {
            var lowest = Math.Max(0, span.Length - 22 - ushort.MaxValue);

            for (var i = span.Length - 22; i >= lowest; i--)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(span[i..]) == EndOfCentralDirectorySignature)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
