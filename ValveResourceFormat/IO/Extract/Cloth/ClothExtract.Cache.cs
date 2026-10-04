using System.Linq;
using System.Runtime.CompilerServices;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    private static readonly ConditionalWeakTable<ClothReconstruction, ExportLookups> Lookups = new();

    private static ExportLookups LookupsOf(ClothReconstruction cloth) => Lookups.GetValue(cloth, static c => new ExportLookups(c));

    /// <summary>
    /// Indexes over a reconstruction's compiled arrays, built lazily and shared, so callers must not modify them.
    /// </summary>
    private sealed class ExportLookups(ClothReconstruction cloth)
    {
        private Dictionary<string, int>? nodeByName;
        private HashSet<int>? jiggleNodes;
        private Dictionary<int, int>? rodNeighbourCounts;
        private Dictionary<uint, string>? boneByHash;
        private RodPairCounts? rodCounts;
        private Dictionary<(int, int), FeModel.Rod>? firstRodByPair;

        /// <summary>Gets the first control node of every control name, matched by ordinal.</summary>
        public Dictionary<string, int> NodeByName => nodeByName ??= IndexNames();

        /// <summary>Gets the first control node of every control name, ignoring case.</summary>
        public Dictionary<string, int> NodeByNameIgnoreCase => cloth.NodeByNameIgnoreCase;

        /// <summary>Gets the control nodes of the jiggle bones.</summary>
        public HashSet<int> JiggleNodes => jiggleNodes ??= [.. cloth.Fe.JiggleBones.Select(static jiggle => jiggle.Node)];

        /// <summary>Gets the number of distinct nodes each node shares an <c>m_Rods</c> record with.</summary>
        public Dictionary<int, int> RodNeighbourCounts => rodNeighbourCounts ??= CountRodNeighbours();

        /// <summary>Gets the name under every name hash, skeleton bones taking precedence over control names.</summary>
        public Dictionary<uint, string> BoneByHash => boneByHash ??= HashBoneNames();

        /// <summary>Gets the number of rods, and of banded rods, on every node pair.</summary>
        public RodPairCounts RodCounts => rodCounts ??= CountRodPairs();

        /// <summary>Gets the first rod the model records on every node pair.</summary>
        public Dictionary<(int, int), FeModel.Rod> FirstRodByPair
            => firstRodByPair ??= cloth.RodsByPair.ToDictionary(static entry => entry.Key, static entry => entry.Value[0]);

        private Dictionary<string, int> IndexNames()
        {
            var index = new Dictionary<string, int>(cloth.Fe.CtrlNames.Length, StringComparer.Ordinal);
            for (var node = 0; node < cloth.Fe.CtrlNames.Length; node++)
            {
                index.TryAdd(cloth.Fe.CtrlNames[node], node);
            }

            return index;
        }

        private Dictionary<int, int> CountRodNeighbours()
        {
            var neighbours = new Dictionary<int, HashSet<int>>();
            foreach (var rod in cloth.Fe.Rods)
            {
                ClothReconstruction.GetOrAdd(neighbours, rod.NodeA).Add(rod.NodeB);
                ClothReconstruction.GetOrAdd(neighbours, rod.NodeB).Add(rod.NodeA);
            }

            return neighbours.ToDictionary(static entry => entry.Key, static entry => entry.Value.Count);
        }

        private Dictionary<uint, string> HashBoneNames()
        {
            var byHash = new Dictionary<uint, string>();
            foreach (var name in (cloth.SkeletonBoneNames ?? Enumerable.Empty<string>()).Concat(cloth.Fe.CtrlNames))
            {
                byHash.TryAdd(StringToken.Get(name), name);
            }

            return byHash;
        }

        private RodPairCounts CountRodPairs()
        {
            var entries = cloth.RodsByPair.ToDictionary(static entry => entry.Key, static entry => entry.Value.Count);
            var banded = new Dictionary<(int, int), int>();
            foreach (var rod in cloth.Fe.Rods)
            {
                if (rod.IsBanded)
                {
                    var key = RodPair(rod);
                    banded[key] = banded.GetValueOrDefault(key) + 1;
                }
            }

            return new RodPairCounts(entries, banded);
        }
    }

    /// <summary>The number of rods, and of banded rods, on every node pair.</summary>
    internal sealed record RodPairCounts(Dictionary<(int, int), int> Entries, Dictionary<(int, int), int> Banded);
}
