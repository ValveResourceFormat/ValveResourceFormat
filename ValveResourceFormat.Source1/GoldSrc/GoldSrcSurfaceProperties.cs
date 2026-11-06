using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Audio;
using ValveResourceFormat.Source1.Physics;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// The surface properties of a GoldSrc game: one surface for each texture type of its <c>sound/materials.txt</c>, served
/// as the <c>surfaceproperties.vsurf</c> resource, with the footstep script and the step sound events of the player.
/// </summary>
internal static class GoldSrcSurfaceProperties
{
    /// <summary>The surface of a texture that has no type, and of what has no texture.</summary>
    public const string DefaultSurface = "default";

    /// <summary>The surface of water volumes, which the player wades through.</summary>
    public const string WaterSurface = "slosh";

    private const string ConcreteSurface = "concrete";
    private const string PlayerTable = "ct_player";

    // The player alternates feet, a right step plays one of the odd waves and a left step one of the even waves
    private static readonly int[] RightWaves = [1, 3];
    private static readonly int[] LeftWaves = [2, 4];

    private readonly record struct StepSurface(string Surface, string WavePrefix, float Volume);

    // The texture types that have sounds of their own. The others step as concrete.
    private static readonly StepSurface[] StepSurfaces =
    [
        new(ConcreteSurface, "pl_step", 0.5f),
        new("metal", "pl_metal", 0.5f),
        new("dirt", "pl_dirt", 0.55f),
        new("vent", "pl_duct", 0.7f),
        new("grate", "pl_grate", 0.5f),
        new("tile", "pl_tile", 0.5f),
        new(WaterSurface, "pl_slosh", 0.5f),
    ];

    private static readonly string[] SurfaceNames =
    [
        DefaultSurface,
        ConcreteSurface,
        "metal",
        "dirt",
        "vent",
        "grate",
        "tile",
        WaterSurface,
        "wood",
        "computer",
        "glass",
        "flesh",
    ];

    /// <summary>Gets the surface of a texture type.</summary>
    /// <param name="type">The letter of the type in <c>sound/materials.txt</c>, or zero for a texture that is not listed.</param>
    public static string GetSurfaceName(char type) => type switch
    {
        'M' => "metal",
        'D' => "dirt",
        'V' => "vent",
        'G' => "grate",
        'T' => "tile",
        'S' => WaterSurface,
        'W' => "wood",
        'P' => "computer",
        'Y' => "glass",
        'F' => "flesh",
        _ => ConcreteSurface,
    };

    /// <summary>Creates the surface properties resource, which lists every surface with the surface it is based on.</summary>
    public static Resource CreateResource()
    {
        var list = KVObject.Array();

        foreach (var name in SurfaceNames)
        {
            var surface = KVObject.Collection();
            surface.Add("surfacePropertyName", name);
            surface.Add("m_nameHash", StringToken.Store(name));

            if (HasStepSound(name))
            {
                surface.Add("m_baseNameHash", 0u);
            }
            else
            {
                surface.Add("m_baseNameHash", StringToken.Store(ConcreteSurface));
                surface.Add("base", ConcreteSurface);
            }

            list.Add(surface);
        }

        var root = KVObject.Collection();
        root.Add("SurfacePropertiesList", list);

        var resource = new Resource(ResourceType.SurfaceProperties);
        resource.AddBlock(new BinaryKV3(BlockType.DATA) { Resource = resource, Data = root.ToKV3Document() });

        return resource;
    }

    /// <summary>Creates the footstep script, which names the step sound events of each foot for the surfaces that have them.</summary>
    public static string CreateFootstepScript()
    {
        var list = KVObject.Array();

        foreach (var step in StepSurfaces)
        {
            list.Add(CreateFootstepEntry(step.Surface, step.Surface));
        }

        list.Add(CreateFootstepEntry(DefaultSurface, ConcreteSurface));

        var table = KVObject.Collection();
        table.Add("SurfacePropertiesList", list);

        var root = KVObject.Collection();
        root.Add(PlayerTable, table);

        return root.ToKV3String();
    }

    private static KVObject CreateFootstepEntry(string surface, string sounds)
    {
        var entry = KVObject.Collection();
        entry.Add("surfacePropertyName", surface);
        entry.Add("walkleft", GetStepEventName(sounds, "StepLeft"));
        entry.Add("walkright", GetStepEventName(sounds, "StepRight"));

        return entry;
    }

    /// <summary>Creates the sound events of the steps of each foot on each surface, for the waves the game has.</summary>
    /// <param name="fileSystem">The game, or <see langword="null"/> when the map is not in a known game.</param>
    public static List<Source1SoundFiles.WaveEvent> CreateStepEvents(GoldSrcFileSystem? fileSystem)
    {
        var events = new List<Source1SoundFiles.WaveEvent>();

        if (fileSystem == null)
        {
            return events;
        }

        foreach (var step in StepSurfaces)
        {
            AddStepEvent(events, fileSystem, step, "StepLeft", LeftWaves);
            AddStepEvent(events, fileSystem, step, "StepRight", RightWaves);
        }

        return events;
    }

    private static void AddStepEvent(List<Source1SoundFiles.WaveEvent> events, GoldSrcFileSystem fileSystem, StepSurface step, string foot, int[] waveNumbers)
    {
        var waves = new List<string>();

        foreach (var number in waveNumbers)
        {
            var wave = $"player/{step.WavePrefix}{number}";

            if (fileSystem.FindFile(Source1Paths.ToWavePath(wave)) != null)
            {
                waves.Add($"{wave}.wav");
            }
        }

        if (waves.Count > 0)
        {
            events.Add(new Source1SoundFiles.WaveEvent(GetStepEventName(step.Surface, foot), step.Volume, waves));

            if (foot == "StepLeft")
            {
                events.Add(new Source1SoundFiles.WaveEvent($"Land_{step.Surface}.{foot}", step.Volume, waves));
            }
        }
    }

    private static string GetStepEventName(string surface, string foot) => $"CT_{surface}.{foot}";

    private static bool HasStepSound(string surface)
    {
        foreach (var step in StepSurfaces)
        {
            if (step.Surface == surface)
            {
                return true;
            }
        }

        return false;
    }
}
