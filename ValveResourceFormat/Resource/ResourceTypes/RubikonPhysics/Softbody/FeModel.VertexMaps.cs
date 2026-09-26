using System.Globalization;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// Orders <paramref name="proxy"/>'s selections so each node's <c>m_DynNodeVertexSet</c> winner precedes every
        /// other selection painting it at the same weight. Returns the proxy's own order when that is not possible.
        /// </summary>
        internal string[] VertexSetStreamOrder(ProxyMesh proxy)
        {
            var names = proxy.VertexMaps.Select(static map => map.Name).ToArray();
            if (names.Length < 2 || DynNodeVertexSet.Length == 0)
            {
                return names;
            }

            var nameOfHash = new Dictionary<uint, string>(VertexMaps.Count);
            foreach (var map in VertexMaps)
            {
                nameOfHash.TryAdd(map.NameHash, map.Name);
            }

            var constraints = new List<(string Winner, string Rival)>();
            for (var vertex = 0; vertex < proxy.NodeIndices.Length; vertex++)
            {
                var dynamic = proxy.NodeIndices[vertex] - StaticNodeCount;
                if (dynamic < 0 || dynamic >= DynNodeVertexSet.Length)
                {
                    continue;
                }

                var set = DynNodeVertexSet[dynamic];
                if (set >= VertexSetNames.Length
                    || !nameOfHash.TryGetValue(VertexSetNames[set], out var winner))
                {
                    continue;
                }

                var top = 0f;
                foreach (var (_, weights) in proxy.VertexMaps)
                {
                    if (vertex < weights.Length)
                    {
                        top = MathF.Max(top, weights[vertex]);
                    }
                }

                if (top <= 0f)
                {
                    continue;
                }

                var winnerCovers = false;
                var rivals = new List<string>();
                foreach (var (mapName, weights) in proxy.VertexMaps)
                {
                    if (vertex >= weights.Length || weights[vertex] < top)
                    {
                        continue;
                    }

                    if (mapName == winner)
                    {
                        winnerCovers = true;
                    }
                    else
                    {
                        rivals.Add(mapName);
                    }
                }

                if (!winnerCovers)
                {
                    continue;
                }

                foreach (var rival in rivals)
                {
                    constraints.Add((winner, rival));
                }
            }

            return OrderByFirstWriterWins(names, constraints);
        }

        internal static string[] OrderByFirstWriterWins(IReadOnlyList<string> names,
            IReadOnlyList<(string Winner, string Rival)> constraints)
        {
            var incoming = new Dictionary<string, HashSet<string>>(names.Count, StringComparer.Ordinal);
            foreach (var name in names)
            {
                incoming[name] = [];
            }

            foreach (var (winner, rival) in constraints)
            {
                if (winner != rival && incoming.ContainsKey(winner) && incoming.TryGetValue(rival, out var before))
                {
                    before.Add(winner);
                }
            }

            var ordered = new List<string>(names.Count);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            while (ordered.Count < names.Count)
            {
                var next = names.FirstOrDefault(name => !placed.Contains(name)
                    && incoming[name].All(placed.Contains));
                if (next is null)
                {
                    return [.. names];
                }

                ordered.Add(next);
                placed.Add(next);
            }

            return [.. ordered];
        }

        /// <summary>Gets the names of the <c>m_VertexMaps</c> records that cover no vertex.</summary>
        internal IReadOnlyList<string> ZeroVertexSelectionNames { get; private set; } = [];

        /// <summary>Gets the name a selection rebuilt from <see cref="VertexSetNames"/> is exported under.</summary>
        private static string SynthesizedVertexSetName(int set)
            => string.Create(CultureInfo.InvariantCulture, $"vertex_set_{set}");

        /// <summary>
        /// Rebuilds the named selections from <see cref="VertexSetNames"/> and <see cref="DynNodeVertexSet"/>.
        /// </summary>
        private List<VertexMap> BuildVertexMapsFromSets()
        {
            var sets = new List<VertexMap>();
            for (var set = 0; set < VertexSetNames.Length; set++)
            {
                var weights = new float[DynNodeVertexSet.Length];
                var members = 0;
                for (var node = 0; node < DynNodeVertexSet.Length; node++)
                {
                    if (DynNodeVertexSet[node] == set)
                    {
                        weights[node] = 1f;
                        members++;
                    }
                }

                if (members > 0)
                {
                    sets.Add(new VertexMap(SynthesizedVertexSetName(set), VertexSetNames[set],
                        StaticNodeCount, DynNodeVertexSet.Length, default, weights));
                }
            }

            return sets;
        }

        private bool vertexMapsFromSets;

        /// <summary>
        /// Drops the selection rebuilt from the vertex set named after the model's file. Selections read from
        /// <c>m_VertexMaps</c> are kept.
        /// </summary>
        /// <param name="modelFileName">The model's file name without directory or extension.</param>
        internal void DropModelNameVertexSet(string modelFileName)
        {
            if (!vertexMapsFromSets)
            {
                return;
            }

            var hash = StringToken.Get(modelFileName);
            VertexMaps = [.. VertexMaps.Where(map => map.NameHash != hash)];
        }

        /// <summary>
        /// Drops the selection rebuilt from the vertex set with name hash 0. Selections read from <c>m_VertexMaps</c> are kept.
        /// </summary>
        internal void DropUnnamedVertexSet()
        {
            if (!vertexMapsFromSets)
            {
                return;
            }

            VertexMaps = [.. VertexMaps.Where(static map => map.NameHash != 0)];
        }

        /// <summary>
        /// Gets the selections <paramref name="node"/> belongs to as <c>name[=weight],...</c>, or null when it belongs to
        /// none. A weight of 1 is written as the bare name.
        /// </summary>
        internal string? GetVertexMapNames(int node)
        {
            var names = new List<string>();
            foreach (var map in VertexMaps)
            {
                var weight = map.WeightOf(node);
                if (weight <= 0f)
                {
                    continue;
                }

                names.Add(weight >= 1f
                    ? map.Name
                    : string.Create(CultureInfo.InvariantCulture, $"{map.Name}={weight}"));
            }

            return names.Count > 0 ? string.Join(',', names) : null;
        }

        /// <summary>
        /// Gets how strongly <paramref name="node"/> belongs to the selection named
        /// <paramref name="mapName"/>, 0 when the selection does not exist or does not cover it.
        /// </summary>
        internal float VertexMapWeight(string mapName, int node)
        {
            foreach (var map in VertexMaps)
            {
                if (map.Name == mapName)
                {
                    return map.WeightOf(node);
                }
            }

            return 0f;
        }

        /// <summary>
        /// Gets the one partial weight every node covered by the selection shares, or null when there is none.
        /// </summary>
        internal float? UniformVertexMapWeight(string mapName)
        {
            float? shared = null;
            foreach (var map in VertexMaps)
            {
                if (map.Name != mapName)
                {
                    continue;
                }

                for (var node = 0; node < CtrlNames.Length; node++)
                {
                    var weight = map.WeightOf(node);
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    if (shared is { } first && MathF.Abs(weight - first) > 0.5f / 255f)
                    {
                        return null;
                    }

                    shared ??= weight;
                }

                break;
            }

            return shared is { } value && value < 1f ? value : null;
        }

        /// <summary>Strips the optional <c>=weight</c> suffix off one entry of a <see cref="GetVertexMapNames"/> list.</summary>
        internal static string VertexMapName(string entry)
        {
            var weight = entry.IndexOf('=', StringComparison.Ordinal);
            return weight < 0 ? entry : entry[..weight];
        }

        /// <summary>
        /// Gets the selection that covers exactly the simulated nodes of <paramref name="proxy"/> (or of the sheets of
        /// <paramref name="group"/> it overlaps) and is not registered as a vertex set, or null when no single one does.
        /// </summary>
        internal string? GetProxyVertexMapName(ProxyMesh proxy, IReadOnlyList<ProxyMesh>? group = null)
        {
            var simulated = SimulatedProxyNodes(proxy);
            if (simulated.Count == 0)
            {
                return null;
            }

            string? found = null;
            foreach (var map in VertexMaps)
            {
                if (Array.IndexOf(VertexSetNames, map.NameHash) >= 0)
                {
                    continue;
                }

                var members = new HashSet<int>();
                for (var i = 0; i < map.Weights.Length; i++)
                {
                    if (map.Weights[i] > 0f)
                    {
                        members.Add(map.VertexBase + i);
                    }
                }

                if (!members.Overlaps(simulated))
                {
                    continue;
                }

                var covered = new HashSet<int>();
                foreach (var sibling in group ?? [proxy])
                {
                    var siblingNodes = SimulatedProxyNodes(sibling);
                    if (members.Overlaps(siblingNodes))
                    {
                        covered.UnionWith(siblingNodes);
                    }
                }

                if (members.SetEquals(covered))
                {
                    if (found is not null)
                    {
                        if (VertexMapAliases(found).Contains(map.Name))
                        {
                            continue;
                        }

                        return null;
                    }

                    found = map.Name;
                }
            }

            return found;
        }

        /// <summary>Gets whether <paramref name="nameHash"/> is registered in <see cref="VertexSetNames"/>.</summary>
        internal bool RegistersVertexSet(uint nameHash) => Array.IndexOf(VertexSetNames, nameHash) >= 0;

        /// <summary>Gets the first selection named <paramref name="name"/>.</summary>
        internal bool TryGetVertexMap(string name, out VertexMap map)
        {
            foreach (var candidate in VertexMaps)
            {
                if (candidate.Name == name)
                {
                    map = candidate;
                    return true;
                }
            }

            map = default;
            return false;
        }

        /// <summary>
        /// Gets every selection, not registered as a vertex set, with the same membership as <paramref name="mapName"/>,
        /// in compiled order and including it. Empty when no selection has that name.
        /// </summary>
        internal IReadOnlyList<string> VertexMapAliases(string mapName)
        {
            if (!TryGetVertexMap(mapName, out var source))
            {
                return [];
            }

            return [.. VertexMaps
                .Where(map => map.Name == mapName
                    || (Array.IndexOf(VertexSetNames, map.NameHash) < 0 && HasSameMembership(map, source)))
                .Select(static map => map.Name)];
        }

        private static bool HasSameMembership(VertexMap a, VertexMap b)
        {
            var first = Math.Min(a.VertexBase, b.VertexBase);
            var last = Math.Max(a.VertexBase + a.VertexCount, b.VertexBase + b.VertexCount);
            for (var node = first; node < last; node++)
            {
                if (MathF.Abs(a.WeightOf(node) - b.WeightOf(node)) > 0.5f / 255f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The control nodes of <paramref name="proxy"/> whose vertices its sheet simulates.</summary>
        private static HashSet<int> SimulatedProxyNodes(ProxyMesh proxy)
        {
            var simulated = new HashSet<int>();
            for (var v = 0; v < proxy.NodeIndices.Length; v++)
            {
                if (v < proxy.ClothEnable.Length && proxy.ClothEnable[v] != 0f)
                {
                    simulated.Add(proxy.NodeIndices[v]);
                }
            }

            return simulated;
        }

        /// <summary>
        /// The weight a selection is painted at on a node <c>m_DynNodeVertexSet</c> puts in its set while its compiled
        /// weight there is 0; it rounds back to 0.
        /// </summary>
        internal const float SubQuantumMembershipWeight = 0.001f;

        private (string Name, float[] Weights)[] BuildVertexMapWeights(int[] nodeIndices)
        {
            var maps = new List<(string, float[])>();
            foreach (var map in VertexMaps)
            {
                var weights = new float[nodeIndices.Length];
                var covers = false;
                for (var i = 0; i < nodeIndices.Length; i++)
                {
                    weights[i] = map.WeightOf(nodeIndices[i]);
                    if (weights[i] <= 0f && InRecordedVertexSet(nodeIndices[i], map.NameHash))
                    {
                        weights[i] = SubQuantumMembershipWeight;
                    }

                    covers |= weights[i] > 0f;
                }

                if (covers)
                {
                    maps.Add((map.Name, weights));
                }
            }

            return [.. maps];
        }

        /// <summary>
        /// Whether <c>m_DynNodeVertexSet</c> puts <paramref name="node"/> in the vertex set keyed by
        /// <paramref name="nameHash"/>. False for a static node, and for every node of a model that ships no per-node
        /// set array.
        /// </summary>
        private bool InRecordedVertexSet(int node, uint nameHash)
        {
            var dynamic = node - StaticNodeCount;
            return dynamic >= 0 && dynamic < DynNodeVertexSet.Length
                && DynNodeVertexSet[dynamic] < VertexSetNames.Length
                && VertexSetNames[DynNodeVertexSet[dynamic]] == nameHash;
        }
    }
}
