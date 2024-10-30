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
        public class Entity
        {
            public KVObject Properties { get; } = new(null);
            // public KVObject Attributes { get; } = new(null);
            public List<KVObject> Connections { get; internal set; }

            public T GetProperty<T>(string name, T defaultValue = default)
            {
                try
                {
                    return Properties.GetProperty(name, defaultValue);
                }
                catch (Exception)
                {
                    return defaultValue;
                }
            }

            //public bool TryGetProperty<T>(string name, out T property) => Properties.TryGetProperty(name, out property);

            public T GetPropertyUnchecked<T>(string name, T defaultValue = default)
                => Properties.GetPropertyUnchecked(name, defaultValue);

            public KVValue GetProperty(string name) => Properties.Properties.GetValueOrDefault(name);

            public bool ContainsKey(string name) => Properties.Properties.ContainsKey(name);

            public Vector3 GetVector3Property(string name, Vector3 defaultValue = default)
            {
                if (Properties.Properties.TryGetValue(name, out var value))
                {
                    if (value.Value is KVObject kv)
                    {
                        return kv.ToVector3();
                    }

                    if (value.Value is string editString)
                    {
                        return EntityTransformHelper.ParseVector(editString);
                    }
                }

                return defaultValue;
            }

            public Vector3 GetColor32Property(string key)
            {
                var defaultColor = new Vector3(255f);
                return GetVector3Property(key, defaultColor) / 255f;
            }
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

            ReadValues(entity, entityKv.Properties["values"]);
            ReadValues(entity, entityKv.Properties["attributes"]);

            return entity;
        }

        private static void ReadValues(Entity entity, KVValue values)
        {
            if (values.Type != KVType.OBJECT)
            {
                throw new UnexpectedMagicException("Unsupported entity data values type", (int)values.Type, nameof(values.Type));
            }

            var properties = ((KVObject)values.Value).Properties;
            entity.Properties.Properties.EnsureCapacity(entity.Properties.Count + properties.Count);

            foreach (var value in properties)
            {
                var hash = StringToken.Store(value.Key.ToLowerInvariant());
                entity.Properties.AddProperty(value.Key, value.Value);
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

                var (kvType, valueObject) = type switch
                {
                    EntityFieldType.Boolean => (KVType.BOOLEAN, (object)dataReader.ReadBoolean()),
                    EntityFieldType.Float => (KVType.DOUBLE, (double)dataReader.ReadSingle()),
                    EntityFieldType.Float64 => (KVType.DOUBLE, dataReader.ReadDouble()),
                    EntityFieldType.Color32 => (KVType.ARRAY, new KVObject("", dataReader.ReadBytes(4).Select(c => new KVValue(KVType.INT64, c)).ToArray())),
                    EntityFieldType.Integer => (KVType.INT64, dataReader.ReadInt32()),
                    EntityFieldType.UInt => (KVType.UINT64, dataReader.ReadUInt32()),
                    EntityFieldType.Integer64 => (KVType.UINT64, dataReader.ReadUInt64()), // TODO: Is supposed to be ReadInt64?
                    EntityFieldType.Vector or EntityFieldType.QAngle => (KVType.STRING, $"{dataReader.ReadSingle()} {dataReader.ReadSingle()} {dataReader.ReadSingle()}"),
                    EntityFieldType.CString => (KVType.BOOLEAN, dataReader.ReadNullTermString(Encoding.UTF8)), // null term variable
                    _ => throw new UnexpectedMagicException("Unknown type", (int)type, nameof(type)),
                };

                var entityProperty = new KVValue(kvType, valueObject);

                if (keyName == null)
                {
                    keyName = StringToken.GetKnownString(keyHash);
                }
                else
                {
                    var calculatedHash = StringToken.Store(keyName);
                    if (calculatedHash != keyHash)
                    {
                        throw new InvalidDataException(
                            $"Key hash for {keyName} ({keyHash}) found in the resource is not the same as the calculated {calculatedHash}."
                        );
                    }
                }

                entity.Properties.Properties.Add(keyName, entityProperty);
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
                    var value = property.Value;

                    if (value == null)
                    {
                        value = "null";
                    }
                    else if (value.GetType() == typeof(byte[]))
                    {
                        var tmp = value as byte[];
                        value = $"Array [{string.Join(", ", tmp.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToArray())}]";
                    }

                    builder.AppendLine(CultureInfo.InvariantCulture, $"{property.Key,-30} {value}");
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
                    var key = property.Key;

                    if (key is "hammeruniqueid" or "classname" or "angles" or "scales" or "origin")
                    {
                        continue;
                    }

                    if (property.Value is string model && key == "model")
                    {
                        if (model.Contains("/entities/", StringComparison.Ordinal) || model.Contains("\\entities\\", StringComparison.Ordinal))
                        {
                            brushEntities.Add(classname);
                        }
                    }

                    //entityProperties.Add((key, property.Value.Type));
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
        private static KVType ConvertKV3TypeToEntityFieldType(EntityFieldType type)
        {
            return type switch
            {
                EntityFieldType.Boolean => KVType.BOOLEAN,
                EntityFieldType.Float64 => KVType.DOUBLE,
                EntityFieldType.Integer => KVType.INT64,
                EntityFieldType.Integer64 => KVType.UINT64,
                EntityFieldType.CString or EntityFieldType.String => KVType.STRING,
                EntityFieldType.Vector or EntityFieldType.QAngle => KVType.OBJECT,
                _ => throw new NotImplementedException($"Unsupported kv3 entity data type: {type}")
            };
        }
    }
}
