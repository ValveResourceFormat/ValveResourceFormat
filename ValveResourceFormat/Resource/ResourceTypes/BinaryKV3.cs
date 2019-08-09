using System;
using System.IO;
using K4os.Compression.LZ4;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class BinaryKV3 : ResourceData
    {
#pragma warning disable SA1310 // Field names should not contain underscore
        private static readonly Guid KV3_ENCODING_BINARY_BLOCK_COMPRESSED = new Guid(new byte[] { 0x46, 0x1A, 0x79, 0x95, 0xBC, 0x95, 0x6C, 0x4F, 0xA7, 0x0B, 0x05, 0xBC, 0xA1, 0xB7, 0xDF, 0xD2 });
        private static readonly Guid KV3_ENCODING_BINARY_UNCOMPRESSED = new Guid(new byte[] { 0x00, 0x05, 0x86, 0x1B, 0xD8, 0xF7, 0xC1, 0x40, 0xAD, 0x82, 0x75, 0xA4, 0x82, 0x67, 0xE7, 0x14 });
        private static readonly Guid KV3_ENCODING_BINARY_BLOCK_LZ4 = new Guid(new byte[] { 0x8A, 0x34, 0x47, 0x68, 0xA1, 0x63, 0x5C, 0x4F, 0xA1, 0x97, 0x53, 0x80, 0x6F, 0xD9, 0xB1, 0x19 });
        private static readonly Guid KV3_FORMAT_GENERIC = new Guid(new byte[] { 0x7C, 0x16, 0x12, 0x74, 0xE9, 0x06, 0x98, 0x46, 0xAF, 0xF2, 0xE6, 0x3E, 0xB5, 0x90, 0x37, 0xE7 });
        public const int MAGIC = 0x03564B56; // VKV3 (3 isn't ascii, its 0x03)
        public const int MAGIC2 = 0x4B563301; // KV3\x01
#pragma warning restore SA1310

        public KVObject Data { get; private set; }
        public Guid Encoding { get; private set; }
        public Guid Format { get; private set; }

        private string[] stringArray;
        private byte[] typesArray;
        private long currentTypeIndex;
        private long currentEightBytesOffset;
        private long currentBinaryBytesOffset = -1;

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;
            var outStream = new MemoryStream();
            var outWrite = new BinaryWriter(outStream);
            var outRead = new BinaryReader(outStream); // Why why why why why why why

            var magic = reader.ReadUInt32();

            if (magic == MAGIC2)
            {
                ReadVersion2(reader, outWrite, outRead);

                return;
            }

            if (magic != MAGIC)
            {
                throw new InvalidDataException($"Invalid KV3 signature {magic}");
            }

            Encoding = new Guid(reader.ReadBytes(16));
            Format = new Guid(reader.ReadBytes(16));

            // Valve's implementation lives in LoadKV3Binary()
            // KV3_ENCODING_BINARY_BLOCK_COMPRESSED calls CBlockCompress::FastDecompress()
            // and then it proceeds to call LoadKV3BinaryUncompressed, which should be the same routine for KV3_ENCODING_BINARY_UNCOMPRESSED
            // Old binary with debug symbols for ref: https://users.alliedmods.net/~asherkin/public/bins/dota_symbols/bin/osx64/libmeshsystem.dylib

            if (Encoding.CompareTo(KV3_ENCODING_BINARY_BLOCK_COMPRESSED) == 0)
            {
                BlockDecompress(reader, outWrite, outRead);
            }
            else if (Encoding.CompareTo(KV3_ENCODING_BINARY_BLOCK_LZ4) == 0)
            {
                DecompressLZ4(reader, outWrite);
            }
            else if (Encoding.CompareTo(KV3_ENCODING_BINARY_UNCOMPRESSED) == 0)
            {
                reader.BaseStream.CopyTo(outStream);
                outStream.Position = 0;
            }
            else
            {
                throw new InvalidDataException($"Unrecognised KV3 Encoding: {Encoding.ToString()}");
            }

            var stringCount = outRead.ReadUInt32();
            stringArray = new string[stringCount];
            for (var i = 0; i < stringCount; i++)
            {
                stringArray[i] = outRead.ReadNullTermString(System.Text.Encoding.UTF8);
            }

            Data = ParseBinaryKV3(outRead, null, true);
        }

        private void ReadVersion2(BinaryReader reader, BinaryWriter outWrite, BinaryReader outRead)
        {
            Format = new Guid(reader.ReadBytes(16));

            var compressionMethod = reader.ReadInt32();
            var countOfBinaryBytes = reader.ReadInt32(); // how many bytes (binary blobs)
            var countOfIntegers = reader.ReadInt32(); // how many 4 byte values (ints)
            var countOfEightByteValues = reader.ReadInt32(); // how many 8 byte values (doubles)

            if (compressionMethod == 0)
            {
                var length = reader.ReadInt32();

                var buffer = new byte[length];
                reader.Read(buffer, 0, length);
                outWrite.Write(buffer);
            }
            else if (compressionMethod == 1)
            {
                DecompressLZ4(reader, outWrite);
            }
            else
            {
                throw new Exception($"Unknown KV3 compression method: {compressionMethod}");
            }

            currentBinaryBytesOffset = 0;
            outRead.BaseStream.Position = countOfBinaryBytes;

            if (outRead.BaseStream.Position % 4 != 0)
            {
                // Align to % 4 after binary blobs
                outRead.BaseStream.Position += 4 - (outRead.BaseStream.Position % 4);
            }

            var countOfStrings = outRead.ReadInt32();
            var kvDataOffset = outRead.BaseStream.Position;

            // Subtract one integer since we already read it (countOfStrings)
            outRead.BaseStream.Position += (countOfIntegers - 1) * 4;

            if (outRead.BaseStream.Position % 8 != 0)
            {
                // Align to % 8 for the start of doubles
                outRead.BaseStream.Position += 8 - (outRead.BaseStream.Position % 8);
            }

            currentEightBytesOffset = outRead.BaseStream.Position;

            outRead.BaseStream.Position += countOfEightByteValues * 8;

            stringArray = new string[countOfStrings];

            for (var i = 0; i < countOfStrings; i++)
            {
                stringArray[i] = outRead.ReadNullTermString(System.Text.Encoding.UTF8);
            }

            // bytes after the string table is kv types, minus 4 static bytes at the end
            var typesLength = outRead.BaseStream.Length - 4 - outRead.BaseStream.Position;
            typesArray = new byte[typesLength];

            for (var i = 0; i < typesLength; i++)
            {
                typesArray[i] = outRead.ReadByte();
            }

            // Move back to the start of the KV data for reading.
            outRead.BaseStream.Position = kvDataOffset;

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
        }

        private void DecompressLZ4(BinaryReader reader, BinaryWriter outWrite)
        {
            var uncompressedSize = reader.ReadUInt32();
            var compressedSize = (int)(Size - (reader.BaseStream.Position - Offset));

            var input = reader.ReadBytes(compressedSize);
            var output = new Span<byte>(new byte[uncompressedSize]);

            LZ4Codec.Decode(input, output);

            outWrite.Write(output.ToArray()); // TODO: Write as span
            outWrite.BaseStream.Position = 0;
        }

        private (KVType, KVFlag) ReadType(BinaryReader reader)
        {
            byte databyte;

            if (typesArray != null)
            {
                databyte = typesArray[currentTypeIndex++];
            }
            else
            {
                databyte = reader.ReadByte();
            }

            var flagInfo = KVFlag.None;

            if ((databyte & 0x80) > 0)
            {
                databyte &= 0x7F; // Remove the flag bit

                if (typesArray != null)
                {
                    flagInfo = (KVFlag)typesArray[currentTypeIndex++];
                }
                else
                {
                    flagInfo = (KVFlag)reader.ReadByte();
                }
            }

            return ((KVType)databyte, flagInfo);
        }

        private KVObject ParseBinaryKV3(BinaryReader reader, KVObject parent, bool inArray = false)
        {
            string name = null;
            if (!inArray)
            {
                var stringID = reader.ReadInt32();
                name = (stringID == -1) ? string.Empty : stringArray[stringID];
            }

            var (datatype, flagInfo) = ReadType(reader);

            return ReadBinaryValue(name, datatype, flagInfo, reader, parent);
        }

        private KVObject ReadBinaryValue(string name, KVType datatype, KVFlag flagInfo, BinaryReader reader, KVObject parent)
        {
            var currentOffset = reader.BaseStream.Position;

            switch (datatype)
            {
                case KVType.NULL:
                    parent.AddProperty(name, MakeValue(datatype, null, flagInfo));
                    break;
                case KVType.BOOLEAN:
                    if (currentBinaryBytesOffset > -1)
                    {
                        reader.BaseStream.Position = currentBinaryBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadBoolean(), flagInfo));

                    if (currentBinaryBytesOffset > -1)
                    {
                        currentBinaryBytesOffset++;
                        reader.BaseStream.Position = currentOffset;
                    }

                    break;
                case KVType.BOOLEAN_TRUE:
                    parent.AddProperty(name, MakeValue(datatype, true, flagInfo));
                    break;
                case KVType.BOOLEAN_FALSE:
                    parent.AddProperty(name, MakeValue(datatype, false, flagInfo));
                    break;
                case KVType.INT64_ZERO:
                    parent.AddProperty(name, MakeValue(datatype, 0L, flagInfo));
                    break;
                case KVType.INT64_ONE:
                    parent.AddProperty(name, MakeValue(datatype, 1L, flagInfo));
                    break;
                case KVType.INT64:
                    if (currentEightBytesOffset > 0)
                    {
                        reader.BaseStream.Position = currentEightBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadInt64(), flagInfo));

                    if (currentEightBytesOffset > 0)
                    {
                        currentEightBytesOffset = reader.BaseStream.Position;
                        reader.BaseStream.Position = currentOffset;
                    }

                    break;
                case KVType.UINT64:
                    if (currentEightBytesOffset > 0)
                    {
                        reader.BaseStream.Position = currentEightBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadUInt64(), flagInfo));

                    if (currentEightBytesOffset > 0)
                    {
                        currentEightBytesOffset = reader.BaseStream.Position;
                        reader.BaseStream.Position = currentOffset;
                    }

                    break;
                case KVType.INT32:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadInt32(), flagInfo));
                    break;
                case KVType.UINT32:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadUInt32(), flagInfo));
                    break;
                case KVType.DOUBLE:
                    if (currentEightBytesOffset > 0)
                    {
                        reader.BaseStream.Position = currentEightBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadDouble(), flagInfo));

                    if (currentEightBytesOffset > 0)
                    {
                        currentEightBytesOffset = reader.BaseStream.Position;
                        reader.BaseStream.Position = currentOffset;
                    }

                    break;
                case KVType.DOUBLE_ZERO:
                    parent.AddProperty(name, MakeValue(datatype, 0.0D, flagInfo));
                    break;
                case KVType.DOUBLE_ONE:
                    parent.AddProperty(name, MakeValue(datatype, 1.0D, flagInfo));
                    break;
                case KVType.STRING:
                    var id = reader.ReadInt32();
                    parent.AddProperty(name, MakeValue(datatype, id == -1 ? string.Empty : stringArray[id], flagInfo));
                    break;
                case KVType.BINARY_BLOB:
                    var length = reader.ReadInt32();

                    if (currentBinaryBytesOffset > -1)
                    {
                        reader.BaseStream.Position = currentBinaryBytesOffset;
                    }

                    parent.AddProperty(name, MakeValue(datatype, reader.ReadBytes(length), flagInfo));

                    if (currentBinaryBytesOffset > -1)
                    {
                        currentBinaryBytesOffset = reader.BaseStream.Position;
                        reader.BaseStream.Position = currentOffset + 4;
                    }

                    break;
                case KVType.ARRAY:
                    var arrayLength = reader.ReadInt32();
                    var array = new KVObject(name, true);
                    for (var i = 0; i < arrayLength; i++)
                    {
                        ParseBinaryKV3(reader, array, true);
                    }

                    parent.AddProperty(name, MakeValue(datatype, array, flagInfo));
                    break;
                case KVType.ARRAY_TYPED:
                    var typeArrayLength = reader.ReadInt32();
                    var (subType, subFlagInfo) = ReadType(reader);
                    var typedArray = new KVObject(name, true);

                    for (var i = 0; i < typeArrayLength; i++)
                    {
                        ReadBinaryValue(name, subType, subFlagInfo, reader, typedArray);
                    }

                    parent.AddProperty(name, MakeValue(datatype, typedArray, flagInfo));
                    break;
                case KVType.OBJECT:
                    var objectLength = reader.ReadInt32();
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
                    throw new InvalidDataException($"Unknown KVType {datatype} for field '{name}' on byte {reader.BaseStream.Position - 1}");
            }

            return parent;
        }

        private static KVType ConvertBinaryOnlyKVType(KVType type)
        {
            switch (type)
            {
                case KVType.BOOLEAN:
                case KVType.BOOLEAN_TRUE:
                case KVType.BOOLEAN_FALSE:
                    return KVType.BOOLEAN;
                case KVType.INT64:
                case KVType.INT32:
                case KVType.INT64_ZERO:
                case KVType.INT64_ONE:
                    return KVType.INT64;
                case KVType.UINT64:
                case KVType.UINT32:
                    return KVType.UINT64;
                case KVType.DOUBLE:
                case KVType.DOUBLE_ZERO:
                case KVType.DOUBLE_ONE:
                    return KVType.DOUBLE;
                case KVType.ARRAY_TYPED:
                    return KVType.ARRAY;
            }

            return type;
        }

        private static KVValue MakeValue(KVType type, object data, KVFlag flag)
        {
            var realType = ConvertBinaryOnlyKVType(type);

            if (flag != KVFlag.None)
            {
                return new KVFlaggedValue(realType, flag, data);
            }

            return new KVValue(realType, data);
        }

        public KV3File GetKV3File()
        {
            // TODO: Other format guids are not "generic" but strings like "vpc19"
            return new KV3File(Data, format: $"generic:version{{{Format.ToString()}}}");
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            Data.Serialize(writer);
        }
    }
}
