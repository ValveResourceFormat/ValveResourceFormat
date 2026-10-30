using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>Breakable behaviour of a model, resolved from its <c>prop_data</c>.</summary>
/// <param name="Health">Damage it takes to break; zero or less is unbreakable.</param>
/// <param name="BulletScale">Multiplier on bullet damage.</param>
/// <param name="ClubScale">Multiplier on melee damage.</param>
/// <param name="ExplosiveScale">Multiplier on blast damage.</param>
/// <param name="FragileImpacts">Whether impacts use the glass damage table.</param>
/// <param name="Pieces">What it breaks into.</param>
/// <param name="Particles">Effects spawned on break.</param>
/// <param name="SpawnMotionDisabled">Whether a prop of the model spawns with motion disabled.</param>
public sealed record PropBreakData(float Health, float BulletScale, float ClubScale, float ExplosiveScale, bool FragileImpacts,
    IReadOnlyList<BreakPiece> Pieces, IReadOnlyList<BreakParticle> Particles, bool SpawnMotionDisabled)
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

    /// <summary>Gets the damage a physics impact at <paramref name="speed"/> deals.</summary>
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
/// <param name="Model">Model name.</param>
/// <param name="Offset">Position in the broken model frame.</param>
/// <param name="Angles">QAngle in the broken model frame.</param>
/// <param name="FadeTime">Seconds until removal; zero keeps it.</param>
/// <param name="Health">Health override; zero keeps the model health.</param>
/// <param name="SpawnChance">Spawn chance from 0 to 1.</param>
/// <param name="IsDebris">Whether it does not collide with the player.</param>
/// <param name="CanPlayerPickup">Whether the player may pick it up.</param>
public sealed record BreakPiece(string Model, Vector3 Offset, Vector3 Angles, float FadeTime, float Health, float SpawnChance, bool IsDebris, bool CanPlayerPickup);

/// <summary>A <c>break_create_particle</c> command; control point indices of -1 are unset.</summary>
/// <param name="Name">The particle system.</param>
/// <param name="Snapshot">Initial particle snapshot.</param>
/// <param name="SkinControlPoint">Receives the skin index.</param>
/// <param name="DamagePositionControlPoint">Receives where the breaking hit landed.</param>
/// <param name="DamageDirectionControlPoint">Receives the direction the breaking hit travelled.</param>
/// <param name="VelocityControlPoint">Receives the velocity.</param>
/// <param name="AngularVelocityControlPoint">Receives the angular velocity.</param>
/// <param name="LocalGravityControlPoint">Receives gravity in model space.</param>
public sealed record BreakParticle(string Name, string? Snapshot, int SkinControlPoint, int DamagePositionControlPoint,
    int DamageDirectionControlPoint, int VelocityControlPoint, int AngularVelocityControlPoint, int LocalGravityControlPoint);

/// <summary>Base classes from <c>scripts/propdata.txt</c> that model <c>prop_data</c> inherits through its <c>base</c> key.</summary>
public sealed class PropDataTable
{
    private const int MaxBaseDepth = 8;

    private readonly Dictionary<string, KVObject> sections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the table with no base classes.</summary>
    public static PropDataTable Empty { get; } = new();

    /// <summary>Loads the table, or <see cref="Empty"/> when there is none.</summary>
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
            ReadBreakPieces(keyValues),
            ReadBreakParticles(keyValues),
            propData?.GetBooleanProperty("spawn_motion_disabled") == true);
    }

    private void ApplyBase(string? name, Dictionary<string, float> values, ref bool fragile, int depth)
    {
        if (string.IsNullOrEmpty(name) || depth > MaxBaseDepth || !sections.TryGetValue(name, out var section))
        {
            return;
        }

        // Deepest base first, derived classes override
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

    // Text keyvalues store every value as a string, compiled model data is typed
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
                string.Equals(entry.GetStringProperty("collision_group_override"), "debris", StringComparison.OrdinalIgnoreCase),
                !entry.ContainsKey("can_player_pickup_piece") || entry.GetBooleanProperty("can_player_pickup_piece")));
        }

        return pieces;
    }

    private static List<BreakParticle> ReadBreakParticles(KVObject keyValues)
    {
        var particles = new List<BreakParticle>();

        if (!keyValues.ContainsKey("break_command_list") || keyValues.GetArray("break_command_list") is not { } list)
        {
            return particles;
        }

        foreach (var command in list)
        {
            if (command.GetStringProperty("break_command") != "break_create_particle"
                || command.GetStringProperty("name") is not { Length: > 0 } name)
            {
                continue;
            }

            var snapshot = command.GetStringProperty("cp0_snapshot");

            particles.Add(new BreakParticle(
                name,
                string.IsNullOrEmpty(snapshot) ? null : snapshot,
                command.GetInt32Property("skin_cp", -1),
                command.GetInt32Property("damage_position_cp", -1),
                command.GetInt32Property("damage_direction_cp", -1),
                command.GetInt32Property("velocity_cp", -1),
                command.GetInt32Property("angular_velocity_cp", -1),
                command.GetInt32Property("local_gravity_cp", -1)));
        }

        return particles;
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
