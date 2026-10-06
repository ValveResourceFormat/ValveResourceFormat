using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer
{
    // Which decal materials a bullet leaves on each surface property: surface properties name an
    // impact decal group, and the group lists materials with relative probabilities
    internal sealed class ImpactDecalTable
    {
        // Sequence names the frame of the material's sprite sheet to use, for a material that holds many decals
        public readonly record struct DecalOption(string Material, string? Sequence, float Probability);

        private readonly record struct SurfaceImpact(string? Decal, string? GrazingDecal);

        private readonly Dictionary<uint, string> surfaceNamesByHash = [];
        private readonly Dictionary<string, string> surfaceBases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SurfaceImpact> surfaceImpacts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DecalOption[]> decalGroups = new(StringComparer.OrdinalIgnoreCase);

        public bool HasDecalGroups => decalGroups.Count > 0;

        public static ImpactDecalTable Load(GameFileLoader fileLoader)
        {
            var table = new ImpactDecalTable();

            using (var surfaceProperties = fileLoader.LoadFileCompiled("surfaceproperties/surfaceproperties.vsurf"))
            {
                if (surfaceProperties?.DataBlock is BinaryKV3 kv3)
                {
                    foreach (var surface in kv3.Data.Root.GetArray("SurfacePropertiesList") ?? [])
                    {
                        var name = surface.GetStringProperty("surfacePropertyName");

                        if (name == null)
                        {
                            continue;
                        }

                        table.surfaceNamesByHash[StringToken.Store(name)] = name;

                        if (surface.GetStringProperty("base") is { } baseName)
                        {
                            table.surfaceBases[name] = baseName;
                        }
                    }
                }
            }

            using (var stream = fileLoader.GetFileStream("scripts/surfaceproperties_impact_effects.txt"))
            {
                if (stream != null)
                {
                    var impactEffects = KVDocumentExtensions.ParseKV3(stream).Root;

                    foreach (var surface in impactEffects.GetArray("SurfacePropertiesList") ?? [])
                    {
                        if (surface.GetStringProperty("surfacePropertyName") is { } name)
                        {
                            table.surfaceImpacts[name] = new SurfaceImpact(
                                surface.GetStringProperty("impactDecalName"),
                                surface.GetStringProperty("impactGrazingDecalName"));
                        }
                    }
                }
            }

            using (var groups = fileLoader.LoadFileCompiled("scripts/decalgroups.vdata"))
            {
                if (groups?.DataBlock is BinaryKV3 kv3)
                {
                    foreach (var (groupName, group) in kv3.Data.Root)
                    {
                        if (group.ValueType != KVValueType.Collection)
                        {
                            continue;
                        }

                        var options = new List<DecalOption>();

                        foreach (var option in group.GetArray("m_vecOptions") ?? [])
                        {
                            if (option.GetStringProperty("m_hMaterial") is { Length: > 0 } material)
                            {
                                var sequence = option.GetStringProperty("m_sSequenceName");

                                options.Add(new DecalOption(material, string.IsNullOrEmpty(sequence) ? null : sequence,
                                    option.GetFloatProperty("m_flProbability", 1f)));
                            }
                        }

                        table.decalGroups[groupName] = [.. options];
                    }
                }
            }

            if (table.decalGroups.Count == 0)
            {
                table.LoadTextDecalGroups(fileLoader);
            }

            return table;
        }

        // The same groups as text, each a list of materials and their weights
        private void LoadTextDecalGroups(GameFileLoader fileLoader)
        {
            using var stream = fileLoader.GetFileStream("scripts/decals_subrect.txt");

            if (stream == null)
            {
                return;
            }

            // The groups sit at the top level, and the format takes a single root
            using var wrapped = new MemoryStream();
            wrapped.Write("\"root\"\n{\n"u8);
            stream.CopyTo(wrapped);
            wrapped.Write("\n}\n"u8);
            wrapped.Position = 0;

            KVObject root;

            try
            {
                root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(wrapped);
            }
            catch (KeyValueException)
            {
                return;
            }

            foreach (var (groupName, group) in root)
            {
                if (group.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                var options = new List<DecalOption>();

                foreach (var (material, weight) in group)
                {
                    // Entries that name a sprite sheet sequence rather than a material are not supported
                    if (material.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase)
                        && float.TryParse((string)weight, NumberStyles.Float, CultureInfo.InvariantCulture, out var probability))
                    {
                        options.Add(new DecalOption(material, null, probability));
                    }
                }

                decalGroups[groupName] = [.. options];
            }
        }

        public DecalOption? PickOption(string? groupName, Random random)
        {
            if (string.IsNullOrEmpty(groupName) || !decalGroups.TryGetValue(groupName, out var options) || options.Length == 0)
            {
                return null;
            }

            var totalProbability = 0f;

            foreach (var option in options)
            {
                totalProbability += option.Probability;
            }

            var pick = random.NextSingle() * totalProbability;

            foreach (var option in options)
            {
                pick -= option.Probability;

                if (pick <= 0f)
                {
                    return option;
                }
            }

            return options[^1];
        }

        // A surface without its own impact entry inherits one through its base surface. An empty decal
        // name, as on no_decal, means no decal at all.
        public string? FindDecalGroup(uint surfacePropertyHash, bool isGrazing)
        {
            var name = surfaceNamesByHash.GetValueOrDefault(surfacePropertyHash, "default");

            for (var depth = 0; depth < 16 && name != null; depth++)
            {
                if (surfaceImpacts.TryGetValue(name, out var impact) && impact.Decal != null)
                {
                    return isGrazing && !string.IsNullOrEmpty(impact.GrazingDecal)
                        ? impact.GrazingDecal
                        : impact.Decal;
                }

                name = surfaceBases.GetValueOrDefault(name);
            }

            return surfaceImpacts.GetValueOrDefault("default").Decal;
        }
    }
}
