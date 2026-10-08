using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Decals
{
    // Which decal materials a bullet leaves on each surface property: surface properties name an
    // impact decal group, and the group lists materials with relative probabilities
    internal sealed class ImpactDecalTable
    {
        // Sequence names the frame of the material's sprite sheet to use, for a material that holds many decals
        public readonly record struct DecalOption(string Material, string? Sequence, float Probability);

        private readonly record struct SurfaceImpact(string? Decal, string? GrazingDecal, string? Effect);

        private readonly Dictionary<uint, string> surfaceNamesByHash = [];
        private readonly Dictionary<uint, string> bulletImpactSounds = [];
        private readonly Dictionary<string, string> surfaceBases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SurfaceImpact> surfaceImpacts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DecalOption[]> decalGroups = new(StringComparer.OrdinalIgnoreCase);

        public bool HasDecalGroups => decalGroups.Count > 0;

        public ImpactDecalTable(GameFileLoader fileLoader)
        {
            LoadSurfaceProperties(fileLoader);
            LoadImpactEffects(fileLoader);
            LoadDecalGroups(fileLoader);

            if (decalGroups.Count == 0)
            {
                LoadTextDecalGroups(fileLoader);
            }
        }

        private void LoadSurfaceProperties(GameFileLoader fileLoader)
        {
            using var resource = fileLoader.LoadFileCompiled("surfaceproperties/surfaceproperties.vsurf");

            if (resource?.DataBlock is not BinaryKV3 kv3)
            {
                return;
            }

            foreach (var surface in kv3.Data.Root.GetArray("SurfacePropertiesList") ?? [])
            {
                if (surface.GetStringProperty("surfacePropertyName") is not { } name)
                {
                    continue;
                }

                var hash = StringToken.Store(name);
                surfaceNamesByHash[hash] = name;

                if (surface.GetSubCollection("audiosounds")?.GetStringProperty("bulletimpact") is { Length: > 0 } sound)
                {
                    bulletImpactSounds[hash] = sound;
                }

                if (surface.GetStringProperty("base") is { } baseName)
                {
                    surfaceBases[name] = baseName;
                }
            }
        }

        private void LoadImpactEffects(GameFileLoader fileLoader)
        {
            using var stream = fileLoader.GetFileStream("scripts/surfaceproperties_impact_effects.txt");

            if (stream == null)
            {
                return;
            }

            foreach (var surface in KVDocumentExtensions.ParseKV3(stream).Root.GetArray("SurfacePropertiesList") ?? [])
            {
                if (surface.GetStringProperty("surfacePropertyName") is { } name)
                {
                    surfaceImpacts[name] = new SurfaceImpact(
                        surface.GetStringProperty("impactDecalName"),
                        surface.GetStringProperty("impactGrazingDecalName"),
                        surface.GetStringProperty("effect"));
                }
            }
        }

        private void LoadDecalGroups(GameFileLoader fileLoader)
        {
            using var resource = fileLoader.LoadFileCompiled("scripts/decalgroups.vdata");

            if (resource?.DataBlock is not BinaryKV3 kv3)
            {
                return;
            }

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

                decalGroups[groupName] = [.. options];
            }
        }

        // The same groups as text, each a list of materials and their weights
        private void LoadTextDecalGroups(GameFileLoader fileLoader)
        {
            using var stream = fileLoader.GetFileStream("scripts/decals_subrect.txt");

            if (stream == null)
            {
                return;
            }

            KVObject root;

            try
            {
                root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).DeserializeWithoutRoot(stream);
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

        // An empty decal name, as on no_decal, means no decal at all
        public string? FindDecalGroup(uint surfacePropertyHash, bool isGrazing)
        {
            var impact = FindImpact(surfacePropertyHash, static impact => impact.Decal != null);

            return isGrazing && !string.IsNullOrEmpty(impact.GrazingDecal)
                ? impact.GrazingDecal
                : impact.Decal;
        }

        // Every surface names its own sound, so there is no base surface to fall back to; some name none
        public string? FindBulletImpactSound(uint surfacePropertyHash)
            => bulletImpactSounds.GetValueOrDefault(surfaceNamesByHash.ContainsKey(surfacePropertyHash)
                ? surfacePropertyHash
                : StringToken.Store("default"));

        public string? FindEffect(uint surfacePropertyHash)
            => FindImpact(surfacePropertyHash, static impact => impact.Effect != null).Effect;

        // A surface without what is asked for inherits it through its base surface
        private SurfaceImpact FindImpact(uint surfacePropertyHash, Func<SurfaceImpact, bool> has)
        {
            var name = surfaceNamesByHash.GetValueOrDefault(surfacePropertyHash, "default");

            for (var depth = 0; depth < 16 && name != null; depth++)
            {
                if (surfaceImpacts.TryGetValue(name, out var impact) && has(impact))
                {
                    return impact;
                }

                name = surfaceBases.GetValueOrDefault(name);
            }

            return surfaceImpacts.GetValueOrDefault("default");
        }
    }
}
