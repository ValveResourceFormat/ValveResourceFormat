using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using ValveResourceFormat.Serialization;
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
                .Select(entity => ParseEntityProperties(entity.GetArray<byte>("m_keyValuesData"), entity.GetArray("m_connections")))
                .ToList();

        private static Entity ParseEntityProperties(byte[] bytes, IKeyValueCollection[] connections)
        {
            using var dataStream = new MemoryStream(bytes);
            using var dataReader = new BinaryReader(dataStream);
            var a = dataReader.ReadUInt32(); // always 1?

            if (a != 1)
            {
                throw new NotImplementedException($"First field in entity lump is not 1");
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
                        EntityFieldType.Color32 => dataReader.ReadBytes(4),
                        EntityFieldType.Integer => dataReader.ReadInt32(),
                        EntityFieldType.UInt => dataReader.ReadUInt32(),
                        EntityFieldType.Integer64 => dataReader.ReadUInt64(),
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

            if (connections.Length > 0)
            {
                entity.Connections = connections.ToList();
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
    }
}
