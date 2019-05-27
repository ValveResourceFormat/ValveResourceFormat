using System;
using System.Collections;
using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.KeyValues;
using Decoder = SevenZip.Compression.LZMA.Decoder;

namespace ValveResourceFormat.ResourceTypes
{
    public class BinaryKV3 : ResourceData
    {
#pragma warning disable SA1310 // Field names should not contain underscore
        private static readonly byte[] KV3_ENCODING_BINARY_BLOCK_COMPRESSED = { 0x46, 0x1A, 0x79, 0x95, 0xBC, 0x95, 0x6C, 0x4F, 0xA7, 0x0B, 0x05, 0xBC, 0xA1, 0xB7, 0xDF, 0xD2 };
        private static readonly byte[] KV3_ENCODING_BINARY_UNCOMPRESSED = { 0x00, 0x05, 0x86, 0x1B, 0xD8, 0xF7, 0xC1, 0x40, 0xAD, 0x82, 0x75, 0xA4, 0x82, 0x67, 0xE7, 0x14 };
        private static readonly byte[] KV3_ENCODING_BINARY_POSSIBLY_LZ4 = { 0x8A, 0x34, 0x47, 0x68, 0xA1, 0x63, 0x5C, 0x4F, 0xA1, 0x97, 0x53, 0x80, 0x6F, 0xD9, 0xB1, 0x19 };
        private static readonly byte[] KV3_FORMAT_GENERIC = { 0x7C, 0x16, 0x12, 0x74, 0xE9, 0x06, 0x98, 0x46, 0xAF, 0xF2, 0xE6, 0x3E, 0xB5, 0x90, 0x37, 0xE7 };
        public const int MAGIC = 0x03564B56; // VKV3 (3 isn't ascii, its 0x03)
#pragma warning restore SA1310

        public KVObject Data { get; private set; }
        private string[] stringArray;

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;
            var outStream = new MemoryStream();
            var outWrite = new BinaryWriter(outStream);
            var outRead = new BinaryReader(outStream); // Why why why why why why why

            if (reader.ReadUInt32() != MAGIC)
            {
                throw new InvalidDataException("Invalid KV Signature");
            }

            var encoding = reader.ReadBytes(16);
            var format = reader.ReadBytes(16);

            // Valve's implementation lives in LoadKV3Binary()
            // KV3_ENCODING_BINARY_BLOCK_COMPRESSED calls CBlockCompress::FastDecompress()
            // and then it proceeds to call LoadKV3BinaryUncompressed, which should be the same routine for KV3_ENCODING_BINARY_UNCOMPRESSED
            // Old binary with debug symbols for ref: https://users.alliedmods.net/~asherkin/public/bins/dota_symbols/bin/osx64/libmeshsystem.dylib

            if (StructuralComparisons.StructuralEqualityComparer.Equals(encoding, KV3_ENCODING_BINARY_BLOCK_COMPRESSED))
            {
                BlockDecompress(reader, outWrite, outRead);
            }
            else if (StructuralComparisons.StructuralEqualityComparer.Equals(encoding, KV3_ENCODING_BINARY_POSSIBLY_LZ4))
            {
                DecompressLZ4(reader, outWrite);
            }
            else if (StructuralComparisons.StructuralEqualityComparer.Equals(encoding, KV3_ENCODING_BINARY_UNCOMPRESSED))
            {
                // Nothing to do here
            }
            else
            {
                throw new InvalidDataException("Unrecognised KV3 Encoding");
            }

            if (!StructuralComparisons.StructuralEqualityComparer.Equals(format, KV3_FORMAT_GENERIC))
            {
                throw new InvalidDataException("Unrecognised KV3 Format");
            }

            Data = ParseBinaryKV3(outRead, null, true);
        }

        private void BlockDecompress(BinaryReader reader, BinaryWriter outWrite, BinaryReader outRead)
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
            var stringCount = outRead.ReadUInt32(); //Assuming UInt
            stringArray = new string[stringCount];
            for (var i = 0; i < stringCount; i++)
            {
                stringArray[i] = outRead.ReadNullTermString(Encoding.UTF8);
            }
        }

        private void DecompressLZ4(BinaryReader reader, BinaryWriter outWrite)
        {
            // TODO
            var decoder = new Decoder();
            decoder.Code(reader.BaseStream, outWrite.BaseStream, Size, reader.ReadUInt32(), null);
        }

        private KVObject ParseBinaryKV3(BinaryReader reader, KVObject parent, bool inArray = false)
        {
            string name = null;
            if (!inArray)
            {
                var stringID = reader.ReadInt32();
                name = (stringID == -1) ? string.Empty : stringArray[stringID];
            }

            var datatype = reader.ReadByte();
            var flagInfo = KVFlag.None;
            if ((datatype & 0x80) > 0)
            {
                datatype &= 0x7F; //Remove the flag bit.
                flagInfo = (KVFlag)reader.ReadByte();
            }

            switch (datatype)
            {
                case (byte)KVType.NULL:
                    parent.AddProperty(name, MakeValue(datatype, null, flagInfo));
                    break;
                case (byte)KVType.BOOLEAN:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadBoolean(), flagInfo));
                    break;
                case (byte)KVType.INTEGER:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadInt64(), flagInfo));
                    break;
                case (byte)KVType.DOUBLE:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadDouble(), flagInfo));
                    break;
                case (byte)KVType.STRING:
                    var id = reader.ReadInt32();
                    parent.AddProperty(name, MakeValue(datatype, id == -1 ? string.Empty : stringArray[id], flagInfo));
                    break;
                case (byte)KVType.ARRAY:
                    var arrayLength = reader.ReadInt32(); //UInt or Int?
                    var array = new KVObject(name, true);
                    for (var i = 0; i < arrayLength; i++)
                    {
                        ParseBinaryKV3(reader, array, true);
                    }

                    parent.AddProperty(name, MakeValue(datatype, array, flagInfo));
                    break;
                case (byte)KVType.OBJECT:
                    var objectLength = reader.ReadInt32(); //UInt or Int?
                    var newObject = new KVObject(name, false);
                    for (var i = 0; i < objectLength; i++)
                    {
                        ParseBinaryKV3(reader, newObject, false);
                    }

                    if (parent == null)
                    {
                        parent = newObject;
                    }
                    else
                    {
                        parent.AddProperty(name, MakeValue(datatype, newObject, flagInfo));
                    }

                    break;
                default:
                    throw new InvalidDataException(string.Format("Unknown KVType {0}", datatype));
            }

            return parent;
        }

        private KVValue MakeValue(byte type, object data, KVFlag flag)
        {
            if (flag != KVFlag.None)
            {
                return new KVFlaggedValue((KVType)type, flag, data);
            }

            return new KVValue((KVType)type, data);
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            Data.Serialize(writer);
        }
    }
}
