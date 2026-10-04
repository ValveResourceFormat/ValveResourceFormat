using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.FeModelIndex;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        /// <summary>The default <c>back_solve_influence_threshold</c> of a <c>ClothProxyMeshFile</c>.</summary>
        internal const float DefaultBackSolveInfluenceThreshold = 0.05f;

        /// <summary>The smallest per-vertex skin influence count a cloth proxy DMX is written with.</summary>
        internal const int ClothProxyInfluenceSlots = 4;

        /// <summary>The number of influencing vertices from which the compiler fits a bone with an <c>m_FitMatrices</c> solve.</summary>
        private const int FitMatrixMinInfluences = 8;

        /// <summary>The most <c>m_CtrlSoftOffsets</c> records the compiler writes for one proxy vertex.</summary>
        private const int ClothProxySoftOffsetSlots = 8;

        /// <summary>Relative gap under which two recovered influence weights are one authored value.</summary>
        private const float TiedWeightEpsilon = 1e-4f;

        private const string ClothRootNodeName = "$cloth_root";

        private Dictionary<string, int>? nodeByNameIgnoreCase;

        /// <summary>Gets the first control node of every control name, ignoring case. Callers must not modify it.</summary>
        internal Dictionary<string, int> NodeByNameIgnoreCase => nodeByNameIgnoreCase ??= IndexNodeNamesIgnoreCase();

        private Dictionary<string, int> IndexNodeNamesIgnoreCase()
        {
            var index = new Dictionary<string, int>(Fe.CtrlName.Length, StringComparer.OrdinalIgnoreCase);
            for (var node = 0; node < Fe.CtrlName.Length; node++)
            {
                index.TryAdd(Fe.CtrlName[node], node);
            }

            return index;
        }

        /// <summary>
        /// Recovers the authored skin weights of the proxy-sheet vertices, the vertices left to the offset network, and the
        /// proxy meshes compiled without back-solving.
        /// </summary>
        private Dictionary<int, SkinInfluence[]> RecoverAuthoredSkinWeights(
            out Dictionary<int, SkinInfluence[]> deferred, out HashSet<int> unbackSolvedMeshes)
        {
            var recovered = new Dictionary<int, SkinInfluence[]>();
            deferred = [];
            unbackSolvedMeshes = [];
            if (Fe.CtrlOffsets.Length == 0)
            {
                return recovered;
            }

            var (fitPerVertex, minIncludedWeight) = ReadFitWeightsPerVertex();

            var rigidParents = new Dictionary<int, int>();
            foreach (var offset in Fe.CtrlOffsets)
            {
                rigidParents[offset.CtrlChild] = offset.CtrlParent;
            }

            var softPerVertex = ReadSoftOffsetsPerVertex();
            unbackSolvedMeshes = FindUnbackSolvedMeshes(rigidParents, softPerVertex);

            var maxOmittedWeight = 0f;
            var fitlessSoft = new List<(int Node, int Primary)>();
            foreach (var (node, primary) in rigidParents)
            {
                if (primary < 0 || primary >= Fe.CtrlName.Length)
                {
                    continue;
                }

                if (ProxyFitMatrixNodes.Count == 0)
                {
                    if (!IsProxyMeshNode(node))
                    {
                        continue;
                    }

                    var painted = new List<SkinInfluence>();
                    foreach (var (bone, weight) in ExpandSoftOffsets(softPerVertex, node, primary))
                    {
                        if (weight > 0f && bone >= 0 && bone < Fe.CtrlName.Length)
                        {
                            painted.Add(new(Fe.CtrlName[bone], weight));
                        }
                    }

                    SnapToBytePartition(painted);
                    recovered[node] = [.. painted];
                    continue;
                }

                if (Index.IsStatic(node))
                {
                    if (fitPerVertex.ContainsKey(node))
                    {
                        recovered[node] = [new(Fe.CtrlName[primary], 1f)];
                        continue;
                    }

                    var pinned = new List<SkinInfluence>();
                    var anchorWeight = 0f;
                    var rival = 0f;
                    foreach (var (bone, weight) in ValidSoftWeights(softPerVertex, node, primary))
                    {
                        if (bone == primary)
                        {
                            anchorWeight = weight;
                        }
                        else
                        {
                            rival = MathF.Max(rival, weight);
                        }

                        pinned.Add(new(Fe.CtrlName[bone], weight));
                    }

                    if (pinned.Count > 0 && anchorWeight >= rival)
                    {
                        SnapToBytePartition(pinned);
                        EnsureAnchorMostBound(pinned, Fe.CtrlName[primary]);
                        recovered[node] = [.. pinned];
                    }
                    else
                    {
                        recovered[node] = [new(Fe.CtrlName[primary], 1f)];
                    }

                    continue;
                }

                if (!fitPerVertex.TryGetValue(node, out var fits))
                {
                    if (!softPerVertex.ContainsKey(node))
                    {
                        recovered[node] = [new(Fe.CtrlName[primary], 1f)];
                    }
                    else
                    {
                        fitlessSoft.Add((node, primary));
                    }

                    continue;
                }

                var dynamicWeights = ExpandSoftOffsets(softPerVertex, node, primary);

                var scale = 1f;
                var bestNormalized = 0f;
                foreach (var (bone, normalized) in dynamicWeights)
                {
                    if (normalized > bestNormalized && fits.TryGetValue(bone, out var fitValue) && normalized > 0f)
                    {
                        bestNormalized = normalized;
                        scale = fitValue / normalized;
                    }
                }

                var influences = new List<SkinInfluence>(dynamicWeights.Count + 1);
                var total = 0f;
                foreach (var (bone, normalized) in dynamicWeights)
                {
                    var weight = normalized * scale;
                    if (weight <= 0f || bone < 0 || bone >= Fe.CtrlName.Length)
                    {
                        continue;
                    }

                    influences.Add(new(Fe.CtrlName[bone], weight));
                    total += weight;
                    if (!fits.ContainsKey(bone))
                    {
                        maxOmittedWeight = MathF.Max(maxOmittedWeight, weight);
                    }
                }

                var remainder = 1f - total;
                if (remainder > 1e-4f && softPerVertex.TryGetValue(node, out var slots)
                    && slots.Count >= ClothProxySoftOffsetSlots)
                {
                    var anchor = FindRealAncestor(primary, Index.IsStatic);
                    var walked = new HashSet<int>();
                    while (anchor >= 0 && influences.Exists(influence => influence.Bone == Fe.CtrlName[anchor]))
                    {
                        anchor = walked.Add(anchor) ? FindRealAncestor(anchor, Index.IsStatic) : -1;
                    }

                    if (anchor >= 0)
                    {
                        influences.Add(new(Fe.CtrlName[anchor], remainder));
                    }
                }

                OrderByWeightKeepingTies(influences);
                EnsureAnchorMostBound(influences, Fe.CtrlName[primary]);
                recovered[node] = [.. influences];
            }

            float? threshold = maxOmittedWeight > 0f && minIncludedWeight < float.MaxValue && maxOmittedWeight < minIncludedWeight
                ? (maxOmittedWeight + minIncludedWeight) * 0.5f
                : null;

            RecoverFitlessSoftWeights(fitlessSoft, rigidParents, softPerVertex, unbackSolvedMeshes, threshold, recovered, deferred);
            return recovered;
        }

        /// <summary>Gets each vertex's <c>m_FitWeights</c> weight per fit bone, and the lightest weight any fit keeps.</summary>
        private (Dictionary<int, Dictionary<int, float>> FitPerVertex, float MinIncludedWeight) ReadFitWeightsPerVertex()
        {
            var fitWeights = Fe.FitWeights;

            var fitPerVertex = new Dictionary<int, Dictionary<int, float>>();
            var minIncludedWeight = float.MaxValue;
            var begin = 0;
            foreach (var fm in Fe.FitMatrices)
            {
                var end = fm.End;
                var bone = fm.Node;
                for (var i = begin; i < end && i < fitWeights.Length; i++)
                {
                    var node = fitWeights[i].Node;
                    var weight = fitWeights[i].Weight;
                    GetOrAdd(fitPerVertex, node)[bone] = weight;
                    minIncludedWeight = MathF.Min(minIncludedWeight, weight);
                }

                begin = end;
            }

            return (fitPerVertex, minIncludedWeight);
        }

        /// <summary>Gets each vertex's <c>m_CtrlSoftOffsets</c> records as (parent, alpha), in array order.</summary>
        private Dictionary<int, List<(int Parent, float Alpha)>> ReadSoftOffsetsPerVertex()
        {
            var softPerVertex = new Dictionary<int, List<(int Parent, float Alpha)>>();
            foreach (var e in Fe.CtrlSoftOffsets)
            {
                GetOrAdd(softPerVertex, e.CtrlChild).Add((e.CtrlParent, e.Alpha));
            }

            return softPerVertex;
        }

        /// <summary>
        /// Gets the proxy mesh indices no fit covers whose simulated vertices bind only to bones fit over other meshes.
        /// </summary>
        private HashSet<int> FindUnbackSolvedMeshes(Dictionary<int, int> rigidParents,
            Dictionary<int, List<(int Parent, float Alpha)>> softPerVertex)
        {
            var backSolvedMeshes = new HashSet<int>();
            var fitBones = new HashSet<int>();
            foreach (var (bone, targets) in Index.FitMatrixTargets)
            {
                foreach (var target in targets)
                {
                    var targetMesh = ProxyMeshIndexOf(target);
                    if (targetMesh >= 0)
                    {
                        backSolvedMeshes.Add(targetMesh);
                        fitBones.Add(bone);
                    }
                }
            }

            if (backSolvedMeshes.Count == 0)
            {
                return [];
            }

            var drivenByMesh = new Dictionary<int, HashSet<int>>();
            foreach (var (node, primary) in rigidParents)
            {
                var mesh = ProxyMeshIndexOf(node);
                if (mesh < 0 || backSolvedMeshes.Contains(mesh) || Index.IsStatic(node)
                    || primary < 0 || primary >= Fe.CtrlName.Length)
                {
                    continue;
                }

                foreach (var (bone, weight) in ExpandSoftOffsets(softPerVertex, node, primary))
                {
                    if (weight < DefaultBackSolveInfluenceThreshold || bone < 0 || bone >= Fe.CtrlName.Length
                        || !IsPositionDriven(bone) || IsProxyNodeName(Fe.CtrlName[bone]))
                    {
                        continue;
                    }

                    GetOrAdd(drivenByMesh, mesh).Add(bone);
                }
            }

            var unbackSolvedMeshes = new HashSet<int>();
            foreach (var (mesh, bones) in drivenByMesh)
            {
                if (bones.All(fitBones.Contains))
                {
                    unbackSolvedMeshes.Add(mesh);
                }
            }

            return unbackSolvedMeshes;
        }

        /// <summary>
        /// Recovers the soft-offset weights of the dynamic vertices no fit covers, into <paramref name="recovered"/> where a
        /// recompile prunes them the same way and into <paramref name="deferred"/> otherwise.
        /// </summary>
        private void RecoverFitlessSoftWeights(List<(int Node, int Primary)> fitlessSoft, Dictionary<int, int> rigidParents,
            Dictionary<int, List<(int Parent, float Alpha)>> softPerVertex, HashSet<int> unbackSolvedMeshes, float? threshold,
            Dictionary<int, SkinInfluence[]> recovered, Dictionary<int, SkinInfluence[]> deferred)
        {
            var fitlessNodes = fitlessSoft.Select(static fitless => fitless.Node).ToHashSet();

            var drivenDynamicBones = new HashSet<int>();
            foreach (var (node, parent) in rigidParents)
            {
                if (fitlessNodes.Contains(node) || Index.IsStatic(node) || parent < 0 || parent >= Fe.CtrlName.Length)
                {
                    continue;
                }

                drivenDynamicBones.Add(parent);
            }

            foreach (var (node, primary) in fitlessSoft)
            {
                var painted = new List<SkinInfluence>();
                var prunable = true;

                var mesh = ProxyMeshIndexOf(node);
                var sheetBackSolves = mesh < 0 || !unbackSolvedMeshes.Contains(mesh);
                foreach (var (bone, weight) in ValidSoftWeights(softPerVertex, node, primary))
                {
                    if (prunable && sheetBackSolves && Index.FitMatrixNodes.Contains(bone)
                        && weight >= (threshold ?? DefaultBackSolveInfluenceThreshold))
                    {
                        prunable = false;
                    }

                    if (prunable && !Index.IsStatic(bone) && !drivenDynamicBones.Contains(bone)
                        && !ReverseOffsetBones.Contains(bone) && !Index.FitMatrixNodes.Contains(bone))
                    {
                        prunable = false;
                    }

                    painted.Add(new(Fe.CtrlName[bone], weight));
                }

                if (painted.Count == 0)
                {
                    continue;
                }

                SnapToBytePartition(painted);
                if (prunable)
                {
                    recovered[node] = [.. painted];
                }
                else
                {
                    deferred[node] = [.. painted];
                }
            }
        }

        /// <summary>
        /// Expands a vertex's primary bone and soft offsets into bone weights by applying each nested lerp in array order.
        /// </summary>
        private static List<(int Bone, float Weight)> ExpandSoftOffsets(
            Dictionary<int, List<(int Parent, float Alpha)>> softPerVertex, int node, int primary)
        {
            var weights = new List<(int Bone, float Weight)> { (primary, 1f) };
            if (!softPerVertex.TryGetValue(node, out var softs))
            {
                return weights;
            }

            foreach (var (parent, alpha) in softs)
            {
                for (var i = 0; i < weights.Count; i++)
                {
                    weights[i] = (weights[i].Bone, weights[i].Weight * alpha);
                }

                var existing = weights.FindIndex(w => w.Bone == parent);
                if (existing >= 0)
                {
                    weights[existing] = (parent, weights[existing].Weight + (1f - alpha));
                }
                else
                {
                    weights.Add((parent, 1f - alpha));
                }
            }

            return weights;
        }

        /// <summary>
        /// Gets the <see cref="ExpandSoftOffsets"/> weights whose bone is a control node, without those at or below zero.
        /// </summary>
        private IEnumerable<(int Bone, float Weight)> ValidSoftWeights(
            Dictionary<int, List<(int Parent, float Alpha)>> softPerVertex, int node, int primary)
        {
            foreach (var (bone, weight) in ExpandSoftOffsets(softPerVertex, node, primary))
            {
                if (weight <= 0f || bone < 0 || bone >= Fe.CtrlName.Length)
                {
                    continue;
                }

                yield return (bone, weight);
            }
        }

        /// <summary>
        /// Gets whether <paramref name="proxy"/> has sheet vertices and all of them belong to <see cref="UnbackSolvedProxyMeshes"/>.
        /// </summary>
        internal bool IsUnbackSolvedProxyMesh(ProxyMesh proxy)
        {
            var sheetVertices = 0;
            foreach (var node in proxy.NodeIndices)
            {
                var mesh = ProxyMeshIndexOf(node);
                if (mesh < 0)
                {
                    continue;
                }

                if (!UnbackSolvedProxyMeshes.Contains(mesh))
                {
                    return false;
                }

                sheetVertices++;
            }

            return sheetVertices > 0;
        }

        /// <summary>
        /// Gets whether a bone outside the position-driven suffix is fit over <paramref name="proxy"/>'s vertices. False when
        /// the compile states no position-driven boundary.
        /// </summary>
        internal bool ProxyFitsUndrivenBone(ProxyMesh proxy)
        {
            if (!HasCompiledFirstPositionDrivenNode || Index.FitMatrixTargets.Count == 0)
            {
                return false;
            }

            var own = new HashSet<int>(proxy.NodeIndices);
            foreach (var (bone, targets) in Index.FitMatrixTargets)
            {
                if (!IsPositionDriven(bone) && Array.Exists(targets, own.Contains))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the <c>back_solve_influence_threshold</c> for <paramref name="proxy"/>: the default, unless the proxy's fit
        /// data keeps a lighter weight, then between that weight and the heaviest weight a fit drops.
        /// </summary>
        internal float GetBackSolveInfluenceThreshold(ProxyMesh proxy)
        {
            var proxyNodes = new HashSet<int>(proxy.NodeIndices);
            var fitTargets = new Dictionary<int, HashSet<int>>(Index.FitMatrixTargets.Count);
            foreach (var (bone, targets) in Index.FitMatrixTargets)
            {
                var covered = new HashSet<int>(targets);
                if (covered.Overlaps(proxyNodes))
                {
                    fitTargets[bone] = covered;
                }
            }

            var painted = new Dictionary<int, List<(int Node, float Weight)>>();
            for (var v = 0; v < proxy.NodeIndices.Length && v < proxy.SkinInfluences.Length; v++)
            {
                var node = proxy.NodeIndices[v];
                foreach (var (boneName, weight) in proxy.SkinInfluences[v])
                {
                    if (!NodeByNameIgnoreCase.TryGetValue(boneName, out var bone)
                        || weight <= 0f || IsProxyNodeName(Fe.CtrlName[bone]) || Index.IsStatic(bone))
                    {
                        continue;
                    }

                    GetOrAdd(painted, bone).Add((node, weight));
                }
            }

            var kept = float.MaxValue;
            var dropped = 0f;
            foreach (var (bone, influences) in painted)
            {
                if (fitTargets.TryGetValue(bone, out var covered))
                {
                    foreach (var (node, weight) in influences)
                    {
                        if (covered.Contains(node))
                        {
                            kept = MathF.Min(kept, weight);
                        }
                        else
                        {
                            dropped = MathF.Max(dropped, weight);
                        }
                    }

                    if (influences.Count >= FitMatrixMinInfluences)
                    {
                        var weights = influences.ConvertAll(static i => i.Weight);
                        weights.Sort();
                        kept = MathF.Min(kept, weights[^FitMatrixMinInfluences]);
                    }
                }
                else if (Index.NodeBases.ContainsKey(bone) && IsPositionDriven(bone))
                {
                    var heaviest = 0f;
                    foreach (var (_, weight) in influences)
                    {
                        heaviest = MathF.Max(heaviest, weight);
                    }

                    kept = MathF.Min(kept, heaviest);
                }
            }

            if (kept >= DefaultBackSolveInfluenceThreshold)
            {
                return DefaultBackSolveInfluenceThreshold;
            }

            return dropped < kept ? (dropped + kept) * 0.5f : kept * 0.5f;
        }

        /// <summary>Gets the nearest ancestor of <paramref name="node"/> with a real bone name that passes <paramref name="accept"/>, or -1.</summary>
        private int FindRealAncestor(int node, Func<int, bool>? accept = null)
        {
            var p = SkelParentOf(node);
            var guard = 0;
            while (p >= 0 && p < Fe.CtrlName.Length && guard++ < AncestorWalkLimit)
            {
                if (!IsProxyNodeName(Fe.CtrlName[p]) && (accept is null || accept(p)))
                {
                    return p;
                }

                p = SkelParentOf(p);
            }

            return -1;
        }

        /// <summary>
        /// Orders influences by descending weight, keeping the existing order for weights within <see cref="TiedWeightEpsilon"/>.
        /// </summary>
        private static void OrderByWeightKeepingTies(List<SkinInfluence> influences)
        {
            var source = influences.ToArray();
            var order = Enumerable.Range(0, source.Length).ToArray();

            Array.Sort(order, (x, y) =>
            {
                var a = source[x].Weight;
                var b = source[y].Weight;
                return MathF.Abs(a - b) > TiedWeightEpsilon * MathF.Max(MathF.Abs(a), MathF.Abs(b))
                    ? b.CompareTo(a)
                    : x.CompareTo(y);
            });

            for (var i = 0; i < order.Length; i++)
            {
                influences[i] = source[order[i]];
            }
        }

        /// <summary>
        /// Moves the anchor bone first, one float step above its heaviest rival, where it trails that rival by at most a
        /// relative 1e-5.
        /// </summary>
        private static void EnsureAnchorMostBound(List<SkinInfluence> influences, string anchor)
        {
            var primaryIndex = influences.FindIndex(i => i.Bone == anchor);
            if (primaryIndex < 0)
            {
                return;
            }

            var rivals = 0f;
            for (var i = 0; i < influences.Count; i++)
            {
                if (i != primaryIndex)
                {
                    rivals = MathF.Max(rivals, influences[i].Weight);
                }
            }

            var weight = influences[primaryIndex].Weight;
            if (weight <= rivals && rivals - weight <= 1e-5f * rivals)
            {
                influences.RemoveAt(primaryIndex);
                influences.Insert(0, new(anchor, MathF.BitIncrement(rivals)));
            }
        }

        /// <summary>Snaps the weights onto whole 1/255 steps where each lies within 0.01 of one and the steps sum to 255.</summary>
        private static void SnapToBytePartition(List<SkinInfluence> influences)
        {
            var bytes = new int[influences.Count];
            var total = 0;
            for (var i = 0; i < influences.Count; i++)
            {
                var scaled = influences[i].Weight * 255f;
                var rounded = MathF.Round(scaled);
                if (MathF.Abs(scaled - rounded) > 0.01f)
                {
                    return;
                }

                bytes[i] = (int)rounded;
                total += bytes[i];
            }

            if (total != 255)
            {
                return;
            }

            for (var i = 0; i < influences.Count; i++)
            {
                influences[i] = new(influences[i].Bone, bytes[i] / 255f);
            }
        }

        /// <summary>Gets whether the compiler created its own <c>$cloth_root</c> node, which it does for an unskinned proxy mesh.</summary>
        private bool HasGeneratedClothRoot => Array.Exists(Fe.CtrlName, static n => n == ClothRootNodeName);

        /// <summary>
        /// Gets whether a control node was generated by the cloth compiler rather than being a skeleton bone a chain can
        /// name as a joint.
        /// </summary>
        internal bool IsGeneratedNodeName(string? name)
            => IsProxyNodeName(name)
                || (SkeletonBoneNames is not null && !SkeletonBoneNames.Contains(name!));

        /// <summary>Gets whether a position-driven control node carries a real bone name.</summary>
        internal bool DrivesRealBones
        {
            get
            {
                for (var i = FirstPositionDrivenNode; i < Fe.CtrlName.Length; i++)
                {
                    if (!IsProxyNodeName(Fe.CtrlName[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Gets the first ancestor of <paramref name="node"/> with a real bone name.</summary>
        internal string? ResolveSkinBone(int node)
        {
            var index = FindRealAncestor(node);
            return index >= 0 ? Fe.CtrlName[index] : null;
        }

        /// <summary>
        /// Gets inverse-square distance weights over the four nearest joints of the anchor's chain, dropping those below
        /// 0.16 of the heaviest.
        /// </summary>
        private SkinInfluence[] BuildChainSkinInfluences(int node)
        {
            var anchor = FindRealAncestor(node);
            if (anchor < 0)
            {
                return [];
            }

            if (node >= Index.InitPosePositions.Length)
            {
                return [new(Fe.CtrlName[anchor], 1f)];
            }

            var weighted = new List<(int Node, float Distance)>();
            foreach (var candidate in GetChainComponent(anchor))
            {
                if (candidate < Index.InitPosePositions.Length)
                {
                    weighted.Add((candidate, Index.RestDistance(node, candidate)));
                }
            }

            if (weighted.Count == 0)
            {
                return [new(Fe.CtrlName[anchor], 1f)];
            }

            weighted.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            if (weighted[0].Distance <= 1e-6f)
            {
                return [new(Fe.CtrlName[weighted[0].Node], 1f)];
            }

            var top = new List<(int Node, float Weight)>(4);
            foreach (var (candidate, distance) in weighted.Take(4))
            {
                top.Add((candidate, 1f / (distance * distance)));
            }

            var maxWeight = top[0].Weight;
            var influences = new List<SkinInfluence>(4);
            var total = 0f;
            foreach (var (candidate, weight) in top)
            {
                if (weight < maxWeight * 0.16f)
                {
                    continue;
                }

                influences.Add(new(Fe.CtrlName[candidate], weight));
                total += weight;
            }

            return [.. influences.Select(i => new SkinInfluence(i.Bone, i.Weight / total))];
        }

        private List<int> GetChainComponent(int bone)
        {
            var (realParent, children) = RealBoneTree;
            if (children[bone].Count > 1)
            {
                return [bone];
            }

            var root = bone;
            var guard = 0;
            while (realParent[root] >= 0 && children[realParent[root]].Count <= 1 && guard++ < AncestorWalkLimit)
            {
                root = realParent[root];
            }

            var component = new List<int>();
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                component.Add(current);
                foreach (var child in children[current])
                {
                    stack.Push(child);
                }
            }

            return component;
        }

        /// <summary>
        /// Gets each control node's <see cref="SkelParents"/> parent where both carry real bone names, else -1, and each
        /// node's such children in ascending order.
        /// </summary>
        private (int[] RealParent, List<int>[] Children) RealBoneTree => realBoneTree ??= BuildRealBoneTree();

        private (int[] RealParent, List<int>[] Children)? realBoneTree;

        private (int[] RealParent, List<int>[] Children) BuildRealBoneTree()
        {
            var n = Fe.CtrlName.Length;
            var realParent = new int[n];
            var children = new List<int>[n];
            for (var i = 0; i < n; i++)
            {
                children[i] = [];
            }

            for (var i = 0; i < n; i++)
            {
                realParent[i] = -1;
                if (IsProxyNodeName(Fe.CtrlName[i]))
                {
                    continue;
                }

                var p = SkelParentOf(i);
                if (p >= 0 && p < n && !IsProxyNodeName(Fe.CtrlName[p]))
                {
                    realParent[i] = p;
                    children[p].Add(i);
                }
            }

            return (realParent, children);
        }
    }
}
