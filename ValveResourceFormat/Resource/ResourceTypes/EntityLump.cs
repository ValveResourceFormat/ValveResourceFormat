using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes.NTROSerialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class EntityLump : NTRO
    {
        public List<List<Tuple<uint, uint, object>>> Datas { get; private set; }

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);

            // Output is PermEntityLumpData_t we need to iterate m_entityKeyValues inside it.
            var entityKeyValues = (NTROArray)Output["m_entityKeyValues"];

            Datas = new List<List<Tuple<uint, uint, object>>>();

            foreach (var entityKV in entityKeyValues)
            {
                // entity is EntityKeyValueData_t
                var entity = ((NTROValue<NTROStruct>)entityKV).Value;
                var dataArray = (NTROArray)entity["m_keyValuesData"];
                var data = new List<byte>();
                foreach (NTROValue<byte> entry in dataArray)
                {
                    data.Add(entry.Value);
                }

                using (var dataStream = new MemoryStream(data.ToArray()))
                using (var dataReader = new BinaryReader(dataStream))
                {
                    var a = dataReader.ReadUInt32(); // always 1?
                    var valuesCount = dataReader.ReadUInt32();
                    var c = dataReader.ReadUInt32(); // always 0?

                    var values = new List<Tuple<uint, uint, object>>();
                    while (dataStream.Position != dataStream.Length)
                    {
                        var miscType = dataReader.ReadUInt32(); //Stuff before type, some pointer?
                        var type = dataReader.ReadUInt32();
                        switch (type)
                        {
                            case 0x06:
                                values.Add(new Tuple<uint, uint, object>(type, miscType, dataReader.ReadByte())); //1
                                break;
                            case 0x01:
                                values.Add(new Tuple<uint, uint, object>(type, miscType, dataReader.ReadSingle())); //4
                                break;
                            case 0x05:
                            case 0x09:
                            case 0x25: //TODO: figure out the difference
                                values.Add(new Tuple<uint, uint, object>(type, miscType, dataReader.ReadBytes(4))); //4
                                break;
                            case 0x1a:
                                values.Add(new Tuple<uint, uint, object>(type, miscType, dataReader.ReadUInt64())); //8
                                break;
                            case 0x03:
                                values.Add(new Tuple<uint, uint, object>(type, miscType, $"{{{dataReader.ReadSingle()}, {dataReader.ReadSingle()}, {dataReader.ReadSingle()}}}")); //12
                                break;
                            case 0x27:
                                values.Add(new Tuple<uint, uint, object>(type, miscType, dataReader.ReadBytes(12))); //12
                                break;
                            case 0x1e:
                                values.Add(new Tuple<uint, uint, object>(type, miscType, dataReader.ReadNullTermString(Encoding.UTF8)));
                                break;
                            default:
                                throw new NotImplementedException($"Unknown type {type}");
                        }
                    }

                    Datas.Add(values);
                }
            }
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            for (var index = 0; index < Datas.Count; index++)
            {
                builder.AppendLine($"===={index}====\r\n");
                var entry = Datas[index];
                for (var i = 0; i < entry.Count; i++)
                {
                    var tuple = entry[i];
                    var value = tuple.Item3;
                    if (value.GetType() == typeof(byte[]))
                    {
                        var tmp = value as byte[];
                        value = $"Array [{string.Join(", ", tmp.Select(p => p.ToString()).ToArray())}]";
                    }

                    switch (tuple.Item2)
                    {
                        case 2433605045:
                            builder.AppendLine($"   {"Ambient Effect", -20} | {value}\n");
                            break;
                        case 2777094460:
                            builder.AppendLine($"   {"Start Disabled", -20} | {value}\n");
                            break;
                        case 3323665506:
                            builder.AppendLine($"   {"Class Name", -20} | {value}\n");
                            break;
                        case 3827302934:
                            builder.AppendLine($"   {"Position", -20} | {value}\n");
                            break;
                        case 3130579663:
                            builder.AppendLine($"   {"Angles", -20} | {value}\n");
                            break;
                        case 432137260:
                            builder.AppendLine($"   {"Scale", -20} | {value}\n");
                            break;
                        case 1226772763:
                            builder.AppendLine($"   {"Disable Shadows", -20} | {value}\n");
                            break;
                        case 3368008710:
                            builder.AppendLine($"   {"World Model", -20} | {value}\n");
                            break;
                        case 1677246174:
                            builder.AppendLine($"   {"FX Colour", -20} | {value}\n");
                            break;
                        case 588463423:
                            builder.AppendLine($"   {"Colour", -20} | {value}\n");
                            break;
                        case 1094168427:
                            builder.AppendLine($"   {"Name", -20} | {value}\n");
                            break;
                        default:
                            builder.AppendLine($"   {i, 3}: {value} (type={tuple.Item1}, meta={tuple.Item2})\n");
                            break;
                    }
                }

                builder.AppendLine($"----{index}----\r\n");
            }

            return builder.ToString();
        }
    }
}
