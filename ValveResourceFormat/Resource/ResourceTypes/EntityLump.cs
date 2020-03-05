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

            public string Name { get; set; }

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

                if (a != 1)
                {
                    throw new NotImplementedException($"First field in entity lump is not 1");
                }

                var hashedFieldsCount = dataReader.ReadUInt32();
                var stringFieldsCount = dataReader.ReadUInt32();

                var properties = new Dictionary<uint, EntityProperty>();

                void ReadTypedValue(uint keyHash, string keyName)
                {
                    var type = dataReader.ReadUInt32();
                    var entityProperty = new EntityProperty
                    {
                        Type = type,
                        Name = keyName,
                    };

                    switch (type)
                    {
                        case 0x06: // boolean
                            entityProperty.Data = dataReader.ReadBoolean(); // 1
                            break;
                        case 0x01: // float
                            entityProperty.Data = dataReader.ReadSingle(); // 4
                            break;
                        case 0x09: // color255
                            entityProperty.Data = dataReader.ReadBytes(4); // 4
                            break;
                        case 0x05: // node_id
                        case 0x25: // flags
                            entityProperty.Data = dataReader.ReadUInt32(); // 4
                            break;
                        case 0x1a: // integer
                            entityProperty.Data = dataReader.ReadUInt64(); // 8
                            break;
                        case 0x03: // vector
                        case 0x27: // angle
                            entityProperty.Data = new Vector3(dataReader.ReadSingle(), dataReader.ReadSingle(), dataReader.ReadSingle()); // 12
                            break;
                        case 0x1e: // string
                            entityProperty.Data = dataReader.ReadNullTermString(Encoding.UTF8); // null term variable
                            break;
                        default:
                            throw new NotImplementedException($"Unknown type {type}");
                    }

                    properties.Add(keyHash, entityProperty);
                }

                for (var i = 0; i < hashedFieldsCount; i++)
                {
                    // murmur2 hashed field name (see EntityLumpKeyLookup)
                    var keyHash = dataReader.ReadUInt32();

                    ReadTypedValue(keyHash, null);
                }

                for (var i = 0; i < stringFieldsCount; i++)
                {
                    var keyHash = dataReader.ReadUInt32();
                    var keyName = dataReader.ReadNullTermString(Encoding.UTF8);

                    ReadTypedValue(keyHash, keyName);
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
                    else if (property.Value.Name != null)
                    {
                        key = property.Value.Name;
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
