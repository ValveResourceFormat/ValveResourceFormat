using System.Buffers;
using System.Diagnostics;
using System.IO;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Compression;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    public partial class BinaryKV3 : ResourceData
    {
        private static readonly Guid KV3_ENCODING_BINARY_BLOCK_COMPRESSED = new([0x46, 0x1A, 0x79, 0x95, 0xBC, 0x95, 0x6C, 0x4F, 0xA7, 0x0B, 0x05, 0xBC, 0xA1, 0xB7, 0xDF, 0xD2]);
        private static readonly Guid KV3_ENCODING_BINARY_UNCOMPRESSED = new([0x00, 0x05, 0x86, 0x1B, 0xD8, 0xF7, 0xC1, 0x40, 0xAD, 0x82, 0x75, 0xA4, 0x82, 0x67, 0xE7, 0x14]);
        private static readonly Guid KV3_ENCODING_BINARY_BLOCK_LZ4 = new([0x8A, 0x34, 0x47, 0x68, 0xA1, 0x63, 0x5C, 0x4F, 0xA1, 0x97, 0x53, 0x80, 0x6F, 0xD9, 0xB1, 0x19]);
        private static readonly Guid KV3_FORMAT_GENERIC = new([0x7C, 0x16, 0x12, 0x74, 0xE9, 0x06, 0x98, 0x46, 0xAF, 0xF2, 0xE6, 0x3E, 0xB5, 0x90, 0x37, 0xE7]);

        private void ReadVersion0(BinaryReader reader)
        {
            var context = new Context
            {
                Version = 0,
            };

            Encoding = new Guid(reader.ReadBytes(16));
            Format = new Guid(reader.ReadBytes(16));

            // Valve's implementation lives in LoadKV3Binary()
            // KV3_ENCODING_BINARY_BLOCK_COMPRESSED calls CBlockCompress::FastDecompress()
            // and then it proceeds to call LoadKV3BinaryUncompressed, which should be the same routine for KV3_ENCODING_BINARY_UNCOMPRESSED
            // Old binary with debug symbols for ref: https://users.alliedmods.net/~asherkin/public/bins/dota_symbols/bin/osx64/libmeshsystem.dylib

            byte[] outputBuf = null;

            try
            {
                int outBufferLength;

                if (Encoding.CompareTo(KV3_ENCODING_BINARY_BLOCK_COMPRESSED) == 0)
                {
                    var info = BlockCompress.GetDecompressedSize(reader);
                    outBufferLength = info.Size;
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);

                    BlockCompress.FastDecompress(info, reader, outputBuf.AsSpan(0, outBufferLength));
                }
                else if (Encoding.CompareTo(KV3_ENCODING_BINARY_BLOCK_LZ4) == 0)
                {
                    outBufferLength = reader.ReadInt32();
                    var compressedSize = (int)(Size - (reader.BaseStream.Position - Offset));
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    DecompressLZ4(reader, outputBuf.AsSpan(0, outBufferLength), compressedSize);
                }
                else if (Encoding.CompareTo(KV3_ENCODING_BINARY_UNCOMPRESSED) == 0)
                {
                    outBufferLength = (int)(Size - (reader.BaseStream.Position - Offset));
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    reader.Read(outputBuf.AsSpan(0, outBufferLength));
                }
                else
                {
                    throw new UnexpectedMagicException("Unrecognised KV3 Encoding", Encoding.ToString(), nameof(Encoding));
                }

                using var outStream = new MemoryStream(outputBuf, 0, outBufferLength);
                using var outRead = new BinaryReader(outStream, System.Text.Encoding.UTF8, true);

                var stringCount = outRead.ReadUInt32();
                context.Strings = new string[stringCount];

                for (var i = 0; i < stringCount; i++)
                {
                    context.Strings[i] = outRead.ReadNullTermString(System.Text.Encoding.UTF8);
                }

                Data = LegacyParseBinaryKV3(context, outRead, null, true);

                var trailer = outRead.ReadUInt32();
                if (trailer != 0xFFFFFFFF)
                {
                    throw new UnexpectedMagicException("Invalid trailer", trailer, nameof(trailer));
                }
            }
            finally
            {
                if (outputBuf != null)
                {
                    ArrayPool<byte>.Shared.Return(outputBuf);
                }
            }
        }

        private static (KV3BinaryNodeType Type, KVFlag Flag) LegacyReadType(BinaryReader reader)
        {
            var databyte = reader.ReadByte();
            var flagInfo = KVFlag.None;

            if ((databyte & 0x80) > 0)
            {
                databyte &= 0x7F; // Remove the flag bit

                flagInfo = (KVFlag)reader.ReadByte();

                if (((int)flagInfo & 4) > 0) // Multiline string
                {
                    Debug.Assert(databyte == (int)KV3BinaryNodeType.STRING);
                    flagInfo ^= (KVFlag)4;
                }

                // Strictly speaking there could be more than one flag set, but in practice it was seemingly never.
                // Valve's new code just sets whichever flag is highest, new kv3 version does not support multiple flags at once.
                flagInfo = (int)flagInfo switch
                {
                    0 => KVFlag.None,
                    1 => KVFlag.Resource,
                    2 => KVFlag.ResourceName,
                    8 => KVFlag.Panorama,
                    16 => KVFlag.SoundEvent,
                    32 => KVFlag.SubClass,
                    _ => throw new UnexpectedMagicException("Unexpected kv3 flag", (int)flagInfo, nameof(flagInfo))
                };
            }

            return ((KV3BinaryNodeType)databyte, flagInfo);
        }

        private static KVObject LegacyParseBinaryKV3(Context context, BinaryReader reader, KVObject parent, bool inArray = false)
        {
            string name = null;
            if (!inArray)
            {
                var stringID = reader.ReadInt32();
                name = (stringID == -1) ? string.Empty : context.Strings[stringID];
            }

            var (datatype, flagInfo) = LegacyReadType(reader);

            return LegacyReadBinaryValue(context, name, datatype, flagInfo, reader, parent);
        }

        private static KVObject LegacyReadBinaryValue(Context context, string name, KV3BinaryNodeType datatype, KVFlag flagInfo, BinaryReader reader, KVObject parent)
        {
            var currentOffset = reader.BaseStream.Position;

            // We don't support non-object roots properly, so this is a hack to handle "null" kv3
            if (datatype != KV3BinaryNodeType.OBJECT && parent == null)
            {
                name ??= "root";
                parent ??= new KVObject(name);
            }

            switch (datatype)
            {
                case KV3BinaryNodeType.NULL:
                    parent.AddProperty(name, MakeValue(datatype, null, flagInfo));
                    break;
                case KV3BinaryNodeType.BOOLEAN:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadBoolean(), flagInfo));
                    break;
                case KV3BinaryNodeType.BOOLEAN_TRUE:
                    parent.AddProperty(name, MakeValue(datatype, true, flagInfo));
                    break;
                case KV3BinaryNodeType.BOOLEAN_FALSE:
                    parent.AddProperty(name, MakeValue(datatype, false, flagInfo));
                    break;
                case KV3BinaryNodeType.INT64_ZERO:
                    parent.AddProperty(name, MakeValue(datatype, 0L, flagInfo));
                    break;
                case KV3BinaryNodeType.INT64_ONE:
                    parent.AddProperty(name, MakeValue(datatype, 1L, flagInfo));
                    break;
                case KV3BinaryNodeType.INT64:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadInt64(), flagInfo));
                    break;
                case KV3BinaryNodeType.UINT64:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadUInt64(), flagInfo));
                    break;
                case KV3BinaryNodeType.INT32:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadInt32(), flagInfo));
                    break;
                case KV3BinaryNodeType.UINT32:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadUInt32(), flagInfo));
                    break;
                case KV3BinaryNodeType.DOUBLE:
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadDouble(), flagInfo));
                    break;
                case KV3BinaryNodeType.DOUBLE_ZERO:
                    parent.AddProperty(name, MakeValue(datatype, 0.0D, flagInfo));
                    break;
                case KV3BinaryNodeType.DOUBLE_ONE:
                    parent.AddProperty(name, MakeValue(datatype, 1.0D, flagInfo));
                    break;
                case KV3BinaryNodeType.STRING:
                    var id = reader.ReadInt32();
                    parent.AddProperty(name, MakeValue(datatype, id == -1 ? string.Empty : context.Strings[id], flagInfo));
                    break;
                case KV3BinaryNodeType.BINARY_BLOB:
                    var length = reader.ReadInt32();
                    parent.AddProperty(name, MakeValue(datatype, reader.ReadBytes(length), flagInfo));
                    break;
                case KV3BinaryNodeType.ARRAY:
                    var arrayLength = reader.ReadInt32();
                    var array = new KVObject(name, isArray: true, capacity: arrayLength);

                    for (var i = 0; i < arrayLength; i++)
                    {
                        LegacyParseBinaryKV3(context, reader, array, true);
                    }

                    parent.AddProperty(name, MakeValue(datatype, array, flagInfo));
                    break;
                case KV3BinaryNodeType.ARRAY_TYPED:
                    var typeArrayLength = reader.ReadInt32();
                    var (subType, subFlagInfo) = LegacyReadType(reader);
                    var typedArray = new KVObject(name, isArray: true, capacity: typeArrayLength);

                    for (var i = 0; i < typeArrayLength; i++)
                    {
                        LegacyReadBinaryValue(context, name, subType, subFlagInfo, reader, typedArray);
                    }

                    parent.AddProperty(name, MakeValue(datatype, typedArray, flagInfo));
                    break;
                case KV3BinaryNodeType.OBJECT:
                    var objectLength = reader.ReadInt32();
                    var newObject = new KVObject(name, isArray: false, capacity: objectLength);

                    for (var i = 0; i < objectLength; i++)
                    {
                        LegacyParseBinaryKV3(context, reader, newObject, false);
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
                    throw new UnexpectedMagicException($"Unknown KVType for field '{name}' on byte {reader.BaseStream.Position}", (int)datatype, nameof(datatype));
            }

            return parent;
        }
    }
}
