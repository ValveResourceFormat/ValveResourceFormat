using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1.Bsp;

namespace ValveResourceFormat.Source1.Entities;

/// <summary>The <c>rendermode</c> of a Source 1 entity.</summary>
internal enum RenderMode
{
    Normal = 0,
    TransColor = 1,
    TransTexture = 2,
    Glow = 3,
    TransAlpha = 4,
    TransAdd = 5,
    Environmental = 6,
    TransAddFrameBlend = 7,
    TransAlphaAdd = 8,
    WorldGlow = 9,
    None = 10,
}

/// <summary>
/// Converts the entities of a Source 1 map to the entities of a compiled Source 2 map, which is what the
/// entity system of the renderer and the entity views of the viewer read.
/// </summary>
/// <remarks>
/// Keys are lowercased and every Source 1 key is kept. Numbers are stored as numbers, other than the keys that name things. Outputs
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
        options ??= Source1EntityConverterOptions.ForMap(bsp);

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
    /// <returns>The converted entity, or <see langword="null"/> when it has no classname or is a point_template, which spawns the entities of an entity lump that a converted map does not have.</returns>
    public static EntityLump.Entity? Convert(BspEntity source, EntityLump parentLump, Source1EntityConverterOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parentLump);
        ArgumentNullException.ThrowIfNull(options);

        var sourceClassName = source.ClassName;

        if (sourceClassName.Length == 0 || sourceClassName.Equals("point_template", StringComparison.OrdinalIgnoreCase))
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

        entity["classname"] = GetClassName(source, entity, options);

        if (source.Properties.TryGetValue("hammerid", out var hammerId))
        {
            entity["hammeruniqueid"] = hammerId;
        }

        ConvertModel(source, entity);
        ConvertLadder(source, entity);

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

    /// <summary>Gets whether an entity does not draw its model when the map starts, by its render mode, effects, or because it starts disabled.</summary>
    /// <param name="entity">The entity.</param>
    internal static bool IsHiddenAtSpawn(BspEntity entity)
    {
        const int NoDrawEffect = 32;
        const int WallToggleStartOff = 1;

        var renderMode = (RenderMode)entity.GetInt("rendermode");

        if (renderMode is RenderMode.Environmental or RenderMode.None || (entity.GetInt("effects") & NoDrawEffect) != 0)
        {
            return true;
        }

        if (entity.ClassName == "func_wall_toggle")
        {
            return (entity.GetInt("spawnflags") & WallToggleStartOff) != 0;
        }

        return entity.GetInt("startdisabled") != 0;
    }

    // A ladder holds both of its ends in world space, where Source 2 holds the end in the space of the entity, which stands at the start
    private static void ConvertLadder(BspEntity source, EntityLump.Entity entity)
    {
        if (!source.ClassName.Equals("func_useableladder", StringComparison.OrdinalIgnoreCase) || !source.Properties.ContainsKey("point0"))
        {
            return;
        }

        var start = source.GetVector3("point0");
        var end = source.GetVector3("point1");

        entity.Remove("point0");
        entity["origin"] = FormatVector(start);
        entity["point1"] = FormatVector(end - start);
    }

    private static void ConvertModel(BspEntity source, EntityLump.Entity entity)
    {
        var modelScale = source.GetFloat("modelscale", 1f);

        if (modelScale != 1f && modelScale > 0f)
        {
            entity["scales"] = string.Create(CultureInfo.InvariantCulture, $"{modelScale} {modelScale} {modelScale}");
        }
    }

    private static KVObject ToValue(string key, string text)
    {
        if (key == "solid")
        {
            return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var solid) ? solid : 0L;
        }

        if (TextKeys.Contains(key) || text.Length == 0)
        {
            return text;
        }

        var first = text[0];

        // An instance parameter the map compiler left unreplaced, which the engine reads as 0
        if (first == '$')
        {
            return 0L;
        }

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
                InputName = GetInputName(parsed.Input),
                OverrideParam = parsed.Parameter,
                Delay = parsed.Delay,
                TimesToFire = parsed.TimesToFire,
                TargetType = EntityIOTargetType.EntityNameOrClassName,
            });
        }

        return connections;
    }

    // The inputs of a sprite that show and hide it are the enable inputs of a brush that is drawn
    private static string GetInputName(string input)
    {
        if (input.Equals("ShowSprite", StringComparison.OrdinalIgnoreCase))
        {
            return "Enable";
        }

        if (input.Equals("HideSprite", StringComparison.OrdinalIgnoreCase))
        {
            return "Disable";
        }

        if (input.Equals("ToggleSprite", StringComparison.OrdinalIgnoreCase))
        {
            return "Toggle";
        }

        return input;
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

    /// <summary>Formats a vector as the <c>x y z</c> text of an entity key.</summary>
    public static string FormatVector(Vector3 value)
        => string.Create(CultureInfo.InvariantCulture, $"{value.X} {value.Y} {value.Z}");
}
