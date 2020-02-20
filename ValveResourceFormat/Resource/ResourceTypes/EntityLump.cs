using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class EntityLump
    {
        public class Entity
        {
            public IEnumerable<EntityProperty> Properties { get; set; }
        }

        public class EntityProperty
        {
            public uint Type { get; set; }

            public uint Key { get; set; }

            public object Data { get; set; }
        }

        private readonly Resource resource;

        public EntityLump(Resource resource)
        {
            this.resource = resource;
        }

        public IKeyValueCollection GetData()
        {
            var data = resource.DataBlock;
            if (data is NTRO ntro)
            {
                return ntro.Output;
            }
            else if (data is BinaryKV3 kv)
            {
                return kv.Data;
            }

            throw new InvalidOperationException($"Unknown entity lump data type {data.GetType().Name}");
        }

        public IEnumerable<string> GetChildEntityNames()
        {
            return GetData().GetArray<string>("m_childLumps");
        }

        public IEnumerable<Entity> GetEntities()
            => GetData().GetArray("m_entityKeyValues")
                .Select(entity => ParseEntityProperties(entity.GetArray<byte>("m_keyValuesData")))
                .ToList();

        private static Entity ParseEntityProperties(byte[] bytes)
        {
            using (var dataStream = new MemoryStream(bytes))
            using (var dataReader = new BinaryReader(dataStream))
            {
                var a = dataReader.ReadUInt32(); // always 1?
                var valuesCount = dataReader.ReadUInt32();
                var c = dataReader.ReadUInt32(); // always 0? (Its been seen to be 1, footer count?)

                var properties = new List<EntityProperty>();
                while (dataStream.Position != dataStream.Length)
                {
                    if (properties.Count == valuesCount)
                    {
                        Console.WriteLine("We hit our values target without reading every byte?!");
                        break;
                    }

                    var miscType = dataReader.ReadUInt32(); // Stuff before type, some pointer?
                    var type = dataReader.ReadUInt32();

                    switch (type)
                    {
                        case 0x06:
                            properties.Add(new EntityProperty
                            {
                                Type = type,
                                Key = miscType,
                                Data = dataReader.ReadByte(),
                            }); //1
                            break;
                        case 0x01:
                            properties.Add(new EntityProperty
                            {
                                Type = type,
                                Key = miscType,
                                Data = dataReader.ReadSingle(),
                            }); //4
                            break;
                        case 0x05:
                        case 0x09:
                        case 0x25: //TODO: figure out the difference
                            properties.Add(new EntityProperty
                            {
                                Type = type,
                                Key = miscType,
                                Data = dataReader.ReadBytes(4),
                            }); //4
                            break;
                        case 0x1a:
                            properties.Add(new EntityProperty
                            {
                                Type = type,
                                Key = miscType,
                                Data = dataReader.ReadUInt64(),
                            }); //8
                            break;
                        case 0x03:
                            properties.Add(new EntityProperty
                            {
                                Type = type,
                                Key = miscType,
                                Data = $"{{{dataReader.ReadSingle()}, {dataReader.ReadSingle()}, {dataReader.ReadSingle()}}}",
                            }); //12
                            break;
                        case 0x27:
                            properties.Add(new EntityProperty
                            {
                                Type = type,
                                Key = miscType,
                                Data = dataReader.ReadBytes(12),
                            }); //12
                            break;
                        case 0x1e:
                            properties.Add(new EntityProperty
                            {
                                Type = type,
                                Key = miscType,
                                Data = dataReader.ReadNullTermString(Encoding.UTF8),
                            });
                            break;
                        default:
                            throw new NotImplementedException($"Unknown type {type}");
                    }
                }

                return new Entity
                {
                    Properties = properties,
                };
            }
        }

        public override string ToString()
        {
            var builder = new StringBuilder();

            var index = 0;
            foreach (var entity in GetEntities())
            {
                builder.AppendLine($"===={index}====\r\n");

                var i = 0;
                foreach (var property in entity.Properties)
                {
                    var value = property.Data;
                    if (value.GetType() == typeof(byte[]))
                    {
                        var tmp = value as byte[];
                        value = $"Array [{string.Join(", ", tmp.Select(p => p.ToString()).ToArray())}]";
                    }

                    switch (property.Key)
                    {
                        case 2433605045:
                            builder.AppendLine($"   {"Ambient Effect",-20} | {value}\n");
                            break;
                        case 2777094460:
                            builder.AppendLine($"   {"Start Disabled",-20} | {value}\n");
                            break;
                        case 3323665506:
                            builder.AppendLine($"   {"Class Name",-20} | {value}\n");
                            break;
                        case 3827302934:
                            builder.AppendLine($"   {"Position",-20} | {value}\n");
                            break;
                        case 3130579663:
                            builder.AppendLine($"   {"Angles",-20} | {value}\n");
                            break;
                        case 432137260:
                            builder.AppendLine($"   {"Scale",-20} | {value}\n");
                            break;
                        case 1226772763:
                            builder.AppendLine($"   {"Disable Shadows",-20} | {value}\n");
                            break;
                        case 3368008710:
                            builder.AppendLine($"   {"World Model",-20} | {value}\n");
                            break;
                        case 1677246174:
                            builder.AppendLine($"   {"FX Colour",-20} | {value}\n");
                            break;
                        case 588463423:
                            builder.AppendLine($"   {"Colour",-20} | {value}\n");
                            break;
                        case 1094168427:
                            builder.AppendLine($"   {"Name",-20} | {value}\n");
                            break;
                        default:
                            builder.AppendLine($"   {i,3}: {value} (type={property.Type}, key={property.Key})\n");
                            break;
                    }

                    ++i;
                }

                builder.AppendLine($"----{index}----\r\n");
                ++index;
            }

            return builder.ToString();
        }
    }
}
