using System.Buffers;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Compression;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public partial class BinaryKV3 : Block
    {
        private void ReadVersion0(BinaryReader reader)
        {
            var context = new Context
            {
                Version = 0,
            };

            var encoding = KV3IDLookup.GetByValue(new Guid(reader.ReadBytes(16)));
            var format = KV3IDLookup.GetByValue(new Guid(reader.ReadBytes(16)));

            // Valve's implementation lives in LoadKV3Binary()
            // KV3_ENCODING_BINARY_BLOCK_COMPRESSED calls CBlockCompress::FastDecompress()
            // and then it proceeds to call LoadKV3BinaryUncompressed, which should be the same routine for KV3_ENCODING_BINARY_UNCOMPRESSED
            // Old binary with debug symbols for ref: https://users.alliedmods.net/~asherkin/public/bins/dota_symbols/bin/osx64/libmeshsystem.dylib

            byte[]? outputBuf = null;

            try
            {
                int outBufferLength;

                if (encoding == KV3IDLookup.Get("binary_bc"))
                {
                    var info = BlockCompress.GetDecompressedSize(reader);
                    outBufferLength = info.Size;
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);

                    BlockCompress.FastDecompress(info, reader, outputBuf.AsSpan(0, outBufferLength));
                }
                else if (encoding == KV3IDLookup.Get("binary_lz4"))
                {
                    outBufferLength = reader.ReadInt32();
                    var compressedSize = (int)(Size - (reader.BaseStream.Position - Offset));
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    DecompressLZ4(reader, outputBuf.AsSpan(0, outBufferLength), compressedSize);
                }
                else if (encoding == KV3IDLookup.Get("binary") || encoding == KV3IDLookup.Get("binary_zstd") || encoding == KV3IDLookup.Get("binary_auto"))
                {
                    // Only block compression and LZ4 were ever implemented for this header, other binary encodings are stored as is
                    outBufferLength = (int)(Size - (reader.BaseStream.Position - Offset));
                    outputBuf = ArrayPool<byte>.Shared.Rent(outBufferLength);
                    reader.Read(outputBuf.AsSpan(0, outBufferLength));
                }
                else
                {
                    throw new UnexpectedMagicException("Unrecognised KV3 Encoding", encoding.ToString(), nameof(encoding));
                }

                using var outStream = new MemoryStream(outputBuf, 0, outBufferLength);
                using var outRead = new BinaryReader(outStream, System.Text.Encoding.UTF8, true);

                var stringCount = outRead.ReadUInt32();
                context.Strings = new string[stringCount];

                for (var i = 0; i < stringCount; i++)
                {
                    context.Strings[i] = outRead.ReadNullTermString(System.Text.Encoding.UTF8);
                }

                var (rootType, rootFlag) = LegacyReadType(outRead);
                var root = LegacyReadBinaryValue(context, rootType, rootFlag, outRead);

                // Do not set encoding on the header here
                Data = new KVDocument(new KVHeader { Format = format }, null, root);

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

            if ((databyte & 0x80) != 0)
            {
                flagInfo = ConvertLegacyFlags(reader.ReadByte());
            }

            return ((KV3BinaryNodeType)(databyte & 0x7F), flagInfo);
        }

        // Before version 3 flags were a bit field
        private static KVFlag ConvertLegacyFlags(byte flags)
        {
            if ((flags & 0x80) != 0)
            {
                throw new UnexpectedMagicException("Unexpected kv3 flag", flags, nameof(flags));
            }

            // Only one flag can be stored, the lowest set bit wins. Bit 4 marked multiline strings and is dropped.
            if ((flags & 1) != 0)
            {
                return KVFlag.Resource;
            }

            if ((flags & 2) != 0)
            {
                return KVFlag.ResourceName;
            }

            if ((flags & 8) != 0)
            {
                return KVFlag.Panorama;
            }

            if ((flags & 16) != 0)
            {
                return KVFlag.SoundEvent;
            }

            if ((flags & 32) != 0)
            {
                return KVFlag.SubClass;
            }

            return KVFlag.None;
        }

        private static void LegacyParseBinaryKV3(Context context, BinaryReader reader, KVObject parent)
        {
            string? name = null;

            if (!parent.IsArray)
            {
                name = GetString(context, reader.ReadInt32());
            }

            var (datatype, flagInfo) = LegacyReadType(reader);
            var result = LegacyReadBinaryValue(context, datatype, flagInfo, reader);

            if (name != null)
            {
                // A repeated member name replaces the earlier value
                parent[name] = result;
            }
            else
            {
                parent.Add(result);
            }
        }

        private static KVObject LegacyReadBinaryValue(Context context, KV3BinaryNodeType datatype, KVFlag flagInfo, BinaryReader reader)
        {
            var result = LegacyReadValue(context, datatype, reader);

            if (flagInfo != KVFlag.None)
            {
                result.Flag = flagInfo;
            }

            return result;
        }

        private static KVObject LegacyReadValue(Context context, KV3BinaryNodeType datatype, BinaryReader reader)
        {
            switch (datatype)
            {
                case KV3BinaryNodeType.NULL:
                    return KVObject.Null();
                case KV3BinaryNodeType.BOOLEAN:
                    return reader.ReadBoolean();
                case KV3BinaryNodeType.BOOLEAN_TRUE:
                    return true;
                case KV3BinaryNodeType.BOOLEAN_FALSE:
                    return false;
                case KV3BinaryNodeType.INT64_ZERO:
                    return 0L;
                case KV3BinaryNodeType.INT64_ONE:
                    return 1L;
                case KV3BinaryNodeType.INT64:
                    return reader.ReadInt64();
                case KV3BinaryNodeType.UINT64:
                    return reader.ReadUInt64();
                case KV3BinaryNodeType.INT32:
                    return reader.ReadInt32();
                case KV3BinaryNodeType.UINT32:
                    return reader.ReadUInt32();
                case KV3BinaryNodeType.DOUBLE:
                    return reader.ReadDouble();
                case KV3BinaryNodeType.DOUBLE_ZERO:
                    return 0.0D;
                case KV3BinaryNodeType.DOUBLE_ONE:
                    return 1.0D;
                case KV3BinaryNodeType.STRING:
                    return GetString(context, reader.ReadInt32());
                case KV3BinaryNodeType.BINARY_BLOB:
                    var length = reader.ReadInt32();
                    return reader.ReadBytes(length);
                case KV3BinaryNodeType.ARRAY:
                    var arrayLength = reader.ReadInt32();
                    var array = KVObject.Array(arrayLength);

                    for (var i = 0; i < arrayLength; i++)
                    {
                        LegacyParseBinaryKV3(context, reader, array);
                    }

                    return array;
                case KV3BinaryNodeType.ARRAY_TYPED:
                    var typeArrayLength = reader.ReadInt32();
                    var (subType, subFlagInfo) = LegacyReadType(reader);
                    var typedArray = KVObject.Array(typeArrayLength);

                    for (var i = 0; i < typeArrayLength; i++)
                    {
                        typedArray.Add(LegacyReadBinaryValue(context, subType, subFlagInfo, reader));
                    }

                    return typedArray;
                case KV3BinaryNodeType.OBJECT:
                    var objectLength = reader.ReadInt32();
                    var newObject = KVObject.Collection(objectLength);

                    for (var i = 0; i < objectLength; i++)
                    {
                        LegacyParseBinaryKV3(context, reader, newObject);
                    }

                    return newObject;
                default:
                    throw new UnexpectedMagicException($"Unknown KVType on byte {reader.BaseStream.Position}", (int)datatype, nameof(datatype));
            }
        }
    }
}
