using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// Converts the <c>$keyvalues</c> block of a model to the key values of a Source 2 model, which hold each
/// data class under the name the model doc writes it with.
/// </summary>
internal static class Source1ModelKeyValues
{
    private static readonly string[] SnapshotWeightLists = ["snapshot_weights_all", "snapshot_weights_upperbody"];

    /// <summary>Converts the key values of a model.</summary>
    /// <param name="source">The model.</param>
    /// <returns>The key values, which are empty when the model has none to convert.</returns>
    public static KVObject Convert(Source1Model source)
    {
        var keyValues = KVObject.Collection();

        if (source.KeyValues is not { } root)
        {
            return keyValues;
        }

        AddSubtree(root, keyValues, "prop_data");
        AddSubtree(root, keyValues, "door_options");
        AddParticles(root, keyValues);

        foreach (var listName in SnapshotWeightLists)
        {
            AddSnapshotWeights(root, keyValues, listName);
        }

        return keyValues;
    }

    private static void AddSubtree(KVObject root, KVObject keyValues, string name)
    {
        if (root.GetSubCollection(name) is { IsCollection: true } subtree)
        {
            keyValues.Add(name, KVObjectDeepClone.Clone(subtree));
        }
    }

    // The path names the particle system of the converted particle files, as the entity properties that name one do.
    private static void AddParticles(KVObject root, KVObject keyValues)
    {
        if (root.GetSubCollection("particles") is not { IsCollection: true } particles)
        {
            return;
        }

        var list = KVObject.Array();

        foreach (var (_, effect) in particles.Children)
        {
            var name = effect.IsCollection ? effect.GetStringProperty("name", string.Empty) : string.Empty;

            if (name.Length == 0)
            {
                continue;
            }

            var particle = KVObject.Collection();
            particle.Add("name", Source1ResourceReferences.Reference(Source1ResourceProvider.GetParticlePath(name)));
            particle.Add("attachment_type", effect.GetStringProperty("attachment_type", string.Empty));
            particle.Add("attachment_point", effect.GetStringProperty("attachment_point", string.Empty));
            list.Add(particle);
        }

        if (list.Count > 0)
        {
            keyValues.Add("particles_list", list);
        }
    }

    private static void AddSnapshotWeights(KVObject root, KVObject keyValues, string name)
    {
        if (root.GetSubCollection(name) is not { IsCollection: true } weights)
        {
            return;
        }

        var list = KVObject.Array();

        foreach (var (boneName, weight) in weights.Children)
        {
            if (weight.IsCollection || weight.IsArray)
            {
                continue;
            }

            var entry = KVObject.Collection();
            entry.Add("bonename", boneName);
            entry.Add("weight", weight);
            list.Add(entry);
        }

        if (list.Count > 0)
        {
            keyValues.Add($"{name}_list", list);
        }
    }
}
