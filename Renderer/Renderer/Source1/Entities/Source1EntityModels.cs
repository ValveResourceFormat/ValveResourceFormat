using System.Globalization;
using System.IO;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.FileSystem;

namespace ValveResourceFormat.Renderer.Source1.Entities;

/// <summary>The model that an entity of a Source 1 map shows, and how it is set up.</summary>
internal sealed class Source1EntityModel
{
    /// <summary>The path of the model.</summary>
    public required string Path { get; init; }

    /// <summary>The skin of the model.</summary>
    public int Skin { get; init; }

    /// <summary>The body value of the entity, which chooses the sub-model of every body group.</summary>
    public int Body { get; init; }

    /// <summary>Sub-models chosen after <see cref="Body"/>, as pairs of body group index and sub-model index.</summary>
    public (int Group, int SubModel)[] BodyGroups { get; init; } = [];

    /// <summary>The activity that the entity plays when it spawns, or <see langword="null"/> to leave the model in its default pose.</summary>
    public string? Activity { get; init; }

    /// <summary>The name of the sequence the game code starts with, tried before <see cref="Activity"/>.</summary>
    public string? Sequence { get; init; }
}

/// <summary>
/// Chooses the model of the entities of a Source 1 map whose model is set by the game code and not by a key:
/// characters, pickups, weapons, flags and control points, plus the entities that take it from the <c>model</c> key.
/// </summary>
internal sealed class Source1EntityModels
{
    private const string IdleActivity = "ACT_IDLE";

    private const int NpcStartEfficient = 1 << 4;
    private const int NpcTemplate = 1 << 11;
    private const int PointTemplateKeepEntities = 1;

    private const int CitizenMedic = 1 << 17;
    private const int CitizenRandomHead = 1 << 18;
    private const int CitizenAmmoResupplier = 1 << 19;
    private const int CitizenRandomHeadMale = 1 << 22;
    private const int CitizenRandomHeadFemale = 1 << 23;
    private const int CitizenSkinResupplier = 2;

    private const int AntlionWorker = 1 << 18;
    private const int GunshipChopperModel = 1 << 13;
    private const int HelicopterDrone = 1 << 17;
    private const int FloorTurretCitizen = 1 << 9;
    private const int CombineShotgunSkin = 1;

    private const int ZombieHeadcrabGroup = 1;
    private const int MetropoliceManhackGroup = 1;
    private const int AmmoCrateLidGroup = 1;

    private const int TeamCount = 4;

    private readonly record struct ModelRule(string Model, bool KeyOverrides = false, bool HasHeadcrab = false);

    // A rule with KeyOverrides takes the model key when the mapper set one, and the others always use their model.
    // A rule without a model has no default, so the key alone sets it.
    private static readonly Dictionary<string, ModelRule> Rules = new(StringComparer.Ordinal)
    {
        ["npc_alyx"] = new("models/alyx.mdl", KeyOverrides: true),
        ["npc_eli"] = new("models/eli.mdl", KeyOverrides: true),
        ["npc_kleiner"] = new("models/kleiner.mdl", KeyOverrides: true),
        ["npc_breen"] = new("models/breen.mdl", KeyOverrides: true),
        ["npc_magnusson"] = new("models/magnusson.mdl", KeyOverrides: true),
        ["npc_vortigaunt"] = new("models/vortigaunt.mdl", KeyOverrides: true),
        ["npc_crow"] = new("models/crow.mdl", KeyOverrides: true),
        ["npc_strider"] = new("models/combine_strider.mdl", KeyOverrides: true),
        ["prop_thumper"] = new("models/props_combine/combinethumper002.mdl", KeyOverrides: true),

        ["generic_actor"] = new(string.Empty, KeyOverrides: true),
        ["generic_monster"] = new(string.Empty, KeyOverrides: true),
        ["monster_generic"] = new(string.Empty, KeyOverrides: true),
        ["npc_furniture"] = new(string.Empty, KeyOverrides: true),
        ["npc_puppet"] = new(string.Empty, KeyOverrides: true),
        ["npc_advisor"] = new(string.Empty, KeyOverrides: true),

        ["npc_barney"] = new("models/barney.mdl"),
        ["npc_mossman"] = new("models/mossman.mdl"),
        ["npc_gman"] = new("models/gman.mdl"),
        ["npc_monk"] = new("models/monk.mdl"),
        ["npc_dog"] = new("models/dog.mdl"),
        ["npc_fisherman"] = new("models/lostcoast/fisherman/fisherman.mdl"),
        ["npc_pigeon"] = new("models/pigeon.mdl"),
        ["npc_seagull"] = new("models/seagull.mdl"),
        ["npc_headcrab"] = new("models/headcrabclassic.mdl"),
        ["npc_headcrab_fast"] = new("models/headcrab.mdl"),
        ["npc_headcrab_black"] = new("models/headcrabblack.mdl"),
        ["npc_headcrab_poison"] = new("models/headcrabblack.mdl"),
        ["npc_antlion_grub"] = new("models/antlion_grub.mdl"),
        ["npc_manhack"] = new("models/manhack.mdl"),
        ["npc_rollermine"] = new("models/roller.mdl"),
        ["npc_turret_ceiling"] = new("models/combine_turrets/ceiling_turret.mdl"),
        ["npc_turret_ground"] = new("models/combine_turrets/ground_turret.mdl"),
        ["npc_miniturret"] = new("models/miniturret.mdl"),
        ["npc_hunter"] = new("models/hunter.mdl"),
        ["npc_barnacle"] = new("models/barnacle.mdl"),
        ["npc_combinedropship"] = new("models/combine_dropship.mdl"),
        ["npc_combine_camera"] = new("models/combine_camera/combine_camera.mdl"),
        ["npc_stalker"] = new("models/stalker.mdl"),
        ["npc_ichthyosaur"] = new("models/ichthyosaur.mdl"),
        ["npc_zombie"] = new("models/zombie/classic.mdl", HasHeadcrab: true),
        ["npc_zombie_torso"] = new("models/zombie/classic_torso.mdl", HasHeadcrab: true),
        ["npc_fastzombie"] = new("models/zombie/fast.mdl", HasHeadcrab: true),
        ["npc_fastzombie_torso"] = new("models/zombie/fast_torso.mdl", HasHeadcrab: true),
        ["npc_poisonzombie"] = new("models/zombie/poison.mdl", HasHeadcrab: true),
        ["npc_zombine"] = new("models/zombie/zombie_soldier.mdl", HasHeadcrab: true),

        ["combine_mine"] = new("models/props_combine/combine_mine01.mdl"),
        ["combine_bouncemine"] = new("models/props_combine/combine_mine01.mdl"),
        ["npc_grenade_frag"] = new("models/weapons/w_grenade.mdl"),
        ["prop_combine_ball"] = new("models/effects/combineball.mdl"),
        ["grenade_helicopter"] = new("models/combine_helicopter/helicopter_bomb01.mdl"),
        ["item_healthcharger"] = new("models/props_combine/health_charger001.mdl"),
        ["item_suitcharger"] = new("models/props_combine/suit_charger001.mdl"),

        ["item_healthkit"] = new("models/items/healthkit.mdl"),
        ["item_healthvial"] = new("models/healthvial.mdl"),
        ["item_battery"] = new("models/items/battery.mdl"),
        ["item_suit"] = new("models/items/hevsuit.mdl"),
        ["item_box_srounds"] = new("models/items/boxsrounds.mdl"),
        ["item_ammo_pistol"] = new("models/items/boxsrounds.mdl"),
        ["item_large_box_srounds"] = new("models/items/boxsrounds.mdl"),
        ["item_ammo_pistol_large"] = new("models/items/boxsrounds.mdl"),
        ["item_box_mrounds"] = new("models/items/boxmrounds.mdl"),
        ["item_ammo_smg1"] = new("models/items/boxmrounds.mdl"),
        ["item_large_box_mrounds"] = new("models/items/boxmrounds.mdl"),
        ["item_ammo_smg1_large"] = new("models/items/boxmrounds.mdl"),
        ["item_box_lrounds"] = new("models/items/combine_rifle_cartridge01.mdl"),
        ["item_ammo_ar2"] = new("models/items/combine_rifle_cartridge01.mdl"),
        ["item_large_box_lrounds"] = new("models/items/combine_rifle_cartridge01.mdl"),
        ["item_ammo_ar2_large"] = new("models/items/combine_rifle_cartridge01.mdl"),
        ["item_ammo_357"] = new("models/items/357ammo.mdl"),
        ["item_ammo_357_large"] = new("models/items/357ammobox.mdl"),
        ["item_ammo_crossbow"] = new("models/items/crossbowrounds.mdl"),
        ["item_flare_round"] = new("models/items/flare.mdl"),
        ["item_box_flare_rounds"] = new("models/items/boxflares.mdl"),
        ["item_ml_grenade"] = new("models/weapons/w_missile_closed.mdl"),
        ["item_rpg_round"] = new("models/weapons/w_missile_closed.mdl"),
        ["item_ar2_grenade"] = new("models/items/ar2_grenade.mdl"),
        ["item_ammo_smg1_grenade"] = new("models/items/ar2_grenade.mdl"),
        ["item_box_sniper_rounds"] = new("models/items/boxsniperrounds.mdl"),
        ["item_box_buckshot"] = new("models/items/boxbuckshot.mdl"),
        ["item_ammo_ar2_altfire"] = new("models/items/combine_rifle_ammo01.mdl"),
    };

    private static readonly string[] AmmoCrateModels =
    [
        "models/items/ammocrate_pistol.mdl",
        "models/items/ammocrate_smg1.mdl",
        "models/items/ammocrate_ar2.mdl",
        "models/items/ammocrate_rockets.mdl",
        "models/items/ammocrate_buckshot.mdl",
        "models/items/ammocrate_grenade.mdl",
        "models/items/ammocrate_smg1.mdl",
        "models/items/ammocrate_smg1.mdl",
        "models/items/ammocrate_ar2.mdl",
        "models/items/ammocrate_smg2.mdl",
    ];

    private static readonly string[] ItemCrateModels =
    [
        "models/items/item_item_crate.mdl",
        "models/items/item_beacon_crate.mdl",
    ];

    private static readonly string[] CitizenHeads =
    [
        "male_01.mdl",
        "male_02.mdl",
        "female_01.mdl",
        "male_03.mdl",
        "female_02.mdl",
        "male_04.mdl",
        "female_03.mdl",
        "male_05.mdl",
        "female_04.mdl",
        "male_06.mdl",
        "female_06.mdl",
        "male_07.mdl",
        "female_07.mdl",
        "male_08.mdl",
        "male_09.mdl",
    ];

    // Map name tags that choose the citizen type when the mapper left it open, in the order they are tried
    private static readonly (string Tag, CitizenType Type)[] CitizenTypeByMap =
    [
        ("trainstation", CitizenType.Downtrodden),
        ("canals", CitizenType.Refugee),
        ("town", CitizenType.Refugee),
        ("coast", CitizenType.Refugee),
        ("prison", CitizenType.Downtrodden),
        ("c17", CitizenType.Rebel),
        ("citadel", CitizenType.Downtrodden),
    ];

    private enum CitizenType
    {
        Default,
        Downtrodden,
        Refugee,
        Rebel,
        Unique,
    }

    private readonly Source1FileSystem? fileSystem;
    private readonly string mapName;
    private readonly HashSet<string> templateNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> weaponModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly int[] citizenHeadCounts = new int[CitizenHeads.Length];

    /// <summary>Creates the chooser for the entities of a map.</summary>
    /// <param name="fileSystem">The file system the weapon scripts are read from.</param>
    /// <param name="mapName">The name of the map, which chooses the type of citizens and the scanners.</param>
    /// <param name="entities">All entities of the map, to find the ones that a point_template spawns later.</param>
    public Source1EntityModels(Source1FileSystem? fileSystem, string mapName, IEnumerable<BspEntity> entities)
    {
        this.fileSystem = fileSystem;
        this.mapName = mapName;

        foreach (var entity in entities)
        {
            if (entity.ClassName != "point_template" || (entity.GetInt("spawnflags") & PointTemplateKeepEntities) != 0)
            {
                continue;
            }

            for (var i = 1; i <= 16; i++)
            {
                var name = entity.GetString(string.Create(CultureInfo.InvariantCulture, $"Template{i:00}"));

                if (name.Length > 0)
                {
                    templateNames.Add(name);
                }
            }
        }
    }

    /// <summary>Gets whether an entity is only a template that is spawned later, so it is not part of the map when it starts.</summary>
    /// <param name="entity">The entity.</param>
    public bool IsSpawnedLater(BspEntity entity)
    {
        if (entity.ClassName.StartsWith("npc_", StringComparison.Ordinal) && (entity.GetInt("spawnflags") & NpcTemplate) != 0)
        {
            return true;
        }

        var targetName = entity.TargetName;

        return targetName.Length > 0 && templateNames.Contains(targetName);
    }

    /// <summary>Gets the model of an entity.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The model, or <see langword="null"/> when the entity does not show one.</returns>
    public Source1EntityModel? GetModel(BspEntity entity)
    {
        var model = Resolve(entity);

        if (model == null || !model.Path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return model;
    }

    private Source1EntityModel? Resolve(BspEntity entity)
    {
        var className = entity.ClassName;

        if (UsesModelKey(className))
        {
            return Create(entity, entity.GetString("model"));
        }

        switch (className)
        {
            case "item_teamflag":
                return ResolveTeamFlag(entity);
            case "team_control_point":
                return ResolveControlPoint(entity);
            case "npc_citizen":
                return ResolveCitizen(entity);
            case "npc_metropolice":
                return ResolveMetropolice(entity);
            case "npc_combine_s":
                return ResolveCombineSoldier(entity);
            case "npc_antlion":
                return ResolveActor(entity, (entity.GetInt("spawnflags") & AntlionWorker) != 0 ? "models/antlion_worker.mdl" : "models/antlion.mdl");
            case "npc_antlionguard":
                return ResolveActor(entity, "models/antlion_guard.mdl", entity.GetInt("cavernbreed") != 0 ? 1 : null);
            case "npc_cscanner":
            case "npc_clawscanner":
                return ResolveActor(entity, IsClawScanner(className) ? "models/shield_scanner.mdl" : "models/combine_scanner.mdl");
            case "npc_combinegunship":
                return ResolveActor(entity, (entity.GetInt("spawnflags") & GunshipChopperModel) != 0 ? "models/combine_helicopter.mdl" : "models/gunship.mdl");
            case "npc_helicopter":
                return ResolveActor(entity, (entity.GetInt("spawnflags") & HelicopterDrone) != 0 ? "models/combine_helicopter/helicopter_bomb01.mdl" : "models/combine_helicopter.mdl");
            case "npc_turret_floor":
                return ResolveFloorTurret(entity);
            case "item_ammo_crate":
                return ResolveAmmoCrate(entity);
            case "item_item_crate":
                return ResolveItemCrate(entity);
        }

        if (className.StartsWith("weapon_", StringComparison.Ordinal))
        {
            return ResolveWeapon(className);
        }

        if (Rules.TryGetValue(className, out var rule))
        {
            return ResolveRule(entity, rule);
        }

        var pickup = GetPickupModelPath(className);

        if (pickup.Length == 0)
        {
            return null;
        }

        var powerupModel = entity.GetString("powerup_model");

        return Create(entity, powerupModel.Length > 0 ? powerupModel : pickup);
    }

    private static Source1EntityModel Create(BspEntity entity, string path, int? skin = null, (int Group, int SubModel)[]? bodyGroups = null, string? activity = null)
    {
        return new Source1EntityModel
        {
            Path = path,
            Skin = skin ?? entity.GetInt("skin"),
            Body = entity.GetInt("body"),
            BodyGroups = bodyGroups ?? [],
            Activity = activity,
        };
    }

    private static string GetModelOrDefault(BspEntity entity, string defaultPath)
    {
        var path = entity.GetString("model");

        return path.Length > 0 ? path : defaultPath;
    }

    private static bool UsesModelKey(string className)
    {
        return className.StartsWith("prop_dynamic", StringComparison.Ordinal)
            || className.StartsWith("prop_physics", StringComparison.Ordinal)
            || className.StartsWith("prop_vehicle", StringComparison.Ordinal)
            || className is "prop_door_rotating" or "prop_static";
    }

    private static Source1EntityModel ResolveRule(BspEntity entity, ModelRule rule)
    {
        var path = rule.KeyOverrides ? GetModelOrDefault(entity, rule.Model) : rule.Model;
        var bodyGroups = rule.HasHeadcrab ? new[] { (ZombieHeadcrabGroup, 1) } : null;

        return Create(entity, path, bodyGroups: bodyGroups, activity: IdleActivity);
    }

    private static Source1EntityModel ResolveActor(BspEntity entity, string path, int? skin = null)
    {
        return Create(entity, path, skin, activity: IdleActivity);
    }

    private bool IsClawScanner(string className)
    {
        return className == "npc_clawscanner" || mapName.StartsWith("d3_c17", StringComparison.OrdinalIgnoreCase);
    }

    private static Source1EntityModel ResolveMetropolice(BspEntity entity)
    {
        var efficient = (entity.GetInt("spawnflags") & NpcStartEfficient) != 0;
        var hasManhack = entity.GetInt("manhacks") > 0;

        var path = efficient ? "models/police_cheaple.mdl" : "models/police.mdl";
        var bodyGroups = hasManhack ? new[] { (MetropoliceManhackGroup, 1) } : null;

        return Create(entity, path, bodyGroups: bodyGroups, activity: IdleActivity);
    }

    private static Source1EntityModel ResolveCombineSoldier(BspEntity entity)
    {
        var skin = entity.GetInt("skin");

        if (entity.GetString("additionalequipment") == "weapon_shotgun")
        {
            skin = CombineShotgunSkin;
        }

        return Create(entity, GetModelOrDefault(entity, "models/combine_soldier.mdl"), skin, activity: IdleActivity);
    }

    private static Source1EntityModel ResolveFloorTurret(BspEntity entity)
    {
        const int FirstCitizenSkin = 1;
        const int LastCitizenSkin = 4;

        var skin = entity.GetInt("skin");

        if ((entity.GetInt("spawnflags") & FloorTurretCitizen) != 0)
        {
            skin = Math.Clamp(entity.GetInt("SkinNumber", FirstCitizenSkin), FirstCitizenSkin, LastCitizenSkin);
        }

        return Create(entity, GetModelOrDefault(entity, "models/combine_turrets/floor_turret.mdl"), skin, activity: IdleActivity);
    }

    private static Source1EntityModel? ResolveAmmoCrate(BspEntity entity)
    {
        var ammoType = entity.GetInt("AmmoType");

        if ((uint)ammoType >= AmmoCrateModels.Length)
        {
            return null;
        }

        return new Source1EntityModel
        {
            Path = AmmoCrateModels[ammoType],
            Skin = entity.GetInt("skin"),
            BodyGroups = [(AmmoCrateLidGroup, 1)],
            Sequence = "Idle",
        };
    }

    private static Source1EntityModel? ResolveItemCrate(BspEntity entity)
    {
        var appearance = entity.GetInt("CrateAppearance");

        if ((uint)appearance >= ItemCrateModels.Length)
        {
            return null;
        }

        return Create(entity, ItemCrateModels[appearance]);
    }

    private Source1EntityModel? ResolveWeapon(string className)
    {
        if (!weaponModels.TryGetValue(className, out var path))
        {
            path = ReadWeaponModel(className);
            weaponModels.Add(className, path);
        }

        if (path.Length == 0)
        {
            return null;
        }

        return new Source1EntityModel { Path = path, Activity = IdleActivity };
    }

    // The world model of a weapon is the "playermodel" of its script
    private string ReadWeaponModel(string className)
    {
        if (fileSystem == null)
        {
            return string.Empty;
        }

        using var stream = fileSystem.OpenFile(string.Create(CultureInfo.InvariantCulture, $"scripts/{className}.txt"));

        if (stream == null)
        {
            return string.Empty;
        }

        try
        {
            KVObject script = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);

            foreach (var (name, value) in script)
            {
                if (name.Equals("playermodel", StringComparison.OrdinalIgnoreCase))
                {
                    return value.ToString() ?? string.Empty;
                }
            }
        }
        catch (Exception e) when (e is IOException or KeyValueException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private Source1EntityModel? ResolveCitizen(BspEntity entity)
    {
        var flags = entity.GetInt("spawnflags");
        var type = (CitizenType)entity.GetInt("citizentype");
        var keyModel = entity.GetString("model");
        var skin = (flags & CitizenAmmoResupplier) != 0 ? CitizenSkinResupplier : entity.GetInt("skin");

        string fileName;

        var randomHead = (flags & (CitizenRandomHead | CitizenRandomHeadMale | CitizenRandomHeadFemale)) != 0 || keyModel.Length == 0;

        if (randomHead)
        {
            if ((flags & NpcStartEfficient) != 0)
            {
                return CreateCitizen("models/humans/male_cheaple.mdl", skin);
            }

            fileName = CitizenHeads[PickCitizenHead(flags)];
        }
        else
        {
            fileName = keyModel[(keyModel.LastIndexOf('/') + 1)..];
        }

        if (type == CitizenType.Unique)
        {
            return randomHead ? null : CreateCitizen(keyModel, skin);
        }

        if (type == CitizenType.Default)
        {
            type = GetCitizenTypeForMap();
        }

        var medic = (flags & CitizenMedic) != 0;
        var folder = type switch
        {
            CitizenType.Refugee => "group02",
            CitizenType.Rebel => medic ? "group03m" : "group03",
            _ => "group01",
        };

        return CreateCitizen(string.Create(CultureInfo.InvariantCulture, $"models/humans/{folder}/{fileName}"), skin);
    }

    private static Source1EntityModel CreateCitizen(string path, int skin)
    {
        return new Source1EntityModel { Path = path, Skin = skin, Activity = IdleActivity };
    }

    private CitizenType GetCitizenTypeForMap()
    {
        foreach (var (tag, type) in CitizenTypeByMap)
        {
            if (mapName.Contains(tag, StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return CitizenType.Downtrodden;
    }

    // Spreads the random heads evenly over the citizens of the map, in the order they are listed
    private int PickCitizenHead(int flags)
    {
        var gender = 'x';

        if ((flags & CitizenRandomHeadMale) != 0)
        {
            gender = 'm';
        }
        else if ((flags & CitizenRandomHeadFemale) != 0)
        {
            gender = 'f';
        }

        var best = -1;

        for (var i = 0; i < CitizenHeads.Length; i++)
        {
            if (gender != 'x' && CitizenHeads[i][0] != gender)
            {
                continue;
            }

            if (best < 0 || citizenHeadCounts[i] < citizenHeadCounts[best])
            {
                best = i;
            }
        }

        citizenHeadCounts[best]++;
        return best;
    }

    private static Source1EntityModel ResolveTeamFlag(BspEntity entity)
    {
        var flagModel = entity.GetString("flag_model");

        return Create(entity, flagModel.Length > 0 ? flagModel : "models/flag/briefcase.mdl", GetTeamSkin(entity.GetInt("TeamNum")));
    }

    private static Source1EntityModel ResolveControlPoint(BspEntity entity)
    {
        var owner = GetControlPointOwner(entity);
        var teamModel = entity.GetString(string.Create(CultureInfo.InvariantCulture, $"team_model_{owner}"));

        var path = teamModel.Length > 0 ? teamModel : entity.GetString("team_model_0");

        return Create(entity, path, GetTeamSkin(owner), bodyGroups: [(0, owner)]);
    }

    private static string GetPickupModelPath(string className)
    {
        if (className == "item_healthammokit")
        {
            return "models/items/medkit_medium.mdl";
        }

        if (className == "item_grenadepack")
        {
            return "models/items/grenade_pack.mdl";
        }

        if (className.StartsWith("item_healthkit_", StringComparison.Ordinal))
        {
            return $"models/items/medkit_{GetPickupSize(className)}.mdl";
        }

        if (className.StartsWith("item_ammopack_", StringComparison.Ordinal))
        {
            return $"models/items/ammopack_{GetPickupSize(className)}.mdl";
        }

        if (className.StartsWith("item_currencypack_", StringComparison.Ordinal) && !className.EndsWith("_custom", StringComparison.Ordinal))
        {
            return $"models/items/currencypack_{GetPickupSize(className)}.mdl";
        }

        return string.Empty;
    }

    private static string GetPickupSize(string className)
    {
        var size = className[(className.LastIndexOf('_') + 1)..];
        return size == "full" ? "large" : size;
    }

    // An owner outside of the four teams falls back to unassigned
    private static int GetControlPointOwner(BspEntity entity)
    {
        var owner = entity.GetInt("point_default_owner");

        return (uint)owner < TeamCount ? owner : 0;
    }

    // The skins of team models start at the red team, and a model without a team uses the third one
    private static int GetTeamSkin(int team) => team == 0 ? 2 : team - 2;
}
