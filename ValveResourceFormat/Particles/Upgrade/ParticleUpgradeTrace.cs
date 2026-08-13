using System.Linq;
using ValveKeyValue;
using ValveKeyValue.KeyValues3;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Upgrade;

/// <summary>
/// Traces the function entries of a particle system document through the vpcf format-conversion
/// chain, reporting per function list which entries survive, under which class name, and which
/// entries the chain removes.
/// </summary>
public static class ParticleUpgradeTrace
{
    /// <summary>
    /// One traced function entry: the class name after upgrading, the stored class name when the
    /// chain renamed the entry, and whether the chain removed the entry entirely.
    /// </summary>
    public sealed record TracedFunction(string Class, string? OriginalClass, bool RemovedByUpgrade);

    /// <summary>
    /// The upgraded document the trace was taken from, and the traced entries keyed by function
    /// list member name. The upgraded root is the same tree the trace describes, so callers that
    /// need both do not have to run the chain twice.
    /// </summary>
    public sealed record TraceResult(KVObject UpgradedRoot, IReadOnlyDictionary<string, IReadOnlyList<TracedFunction>> Functions);

    private static readonly string[] FunctionListNames =
    [
        "m_PreEmissionOperators",
        "m_Emitters",
        "m_Initializers",
        "m_Operators",
        "m_ForceGenerators",
        "m_Constraints",
        "m_Renderers",
    ];

    /// <summary>
    /// Upgrades a clone of the given document root through the chain while tracking each function
    /// entry by identity, so renames, cross-list moves and reorders pair up exactly. Surviving
    /// entries appear in upgraded order in the list holding them after upgrading, and removed
    /// entries follow at the tail of the list that held them in the stored document.
    /// </summary>
    public static TraceResult TraceFunctions(KVObject root, KV3ID? storedFormat)
    {
        ArgumentNullException.ThrowIfNull(root);

        var clone = KVObjectDeepClone.Clone(root);
        var sources = new List<(string ListName, string Class)>();
        var sourceIndexByNode = new Dictionary<KVObject, int>(ReferenceEqualityComparer.Instance);

        foreach (var listName in FunctionListNames)
        {
            foreach (var element in clone.ElementsOf(listName))
            {
                if (!UpgradeKV.IsObject(element))
                {
                    continue;
                }

                sourceIndexByNode[element] = sources.Count;
                sources.Add((listName, element.GetString("_class", "")));
            }
        }

        ParticleFormatUpgrader.ApplyFrom(clone, ParticleFormatUpgrader.ResolveStartIndex(storedFormat));

        var lists = new Dictionary<string, List<TracedFunction>>(FunctionListNames.Length);
        var survived = new bool[sources.Count];

        foreach (var listName in FunctionListNames)
        {
            var entries = new List<TracedFunction>();

            foreach (var element in clone.ElementsOf(listName))
            {
                if (!UpgradeKV.IsObject(element))
                {
                    continue;
                }

                var className = element.GetString("_class", "");
                string? originalClass = null;

                if (sourceIndexByNode.TryGetValue(element, out var id))
                {
                    survived[id] = true;

                    if (sources[id].Class != className)
                    {
                        originalClass = sources[id].Class;
                    }
                }

                entries.Add(new TracedFunction(className, originalClass, RemovedByUpgrade: false));
            }

            lists[listName] = entries;
        }

        for (var id = 0; id < sources.Count; id++)
        {
            if (!survived[id])
            {
                lists[sources[id].ListName].Add(new TracedFunction(sources[id].Class, null, RemovedByUpgrade: true));
            }
        }

        return new TraceResult(clone, lists.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<TracedFunction>)entry.Value));
    }
}
