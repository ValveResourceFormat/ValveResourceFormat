using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;
using KVValueType = ValveKeyValue.KVValueType;

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
        public class Entity
        {
            /// <summary>
            /// Gets the entity properties collection.
            /// </summary>
            public KVObject Properties { get; } = new(null);
            // public KVObject Attributes { get; } = new(null);
            /// <summary>
            /// Gets or sets the entity connections (inputs/outputs).
            /// </summary>
            public List<KVObject>? Connections { get; internal set; }
            /// <summary>
            /// Gets or initializes the parent entity lump that contains this entity.
            /// </summary>
            public required EntityLump ParentLump { get; init; }

            /// <summary>
            /// Gets a strongly-typed property value by name, returning a default value if not found or on error.
            /// </summary>
            /// <typeparam name="T">The type to convert the property value to.</typeparam>
            /// <param name="name">The property name.</param>
            /// <param name="defaultValue">The default value to return if the property is not found or conversion fails.</param>
            /// <returns>The property value or the default value.</returns>
            /// <exception cref="InvalidOperationException">Thrown when attempting to use Vector3 type (use GetVector3Property instead).</exception>
            [return: NotNullIfNotNull(nameof(defaultValue))]
            public T? GetProperty<T>(string name, T? defaultValue = default)
            {
                if (typeof(T) == typeof(Vector3))
                {
                    throw new InvalidOperationException("Entity.GetProperty<Vector3> has been removed. Use Entity.GetVector3Property.");
                }

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

            /// <summary>
            /// Gets a strongly-typed struct property value by name without type checking.
            /// </summary>
            /// <typeparam name="T">The struct type to convert the property value to.</typeparam>
            /// <param name="name">The property name.</param>
            /// <param name="defaultValue">The default value to return if the property is not found.</param>
            /// <returns>The property value or the default value.</returns>
            public T GetPropertyUnchecked<T>(string name, T defaultValue = default) where T : struct
                => Properties.GetPropertyUnchecked(name, defaultValue);

            /// <summary>
            /// Gets a property value by name.
            /// </summary>
            /// <param name="name">The property name.</param>
            /// <returns>The property value or the default <see cref="KVValue"/> (with <see cref="KVValueType.Null"/>) if not found.</returns>
            public KVValue GetProperty(string name) => Properties.Properties.GetValueOrDefault(name);

            /// <summary>
            /// Determines whether the entity contains a property with the specified name.
            /// </summary>
            /// <param name="name">The property name to check.</param>
            /// <returns>True if the property exists, false otherwise.</returns>
            public bool ContainsKey(string name) => Properties.Properties.ContainsKey(name);

            /// <summary>
            /// Gets a Vector3 property value by name.
            /// </summary>
            /// <param name="name">The property name.</param>
            /// <param name="defaultValue">The default value to return if the property is not found.</param>
            /// <returns>The Vector3 property value or the default value.</returns>
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
        public string Name => Data.GetProperty<string>("m_name");

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
            if (!entity.Properties.ContainsKey("classname"))
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

            ReadValues(entity, entityKv.Properties["values"]);
            ReadValues(entity, entityKv.Properties["attributes"]);

            return entity;
        }

        private static void ReadValues(Entity entity, KVValue values)
        {
            if (values.Type == KVValueType.Null)
            {
                return;
            }

            if (values.Type != KVValueType.Collection)
            {
                throw new UnexpectedMagicException("Unsupported entity data values type", (int)values.Type, nameof(values.Type));
            }

            if (values.Value is not KVObject kvObject)
            {
                throw new InvalidDataException("Expected KVObject for entity values");
            }

            var properties = kvObject.Properties;
            entity.Properties.Properties.EnsureCapacity(entity.Properties.Count + properties.Count);

            foreach (var value in properties)
            {
                // All entity property keys will be stored in lowercase
                var lowercaseKey = value.Key.ToLowerInvariant();

                var hash = StringToken.Store(lowercaseKey);
                entity.Properties.AddProperty(lowercaseKey, value.Value);
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

                var (kvType, valueObject) = type switch
                {
                    EntityFieldType.Boolean => (KVValueType.Boolean, (object)dataReader.ReadBoolean()),
                    EntityFieldType.Float => (KVValueType.FloatingPoint64, (double)dataReader.ReadSingle()),
                    EntityFieldType.Float64 => (KVValueType.FloatingPoint64, dataReader.ReadDouble()),
                    EntityFieldType.Color32 => (KVValueType.Array, new KVObject("", dataReader.ReadBytes(4).Select(c => new KVValue(KVValueType.Int64, c)).ToArray())),
                    EntityFieldType.Integer => (KVValueType.Int64, (long)dataReader.ReadInt32()),
                    EntityFieldType.UInt => (KVValueType.UInt64, (ulong)dataReader.ReadUInt32()),
                    EntityFieldType.Integer64 => (KVValueType.UInt64, dataReader.ReadUInt64()), // Is this supposed to be ReadInt64?
                    EntityFieldType.Vector or EntityFieldType.QAngle => (KVValueType.String, $"{dataReader.ReadSingle()} {dataReader.ReadSingle()} {dataReader.ReadSingle()}"),
                    EntityFieldType.CString => (KVValueType.String, dataReader.ReadNullTermString(Encoding.UTF8)),
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
                            $"Key hash for {keyName} ({keyHash}) found in resource is not the same as the calculated {calculatedHash}."
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

                foreach (var property in entity.Properties)
                {
                    var value = property.Value;

                    if (value == null)
                    {
                        value = "null";
                    }
                    else if (value is KVObject kvArray)
                    {
                        value = $"Array [{string.Join(", ", kvArray.Select(p => p.Value.ToString()).ToArray())}]";
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
                                builder.Append(CultureInfo.InvariantCulture, $"Only{timesToFire}Times ");
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
                var classname = entity.GetProperty<string>("classname")?.ToLowerInvariant();
                if (classname == null)
                {
                    continue;
                }

                if (!uniqueEntityProperties.TryGetValue(classname, out var entityProperties))
                {
                    entityProperties = [];
                    uniqueEntityProperties.Add(classname, entityProperties);
                }

                foreach (var property in entity.Properties.Properties)
                {
                    var key = property.Key;

                    if (key is "hammeruniqueid" or "classname" or "angles" or "scales" or "origin")
                    {
                        continue;
                    }

                    if (property.Value.Value is string model && key == "model")
                    {
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
    }
}
