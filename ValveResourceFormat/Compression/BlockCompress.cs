using System.IO;
using System.Text;

namespace ValveResourceFormat.Compression
{
    public static class BlockCompress
    {
        public static Stream Decompress(BinaryReader reader)
        {
            var outStream = new MemoryStream();

            // Make sure reader/writer don't close the stream once they're done.
            using var outWriter = new BinaryWriter(outStream, Encoding.Default, true);
            using var outReader = new BinaryReader(outStream, Encoding.Default, true);

            FastDecompress(reader, outWriter, outReader);

            return outStream;
        }

        public static Stream Decompress(BinaryReader reader, uint length)
        {
            // Inefficient, copies data while that should technically not be required
            using var tempStream = new MemoryStream(reader.ReadBytes((int)length));
            using var tempReader = new BinaryReader(tempStream);

            return Decompress(tempReader);
        }

        // TODO: Make private and use the sane interface instead.
        public static void FastDecompress(BinaryReader reader, BinaryWriter outWrite, BinaryReader outRead)
        {
            // It is flags, right?
            var flags = reader.ReadBytes(4); // TODO: Figure out what this is

            // outWrite.Write(flags);
            if ((flags[3] & 0x80) > 0)
            {
                outWrite.Write(reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position)));
            }
            else
            {
                var running = true;
                while (reader.BaseStream.Position != reader.BaseStream.Length && running)
                {
                    try
                    {
                        var blockMask = reader.ReadUInt16();
                        for (var i = 0; i < 16; i++)
                        {
                            // is the ith bit 1
                            if ((blockMask & (1 << i)) > 0)
                            {
                                var offsetSize = reader.ReadUInt16();
                                var offset = ((offsetSize & 0xFFF0) >> 4) + 1;
                                var size = (offsetSize & 0x000F) + 3;

                                var lookupSize = (offset < size) ? offset : size; // If the offset is larger or equal to the size, use the size instead.

                                // Kill me now
                                var p = outRead.BaseStream.Position;
                                outRead.BaseStream.Position = p - offset;
                                var data = outRead.ReadBytes(lookupSize);
                                outWrite.BaseStream.Position = p;

                                while (size > 0)
                                {
                                    outWrite.Write(data, 0, (lookupSize < size) ? lookupSize : size);
                                    size -= lookupSize;
                                }
                            }
                            else
                            {
                                var data = reader.ReadByte();
                                outWrite.Write(data);
                            }

                            //TODO: is there a better way of making an unsigned 12bit number?
                            if (outWrite.BaseStream.Length == (flags[2] << 16) + (flags[1] << 8) + flags[0])
                            {
                                running = false;
                                break;
                            }
                        }
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }
                }
            }

            outRead.BaseStream.Position = 0;
        }
    }
}
