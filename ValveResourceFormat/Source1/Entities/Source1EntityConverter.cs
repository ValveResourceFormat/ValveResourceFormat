using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1.Bsp;

namespace ValveResourceFormat.Source1.Entities
{
    /// <summary>
    /// Converts the entities of a Source 1 map to the entities of a compiled Source 2 map, which is what the
    /// entity system of the renderer and the entity views of the viewer read.
    /// </summary>
    /// <remarks>
    /// Keys are lowercased and every Source 1 key is kept, so the original classname is in
    /// <c>source1_classname</c>. Numbers are stored as numbers, other than the keys that name things. Outputs
    /// are not keys, they become <see cref="EntityLump.Entity.Connections"/>. Lights get the keys of the Source 2
    /// light entities, see <see cref="Source1LightConverter"/>.
    /// </remarks>
    internal static class Source1EntityConverter
    {
        private const char OutputSeparator = ',';
        private const char NewOutputSeparator = '\x1b';

        private static readonly Dictionary<string, string> ClassNameRemaps = new(StringComparer.OrdinalIgnoreCase)
        {
            ["info_player_teamspawn"] = "info_team_spawn",
            ["prop_physics"] = "prop_dynamic",
            ["prop_physics_multiplayer"] = "prop_dynamic",
            ["prop_physics_override"] = "prop_dynamic",
            ["prop_ragdoll"] = "prop_dynamic",
            ["func_detail"] = "func_brush",
            ["func_wall"] = "func_brush",
            ["func_wall_toggle"] = "func_brush",
            ["func_illusionary"] = "func_brush",
            ["func_lod"] = "func_brush",
        };

        // Keys read as text by the entity classes, which a number would not be
        private static readonly HashSet<string> TextKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "classname",
            "targetname",
            "parentname",
            "parentattachmentname",
            "target",
            "target1",
            "filtername",
            "damagefilter",
            "damagetarget",
            "lightingorigin",
            "lightingoriginhack",
            "globalname",
            "model",
            "skin",
            "pattern",
            "defaultanim",
            "idleanim",
            "startinganim",
            "hammeruniqueid",
            "hammerid",
            "message",
            "sound",
            "soundname",
            "noise",
            "origin",
            "angles",
        };

        /// <summary>
        /// Creates the entity lump that owns converted entities. It holds none of its own.
        /// </summary>
        /// <param name="name">The name of the lump.</param>
        /// <returns>An empty entity lump.</returns>
        public static EntityLump CreateLump(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            var resource = new Resource(ResourceType.EntityLump);

            return new EntityLump
            {
                Resource = resource,
                Data = new KVObject
                {
                    { "m_name", name },
                    { "m_childLumps", KVObject.Array() },
                    { "m_entityKeyValues", KVObject.Array() },
                },
            };
        }

        /// <summary>
        /// Converts every entity of a map, in the order the map lists them.
        /// </summary>
        /// <param name="bsp">The map.</param>
        /// <param name="options">The conversion options, or <see langword="null"/> for the defaults.</param>
        /// <returns>The converted entities, which share one lump.</returns>
        public static List<EntityLump.Entity> Convert(BspFile bsp, Source1EntityConverterOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(bsp);

            var lump = CreateLump(bsp.Name);
            options ??= new Source1EntityConverterOptions { HdrLighting = bsp.HasHdrLighting };

            return Convert(bsp.Entities, lump, options);
        }

        /// <summary>
        /// Converts entities into the entities of a lump.
        /// </summary>
        /// <param name="entities">The Source 1 entities.</param>
        /// <param name="parentLump">The lump the converted entities belong to, see <see cref="CreateLump"/>.</param>
        /// <param name="options">The conversion options, or <see langword="null"/> for the defaults.</param>
        /// <returns>The converted entities. An entity without a classname is skipped.</returns>
        public static List<EntityLump.Entity> Convert(IReadOnlyList<BspEntity> entities, EntityLump parentLump, Source1EntityConverterOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(parentLump);

            options ??= new Source1EntityConverterOptions();

            var converted = new List<EntityLump.Entity>(entities.Count);

            foreach (var source in entities)
            {
                if (Convert(source, parentLump, options) is { } entity)
                {
                    converted.Add(entity);
                }
            }

            return converted;
        }

        /// <summary>
        /// Converts one entity.
        /// </summary>
        /// <param name="source">The Source 1 entity.</param>
        /// <param name="parentLump">The lump the converted entity belongs to, see <see cref="CreateLump"/>.</param>
        /// <param name="options">The conversion options.</param>
        /// <returns>The converted entity, or <see langword="null"/> when it has no classname.</returns>
        public static EntityLump.Entity? Convert(BspEntity source, EntityLump parentLump, Source1EntityConverterOptions options)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(parentLump);
            ArgumentNullException.ThrowIfNull(options);

            var sourceClassName = source.ClassName;

            if (sourceClassName.Length == 0)
            {
                return null;
            }

            var entity = new EntityLump.Entity { ParentLump = parentLump };
            var outputs = new List<(string Name, string Value)>();

            foreach (var (key, value) in source.AllProperties)
            {
                var name = key.ToLowerInvariant();

                if (TryParseOutput(value, out _))
                {
                    outputs.Add((key, value));
                    continue;
                }

                entity[name] = ToValue(name, value);
            }

            entity["source1_classname"] = sourceClassName;
            entity["classname"] = GetClassName(source, entity, options);

            if (source.Properties.TryGetValue("hammerid", out var hammerId))
            {
                entity["hammeruniqueid"] = hammerId;
            }

            ConvertModel(source, entity);
            ConvertCubemap(source, entity);

            if (Source1LightConverter.IsLightClass(sourceClassName.ToLowerInvariant()))
            {
                entity["classname"] = Source1LightConverter.Apply(source, entity, options);
            }

            if (outputs.Count > 0)
            {
                entity.Connections = CreateConnections(entity, outputs);
            }

            return entity;
        }

        private static string GetClassName(BspEntity source, EntityLump.Entity entity, Source1EntityConverterOptions options)
        {
            var sourceClassName = source.ClassName;

            if (!options.RemapClassNames || !ClassNameRemaps.TryGetValue(sourceClassName, out var remapped))
            {
                return sourceClassName;
            }

            if (sourceClassName.Equals("func_illusionary", StringComparison.OrdinalIgnoreCase))
            {
                entity["solid"] = 0L;
            }

            return remapped;
        }

        // Brush entities name the model of the map they are a part of, as *N
        private static void ConvertModel(BspEntity source, EntityLump.Entity entity)
        {
            var model = source.GetString("model");

            if (model.Length > 1 && model[0] == '*' && int.TryParse(model.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                entity["source1_brush_model"] = (long)index;
            }

            var modelScale = source.GetFloat("modelscale", 1f);

            if (modelScale != 1f && modelScale > 0f)
            {
                entity["scales"] = string.Create(CultureInfo.InvariantCulture, $"{modelScale} {modelScale} {modelScale}");
            }
        }

        // Source 1 cubemaps are the textures the map is compiled with, named after where the cubemap was taken
        private static void ConvertCubemap(BspEntity source, EntityLump.Entity entity)
        {
            if (!source.ClassName.Equals("env_cubemap", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var origin = source.Origin;
            var name = string.Create(CultureInfo.InvariantCulture, $"c{(int)origin.X}_{(int)origin.Y}_{(int)origin.Z}");

            entity["source1_cubemap"] = name;

            if (!entity.ContainsKey("influenceradius"))
            {
                entity["influenceradius"] = 512L;
            }
        }

        private static KVObject ToValue(string key, string text)
        {
            if (TextKeys.Contains(key) || text.Length == 0)
            {
                return text;
            }

            var first = text[0];

            if (!char.IsAsciiDigit(first) && first != '-' && first != '+' && first != '.')
            {
                return text;
            }

            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            {
                return integer;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            {
                return number;
            }

            return text;
        }

        private static List<EntityLump.Connection> CreateConnections(EntityLump.Entity source, List<(string Name, string Value)> outputs)
        {
            var connections = new List<EntityLump.Connection>(outputs.Count);

            foreach (var (name, value) in outputs)
            {
                _ = TryParseOutput(value, out var parsed);

                connections.Add(new EntityLump.Connection
                {
                    SourceEntity = source,
                    OutputName = name,
                    TargetName = parsed.Target,
                    InputName = parsed.Input,
                    OverrideParam = parsed.Parameter,
                    Delay = parsed.Delay,
                    TimesToFire = parsed.TimesToFire,
                    TargetType = EntityIOTargetType.EntityNameOrClassName,
                });
            }

            return connections;
        }

        private readonly record struct ParsedOutput(string Target, string Input, string Parameter, float Delay, int TimesToFire);

        // An output is stored as a key, "target,input,parameter,delay,times to fire"
        private static bool TryParseOutput(string value, out ParsedOutput output)
        {
            output = default;

            var separator = value.Contains(NewOutputSeparator, StringComparison.Ordinal) ? NewOutputSeparator : OutputSeparator;
            var parts = value.Split(separator);

            if (parts.Length < 5)
            {
                return false;
            }

            if (!TryParseLeadingNumber(parts[^2], out var delay) || !TryParseLeadingNumber(parts[^1], out var count))
            {
                return false;
            }

            // Zero times to fire means every time
            var timesToFire = count == 0f ? -1 : (int)count;
            var parameter = string.Join(separator, parts[2..^2]);

            output = new ParsedOutput(parts[0], parts[1], parameter, delay, timesToFire);
            return true;
        }

        // The number at the start of a token, where an empty token is zero. A token that does not start with
        // a number is not part of an output.
        private static bool TryParseLeadingNumber(string text, out float number)
        {
            number = 0f;

            var token = text.AsSpan().Trim();

            if (token.IsEmpty)
            {
                return true;
            }

            var length = 0;

            while (length < token.Length && (char.IsAsciiDigit(token[length]) || token[length] is '.' or '-' or '+'))
            {
                length++;
            }

            return float.TryParse(token[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }
    }
}
