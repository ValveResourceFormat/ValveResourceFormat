using System.Globalization;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Input;

/// <summary>What a Deadlock hero's bullet leaves on one kind of surface.</summary>
/// <param name="Decal">The decal group it marks the surface with, or null for none.</param>
/// <param name="Particle">The particle it throws up, or null for none.</param>
/// <param name="Sound">The sound event it makes, or null for none.</param>
public readonly record struct DeadlockBulletImpact(string? Decal, string? Particle, string? Sound);

/// <summary>
/// What sets one Deadlock hero's movement apart from another's, as the game data states it.
/// The defaults are those of a hero that overrides nothing.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/deadlock/server/CitadelHeroData_t">CitadelHeroData_t</seealso>
public sealed record DeadlockHero
{
    private const string HeroesFile = "scripts/heroes.vdata";
    private const string AbilitiesFile = "scripts/abilities.vdata";

    // The game data measures in metres
    private const float UnitsPerMeter = 1f / 0.0254f;

    /// <summary>Gets the name the hero goes by in the game data, such as <c>hero_unicorn</c>.</summary>
    public string Name { get; init; } = "hero_base";

    /// <summary>Gets the resource name of the hero's model.</summary>
    public string? ModelName { get; init; }

    /// <summary>Gets the fastest the hero runs, in units per second.</summary>
    public float RunSpeed { get; init; } = 7.2f * UnitsPerMeter;

    /// <summary>Gets the speed sprinting adds, in units per second.</summary>
    public float SprintSpeed { get; init; }

    /// <summary>Gets the fastest the hero moves crouched, in units per second.</summary>
    public float CrouchSpeed { get; init; } = 4.75f * UnitsPerMeter;

    /// <summary>Gets the stamina capacity, in bars.</summary>
    public float Stamina { get; init; } = 3f;

    /// <summary>Gets the stamina regained per second, in bars.</summary>
    public float StaminaRegenPerSecond { get; init; } = 0.2222f;

    /// <summary>Gets how far a ground dash goes, in units.</summary>
    public float GroundDashDistance { get; init; } = 10f * UnitsPerMeter;

    /// <summary>Gets how long a ground dash lasts, in seconds.</summary>
    public float GroundDashDuration { get; init; } = 0.68f;

    /// <summary>Gets how far an air dash goes, in units.</summary>
    public float AirDashDistance { get; init; } = 8f * UnitsPerMeter;

    /// <summary>Gets how long an air dash lasts, in seconds.</summary>
    public float AirDashDuration { get; init; } = 0.47f;

    /// <summary>Gets the tallest step the hero walks up, in units.</summary>
    public float StepHeight { get; init; } = 32f;

    /// <summary>Gets the scale the hero applies to gravity.</summary>
    public float GravityScale { get; init; } = 1f;

    /// <summary>Gets the scale the hero applies to how fast the held direction may steer it in the air.</summary>
    public float AirControlScale { get; init; } = 1f;

    /// <summary>Gets the scale the hero applies to its acceleration in the air.</summary>
    public float AirAccelerationScale { get; init; } = 1f;

    /// <summary>Gets the time between two shots of the hero's weapon, or after the last shot of a burst, in seconds.</summary>
    public float FireInterval { get; init; } = 0.2f;

    /// <summary>Gets how many shots one pull of the trigger fires.</summary>
    public int BurstShotCount { get; init; } = 1;

    /// <summary>Gets the time between the shots of a burst, in seconds.</summary>
    public float BurstShotInterval { get; init; }

    /// <summary>Gets how many bullets one shot fires.</summary>
    public int BulletsPerShot { get; init; } = 1;

    /// <summary>Gets the half angle of the cone the bullets of one shot scatter in, in degrees.</summary>
    public float Spread { get; init; }

    /// <summary>Gets whether the first bullet of a shot scatters too, rather than going where the aim points.</summary>
    public bool SpreadFirstBullet { get; init; }

    /// <summary>Gets how fast a bullet flies, in units per second.</summary>
    public float BulletSpeed { get; init; } = 25000f;

    /// <summary>Gets the share of gravity that pulls a bullet down.</summary>
    public float BulletGravityScale { get; init; } = 0.8f;

    /// <summary>Gets how long a bullet flies before it is gone, in seconds.</summary>
    public float BulletLifetime { get; init; } = 2f;

    /// <summary>Gets what a bullet leaves on each surface, by surface property name; <c>default</c> covers the rest.</summary>
    public IReadOnlyDictionary<string, DeadlockBulletImpact> BulletImpacts { get; init; } = new Dictionary<string, DeadlockBulletImpact>();

    /// <summary>Gets the sound event a shot of the hero's weapon plays.</summary>
    public string? ShootSound { get; init; }

    /// <summary>Gets the particle a shot flashes at the muzzle.</summary>
    public string? MuzzleFlashEffect { get; init; }

    /// <summary>Gets the particle a shot draws from the muzzle to where it hits.</summary>
    public string? TracerEffect { get; init; }

    /// <summary>Gets the particle a bullet of this weapon leaves where it hits, whatever the surface.</summary>
    public string? ImpactEffect { get; init; }

    /// <summary>Gets the sound event a bullet of this weapon makes where it hits, whatever the surface.</summary>
    public string? ImpactSound { get; init; }

    /// <summary>Gets the particle a light melee swing plays.</summary>
    public string? LightMeleeEffect { get; init; }

    /// <summary>Gets the particle a parry plays.</summary>
    public string? ParryEffect { get; init; }

    /// <summary>Gets the attachment melee particles play at, or null for the body.</summary>
    public string? MeleeEffectAttachment { get; init; }

    /// <summary>Gets how long after a light melee the next one can start, in seconds.</summary>
    public float LightMeleeCooldown { get; init; } = 0.75f;

    /// <summary>Gets how long after a parry the next one can start, in seconds.</summary>
    public float ParryCooldown { get; init; } = 4.5f;

    /// <summary>Lists the heroes the game data holds that have a model, by name.</summary>
    /// <param name="fileLoader">Loader for the game data.</param>
    public static IEnumerable<string> GetNames(IFileLoader fileLoader)
    {
        if (LoadData(fileLoader, HeroesFile) is not { } heroes)
        {
            yield break;
        }

        foreach (var (name, hero) in heroes.Children)
        {
            if (hero.IsCollection && hero.GetStringProperty("m_strModelName") is { Length: > 0 })
            {
                yield return name;
            }
        }
    }

    /// <summary>Reads a hero from the game data.</summary>
    /// <param name="fileLoader">Loader for the game data.</param>
    /// <param name="name">The name of the hero, such as <c>hero_unicorn</c>.</param>
    /// <returns>The hero, or <see langword="null"/> when the game data has no such hero.</returns>
    public static DeadlockHero? TryLoad(IFileLoader fileLoader, string name)
    {
        if (LoadData(fileLoader, HeroesFile)?.GetSubCollection(name) is not { } data)
        {
            return null;
        }

        var defaults = new DeadlockHero();
        var stats = data.GetSubCollection("m_mapStartingStats");

        float Stat(string key, float fallback, float scale = 1f)
            => stats != null && stats.ContainsKey(key) ? stats.GetFloatProperty(key) * scale : fallback;

        var hero = defaults with
        {
            Name = name,
            ModelName = data.GetStringProperty("m_strModelName"),
            RunSpeed = Stat("EMaxMoveSpeed", defaults.RunSpeed, UnitsPerMeter),
            SprintSpeed = Stat("ESprintSpeed", defaults.SprintSpeed, UnitsPerMeter),
            CrouchSpeed = Stat("ECrouchSpeed", defaults.CrouchSpeed, UnitsPerMeter),
            Stamina = Stat("EStamina", defaults.Stamina),
            StaminaRegenPerSecond = Stat("EStaminaRegenPerSecond", defaults.StaminaRegenPerSecond),
            GroundDashDistance = Stat("EGroundDashDistanceInMeters", defaults.GroundDashDistance, UnitsPerMeter),
            GroundDashDuration = Stat("EGroundDashDuration", defaults.GroundDashDuration),
            AirDashDistance = Stat("EAirDashDistanceInMeters", defaults.AirDashDistance, UnitsPerMeter),
            AirDashDuration = Stat("EAirDashDuration", defaults.AirDashDuration),
            StepHeight = data.GetFloatProperty("m_flStepHeight", defaults.StepHeight),
        };

        var abilities = LoadData(fileLoader, AbilitiesFile);
        var boundAbilities = data.GetSubCollection("m_mapBoundAbilities");

        if (abilities == null || boundAbilities == null)
        {
            return hero;
        }

        // Melee cools down by how the swing went, and in the viewer it always misses
        if (abilities.GetSubCollection(boundAbilities.GetStringProperty("ESlot_Weapon_Melee", string.Empty)) is { } melee
            && melee.GetSubCollection("m_mapAttacks")?.GetSubCollection("EAttackType_Light") is { } lightMelee)
        {
            hero = hero with
            {
                LightMeleeCooldown = lightMelee.GetFloatProperty("m_flCooldownOnMiss", hero.LightMeleeCooldown),
                LightMeleeEffect = NonEmpty(lightMelee.GetStringProperty("m_MeleeSwingParticle")),
                ParryEffect = NonEmpty(melee.GetStringProperty("m_ParryActivateParticle")),
                MeleeEffectAttachment = NonEmpty(melee.GetStringProperty("m_strEffectsAttachName")),
            };
        }

        if (abilities.GetSubCollection(boundAbilities.GetStringProperty("ESlot_Ability_Innate_3", string.Empty)) is { } parry
            && PropertyValue(parry.GetSubCollection("m_mapAbilityProperties"), "AbilityCooldown") is > 0f and var parryCooldown)
        {
            hero = hero with { ParryCooldown = parryCooldown };
        }

        // Gravity and air control are not stats: they ride on the hero's weapon
        if (abilities.GetSubCollection(boundAbilities.GetStringProperty("ESlot_Weapon_Primary", string.Empty)) is not { } weapon)
        {
            return hero;
        }

        var properties = weapon.GetSubCollection("m_mapAbilityProperties");
        var gravityChange = 0f;

        foreach (var modifier in weapon.GetArray("m_AutoIntrinsicModifiers") ?? [])
        {
            gravityChange += modifier.GetFloatProperty("m_flGravityChange");
        }

        var weaponInfo = weapon.GetSubCollection("m_mapWeaponInfos")?.GetSubCollection("primary");

        if (weaponInfo == null)
        {
            return hero;
        }

        var impacts = new Dictionary<string, DeadlockBulletImpact>(StringComparer.OrdinalIgnoreCase);

        foreach (var (surface, impact) in weaponInfo.GetSubCollection("m_mapImpactEffects")?.Children ?? [])
        {
            if (impact.IsCollection)
            {
                impacts[surface] = new DeadlockBulletImpact(
                    NonEmpty(impact.GetStringProperty("m_strDecal")),
                    NonEmpty(impact.GetStringProperty("m_strParticle")),
                    NonEmpty(impact.GetStringProperty("m_strSound")));
            }
        }

        return hero with
        {
            FireInterval = weaponInfo.GetFloatProperty("m_flCycleTime", hero.FireInterval),
            BurstShotCount = Math.Max(1, weaponInfo.GetInt32Property("m_iBurstShotCount", hero.BurstShotCount)),
            BurstShotInterval = weaponInfo.GetFloatProperty("m_flIntraBurstCycleTime", hero.BurstShotInterval),
            BulletsPerShot = Math.Max(1, weaponInfo.GetInt32Property("m_iBullets", hero.BulletsPerShot)),
            Spread = weaponInfo.GetFloatProperty("m_Spread", hero.Spread),
            SpreadFirstBullet = weaponInfo.GetBooleanProperty("m_bApplySpreadToFirstPellet", hero.SpreadFirstBullet),
            BulletSpeed = weaponInfo.GetFloatProperty("m_flBulletSpeed", hero.BulletSpeed),
            BulletGravityScale = weaponInfo.GetFloatProperty("m_flBulletGravityScale", hero.BulletGravityScale),
            BulletLifetime = weaponInfo.GetFloatProperty("m_flBulletLifetime", hero.BulletLifetime),
            BulletImpacts = impacts,
            ShootSound = NonEmpty(weaponInfo.GetStringProperty("m_strShootSound")),
            MuzzleFlashEffect = NonEmpty(weaponInfo.GetStringProperty("m_szMuzzleFlashEffectName")),

            // The player's own shots have their own tracer where the weapon has one
            TracerEffect = NonEmpty(weaponInfo.GetStringProperty("m_szSelfBulletTravelTracerParticle"))
                ?? NonEmpty(weaponInfo.GetStringProperty("m_szBulletTravelTracerParticle")),
            ImpactEffect = NonEmpty(weaponInfo.GetStringProperty("m_strWeaponImpactEffect")),
            ImpactSound = NonEmpty(weaponInfo.GetStringProperty("m_strBulletImpactSound")),
            GravityScale = 1f + (gravityChange / 100f),
            AirControlScale = 1f + (PropertyValue(properties, "AirControlPercent") / 100f),
            AirAccelerationScale = 1f + (PropertyValue(properties, "AirControlAccelPercent") / 100f),
        };
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static float PropertyValue(KVObject? properties, string name)
    {
        var value = properties?.GetSubCollection(name)?.GetStringProperty("m_strValue");

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0f;
    }

    private static KVObject? LoadData(IFileLoader fileLoader, string fileName)
        => (fileLoader.LoadFileCompiled(fileName)?.DataBlock as BinaryKV3)?.Data.Root;
}
