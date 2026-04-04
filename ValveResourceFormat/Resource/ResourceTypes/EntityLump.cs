using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents an entity lump resource containing entity definitions and their properties.
    /// </summary>
    public class EntityLump : KeyValuesOrNTRO
    {
        /// <summary>
        /// Represents a single entity with its properties and connections.
        /// </summary>
        public class Entity : KVObject
        {
            /// <summary>
            /// Gets or sets the entity connections (inputs/outputs).
            /// </summary>
            public List<KVObject>? Connections { get; internal set; }
            /// <summary>
            /// Gets or initializes the parent entity lump that contains this entity.
            /// </summary>
            public required EntityLump ParentLump { get; init; }

            /// <summary>
            /// Gets a Vector2 property value by name.
            /// </summary>
            /// <param name="name">The property name.</param>
            /// <param name="defaultValue">The default value to return if the property is not found.</param>
            /// <returns>The Vector2 property value or the default value.</returns>
            public Vector2 GetVector2Property(string name, Vector2 defaultValue = default)
            {
                if (!TryGetValue(name, out var value))
                {
                    return defaultValue;
                }

                if (value != null && value.ValueType != KVValueType.Null)
                {
                    if (value.ValueType is KVValueType.Collection or KVValueType.Array)
                    {
                        return value.ToVector2();
                    }

                    if (value.ValueType == KVValueType.String)
                    {
                        return EntityTransformHelper.ParseVector2((string)value);
                    }
                }

                return defaultValue;
            }

            /// <summary>
            /// Gets a Vector3 property value by name.
            /// </summary>
            /// <param name="name">The property name.</param>
            /// <param name="defaultValue">The default value to return if the property is not found.</param>
            /// <returns>The Vector3 property value or the default value.</returns>
            public Vector3 GetVector3Property(string name, Vector3 defaultValue = default)
            {
                if (!TryGetValue(name, out var value))
                {
                    return defaultValue;
                }

                if (value != null && value.ValueType != KVValueType.Null)
                {
                    if (value.ValueType is KVValueType.Collection or KVValueType.Array)
                    {
                        return value.ToVector3();
                    }

                    if (value.ValueType == KVValueType.String)
                    {
                        return EntityTransformHelper.ParseVector((string)value);
                    }
                }

                return defaultValue;
            }

            /// <summary>
            /// Gets a Color32 property value as a normalized Vector3 (0-1 range).
            /// </summary>
            /// <param name="key">The property name.</param>
            /// <returns>The normalized color vector (0-1 range).</returns>
            public Vector3 GetColor32Property(string key)
            {
                var defaultColor = new Vector3(255f);
                return GetVector3Property(key, defaultColor) / 255f;
            }
        }

        /// <summary>
        /// Gets the name of this entity lump.
        /// </summary>
        public string Name => Data.GetStringProperty("m_name");

        /// <summary>
        /// Gets the names of child entity lumps.
        /// </summary>
        /// <returns>An array of child entity lump names.</returns>
        public string[] GetChildEntityNames()
        {
            return Data.GetArray<string>("m_childLumps")!;
        }

        /// <summary>
        /// Gets all entities contained in this entity lump.
        /// </summary>
        /// <returns>A list of entities.</returns>
        public List<Entity> GetEntities()
            => Data.GetArray("m_entityKeyValues")
                .Select(ParseEntityProperties)
                .OfType<Entity>()
                .ToList();

        private Entity? ParseEntityProperties(KVObject entityKv)
        {
            var connections = entityKv.GetArray("m_connections");
            Entity entity;

            if (entityKv.ContainsKey("keyValues3Data"))
            {
                entity = ParseEntityPropertiesKV3(entityKv.GetSubCollection("keyValues3Data"));
            }
            else
            {
                entity = ParseEntityProperties(entityKv.GetArray<byte>("m_keyValuesData")!);
            }

            // are there any kinds of valid entities which don't contain a classname?
            if (!entity.ContainsKey("classname"))
            {
                return null;
            }

            if (connections.Length > 0)
            {
                entity.Connections = [.. connections];
            }

            return entity;
        }

        private Entity ParseEntityPropertiesKV3(KVObject entityKv)
        {
            var entityVersion = entityKv.GetInt32Property("version");

            if (entityVersion != 1)
            {
                throw new UnexpectedMagicException("Unsupported entity data version", entityVersion, nameof(entityVersion));
            }

            var entity = new Entity { ParentLump = this };

            entityKv.TryGetValue("values", out var values);
            entityKv.TryGetValue("attributes", out var attributes);

            ReadValues(entity, values);
            ReadValues(entity, attributes);

            return entity;
        }

        private static KVObject MakeColor32(byte[] bytes)
            => KVObject.Array(bytes.Select(b => (KVObject)(long)b));

        private static void ReadValues(Entity entity, KVObject? values)
        {
            if (values == null || values.ValueType == KVValueType.Null)
            {
                return;
            }

            if (values.ValueType != KVValueType.Collection)
            {
                throw new UnexpectedMagicException("Unsupported entity data values type", (int)values.ValueType, nameof(values));
            }

            foreach (var child in values.Children)
            {
                // All entity property keys will be stored in lowercase
                var lowercaseKey = child.Key.ToLowerInvariant();

                var hash = StringToken.Store(lowercaseKey);
                entity.Add(lowercaseKey, child.Value);
            }
        }

        private Entity ParseEntityProperties(byte[] bytes)
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

            var entity = new Entity { ParentLump = this };

            void ReadTypedValue(uint keyHash, string? keyName)
            {
                var type = (EntityFieldType)dataReader.ReadUInt32();

                KVObject entityProperty = type switch
                {
                    EntityFieldType.Boolean => dataReader.ReadBoolean(),
                    EntityFieldType.Float => (double)dataReader.ReadSingle(),
                    EntityFieldType.Float64 => dataReader.ReadDouble(),
                    EntityFieldType.Color32 => MakeColor32(dataReader.ReadBytes(4)),
                    EntityFieldType.Integer => (long)dataReader.ReadInt32(),
                    EntityFieldType.UInt => (ulong)dataReader.ReadUInt32(),
                    EntityFieldType.Integer64 => dataReader.ReadUInt64(), // Is this supposed to be ReadInt64?
                    EntityFieldType.Vector or EntityFieldType.QAngle => (KVObject)$"{dataReader.ReadSingle()} {dataReader.ReadSingle()} {dataReader.ReadSingle()}",
                    EntityFieldType.CString => dataReader.ReadNullTermString(Encoding.UTF8),
                    _ => throw new UnexpectedMagicException("Unknown type", (int)type, nameof(type)),
                };

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
                            $"Key hash for {keyName} ({keyHash}) found in resource is not the same as the calculated {calculatedHash}."
                        );
                    }
                }

                entity.Add(keyName, entityProperty);
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

        /// <summary>
        /// Converts the entity lump to a human-readable string representation.
        /// </summary>
        /// <returns>A formatted string containing all entities and their properties.</returns>
        public string ToEntityDumpString()
        {
            var knownKeys = StringToken.InvertedTable;
            var builder = new StringBuilder();
            var unknownKeys = new Dictionary<uint, uint>();

            var index = 0;
            foreach (var entity in GetEntities())
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"===={index++}====");

                foreach (var property in entity.Children)
                {
                    var value = StringifyValue(property.Value);

                    builder.AppendLine(CultureInfo.InvariantCulture, $"{property.Key,-30} {value}");
                }

                if (entity.Connections != null)
                {
                    foreach (var connection in entity.Connections)
                    {
                        builder.Append('@');
                        builder.Append(connection.GetStringProperty("m_outputName"));
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
                                builder.Append(CultureInfo.InvariantCulture, $"Only{timesToFire}Times ");
                                break;
                        }

                        builder.Append(connection.GetStringProperty("m_inputName"));
                        builder.Append(' ');
                        builder.Append(connection.GetStringProperty("m_targetName"));

                        var param = connection.GetStringProperty("m_overrideParam");

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

        /// <summary>
        /// Converts the entity lump to a Forge Game Data (FGD) format string.
        /// </summary>
        /// <returns>An FGD-formatted string containing entity class definitions.</returns>
        public string ToForgeGameData()
        {
            var knownKeys = StringToken.InvertedTable;
            var uniqueEntityProperties = new Dictionary<string, HashSet<(string Name, KVValueType Type)>>();
            var uniqueEntityConnections = new Dictionary<string, HashSet<string>>();
            var brushEntities = new HashSet<string>();

            foreach (var entity in GetEntities())
            {
                var classname = entity.GetStringProperty("classname")?.ToLowerInvariant();
                if (classname == null)
                {
                    continue;
                }

                if (!uniqueEntityProperties.TryGetValue(classname, out var entityProperties))
                {
                    entityProperties = [];
                    uniqueEntityProperties.Add(classname, entityProperties);
                }

                foreach (var property in entity.Children)
                {
                    var key = property.Key;

                    if (key is "hammeruniqueid" or "classname" or "angles" or "scales" or "origin")
                    {
                        continue;
                    }

                    if (property.Value.ValueType == KVValueType.String && key == "model")
                    {
                        var model = (string)property.Value;
                        if (model.Contains("/entities/", StringComparison.Ordinal) || model.Contains("\\entities\\", StringComparison.Ordinal))
                        {
                            brushEntities.Add(classname);
                        }
                    }

                    entityProperties.Add((key, property.Value.ValueType));
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
                        var outputName = connection.GetStringProperty("m_outputName");

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
                        KVValueType.FloatingPoint64 => "float",
                        KVValueType.Int64 or KVValueType.UInt64 => "integer",
                        KVValueType.Array => "vector", // sometimes also "color255", but with kv3 entities this information is lost
                        KVValueType.String => "string",
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

        /// <summary>
        /// Return a string representation of an entity property.
        /// </summary>
        /// <param name="value">Entity property.</param>
        /// <returns>Stringified value.</returns>
        public static string StringifyValue(object? value)
        {
            var valueStr = string.Empty;

            if (value is KVObject kvObject)
            {
                using var ms = new MemoryStream();
                var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues3Text);
                serializer.Serialize(ms, kvObject, new KVSerializerOptions
                {
                    SkipHeader = true
                });
                ms.Position = 0;
                using var reader = new StreamReader(ms);
                valueStr = reader.ReadToEnd();
            }
            else if (value is not null)
            {
                valueStr = value.ToString() ?? string.Empty;
            }

            return valueStr;
        }
    }
}
