using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class EntityLump
    {
        public class Entity
        {
            public Dictionary<uint, EntityProperty> Properties { get; set; }

            public T GetProperty<T>(string name)
                => GetProperty<T>(EntityLumpKeyLookup.Get(name));

            public T GetProperty<T>(uint hash)
            {
                if (Properties.ContainsKey(hash))
                {
                    return (T)Properties[hash].Data;
                }

                return default;
            }
        }

        public class EntityProperty
        {
            public uint Type { get; set; }

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

                var properties = new Dictionary<uint, EntityProperty>();
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
                        case 0x06: // boolean
                            properties.Add(miscType, new EntityProperty
                            {
                                Type = type,
                                Data = dataReader.ReadBoolean(),
                            }); //1
                            break;
                        case 0x01: // float
                            properties.Add(miscType, new EntityProperty
                            {
                                Type = type,
                                Data = dataReader.ReadSingle(),
                            }); //4
                            break;
                        case 0x09: // color255
                            properties.Add(miscType, new EntityProperty
                            {
                                Type = type,
                                Data = dataReader.ReadBytes(4),
                            }); //4
                            break;
                        case 0x05: // node_id
                        case 0x25: // flags
                            properties.Add(miscType, new EntityProperty
                            {
                                Type = type,
                                Data = dataReader.ReadUInt32(),
                            }); //4
                            break;
                        case 0x1a: // integer
                            properties.Add(miscType, new EntityProperty
                            {
                                Type = type,
                                Data = dataReader.ReadUInt64(),
                            }); //8
                            break;
                        case 0x03: // vector
                        case 0x27: // angle
                            properties.Add(miscType, new EntityProperty
                            {
                                Type = type,
                                Data = new Vector3(dataReader.ReadSingle(), dataReader.ReadSingle(), dataReader.ReadSingle()),
                            }); //12
                            break;
                        case 0x1e: // string
                            properties.Add(miscType, new EntityProperty
                            {
                                Type = type,
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
            var knownKeys = new EntityLumpKnownKeys().Fields;
            var builder = new StringBuilder();
            var unknownKeys = new Dictionary<uint, uint>();

            var types = new Dictionary<uint, string>
            {
                { 0x01, "float" },
                { 0x03, "vector" },
                { 0x05, "node_id" },
                { 0x06, "boolean" },
                { 0x09, "color255" },
                { 0x1a, "integer" },
                { 0x1e, "string" },
                { 0x25, "flags" },
                { 0x27, "angle" },
            };

            var index = 0;
            foreach (var entity in GetEntities())
            {
                builder.AppendLine($"===={index++}====");

                foreach (var property in entity.Properties)
                {
                    var value = property.Value.Data;
                    if (value.GetType() == typeof(byte[]))
                    {
                        var tmp = value as byte[];
                        value = $"Array [{string.Join(", ", tmp.Select(p => p.ToString()).ToArray())}]";
                    }

                    string key;

                    if (knownKeys.ContainsKey(property.Key))
                    {
                        key = knownKeys[property.Key];
                    }
                    else
                    {
                        key = $"key={property.Key}";

                        if (!unknownKeys.ContainsKey(property.Key))
                        {
                            unknownKeys.Add(property.Key, 1);
                        }
                        else
                        {
                            unknownKeys[property.Key]++;
                        }
                    }

                    builder.AppendLine($"{key,-30} {types[property.Value.Type],-10} {value}");
                }

                builder.AppendLine();
            }

            if (unknownKeys.Count > 0)
            {
                builder.AppendLine($"@@@@@ UNKNOWN KEY LOOKUPS:");
                builder.AppendLine($"If you know what these are, add them to EntityLumpKnownKeys.cs");

                foreach (var unknownKey in unknownKeys)
                {
                    builder.AppendLine($"key={unknownKey.Key} hits={unknownKey.Value}");
                }
            }

            return builder.ToString();
        }
    }
}
