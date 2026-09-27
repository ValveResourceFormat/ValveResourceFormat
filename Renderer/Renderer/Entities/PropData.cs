using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// How a model breaks: its health and per-type damage scales, resolved from its <c>prop_data</c>
/// against the game's <c>scripts/propdata.txt</c> base classes, and the pieces it breaks into.
/// </summary>
/// <param name="Health">Damage it takes to break; zero or less is unbreakable.</param>
/// <param name="BulletScale">Multiplier on bullet damage.</param>
/// <param name="ClubScale">Multiplier on melee damage.</param>
/// <param name="ExplosiveScale">Multiplier on blast damage.</param>
/// <param name="FragileImpacts">Whether impacts use the fragile table, for glass, rather than the default one.</param>
/// <param name="Pieces">What it breaks into.</param>
public sealed record PropBreakData(float Health, float BulletScale, float ClubScale, float ExplosiveScale, bool FragileImpacts, IReadOnlyList<BreakPiece> Pieces)
{
    /// <summary>Gets whether the model can be broken at all.</summary>
    public bool IsBreakable => Health > 0f;

    /// <summary>Gets the multiplier for one kind of damage.</summary>
    public float ScaleFor(DamageType type) => type switch
    {
        DamageType.Bullet => BulletScale,
        DamageType.Club => ClubScale,
        DamageType.Explosive => ExplosiveScale,
        _ => 1f,
    };

    /// <summary>
    /// The damage a physics impact at <paramref name="speed"/> deals. Below the first step nothing
    /// happens, so setting a prop down or a slow shove never hurts it; each step up the table deals
    /// its damage. Glass breaks far sooner.
    /// </summary>
    public float ImpactDamage(float speed)
    {
        var table = FragileImpacts ? FragileImpactTable : DefaultImpactTable;
        var damage = 0f;

        foreach (var (minimumSpeed, stepDamage) in table)
        {
            if (speed < minimumSpeed)
            {
                break;
            }

            damage = stepDamage;
        }

        return damage;
    }

    private static readonly (float Speed, float Damage)[] DefaultImpactTable =
        [(150f, 5f), (250f, 10f), (450f, 20f), (550f, 50f), (700f, 100f), (1000f, 500f)];

    private static readonly (float Speed, float Damage)[] FragileImpactTable =
        [(25f, 10f), (50f, 20f), (100f, 50f), (200f, 75f), (300f, 100f), (400f, 300f), (500f, 500f)];
}

/// <summary>One piece a broken model spawns, from its <c>break_list</c>.</summary>
/// <param name="Model">The piece's model.</param>
/// <param name="Offset">Where it sits in the broken model's frame.</param>
/// <param name="Angles">How it is turned in the broken model's frame, as a QAngle.</param>
/// <param name="FadeTime">Seconds until it is removed; zero keeps it.</param>
/// <param name="Health">Its own health, overriding its model's; zero keeps the model's.</param>
/// <param name="SpawnChance">Chance from 0 to 1 that it spawns at all.</param>
/// <param name="IsDebris">Whether it stays out of the player's way and other debris's.</param>
public sealed record BreakPiece(string Model, Vector3 Offset, Vector3 Angles, float FadeTime, float Health, float SpawnChance, bool IsDebris);

/// <summary>
/// The game's <c>scripts/propdata.txt</c>: named base classes of breakable behaviour that model
/// <c>prop_data</c> derives from with its <c>base</c> key. A model value of -1, or a blank one,
/// means it takes the base class's.
/// </summary>
public sealed class PropDataTable
{
    private const int MaxBaseDepth = 8;

    private readonly Dictionary<string, KVObject> sections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the table with no base classes, for a game that ships none.</summary>
    public static PropDataTable Empty { get; } = new();

    /// <summary>Loads the game's table, or <see cref="Empty"/> when it has none.</summary>
    public static PropDataTable Load(IFileLoader fileLoader)
    {
        using var stream = fileLoader.GetFileStream("scripts/propdata.txt");

        if (stream == null)
        {
            return Empty;
        }

        KVObject root;

        try
        {
            root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, KVSerializerOptions.DefaultOptions);
        }
        catch (InvalidDataException)
        {
            return Empty;
        }

        var table = new PropDataTable();

        foreach (var (name, section) in root)
        {
            table.sections[name] = section;
        }

        return table;
    }

    /// <summary>Resolves a model's breakable behaviour.</summary>
    public PropBreakData Resolve(Model model)
    {
        var keyValues = model.KeyValues;
        var propData = keyValues.ContainsKey("prop_data") ? keyValues.GetSubCollection("prop_data") : null;

        var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var fragile = false;

        if (propData != null)
        {
            ApplyBase(propData.GetStringProperty("base"), values, ref fragile, depth: 0);
            Apply(propData, values, ref fragile);
        }

        return new PropBreakData(
            values.GetValueOrDefault("health"),
            values.GetValueOrDefault("dmg.bullets", 1f),
            values.GetValueOrDefault("dmg.club", 1f),
            values.GetValueOrDefault("dmg.explosive", 1f),
            fragile,
            ReadBreakPieces(keyValues));
    }

    private void ApplyBase(string? name, Dictionary<string, float> values, ref bool fragile, int depth)
    {
        if (string.IsNullOrEmpty(name) || depth > MaxBaseDepth || !sections.TryGetValue(name, out var section))
        {
            return;
        }

        // The deepest base first, so each derived class overrides what it inherits
        ApplyBase(section.GetStringProperty("base"), values, ref fragile, depth + 1);
        Apply(section, values, ref fragile);
    }

    private static void Apply(KVObject section, Dictionary<string, float> values, ref bool fragile)
    {
        foreach (var key in (ReadOnlySpan<string>)["health", "dmg.bullets", "dmg.club", "dmg.explosive"])
        {
            if (TryReadNumber(section, key, out var value) && value != -1f)
            {
                values[key] = value;
            }
        }

        if (section.GetStringProperty("damage_table") is { Length: > 0 } table)
        {
            fragile = table.Equals("glass", StringComparison.OrdinalIgnoreCase);
        }
    }

    // Base classes are text keyvalues, where every value is a string; compiled model data is typed
    private static bool TryReadNumber(KVObject section, string key, out float value)
    {
        if (section.GetStringProperty(key) is { } text)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        value = section.GetFloatProperty(key, float.NaN);
        return !float.IsNaN(value) && section.ContainsKey(key);
    }

    private static List<BreakPiece> ReadBreakPieces(KVObject keyValues)
    {
        var pieces = new List<BreakPiece>();

        if (!keyValues.ContainsKey("break_list") || keyValues.GetArray("break_list") is not { } list)
        {
            return pieces;
        }

        foreach (var entry in list)
        {
            var model = entry.GetStringProperty("model");

            if (string.IsNullOrEmpty(model))
            {
                continue;
            }

            pieces.Add(new BreakPiece(
                model,
                ReadVector(entry, "offset"),
                ReadVector(entry, "offset_rotation"),
                entry.GetFloatProperty("fadetime"),
                entry.GetFloatProperty("health_override"),
                entry.ContainsKey("random_spawn_chance") ? entry.GetFloatProperty("random_spawn_chance") : 1f,
                string.Equals(entry.GetStringProperty("collision_group_override"), "debris", StringComparison.OrdinalIgnoreCase)));
        }

        return pieces;
    }

    private static Vector3 ReadVector(KVObject entry, string key)
    {
        if (!entry.ContainsKey(key) || entry.GetFloatArray(key) is not { Length: >= 3 } values)
        {
            return Vector3.Zero;
        }

        return new Vector3(values[0], values[1], values[2]);
    }
}
