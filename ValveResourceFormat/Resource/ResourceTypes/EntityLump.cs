using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class EntityLump : KeyValuesOrNTRO
    {
        public class Entity
        {
            public Dictionary<uint, EntityProperty> Properties { get; } = new Dictionary<uint, EntityProperty>();
            public List<IKeyValueCollection> Connections { get; internal set; }

            public T GetProperty<T>(string name)
                => GetProperty<T>(StringToken.Get(name));

            public EntityProperty GetProperty(string name)
                => GetProperty(StringToken.Get(name));

            public T GetProperty<T>(uint hash)
            {
                if (Properties.TryGetValue(hash, out var property))
                {
                    return (T)property.Data;
                }

                return default;
            }

            public EntityProperty GetProperty(uint hash)
            {
                if (Properties.TryGetValue(hash, out var property))
                {
                    return property;
                }

                return default;
            }
        }

        public class EntityProperty
        {
            public EntityFieldType Type { get; set; }

            public string Name { get; set; }

            public object Data { get; set; }
        }

        public IEnumerable<string> GetChildEntityNames()
        {
            return Data.GetArray<string>("m_childLumps");
        }

        public IEnumerable<Entity> GetEntities()
            => Data.GetArray("m_entityKeyValues")
                .Select(ParseEntityProperties)
                .ToList();

        private static Entity ParseEntityProperties(IKeyValueCollection entityKv)
        {
            var connections = entityKv.GetArray("m_connections");
            Entity entity;

            if (entityKv.ContainsKey("keyValues3Data"))
            {
                entity = ParseEntityPropertiesKV3(entityKv.GetSubCollection("keyValues3Data"));
            }
            else
            {
                entity = ParseEntityProperties(entityKv.GetArray<byte>("m_keyValuesData"));
            }

            if (connections.Length > 0)
            {
                entity.Connections = connections.ToList();
            }

            return entity;
        }

        private static Entity ParseEntityPropertiesKV3(IKeyValueCollection entityKv)
        {
            var entityVersion = entityKv.GetInt32Property("version");

            if (entityVersion != 1)
            {
                throw new UnexpectedMagicException("Unsupported entity data version", entityVersion, nameof(entityVersion));
            }

            if (entityKv.GetSubCollection("attributes").Any())
            {
                throw new NotImplementedException("Entity has attributes, not yet supported");
            }

            var entity = new Entity();

            var values = ((KVObject)entityKv).Properties["values"];

            if (values.Type != KVType.OBJECT)
            {
                throw new UnexpectedMagicException("Unsupported entity data values type", (int)values.Type, nameof(values.Type));
            }

            foreach (var value in ((KVObject)values.Value).Properties)
            {
                var hash = StringToken.Get(value.Key.ToLowerInvariant());
                var data = value.Value.Value;
                EntityFieldType type;

                if (value.Value.Type == KVType.ARRAY)
                {
                    var arrayKv = (IKeyValueCollection)value.Value.Value;

                    type = arrayKv.Count() switch
                    {
                        2 => EntityFieldType.Vector2d, // Did binary entity lumps not store vec2?
                        3 => EntityFieldType.Vector,
                        _ => throw new NotImplementedException($"Unsupported array length of {arrayKv.Count()}"),
                    };
                    data = type switch
                    {
                        EntityFieldType.Vector2d => new Vector2(arrayKv.GetFloatProperty("0"), arrayKv.GetFloatProperty("1")),
                        EntityFieldType.Vector => arrayKv.ToVector3(),
                        _ => throw new NotImplementedException(),
                    };
                }
                else
                {
                    type = ConvertKV3TypeToEntityFieldType(value.Value.Type);
                }

                var entityProperty = new EntityProperty
                {
                    Type = type,
                    Name = value.Key,
                    Data = data,
                };
                entity.Properties.Add(hash, entityProperty);
            }

            return entity;
        }

        private static Entity ParseEntityProperties(byte[] bytes)
        {
            using var dataStream = new MemoryStream(bytes);
            using var dataReader = new BinaryReader(dataStream);
            var entityVersion = dataReader.ReadUInt32();

            if (entityVersion != 1)
            {
                throw new UnexpectedMagicException("Unsupported entity data version", entityVersion, nameof(entityVersion));
            }

            var hashedFieldsCount = dataReader.ReadUInt32();
            var stringFieldsCount = dataReader.ReadUInt32();

            var entity = new Entity();

            void ReadTypedValue(uint keyHash, string keyName)
            {
                var type = (EntityFieldType)dataReader.ReadUInt32();
                var entityProperty = new EntityProperty
                {
                    Type = type,
                    Name = keyName,
                    Data = type switch
                    {
                        EntityFieldType.Boolean => dataReader.ReadBoolean(),
                        EntityFieldType.Float => dataReader.ReadSingle(),
                        EntityFieldType.Float64 => dataReader.ReadDouble(),
                        EntityFieldType.Color32 => dataReader.ReadBytes(4),
                        EntityFieldType.Integer => dataReader.ReadInt32(),
                        EntityFieldType.UInt => dataReader.ReadUInt32(),
                        EntityFieldType.Integer64 => dataReader.ReadUInt64(), // TODO: Is supposed to be ReadInt64?
                        EntityFieldType.Vector or EntityFieldType.QAngle => new Vector3(dataReader.ReadSingle(), dataReader.ReadSingle(), dataReader.ReadSingle()),
                        EntityFieldType.CString => dataReader.ReadNullTermString(Encoding.UTF8), // null term variable
                        _ => throw new UnexpectedMagicException("Unknown type", (int)type, nameof(type)),
                    }
                };
                entity.Properties.Add(keyHash, entityProperty);
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

            return entity;
        }

        public string ToEntityDumpString()
        {
            var knownKeys = StringToken.InvertedTable;
            var builder = new StringBuilder();
            var unknownKeys = new Dictionary<uint, uint>();

            var index = 0;
            foreach (var entity in GetEntities())
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"===={index++}====");

                foreach (var property in entity.Properties)
                {
                    var value = property.Value.Data;
                    if (value.GetType() == typeof(byte[]))
                    {
                        var tmp = value as byte[];
                        value = $"Array [{string.Join(", ", tmp.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToArray())}]";
                    }

                    string key;

                    if (knownKeys.TryGetValue(property.Key, out var knownKey))
                    {
                        key = knownKey;
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

                    builder.AppendLine(CultureInfo.InvariantCulture, $"{key,-30} {property.Value.Type.ToString(),-10} {value}");
                }

                if (entity.Connections != null)
                {
                    foreach (var connection in entity.Connections)
                    {
                        builder.Append('@');
                        builder.Append(connection.GetProperty<string>("m_outputName"));
                        builder.Append(' ');

                        var delay = connection.GetFloatProperty("m_flDelay");

                        if (delay > 0)
                        {
                            builder.Append(CultureInfo.InvariantCulture, $"Delay={delay} ");
                        }

                        var timesToFire = connection.GetInt32Property("m_nTimesToFire");

                        if (timesToFire == 1)
                        {
                            builder.Append("OnlyOnce ");
                        }
                        else if (timesToFire != -1)
                        {
                            throw new UnexpectedMagicException("Unexpected times to fire", timesToFire, nameof(timesToFire));
                        }

                        builder.Append(connection.GetProperty<string>("m_inputName"));
                        builder.Append(' ');
                        builder.Append(connection.GetProperty<string>("m_targetName"));

                        var param = connection.GetProperty<string>("m_overrideParam");

                        if (!string.IsNullOrEmpty(param) && param != "(null)")
                        {
                            builder.Append(' ');
                            builder.Append(param);
                        }

                        builder.AppendLine();
                    }
                }

                builder.AppendLine();
            }

            if (unknownKeys.Count > 0)
            {
                builder.AppendLine($"@@@@@ UNKNOWN KEY LOOKUPS:");
                builder.AppendLine($"If you know what these are, add them to EntityLumpKnownKeys.cs");

                foreach (var unknownKey in unknownKeys)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"key={unknownKey.Key} hits={unknownKey.Value}");
                }
            }

            return builder.ToString();
        }

        private static EntityFieldType ConvertKV3TypeToEntityFieldType(KVType type)
        {
            return type switch
            {
                KVType.BOOLEAN => EntityFieldType.Boolean,
                KVType.DOUBLE => EntityFieldType.Float64,
                KVType.INT64 => EntityFieldType.Integer, // TODO: Incorrect type?
                KVType.UINT64 => EntityFieldType.Integer64,
                KVType.STRING => EntityFieldType.CString,
                _ => throw new NotImplementedException($"Unsupported kv3 entity data type: {type}")
            };
        }
    }
}
