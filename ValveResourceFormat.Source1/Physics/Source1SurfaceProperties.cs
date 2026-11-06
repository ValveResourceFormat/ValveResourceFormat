using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1.Physics;

/// <summary>
/// The surface properties of a Source 1 game, read from the files its <c>surfaceproperties_manifest.txt</c> lists and
/// served as the <c>surfaceproperties.vsurf</c> resource and the footstep script of a Source 2 game.
/// </summary>
internal sealed class Source1SurfaceProperties
{
    /// <summary>The path of the manifest that lists the files of the game.</summary>
    public const string ManifestPath = "scripts/surfaceproperties_manifest.txt";

    /// <summary>The path the surface properties resource is served by.</summary>
    public const string ResourcePath = "surfaceproperties/surfaceproperties.vsurf";

    /// <summary>The path the footstep script is served by.</summary>
    public const string FootstepsPath = "scripts/surfaceproperties_footsteps.txt";

    private const string DefaultSurface = "default";
    private const string BaseKey = "base";
    private const string PlayerTable = "ct_player";
    private const string TeamPrefix = "CT_";
    private const string LandPrefix = "Land_";
    private const string WaterPrefix = "Water.";
    private const string WaterLandSound = "Land_WaterVol.StepLeft";
    private const int MaxBaseDepth = 16;

    private static readonly string[] PhysicsKeys = ["density", "elasticity", "friction", "dampening", "thickness"];
    private static readonly string[] SoundKeys = ["bulletimpact", "impacthard", "impactsoft", "scrapesmooth", "scraperough", "rolling", "break", "strain"];
    private static readonly string[] AudioParamKeys =
    [
        "audioreflectivity",
        "audiohardnessfactor",
        "audioroughnessfactor",
        "scrapeRoughThreshold",
        "impactHardThreshold",
        "hardVelocityThreshold",
        "staticImpactVolume",
        "audioHardMinVelocity",
    ];

    private readonly List<string> names = [];
    private readonly Dictionary<string, Dictionary<string, string>> surfaces = new(StringComparer.OrdinalIgnoreCase);

    private Source1SurfaceProperties()
    {
    }

    /// <summary>Reads the surface properties of a game.</summary>
    /// <param name="openFile">Opens a file of the game by path, or returns <see langword="null"/> when it has none.</param>
    /// <returns>The properties, or <see langword="null"/> when the game has no manifest or lists no surface.</returns>
    public static Source1SurfaceProperties? Load(Func<string, Stream?> openFile)
    {
        using var manifest = openFile(ManifestPath);

        if (manifest == null)
        {
            return null;
        }

        var properties = new Source1SurfaceProperties();

        foreach (var path in ReadManifest(manifest))
        {
            using var stream = openFile(path);

            if (stream != null)
            {
                properties.AddFile(stream);
            }
        }

        return properties.surfaces.Count > 0 ? properties : null;
    }

    private static List<string> ReadManifest(Stream stream)
    {
        KVObject document = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        var paths = new List<string>();

        foreach (var (key, value) in document)
        {
            if (key.Equals("file", StringComparison.OrdinalIgnoreCase) && value.ToString() is { Length: > 0 } path)
            {
                paths.Add(path.Replace('\\', '/'));
            }
        }

        return paths;
    }

    // A surface listed again overrides the keys it sets, and keeps the others
    private void AddFile(Stream stream)
    {
        var root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).DeserializeWithoutRoot(stream);

        foreach (var (name, block) in root)
        {
            if (block.ValueType != KVValueType.Collection)
            {
                continue;
            }

            if (!surfaces.TryGetValue(name, out var keys))
            {
                keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                surfaces.Add(name, keys);
                names.Add(name);
            }

            foreach (var (key, value) in block)
            {
                if (value.ValueType != KVValueType.Collection && value.ToString() is { } text)
                {
                    keys[key] = text;
                }
            }
        }
    }

    /// <summary>Gets the names of the surfaces, in the order the files list them.</summary>
    public IReadOnlyList<string> Names => names;

    /// <summary>Gets the <c>gamematerial</c> of a surface, which it may inherit, or <see langword="null"/> when it has none.</summary>
    public string? GetGameMaterial(string name) => Flatten(name).GetValueOrDefault("gamematerial");

    /// <summary>Creates the surface properties resource, which lists every surface with the keys it inherits.</summary>
    public Resource CreateResource()
    {
        var list = KVObject.Array();

        foreach (var name in names)
        {
            var keys = Flatten(name);

            var surface = KVObject.Collection();
            surface.Add("surfacePropertyName", name);
            surface.Add("physics", CreateSection(keys, PhysicsKeys));
            surface.Add("audiosounds", CreateStringSection(keys, SoundKeys));
            surface.Add("audioparams", CreateSection(keys, AudioParamKeys));
            surface.Add("m_nameHash", StringToken.Store(name));

            if (keys.TryGetValue(BaseKey, out var baseName))
            {
                surface.Add("m_baseNameHash", StringToken.Store(baseName));
                surface.Add(BaseKey, baseName);
            }
            else
            {
                surface.Add("m_baseNameHash", 0u);
            }

            list.Add(surface);
        }

        var root = KVObject.Collection();
        root.Add("SurfacePropertiesList", list);

        var resource = new Resource(ResourceType.SurfaceProperties);
        resource.AddBlock(new BinaryKV3(BlockType.DATA) { Resource = resource, Data = root.ToKV3Document() });

        return resource;
    }

    /// <summary>
    /// Creates the footstep script, which names the step sound of each foot for every surface in the table of the player.
    /// </summary>
    public string CreateFootstepScript()
    {
        var list = KVObject.Array();

        foreach (var name in names)
        {
            var keys = Flatten(name);

            if (!keys.TryGetValue("stepleft", out var stepLeft) || stepLeft.Length == 0)
            {
                continue;
            }

            var stepRight = keys.TryGetValue("stepright", out var right) && right.Length > 0 ? right : stepLeft;

            var surface = KVObject.Collection();
            surface.Add("surfacePropertyName", name);
            surface.Add("walkleft", TeamPrefix + stepLeft);
            surface.Add("walkright", TeamPrefix + stepRight);
            list.Add(surface);
        }

        var table = KVObject.Collection();
        table.Add("SurfacePropertiesList", list);

        var root = KVObject.Collection();
        root.Add(PlayerTable, table);

        return root.ToKV3String();
    }

    /// <summary>
    /// Gets the sound events the footstep script names, each with the sound event of the game it plays: the step of each
    /// foot as the team step event, and the landing on a surface as the left step.
    /// </summary>
    public List<(string Name, string Source)> CreateStepAliases()
    {
        var aliases = new List<(string Name, string Source)>();

        foreach (var name in names)
        {
            var keys = Flatten(name);

            if (!keys.TryGetValue("stepleft", out var stepLeft) || stepLeft.Length == 0)
            {
                continue;
            }

            var stepRight = keys.TryGetValue("stepright", out var right) && right.Length > 0 ? right : stepLeft;

            aliases.Add((TeamPrefix + stepLeft, stepLeft));
            aliases.Add((TeamPrefix + stepRight, stepRight));

            var landName = stepLeft.StartsWith(WaterPrefix, StringComparison.OrdinalIgnoreCase) ? WaterLandSound : LandPrefix + stepLeft;
            aliases.Add((landName, stepLeft));
        }

        return aliases;
    }

    // The keys of a surface over the keys of its base, over the keys of the default surface
    private Dictionary<string, string> Flatten(string name)
    {
        var chain = new List<Dictionary<string, string>>();
        var current = name;

        for (var depth = 0; depth < MaxBaseDepth && current != null; depth++)
        {
            if (!surfaces.TryGetValue(current, out var keys))
            {
                break;
            }

            chain.Add(keys);
            current = keys.GetValueOrDefault(BaseKey);
        }

        if (!name.Equals(DefaultSurface, StringComparison.OrdinalIgnoreCase) && surfaces.TryGetValue(DefaultSurface, out var defaultKeys))
        {
            chain.Add(defaultKeys);
        }

        var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            foreach (var (key, value) in chain[i])
            {
                flat[key] = value;
            }
        }

        if (surfaces[name].TryGetValue(BaseKey, out var ownBase))
        {
            flat[BaseKey] = ownBase;
        }
        else
        {
            flat.Remove(BaseKey);
        }

        return flat;
    }

    private static KVObject CreateSection(Dictionary<string, string> keys, string[] sectionKeys)
    {
        var section = KVObject.Collection();

        foreach (var key in sectionKeys)
        {
            if (keys.TryGetValue(key, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                section.Add(key, value);
            }
        }

        return section;
    }

    private static KVObject CreateStringSection(Dictionary<string, string> keys, string[] sectionKeys)
    {
        var section = KVObject.Collection();

        foreach (var key in sectionKeys)
        {
            if (keys.TryGetValue(key, out var text))
            {
                section.Add(key, text);
            }
        }

        return section;
    }
}
