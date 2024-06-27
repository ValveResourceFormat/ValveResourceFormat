using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class EntityLump : KeyValuesOrNTRO
    {
        public static class CommonHashes
        {
            public static readonly uint Classname = StringToken.Get("classname");
            public static readonly uint Origin = StringToken.Get("origin");
            public static readonly uint Angles = StringToken.Get("angles");
            public static readonly uint Scales = StringToken.Get("scales");
            public static readonly uint HammerUniqueId = StringToken.Get("hammeruniqueid");
            public static readonly uint Model = StringToken.Get("model");
        }

        public class Entity
        {
            public Dictionary<uint, EntityProperty> Properties { get; } = [];
            public List<KVObject> Connections { get; internal set; }

            public T GetProperty<T>(string name, T defaultValue = default)
                => GetProperty<T>(StringToken.Get(name), defaultValue);

            public T GetPropertyUnchecked<T>(string name, T defaultValue = default)
                => GetPropertyUnchecked<T>(StringToken.Get(name), defaultValue);

            public EntityProperty GetProperty(string name)
                => GetProperty(StringToken.Get(name));

            public T GetProperty<T>(uint hash, T defaultValue = default)
            {
                if (Properties.TryGetValue(hash, out var property))
                {
                    return (T)property.Data;
                }

                return defaultValue;
            }

            public T GetPropertyUnchecked<T>(uint hash, T defaultValue = default)
            {
                if (Properties.TryGetValue(hash, out var property))
                {
                    try
                    {
                        return (T)Convert.ChangeType(property.Data, typeof(T), CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        // String format
                    }
                }

                return defaultValue;
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

        [DebuggerDisplay("{Type} \"{Name}\" = \"{Data}\"")]
        public class EntityProperty
        {
            public EntityFieldType Type { get; set; }

            public string Name { get; set; }

            public object Data { get; set; }
        }

        public string[] GetChildEntityNames()
        {
            return Data.GetArray<string>("m_childLumps");
        }

        public List<Entity> GetEntities()
            => Data.GetArray("m_entityKeyValues")
                .Select(ParseEntityProperties)
                .ToList();

        private static Entity ParseEntityProperties(KVObject entityKv)
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
                entity.Connections = [.. connections];
            }

            return entity;
        }

        private static Entity ParseEntityPropertiesKV3(KVObject entityKv)
        {
            var entityVersion = entityKv.GetInt32Property("version");

            if (entityVersion != 1)
            {
                throw new UnexpectedMagicException("Unsupported entity data version", entityVersion, nameof(entityVersion));
            }

            var entity = new Entity();

            ReadValues(entity, ((KVObject)entityKv).Properties["values"]);
            ReadValues(entity, ((KVObject)entityKv).Properties["attributes"]);

            return entity;
        }

        private static void ReadValues(Entity entity, KVValue values)
        {
            if (values.Type != KVType.OBJECT)
            {
                throw new UnexpectedMagicException("Unsupported entity data values type", (int)values.Type, nameof(values.Type));
            }

            var properties = ((KVObject)values.Value).Properties;
            entity.Properties.EnsureCapacity(entity.Properties.Count + properties.Count);

            foreach (var value in properties)
            {
                var hash = StringToken.Get(value.Key.ToLowerInvariant());
                var data = value.Value.Value;
                EntityFieldType type;

                if (value.Value.Type == KVType.ARRAY)
                {
                    var arrayKv = (KVObject)value.Value.Value;

                    type = arrayKv.Count switch
                    {
                        2 => EntityFieldType.Vector2d, // Did binary entity lumps not store vec2?
                        3 => EntityFieldType.Vector,
                        4 => EntityFieldType.Vector4D,
                        _ => throw new NotImplementedException($"Unsupported array length of {arrayKv.Count}"),
                    };
                    data = type switch
                    {
                        EntityFieldType.Vector2d => new Vector2(arrayKv.GetFloatProperty("0"), arrayKv.GetFloatProperty("1")),
                        EntityFieldType.Vector => arrayKv.ToVector3(),
                        EntityFieldType.Vector4D => arrayKv.ToVector4(),
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

            // TODO: Is this attributes like in KV3 version, should we put them into separate property?
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

                    if (value == null)
                    {
                        value = "null";
                    }
                    else if (value.GetType() == typeof(byte[]))
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

                        unknownKeys.TryGetValue(property.Key, out var currentCount);
                        unknownKeys[property.Key] = currentCount + 1;
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

                        switch (timesToFire)
                        {
                            case 1:
                                builder.Append("OnlyOnce ");
                                break;
                            case >= 2:
                                builder.Append($"Only{timesToFire}Times ");
                                break;
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

        public string ToForgeGameData()
        {
            var knownKeys = StringToken.InvertedTable;
            var uniqueEntityProperties = new Dictionary<string, HashSet<(string Name, EntityFieldType Type)>>();
            var uniqueEntityConnections = new Dictionary<string, HashSet<string>>();
            var brushEntities = new HashSet<string>();

            foreach (var entity in GetEntities())
            {
                var classname = entity.GetProperty<string>("classname").ToLowerInvariant();

                if (!uniqueEntityProperties.TryGetValue(classname, out var entityProperties))
                {
                    entityProperties = [];
                    uniqueEntityProperties.Add(classname, entityProperties);
                }

                foreach (var property in entity.Properties)
                {
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
                        continue;
                    }

                    if (key is "hammeruniqueid" or "classname" or "angles" or "scales" or "origin")
                    {
                        continue;
                    }

                    if (property.Value.Type == EntityFieldType.CString && key == "model")
                    {
                        var model = (string)property.Value.Data;

                        if (model.Contains("/entities/", StringComparison.Ordinal) || model.Contains("\\entities\\", StringComparison.Ordinal))
                        {
                            brushEntities.Add(classname);
                        }
                    }

                    entityProperties.Add((key, property.Value.Type));
                }

                if (entity.Connections != null)
                {
                    if (!uniqueEntityConnections.TryGetValue(classname, out var entityConnections))
                    {
                        entityConnections = [];
                        uniqueEntityConnections.Add(classname, entityConnections);
                    }

                    foreach (var connection in entity.Connections)
                    {
                        var outputName = connection.GetProperty<string>("m_outputName");

                        entityConnections.Add(outputName);
                    }
                }
            }

            var builder = new StringBuilder();
            builder.AppendLine(CultureInfo.InvariantCulture, $"// Generated with {StringToken.VRF_GENERATOR}");
            builder.AppendLine();

            foreach (var (classname, properties) in uniqueEntityProperties.OrderBy(x => x.Key))
            {
                if (brushEntities.Contains(classname))
                {
                    builder.Append("@SolidClass ");
                }
                else
                {
                    builder.Append("@PointClass ");
                }

                if (properties.RemoveWhere(x => x.Name == "targetname") > 0)
                {
                    builder.Append("base(Targetname) ");
                }

                builder.AppendLine(CultureInfo.InvariantCulture, $"{classname} : \"\"");
                builder.AppendLine("[");

                foreach (var property in properties.OrderBy(x => x.Name))
                {
                    var type = property.Type switch
                    {
                        EntityFieldType.Float64 => "float",
                        EntityFieldType.Color32 => "color255",
                        EntityFieldType.UInt => "integer",
                        EntityFieldType.Integer64 => "integer",
                        EntityFieldType.Vector or EntityFieldType.QAngle => "vector",
                        EntityFieldType.CString => "string",
                        _ => property.Type.ToString().ToLowerInvariant()
                    };

                    builder.AppendLine(CultureInfo.InvariantCulture, $"\t{property.Name}({type}) : \"\"");
                }

                if (uniqueEntityConnections.TryGetValue(classname, out var entityConnections) && entityConnections.Count > 0)
                {
                    builder.AppendLine();

                    foreach (var connection in entityConnections.OrderBy(x => x))
                    {
                        // TODO: Inputs?
                        builder.AppendLine(CultureInfo.InvariantCulture, $"\toutput {connection}(void) : \"\"");
                    }
                }

                builder.AppendLine("]");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        // TODO: Invert this, and upconvert legacy entity fields into keyvalues
        private static EntityFieldType ConvertKV3TypeToEntityFieldType(KVType type)
        {
            return type switch
            {
                KVType.BOOLEAN => EntityFieldType.Boolean,
                KVType.DOUBLE => EntityFieldType.Float64,
                KVType.INT64 => EntityFieldType.Integer, // TODO: Incorrect type?
                KVType.UINT64 => EntityFieldType.Integer64,
                KVType.STRING => EntityFieldType.CString,
                KVType.NULL => EntityFieldType.CString,
                _ => throw new NotImplementedException($"Unsupported kv3 entity data type: {type}")
            };
        }
    }
}
