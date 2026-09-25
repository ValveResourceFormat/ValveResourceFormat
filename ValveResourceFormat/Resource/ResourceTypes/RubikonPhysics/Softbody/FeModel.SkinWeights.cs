using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// Recovers the authored skin weights of the proxy-sheet vertices, the vertices left to the offset network, and the
        /// proxy meshes compiled without back-solving.
        /// </summary>
        Dictionary<int, (string Bone, float Weight)[]> RecoverAuthoredSkinWeights(KVObject data,
            out Dictionary<int, (string Bone, float Weight)[]> deferred, out HashSet<int> unbackSolvedMeshes)
        {
            var recovered = new Dictionary<int, (string Bone, float Weight)[]>();
            deferred = [];
            unbackSolvedMeshes = [];
            var fitMatrices = data.GetArray("m_FitMatrices");
            var ctrlOffsets = data.GetArray("m_CtrlOffsets");
            if (ctrlOffsets is null)
            {
                return recovered;
            }

            var fitWeights = data.GetArray("m_FitWeights") ?? [];

            var fitPerVertex = new Dictionary<int, Dictionary<int, float>>();
            var minIncludedWeight = float.MaxValue;
            var begin = 0;
            foreach (var fm in fitMatrices ?? [])
            {
                var end = fm.GetInt32Property("nEnd");
                var bone = fm.GetInt32Property("nNode");
                for (var i = begin; i < end && i < fitWeights.Count; i++)
                {
                    var node = fitWeights[i].GetInt32Property("nNode");
                    var weight = fitWeights[i].GetFloatProperty("flWeight");
                    if (!fitPerVertex.TryGetValue(node, out var boneWeights))
                    {
                        boneWeights = [];
                        fitPerVertex[node] = boneWeights;
                    }

                    boneWeights[bone] = weight;
                    minIncludedWeight = MathF.Min(minIncludedWeight, weight);
                }

                begin = end;
            }

            var rigidParents = new Dictionary<int, int>();
            foreach (var e in ctrlOffsets)
            {
                rigidParents[e.GetInt32Property("nCtrlChild")] = e.GetInt32Property("nCtrlParent");
            }

            var softPerVertex = new Dictionary<int, List<(int Parent, float Alpha)>>();
            if (data.GetArray("m_CtrlSoftOffsets") is { } softOffsets)
            {
                foreach (var e in softOffsets)
                {
                    var child = e.GetInt32Property("nCtrlChild");
                    if (!softPerVertex.TryGetValue(child, out var list))
                    {
                        list = [];
                        softPerVertex[child] = list;
                    }

                    list.Add((e.GetInt32Property("nCtrlParent"), e.GetFloatProperty("flAlpha")));
                }
            }

            List<(int Bone, float Weight)> ExpandSoftOffsets(int node, int primary)
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

            var backSolvedMeshes = new HashSet<int>();
            var fitBoneMeshes = new Dictionary<int, HashSet<int>>();
            foreach (var (bone, targets) in FitMatrixTargets)
            {
                foreach (var target in targets)
                {
                    var targetMesh = target >= 0 && target < CtrlNames.Length
                        ? ParseProxyMeshIndex(CtrlNames[target]) : -1;
                    if (targetMesh < 0)
                    {
                        continue;
                    }

                    backSolvedMeshes.Add(targetMesh);
                    if (!fitBoneMeshes.TryGetValue(bone, out var boneMeshes))
                    {
                        boneMeshes = [];
                        fitBoneMeshes[bone] = boneMeshes;
                    }

                    boneMeshes.Add(targetMesh);
                }
            }

            if (backSolvedMeshes.Count > 0)
            {
                var drivenByMesh = new Dictionary<int, HashSet<int>>();
                foreach (var (node, primary) in rigidParents)
                {
                    var mesh = node >= 0 && node < CtrlNames.Length ? ParseProxyMeshIndex(CtrlNames[node]) : -1;
                    if (mesh < 0 || backSolvedMeshes.Contains(mesh) || IsStatic(node)
                        || primary < 0 || primary >= CtrlNames.Length)
                    {
                        continue;
                    }

                    foreach (var (bone, weight) in ExpandSoftOffsets(node, primary))
                    {
                        if (weight < DefaultBackSolveInfluenceThreshold || bone < 0 || bone >= CtrlNames.Length
                            || !IsPositionDriven(bone) || IsProxyNodeName(CtrlNames[bone]))
                        {
                            continue;
                        }

                        if (!drivenByMesh.TryGetValue(mesh, out var bones))
                        {
                            bones = [];
                            drivenByMesh[mesh] = bones;
                        }

                        bones.Add(bone);
                    }
                }

                foreach (var (mesh, bones) in drivenByMesh)
                {
                    var fitElsewhere = bones.Count > 0;
                    foreach (var bone in bones)
                    {
                        if (!fitBoneMeshes.ContainsKey(bone))
                        {
                            fitElsewhere = false;
                            break;
                        }
                    }

                    if (fitElsewhere)
                    {
                        unbackSolvedMeshes.Add(mesh);
                    }
                }
            }

            var maxOmittedWeight = 0f;
            var fitlessSoft = new List<(int Node, int Primary)>();
            foreach (var (node, primary) in rigidParents)
            {
                if (primary < 0 || primary >= CtrlNames.Length)
                {
                    continue;
                }

                if (ProxyFitMatrixNodes.Count == 0)
                {
                    if (!IsProxyMeshNode(node))
                    {
                        continue;
                    }

                    var painted = new List<(string Bone, float Weight)>();
                    foreach (var (bone, weight) in ExpandSoftOffsets(node, primary))
                    {
                        if (weight > 0f && bone < CtrlNames.Length)
                        {
                            painted.Add((CtrlNames[bone], weight));
                        }
                    }

                    SnapToBytePartition(painted);
                    recovered[node] = [.. painted];
                    continue;
                }

                if (IsStatic(node))
                {
                    if (fitPerVertex.ContainsKey(node))
                    {
                        recovered[node] = [(CtrlNames[primary], 1f)];
                        continue;
                    }

                    var pinned = new List<(string Bone, float Weight)>();
                    var anchorWeight = 0f;
                    var rival = 0f;
                    foreach (var (bone, weight) in ExpandSoftOffsets(node, primary))
                    {
                        if (weight <= 0f || bone >= CtrlNames.Length)
                        {
                            continue;
                        }

                        if (bone == primary)
                        {
                            anchorWeight = weight;
                        }
                        else
                        {
                            rival = MathF.Max(rival, weight);
                        }

                        pinned.Add((CtrlNames[bone], weight));
                    }

                    if (pinned.Count > 0 && anchorWeight >= rival)
                    {
                        SnapToBytePartition(pinned);
                        EnsureAnchorMostBound(pinned, CtrlNames[primary]);
                        recovered[node] = [.. pinned];
                    }
                    else
                    {
                        recovered[node] = [(CtrlNames[primary], 1f)];
                    }

                    continue;
                }

                if (!fitPerVertex.TryGetValue(node, out var fits))
                {
                    if (!softPerVertex.ContainsKey(node))
                    {
                        recovered[node] = [(CtrlNames[primary], 1f)];
                    }
                    else
                    {
                        fitlessSoft.Add((node, primary));
                    }

                    continue;
                }

                var dynamicWeights = ExpandSoftOffsets(node, primary);

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

                var influences = new List<(string Bone, float Weight)>(dynamicWeights.Count + 1);
                var total = 0f;
                foreach (var (bone, normalized) in dynamicWeights)
                {
                    var weight = normalized * scale;
                    if (weight <= 0f || bone >= CtrlNames.Length)
                    {
                        continue;
                    }

                    influences.Add((CtrlNames[bone], weight));
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
                    var anchor = FindStaticRealAncestor(primary);
                    while (anchor >= 0 && influences.Exists(influence => influence.Bone == CtrlNames[anchor]))
                    {
                        anchor = FindStaticRealAncestor(anchor);
                    }

                    if (anchor >= 0)
                    {
                        influences.Add((CtrlNames[anchor], remainder));
                    }
                }

                OrderByWeightKeepingTies(influences);
                EnsureAnchorMostBound(influences, CtrlNames[primary]);
                recovered[node] = [.. influences];
            }

            float? threshold = maxOmittedWeight > 0f && minIncludedWeight < float.MaxValue && maxOmittedWeight < minIncludedWeight
                ? (maxOmittedWeight + minIncludedWeight) * 0.5f
                : null;

            var fitlessNodes = new HashSet<int>(fitlessSoft.Count);
            foreach (var (node, _) in fitlessSoft)
            {
                fitlessNodes.Add(node);
            }

            var drivenDynamicBones = new HashSet<int>();
            foreach (var (node, parent) in rigidParents)
            {
                if (fitlessNodes.Contains(node) || IsStatic(node) || parent < 0 || parent >= CtrlNames.Length)
                {
                    continue;
                }

                drivenDynamicBones.Add(parent);
            }

            var backSolvedBones = new HashSet<int>();
            foreach (var entry in data.GetArray("m_ReverseOffsets") ?? [])
            {
                backSolvedBones.Add(entry.GetInt32Property("nBoneCtrl"));
            }

            foreach (var (node, primary) in fitlessSoft)
            {
                var painted = new List<(string Bone, float Weight)>();
                var prunable = true;

                var mesh = node >= 0 && node < CtrlNames.Length ? ParseProxyMeshIndex(CtrlNames[node]) : -1;
                var sheetBackSolves = mesh < 0 || !unbackSolvedMeshes.Contains(mesh);
                foreach (var (bone, weight) in ExpandSoftOffsets(node, primary))
                {
                    if (weight <= 0f || bone >= CtrlNames.Length)
                    {
                        continue;
                    }

                    if (prunable && sheetBackSolves && FitMatrixNodes.Contains(bone)
                        && weight >= (threshold ?? DefaultBackSolveInfluenceThreshold))
                    {
                        prunable = false;
                    }

                    if (prunable && !IsStatic(bone) && !drivenDynamicBones.Contains(bone)
                        && !backSolvedBones.Contains(bone) && !FitMatrixNodes.Contains(bone))
                    {
                        prunable = false;
                    }

                    painted.Add((CtrlNames[bone], weight));
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

            return recovered;
        }

        /// <summary>
        /// Gets the proxy mesh indices compiled without back-solving: no <c>m_FitWeights</c> range names their vertices, and
        /// every position-driven bone their simulated vertices bind to is fit over another mesh.
        /// </summary>
        public IReadOnlySet<int> UnbackSolvedProxyMeshes { get; }

        /// <summary>
        /// Gets whether <paramref name="proxy"/> has sheet vertices and all of them belong to <see cref="UnbackSolvedProxyMeshes"/>.
        /// </summary>
        public bool IsUnbackSolvedProxyMesh(ProxyMesh proxy)
        {
            var sheetVertices = 0;
            foreach (var node in proxy.NodeIndices)
            {
                var mesh = node >= 0 && node < CtrlNames.Length ? ParseProxyMeshIndex(CtrlNames[node]) : -1;
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
        public bool ProxyFitsUndrivenBone(ProxyMesh proxy)
        {
            if (!HasCompiledFirstPositionDrivenNode || FitMatrixTargets.Count == 0)
            {
                return false;
            }

            var own = new HashSet<int>(proxy.NodeIndices);
            foreach (var (bone, targets) in FitMatrixTargets)
            {
                if (!IsPositionDriven(bone) && Array.Exists(targets, own.Contains))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The ModelDoc default for <c>ClothProxyMeshFile.back_solve_influence_threshold</c>.
        /// </summary>
        public const float DefaultBackSolveInfluenceThreshold = 0.05f;

        /// <summary>
        /// The number of surviving influence vertices from which the compiler fits a bone with an <c>m_FitMatrices</c> solve.
        /// </summary>
        public const int FitMatrixMinInfluences = 8;

        /// <summary>
        /// The smallest per-vertex skin influence count a cloth proxy DMX is written with.
        /// </summary>
        public const int ClothProxyInfluenceSlots = 4;

        /// <summary>
        /// The most <c>m_CtrlSoftOffsets</c> records the compiler writes for one proxy vertex.
        /// </summary>
        public const int ClothProxySoftOffsetSlots = 8;

        /// <summary>
        /// Gets the <c>back_solve_influence_threshold</c> for <paramref name="proxy"/>: the default, unless the proxy's fit
        /// data keeps a lighter weight, then between that weight and the heaviest weight a fit drops.
        /// </summary>
        public float GetBackSolveInfluenceThreshold(ProxyMesh proxy)
        {
            var ctrlIndex = new Dictionary<string, int>(CtrlNames.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < CtrlNames.Length; i++)
            {
                ctrlIndex.TryAdd(CtrlNames[i], i);
            }

            var proxyNodes = new HashSet<int>(proxy.NodeIndices);
            var fitTargets = new Dictionary<int, HashSet<int>>(FitMatrixTargets.Count);
            foreach (var (bone, targets) in FitMatrixTargets)
            {
                var covered = new HashSet<int>(targets);
                if (covered.Overlaps(proxyNodes))
                {
                    fitTargets[bone] = covered;
                }
            }

            const int slots = int.MaxValue;
            var painted = new Dictionary<int, List<(int Node, float Weight)>>();
            for (var v = 0; v < proxy.NodeIndices.Length && v < proxy.SkinInfluences.Length; v++)
            {
                var node = proxy.NodeIndices[v];
                var slot = 0;
                foreach (var (boneName, weight) in proxy.SkinInfluences[v])
                {
                    if (slot >= slots)
                    {
                        break;
                    }

                    if (!ctrlIndex.TryGetValue(boneName, out var bone))
                    {
                        continue;
                    }

                    slot++;
                    if (weight <= 0f || IsProxyNodeName(CtrlNames[bone]) || IsStatic(bone))
                    {
                        continue;
                    }

                    if (!painted.TryGetValue(bone, out var influences))
                    {
                        influences = [];
                        painted[bone] = influences;
                    }

                    influences.Add((node, weight));
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
                else if (NodeBases.ContainsKey(bone) && IsPositionDriven(bone))
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

        int FindStaticRealAncestor(int node)
        {
            var p = node >= 0 && node < SkelParents.Length ? SkelParents[node] : -1;
            var guard = 0;
            while (p >= 0 && p < CtrlNames.Length && guard++ < 256)
            {
                if (!IsProxyNodeName(CtrlNames[p]) && IsStatic(p))
                {
                    return p;
                }

                p = p < SkelParents.Length ? SkelParents[p] : -1;
            }

            return -1;
        }

        /// <summary>Relative gap under which two recovered influence weights are one authored value.</summary>
        const float TiedWeightEpsilon = 1e-4f;

        /// <summary>
        /// Orders influences by descending weight, keeping the existing order for weights within <see cref="TiedWeightEpsilon"/>.
        /// </summary>
        static void OrderByWeightKeepingTies(List<(string Bone, float Weight)> influences)
        {
            var source = influences.ToArray();
            var order = new int[source.Length];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

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
        static void EnsureAnchorMostBound(List<(string Bone, float Weight)> influences, string anchor)
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
                influences.Insert(0, (anchor, MathF.BitIncrement(rivals)));
            }
        }

        /// <summary>
        /// Snaps the weights onto whole 1/255 steps where each lies within 0.01 of one and the steps sum to 255.
        /// </summary>
        static void SnapToBytePartition(List<(string Bone, float Weight)> influences)
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
                influences[i] = (influences[i].Bone, bytes[i] / 255f);
            }
        }

        static int[][] ReadNodeIndexArray(KVObject data, string key, int expectedLength)
        {
            var arr = data.GetArray(key);
            if (arr is null)
            {
                return [];
            }

            var faces = new List<int[]>(arr.Count);
            foreach (var face in arr)
            {
                var nodes = face.GetIntegerArray("nNode");
                if (nodes.Length >= expectedLength)
                {
                    faces.Add(nodes.Take(expectedLength).Select(static v => (int)v).ToArray());
                }
            }

            return [.. faces];
        }

        static (int[][] Faces, (int, int)[] Springs) ReadSourceElems(KVObject data)
        {
            if (!data.ContainsKey("m_SourceElems") || !data.IsNotBlobType("m_SourceElems"))
            {
                return ([], []);
            }

            var elems = data.GetIntegerArray("m_SourceElems");
            if (elems.Length < SourceElemArities)
            {
                return ([], []);
            }

            var counted = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                var count = elems[arity - 1];
                if (count < 0 || count > elems.Length)
                {
                    return ([], []);
                }

                counted += arity * (int)count;
            }

            if (counted != elems.Length)
            {
                return ([], []);
            }

            var faces = new List<int[]>();
            var springs = new List<(int, int)>();
            var read = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                for (var remaining = (int)elems[arity - 1]; remaining > 0; remaining--, read += arity)
                {
                    if (arity == 2)
                    {
                        var a = (int)elems[read];
                        var b = (int)elems[read + 1];
                        if (a != b)
                        {
                            springs.Add((a, b));
                        }

                        continue;
                    }

                    if (arity < 3)
                    {
                        continue;
                    }

                    var corners = new List<int>(arity);
                    for (var c = 0; c < arity; c++)
                    {
                        var node = (int)elems[read + c];
                        if (!corners.Contains(node))
                        {
                            corners.Add(node);
                        }
                    }

                    if (corners.Count >= 3)
                    {
                        faces.Add([.. corners]);
                    }
                }
            }

            return ([.. faces], [.. springs]);
        }

        const int SourceElemArities = 4;

        /// <summary>
        /// Gets the authored proxy-mesh faces recovered from <c>m_SourceElems</c>, as control-node index
        /// lists in winding order (four corners for a quad, three for a triangle).
        /// </summary>
        public int[][] SourceFaces { get; } = [];

        bool DrivesProxySheetVertex(int node)
        {
            foreach (var offset in CtrlOffsets)
            {
                if (offset.CtrlParent == node && offset.CtrlChild >= 0 && offset.CtrlChild < CtrlNames.Length
                    && ParseProxyMeshIndex(CtrlNames[offset.CtrlChild]) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        static string SurfaceElementKey(IEnumerable<int> corners)
        {
            var sorted = corners.ToArray();
            Array.Sort(sorted);
            return string.Join(',', sorted);
        }

        /// <summary>
        /// Gets the two-corner elements of <c>m_SourceElems</c>, one per authored <c>ClothSpring</c>.
        /// </summary>
        public (int, int)[] SourceSprings { get; } = [];

        /// <summary>
        /// Gets whether the compiler created its own <c>$cloth_root</c> node, which it does for an unskinned proxy mesh.
        /// </summary>
        public bool HasGeneratedClothRoot => Array.Exists(CtrlNames, static n => n == ClothRootNodeName);

        const string ClothRootNodeName = "$cloth_root";

        /// <summary>
        /// Returns the node pairs the compiler regenerates as <c>m_Rods</c> from <paramref name="faces"/>:
        /// every face edge plus every face diagonal, deduplicated.
        /// </summary>
        public static HashSet<(int, int)> DeriveRodsFromFaces(IEnumerable<int[]> faces)
        {
            var derived = new HashSet<(int, int)>();
            foreach (var face in faces)
            {
                for (var a = 0; a < face.Length; a++)
                {
                    for (var b = a + 1; b < face.Length; b++)
                    {
                        var (x, y) = face[a] < face[b] ? (face[a], face[b]) : (face[b], face[a]);
                        derived.Add((x, y));
                    }
                }
            }

            return derived;
        }

        /// <summary>
        /// Gets the authored <c>additional_shear_stretch</c> from the slackest rod between two sheet vertices, or from the
        /// <see cref="ShearResistance"/> base relaxation where the diagonals disagree.
        /// </summary>
        public float AdditionalShearStretch
        {
            get
            {
                var slackest = float.MaxValue;
                if (ShearResistance is { } shear)
                {
                    slackest = shear.BaseRelaxation;
                }
                else
                {
                    foreach (var rod in Rods)
                    {
                        if (!IsProxyMeshNode(rod.NodeA) || !IsProxyMeshNode(rod.NodeB))
                        {
                            continue;
                        }

                        var relaxation = UnstretchedRelaxation(rod);
                        if (relaxation > 0f && relaxation < slackest)
                        {
                            slackest = relaxation;
                        }
                    }
                }

                if (slackest is float.MaxValue or >= 1f)
                {
                    return 0f;
                }

                return Math.Max(0f, -MathF.Log(slackest) - DefaultSurfaceStretch);
            }
        }

        /// <summary>
        /// Gets a value indicating whether this FeModel carries any control nodes.
        /// </summary>
        public bool HasData => CtrlNames.Length > 0;

        /// <summary>
        /// Gets a value indicating whether <c>m_SkelParents</c> was present in the compiled data. False on
        /// old-era compiles (and rope cloth), where <see cref="SkelParents"/> is synthesized from
        /// <c>m_Ropes</c>/<c>m_FollowNodes</c> or the skeleton instead.
        /// </summary>
        public bool HasCompiledSkelParents { get; }

        /// <summary>
        /// Returns whether a control-node name is an auto-generated cloth proxy node (not a real skeleton bone).
        /// </summary>
        public static bool IsProxyNodeName(string? name)
            => string.IsNullOrEmpty(name) || name.StartsWith('$');

        /// <summary>The prefix of a control node created for an authored free-standing <c>ClothNode</c>.</summary>
        public const string FreeClothNodePrefix = "$cloth_node_";

        /// <summary>
        /// Gets or sets the names of the skeleton's real bones, used to tell generated nodes without a <c>$</c> prefix apart.
        /// </summary>
        public IReadOnlySet<string>? SkeletonBoneNames { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="GetCulledBoneCtrls"/> nodes, captured before their names join <see cref="SkeletonBoneNames"/>.
        /// </summary>
        public IReadOnlySet<int>? CulledBoneCtrlNodes { get; set; }

        /// <summary>Gets or sets each skeleton bone's parent bone name.</summary>
        public IReadOnlyDictionary<string, string?>? SkeletonBoneParents { get; set; }

        /// <summary>
        /// Gets or sets the bind position a chain joint's ring is measured from, for bones a scaled proxy skeleton moved.
        /// </summary>
        public IReadOnlyDictionary<string, Vector3>? ChainExtrudeOrigins { get; set; }

        /// <summary>
        /// Rebuilds <see cref="SkelParents"/> from the bone hierarchy when the compile carries none: each node takes its
        /// nearest ancestor bone that is a control node.
        /// </summary>
        public void SetSkeletonParents(IReadOnlyDictionary<string, string?> boneParents)
        {
            if (SkelParents.Length > 0 || CtrlNames.Length == 0 || NodeCount <= 0)
            {
                return;
            }

            foreach (var name in CtrlNames)
            {
                if (IsProxyNodeName(name))
                {
                    return;
                }
            }

            var nodeByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var node = 0; node < CtrlNames.Length && node < NodeCount; node++)
            {
                nodeByName.TryAdd(CtrlNames[node], node);
            }

            var parents = new int[NodeCount];
            Array.Fill(parents, -1);
            var parented = false;

            foreach (var (name, node) in nodeByName)
            {
                var ancestor = boneParents.GetValueOrDefault(name);
                while (ancestor is not null)
                {
                    if (nodeByName.TryGetValue(ancestor, out var parentNode) && parentNode != node)
                    {
                        parents[node] = parentNode;
                        parented = true;
                        break;
                    }

                    ancestor = boneParents.GetValueOrDefault(ancestor);
                }
            }

            if (parented)
            {
                SkelParents = parents;
            }
        }

        /// <summary>
        /// Returns whether a control node is generated by the cloth compiler rather than being a skeleton
        /// bone the chain can name as a joint.
        /// </summary>
        public bool IsGeneratedNodeName(string? name)
            => IsProxyNodeName(name)
                || (SkeletonBoneNames is not null && !SkeletonBoneNames.Contains(name!));

        /// <summary>
        /// Gets the control nodes named after bones the compiled skeleton does not contain, excluding generated ring and
        /// strip members.
        /// </summary>
        public List<(int Node, string Name)> GetCulledBoneCtrls()
        {
            var result = new List<(int Node, string Name)>();
            if (SkeletonBoneNames is null)
            {
                return result;
            }

            var generatedChildren = new HashSet<int>();
            foreach (var offset in CtrlOffsets)
            {
                generatedChildren.Add(offset.CtrlChild);
            }

            foreach (var pair in CtrlOsOffsets)
            {
                generatedChildren.Add(pair.CtrlChild);
            }

            for (var node = 0; node < CtrlNames.Length; node++)
            {
                var name = CtrlNames[node];
                if (IsProxyNodeName(name) || SkeletonBoneNames.Contains(name)
                    || generatedChildren.Contains(node) || node >= InitPosePositions.Length)
                {
                    continue;
                }

                result.Add((node, name));
            }

            return result;
        }

        /// <summary>
        /// Gets whether a position-driven control node carries a real bone name.
        /// </summary>
        public bool DrivesRealBones
        {
            get
            {
                for (var i = FirstPositionDrivenNode; i < CtrlNames.Length; i++)
                {
                    if (!IsProxyNodeName(CtrlNames[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Returns whether the node at <paramref name="node"/> is a static (pinned, invMass == 0) anchor.
        /// </summary>
        public bool IsStatic(int node)
            => node >= 0 && node < NodeInvMasses.Length && NodeInvMasses[node] == 0f;

        /// <summary>
        /// Gets the first ancestor of <paramref name="node"/> with a real bone name.
        /// </summary>
        public string? ResolveSkinBone(int node)
        {
            var index = ResolveSkinBoneNode(node);
            return index >= 0 ? CtrlNames[index] : null;
        }

        int ResolveSkinBoneNode(int node)
        {
            var p = node >= 0 && node < SkelParents.Length ? SkelParents[node] : -1;
            var guard = 0;
            while (p >= 0 && p < CtrlNames.Length && guard++ < 256)
            {
                if (!IsProxyNodeName(CtrlNames[p]))
                {
                    return p;
                }

                p = p < SkelParents.Length ? SkelParents[p] : -1;
            }

            return -1;
        }

        /// <summary>
        /// Gets inverse-square distance weights over the four nearest joints of the anchor's chain, dropping those below
        /// 0.16 of the heaviest.
        /// </summary>
        (string Bone, float Weight)[] BuildChainSkinInfluences(int node)
        {
            var anchor = ResolveSkinBoneNode(node);
            if (anchor < 0)
            {
                return [];
            }

            if (node >= InitPosePositions.Length)
            {
                return [(CtrlNames[anchor], 1f)];
            }

            var weighted = new List<(int Node, float Distance)>();
            foreach (var candidate in GetChainComponent(anchor))
            {
                if (candidate < InitPosePositions.Length)
                {
                    weighted.Add((candidate, Vector3.Distance(InitPosePositions[node], InitPosePositions[candidate])));
                }
            }

            if (weighted.Count == 0)
            {
                return [(CtrlNames[anchor], 1f)];
            }

            weighted.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            if (weighted[0].Distance <= 1e-6f)
            {
                return [(CtrlNames[weighted[0].Node], 1f)];
            }

            var top = new List<(int Node, float Weight)>(4);
            foreach (var (candidate, distance) in weighted.Take(4))
            {
                top.Add((candidate, 1f / (distance * distance)));
            }

            var maxWeight = top[0].Weight;
            var influences = new List<(string Bone, float Weight)>(4);
            var total = 0f;
            foreach (var (candidate, weight) in top)
            {
                if (weight < maxWeight * 0.16f)
                {
                    continue;
                }

                influences.Add((CtrlNames[candidate], weight));
                total += weight;
            }

            return [.. influences.Select(i => (i.Bone, i.Weight / total))];
        }

        List<int> GetChainComponent(int bone)
        {
            var n = CtrlNames.Length;

            var realParent = new int[n];
            for (var i = 0; i < n; i++)
            {
                realParent[i] = -1;
                if (IsProxyNodeName(CtrlNames[i]))
                {
                    continue;
                }

                var p = i < SkelParents.Length ? SkelParents[i] : -1;
                if (p >= 0 && p < n && !IsProxyNodeName(CtrlNames[p]))
                {
                    realParent[i] = p;
                }
            }

            var childCount = new int[n];
            for (var i = 0; i < n; i++)
            {
                if (realParent[i] >= 0)
                {
                    childCount[realParent[i]]++;
                }
            }

            if (childCount[bone] > 1)
            {
                return [bone];
            }

            var root = bone;
            var guard = 0;
            while (realParent[root] >= 0 && childCount[realParent[root]] <= 1 && guard++ < 256)
            {
                root = realParent[root];
            }

            var component = new List<int>();
            var stack = new Stack<int>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                component.Add(current);
                for (var i = 0; i < n; i++)
                {
                    if (realParent[i] == current)
                    {
                        stack.Push(i);
                    }
                }
            }

            return component;
        }
    }
}
