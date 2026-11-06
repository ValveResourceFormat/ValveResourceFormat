using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Material;

namespace ValveResourceFormat.Source1.Physics;

/// <summary>
/// The decals a bullet leaves, read from the decal list of a Source 1 game and served as the impact effects script and the
/// decal groups resource of a Source 2 game.
/// </summary>
internal sealed class Source1ImpactDecals
{
    /// <summary>The path the impact effects script is served by.</summary>
    public const string EffectsPath = "scripts/surfaceproperties_impact_effects.txt";

    /// <summary>The path the decal groups resource is served by.</summary>
    public const string GroupsPath = "scripts/decalgroups.vdata";

    private const string DecalListPath = "scripts/decals_subrect.txt";
    private const string TranslationSection = "TranslationData";
    private const string SubrectShader = "Subrect";
    private const string ModelMaterialKey = "$modelmaterial";
    private const string BulletDecal = "Impact.Concrete";
    private const char ConcreteMaterial = 'C';
    private const char NoDecalMaterial = '-';

    private readonly Source1FileSystem fileSystem;
    private readonly Dictionary<char, string> translations = [];
    private readonly Dictionary<string, List<(string Material, float Weight)>> groups = new(StringComparer.OrdinalIgnoreCase);

    private Source1ImpactDecals(Source1FileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    /// <summary>Reads the decal list of a game.</summary>
    /// <param name="fileSystem">The files of the game.</param>
    /// <returns>The decals, or <see langword="null"/> when the game has no decal list.</returns>
    public static Source1ImpactDecals? Load(Source1FileSystem fileSystem)
    {
        using var stream = fileSystem.OpenFile(DecalListPath);

        if (stream == null)
        {
            return null;
        }

        var decals = new Source1ImpactDecals(fileSystem);
        decals.Read(stream);

        return decals.groups.Count > 0 ? decals : null;
    }

    private void Read(Stream stream)
    {
        var root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).DeserializeWithoutRoot(stream);

        foreach (var (name, block) in root)
        {
            if (block.ValueType != KVValueType.Collection)
            {
                continue;
            }

            if (name.Equals(TranslationSection, StringComparison.OrdinalIgnoreCase))
            {
                ReadTranslations(block);
                continue;
            }

            var options = new List<(string Material, float Weight)>();

            foreach (var (material, weight) in block)
            {
                if (float.TryParse(weight.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var probability))
                {
                    options.Add((material, probability));
                }
            }

            groups[name] = options;
        }
    }

    // A game material that translates to no name is left out
    private void ReadTranslations(KVObject block)
    {
        foreach (var (key, value) in block)
        {
            if (key.Length == 1 && value.ToString() is { Length: > 0 } decal)
            {
                translations[key[0]] = decal;
            }
        }
    }

    /// <summary>
    /// Creates the impact effects script, which names the decal group a bullet leaves on every surface.
    /// </summary>
    /// <param name="surfaceProperties">The surfaces of the game.</param>
    public string CreateEffectsScript(Source1SurfaceProperties surfaceProperties)
    {
        var list = KVObject.Array();

        foreach (var name in surfaceProperties.Names)
        {
            var surface = KVObject.Collection();
            surface.Add("surfacePropertyName", name);
            surface.Add("impactDecalName", GetBulletDecal(surfaceProperties.GetGameMaterial(name)));
            list.Add(surface);
        }

        var root = KVObject.Collection();
        root.Add("SurfacePropertiesList", list);

        return root.ToKV3String();
    }

    /// <summary>
    /// Creates the decal groups resource, which lists the materials of each group with their weights.
    /// </summary>
    public Resource CreateGroupsResource()
    {
        var root = KVObject.Collection();
        root.Add("generic_data_type", "CDecalGroupVData");

        foreach (var (name, options) in groups)
        {
            var list = KVObject.Array();

            foreach (var (material, weight) in options)
            {
                if (GetDecalMaterial(material) is not { } materialPath)
                {
                    continue;
                }

                var option = KVObject.Collection();
                option.Add("m_hMaterial", Source1ResourceReferences.Reference(materialPath));
                option.Add("m_flProbability", weight);
                list.Add(option);
            }

            var group = KVObject.Collection();
            group.Add("m_vecOptions", list);
            root.Add(name, group);
        }

        var resource = new Resource(ResourceType.VData);
        resource.AddBlock(new BinaryKV3(BlockType.DATA) { Resource = resource, Data = root.ToKV3Document() });
        Source1ResourceReferences.Update(resource);

        return resource;
    }

    // Bullets leave the concrete decal, which another game material replaces by the decal of its own
    private string GetBulletDecal(string? gameMaterial)
    {
        if (!TryParseGameMaterial(gameMaterial, out var character) || character == ConcreteMaterial)
        {
            return BulletDecal;
        }

        if (character == NoDecalMaterial)
        {
            return string.Empty;
        }

        return translations.GetValueOrDefault(character, BulletDecal);
    }

    // A single character names the material, anything else is its character code
    private static bool TryParseGameMaterial(string? text, out char character)
    {
        character = '\0';

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.Length == 1 && !char.IsAsciiDigit(text[0]))
        {
            character = text[0];
            return true;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        character = (char)code;

        return true;
    }

    // A sub-rectangle of the shared decal texture names the material the decal is drawn with on models
    private string? GetDecalMaterial(string name)
    {
        var path = Source1Paths.ToGameMaterialPath(name);

        if (!fileSystem.Exists(path))
        {
            return null;
        }

        var vmt = VmtFile.Load(path, fileSystem.OpenFile);

        if (vmt.ShaderName.Equals(SubrectShader, StringComparison.OrdinalIgnoreCase)
            && vmt.Values.String(ModelMaterialKey) is { Length: > 0 } modelMaterial)
        {
            path = Source1Paths.ToGameMaterialPath(modelMaterial);

            if (!fileSystem.Exists(path))
            {
                return null;
            }
        }

        return Source1Paths.AddVariant(Source1Paths.ToMaterialPath(path), Source1ResourceProvider.ProjectedDecalVariant);
    }
}
