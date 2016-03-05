using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Text;
using ValveResourceFormat.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class BinaryKV3 : Blocks.ResourceData
    {
        private static readonly byte[] ENCODING = { 0x46, 0x1A, 0x79, 0x95, 0xBC, 0x95, 0x6C, 0x4F, 0xA7, 0x0B, 0x05, 0xBC, 0xA1, 0xB7, 0xDF, 0xD2 };
        private static readonly byte[] FORMAT = { 0x7C, 0x16, 0x12, 0x74, 0xE9, 0x06, 0x98, 0x46, 0xAF, 0xF2, 0xE6, 0x3E, 0xB5, 0x90, 0x37, 0xE7 };
        private static readonly byte[] SIG = { 0x56, 0x4B, 0x56, 0x03 }; // VKV3 (3 isn't ascii, its 0x03)

        public KVObject Data { get; private set; }
        private string[] stringArray;

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;
            var outStream = new MemoryStream();
            BinaryWriter outWrite = new BinaryWriter(outStream);
            BinaryReader outRead = new BinaryReader(outStream); // Why why why why why why why

            var sig = reader.ReadBytes(4);
            if (Encoding.ASCII.GetString(sig) != Encoding.ASCII.GetString(SIG))
            {
                throw new InvalidDataException("Invalid KV Signature");
            }

            // outWrite.Write(sig);
            var encoding = reader.ReadBytes(16);
            if (Encoding.ASCII.GetString(encoding) != Encoding.ASCII.GetString(ENCODING))
            {
                throw new InvalidDataException("Unrecognized KV3 Encoding");
            }

            // outWrite.Write(encoding);
            var format = reader.ReadBytes(16);
            if (Encoding.ASCII.GetString(format) != Encoding.ASCII.GetString(FORMAT))
            {
                throw new InvalidDataException("Unrecognised KV3 Format");
            }

            // outWrite.Write(format);

            // Ok we are 100% sure its now KV, good

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
                        for (int i = 0; i < 16; i++)
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

            Data = ParseBinaryKV3(outRead, null, true);
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
            KVFlag flagInfo = KVFlag.None;
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
            else
            {
                return new KVValue((KVType)type, data);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            Data.Serialize(writer);
        }
    }
}