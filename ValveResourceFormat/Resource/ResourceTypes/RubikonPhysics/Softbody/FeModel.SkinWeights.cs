using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        // Recovers the authored per-vertex skin weights from the compiled back-solve bookkeeping - see
        // the RecoveredSkinWeights property remarks for the data model and its verification. This is the
        // same "read the compiled array directly instead of guessing a geometric rule" approach that
        // already made m_Rods/m_Twists/m_NodeBases exact; it supersedes BuildChainSkinInfluences'
        // inverse-square-distance synthesis for every vertex the compiled data still carries weights for
        // (the synthesis remains the fallback only for a vertex with no m_CtrlOffsets entry at all).
        (IReadOnlyDictionary<int, (string Bone, float Weight)[]>, float?) RecoverAuthoredSkinWeights(KVObject data)
        {
            var recovered = new Dictionary<int, (string Bone, float Weight)[]>();
            var fitMatrices = data.GetArray("m_FitMatrices");
            var ctrlOffsets = data.GetArray("m_CtrlOffsets");
            if (ctrlOffsets is null)
            {
                return (recovered, null);
            }

            var fitWeights = data.GetArray("m_FitWeights") ?? [];

            // flWeight per (vertex, fit bone), from each fit matrix's [begin, nEnd) range of m_FitWeights.
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

            // The primary (rigid-anchor) bone per vertex.
            var rigidParents = new Dictionary<int, int>();
            foreach (var e in ctrlOffsets)
            {
                rigidParents[e.GetInt32Property("nCtrlChild")] = e.GetInt32Property("nCtrlParent");
            }

            // Soft-offset alphas per vertex, kept in ARRAY ORDER - the nested-lerp expansion below only
            // reproduces the fit weights when applied in the order the compiler serialized them (verified:
            // reversing the order breaks exactly the vertices with 2+ soft offsets).
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

            // Expands the nested lerps: start at weight 1 on the primary; each soft offset scales
            // everything accumulated so far by flAlpha and gives (1 - flAlpha) to its own parent.
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

            var maxOmittedWeight = 0f;
            foreach (var (node, primary) in rigidParents)
            {
                if (primary < 0 || primary >= CtrlNames.Length)
                {
                    continue;
                }

                // The offset network alone recovers the weights whenever nothing else consumed the
                // vertex's authored weight outside it: every vertex of a model with no fit matrix, and
                // every vertex no fit covers on a model whose fits are taken over CHAIN rings rather
                // than over the sheet. The compiler drops each authored influence below its keep
                // threshold and renormalizes the rest, so the network's own expansion IS the authored
                // set as the compiler sees it, and re-painting it re-derives the same network:
                // renormalizing only scales weights up, so nothing that cleared the threshold can fall
                // back under it. Where the SHEET back-solves bones its weights are also inputs to the
                // compiler's own fit solve, so a vertex the fits leave out keeps the rigid fallback -
                // re-painting its network there perturbs fits the original has (marci_base gains six
                // node-count and mass keys, cm_ti9_immortal_weapon two fit keys).
                if (ProxyFitMatrixNodes.Count == 0)
                {
                    // Only a node the original itself compiled as a sheet vertex carries an authored
                    // proxy skin paint. On a model with no sheet the same offset network describes free
                    // ClothNode anchors, and reading it as skin weights re-binds the synthesised
                    // stand-in sheet to bones the author never painted.
                    if (!IsProxyMeshNode(node))
                    {
                        continue;
                    }

                    // Left in the compiled array's own order (primary, then each soft offset as
                    // serialized) rather than sorted by weight: that order is the authored influence
                    // slot order, which the importer keeps for influences of equal weight.
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

                // A pinned vertex is anchored rigidly to exactly its primary bone.
                if (IsStatic(node))
                {
                    recovered[node] = [(CtrlNames[primary], 1f)];
                    continue;
                }

                // A simulated vertex needs at least one fit entry to anchor the ABSOLUTE weight scale
                // (soft-offset alphas alone are renormalized over the dynamic bones). Without any fit
                // entry: no soft offsets either means the compiled data itself says the vertex is
                // anchored 100% to its primary bone (dark_willow's lantern/waist-seam vertices - the
                // geometric fallback used to smear these across nearby chain joints, producing extra
                // m_CtrlSoftOffsets entries absent from the original); with soft offsets the fit solve
                // this sheet feeds is what decides the rest, so leave those to the fallback.
                if (!fitPerVertex.TryGetValue(node, out var fits))
                {
                    if (!softPerVertex.ContainsKey(node))
                    {
                        recovered[node] = [(CtrlNames[primary], 1f)];
                    }

                    continue;
                }

                var dynamicWeights = ExpandSoftOffsets(node, primary);

                // Absolute scale from the largest fit-covered component (numerically safest anchor).
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

                // The rest of the authored weight went to a static bone (below the original's back-solve
                // threshold or simply not back-solvable) - the primary's nearest static real ancestor.
                var remainder = 1f - total;
                if (remainder > 1e-4f)
                {
                    var anchor = FindStaticRealAncestor(primary);
                    if (anchor >= 0)
                    {
                        influences.Add((CtrlNames[anchor], remainder));
                    }
                }

                influences.Sort(static (a, b) => b.Weight.CompareTo(a.Weight));
                recovered[node] = [.. influences];
            }

            float? threshold = maxOmittedWeight > 0f && minIncludedWeight < float.MaxValue && maxOmittedWeight < minIncludedWeight
                ? (maxOmittedWeight + minIncludedWeight) * 0.5f
                : null;
            return (recovered, threshold);
        }

        // Walks the skeleton-parent chain from `node` (exclusive) up to the first STATIC real-bone
        // control node - the bone the author's remaining (non-back-solved) skin weight is assigned to.
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

        // Restores a vertex's authored 1/255 weight quantum where the expansion lands on a whole-byte
        // partition of 255 and only the float32 alpha chain moved it off (synth paints $cloth_m0p329 as
        // 89/77/77/12, which comes back as .34901939/.30196129/.30196032/.047059 - two influences the
        // author painted equal, no longer equal, and the importer's weight sort then swaps them). A set
        // that resolves to no such partition is left exactly as recovered: chrono's two-influence
        // 127.5/127.5 vertex is not byte-painted, and collapsing its halves together would destroy the
        // ordering the importer's own primary pick reads.
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

        // Reads an array of cloth faces (m_Quads/m_Tris), returning each face's nNode index list.
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

        // m_SourceElems is the authored proxy mesh's own element list: four counts lead, one per element
        // arity, and the elements themselves follow grouped by arity - first the single corners, then the
        // pairs, the triangles and the quads, each a run of control-node indices in cyclic winding order.
        // Only arity three and up describe a face. Unlike m_Quads/m_Tris it survives even when the compiler
        // collapses the whole surface into rods, which is the only record of the authored topology for such
        // models.
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

            // The counts have to account for the array exactly, or this is not the layout being read.
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

        // Whether a bone anchors any proxy-sheet vertex, which is what registers it as a control node
        // independently of any cloth chain that also names it.
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

        // A source element identified by its corner set alone. A chain surface is recorded once per
        // winding with its corners rotated freely, so membership is the only stable part of it.
        static string SurfaceElementKey(IEnumerable<int> corners)
        {
            var sorted = corners.ToArray();
            Array.Sort(sorted);
            return string.Join(',', sorted);
        }

        /// <summary>
        /// Gets the authored two-corner elements of <c>m_SourceElems</c>: the edges the source declared as
        /// explicit springs rather than as part of a face. Each is one authored <c>ClothSpring</c>, and each
        /// contributes its own rod on top of whatever the surface and the chains generate.
        /// </summary>
        public (int, int)[] SourceSprings { get; } = [];

        /// <summary>
        /// Gets whether the compiler anchored this cloth to a static root node of its own making, which is
        /// what it does for a proxy mesh that arrives with no skinning. Its absence means every sheet was
        /// skinned, so exporting one unskinned would add a node the original never had.
        /// </summary>
        public bool HasGeneratedClothRoot => Array.Exists(CtrlNames, static n => n == ClothRootNodeName);

        const string ClothRootNodeName = "$cloth_root";

        /// <summary>
        /// Returns the node pairs the compiler regenerates as <c>m_Rods</c> from <paramref name="faces"/>:
        /// every face edge plus every face diagonal, deduplicated. Verified set-equal to the shipped
        /// <c>m_Rods</c> of a pure proxy-mesh cloth (a 256-quad sheet yields 544 edges + 512 diagonals).
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
        /// Gets the authored <c>additional_shear_stretch</c>. A rod's <c>flRelaxationFactor</c> is
        /// <c>exp(-stretch)</c>, where a face edge uses the surface stretch and a face diagonal uses the
        /// surface stretch plus this value, so the slackest rod recovers it. The compiler clamps the
        /// authored value at zero, which is why a negative original is indistinguishable from zero.
        /// </summary>
        public float AdditionalShearStretch
        {
            get
            {
                var slackest = float.MaxValue;
                foreach (var rod in Rods)
                {
                    if (rod.RelaxationFactor > 0f && rod.RelaxationFactor < slackest)
                    {
                        slackest = rod.RelaxationFactor;
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

        /// <summary>
        /// The prefix the compiler gives a control node it created for an authored free-standing
        /// <c>ClothNode</c> element (the element name follows). Distinct from the sheet-vertex
        /// (<c>$cloth_m&lt;N&gt;p&lt;M&gt;</c>) and chain-extrude (<c>$cc&lt;joint&gt;_&lt;n&gt;</c>) families.
        /// </summary>
        public const string FreeClothNodePrefix = "$cloth_node_";

        /// <summary>
        /// Gets or sets the names of the skeleton's real bones. Cloth extrusion does not always mark what it
        /// generates with the <c>$</c> prefix - a two-column strip names its second column after the bone it
        /// widens - so without the skeleton to compare against, a generated node is indistinguishable from a
        /// real one and gets authored as a chain joint the compiler then cannot resolve.
        /// </summary>
        public IReadOnlySet<string>? SkeletonBoneNames { get; set; }

        /// <summary>
        /// Gets or sets each skeleton bone's parent bone name. Used to orient chain links recovered from
        /// the rod mesh on compiles that ship no <c>m_SkelParents</c>: the rod evidence alone cannot tell
        /// parent from child on a strap anchored at both ends.
        /// </summary>
        public IReadOnlyDictionary<string, string?>? SkeletonBoneParents { get; set; }

        /// <summary>
        /// Rebuilds <see cref="SkelParents"/> from the model's own bone hierarchy, for cloth that ships
        /// neither <c>m_SkelParents</c> nor the <c>m_Ropes</c>/<c>m_FollowNodes</c> trail
        /// <see cref="BuildRopeParents"/> reads. A control node takes the nearest ancestor bone that is
        /// itself a control node. Does nothing once either of those two sources has produced a hierarchy.
        /// </summary>
        public void SetSkeletonParents(IReadOnlyDictionary<string, string?> boneParents)
        {
            if (SkelParents.Length > 0 || CtrlNames.Length == 0 || NodeCount <= 0)
            {
                return;
            }

            // Only for cloth built purely out of real bones. Once the compiler has generated nodes of its
            // own, they carry the hierarchy the skeleton cannot express, and imposing the bone tree on top
            // re-parents the surrounding network instead of completing it.
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
        /// Gets the control nodes that were authored as skeleton bones but culled from the compiled
        /// skeleton (unskinned cloth-only bones - watchmen's cape joints, es_cosmic's loincloth roots).
        /// A cloth construct can only reference a bone the document skeleton contains, so the export has
        /// to re-declare these as Bone nodes. Generated ring/strip members are excluded: they are the
        /// compiler's own extrude output (a CtrlOffsets child) or a strip's paired second column (a
        /// CtrlOsOffsets child), and re-declaring one collides with its regeneration.
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
        /// Whether the cloth drives any REAL (non auto-generated proxy) skeleton bone: at least one
        /// position-driven control node (index &gt;= <see cref="FirstPositionDrivenNode"/>) carries a real
        /// bone name. Those bones are back-solved from the simulated proxy nodes - whether the mechanism is
        /// <c>m_FitMatrices</c> (dark_willow's Coattail/HairStrand) or <c>m_CtrlOffsets</c> alone with no
        /// fit matrices at all (primal_beast's leg_chain/back_chain/neck_skin, m_FitMatrices empty). It is
        /// the signal that a reconstructed proxy mesh must emit <c>back_solve_joints = true</c> so those
        /// real bones move with the sim (without it the render mesh skinned to them stays frozen), and it
        /// is a superset of <see cref="FitMatrixNodes"/> being non-empty.
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
        /// Walks the skeleton-parent chain from <paramref name="node"/> up to the first real (non
        /// auto-generated cloth proxy) control-node name. This is the skeleton bone that an auto-generated
        /// proxy node is anchored/skinned to.
        /// </summary>
        public string? ResolveSkinBone(int node)
        {
            var index = ResolveSkinBoneNode(node);
            return index >= 0 ? CtrlNames[index] : null;
        }

        // Same walk as ResolveSkinBone, returning the control-node index of the bone instead of its name.
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

        // Builds the smooth skin influences of a SIMULATED proxy vertex: inverse-distance weights over the
        // nearest joints of the anchor's bone chain (up to 4, thresholded - see below). The first-real-
        // ancestor walk alone under-covers chains (dark_willow's Coattail_1_L/R middle joints had no
        // skinned vertex at all and dropped out of the recompiled FeModel), and hard single-bone weights
        // (even picked by true nearest-distance, re-verified directly against the compiler) make it drive
        // every chain joint as a point rope instead of back-solving a fit matrix from its weighted
        // vertices - dark_willow's 8 fit matrices collapsed to 1 with hard single-bone weights.
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

            // Inverse-square distance weights over the (up to) four nearest chain joints. The wider spread
            // matters: a fit matrix needs several well-separated weighted points per bone, or the compiler
            // falls back to a point rope for that joint (dark_willow's Coattail_2 chain tips with 2-joint
            // weighting still compiled as ropes).
            //
            // Weight each candidate joint by inverse-square distance. The original per-vertex weights are
            // hand-painted art data, not a function of bone-to-vertex distance (implied falloff exponents
            // measured from dark_willow's own weight ratios range wildly from -13 to +5), so no distance
            // formula reproduces them exactly; inverse-square is the closest general fit for the back-solve.
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

            // Keep only influences within 16% of the strongest weight (of the 4 nearest). A flat Take(4)
            // over-covers each bone (dark_willow: 118 m_FitWeights vs the original's 89) - the compiler
            // back-solves a fit matrix from whichever vertices reference a bone, so extra long-tail
            // influences just add noise entries. Thresholding lets tightly-clustered vertices keep 2-3
            // influences and sparse ones keep 4, without dropping below 2 (2 candidates collapse 2 of 8 fit
            // matrices back to ropes - see BuildChainSkinInfluences remarks). 0.16 is the closest fit: 88
            // m_FitWeights (vs 89) with all 8 fit matrices intact, measured against the back-solved fit's
            // actual vCenter/bone-translation, not just m_FitWeights count. It is not globally optimal on
            // every bone (lower thresholds trade Coattail_0_L/R accuracy for Coattail_2_L/R), so validate any
            // change against the FULL per-bone vCenter table, not an average, which can hide a per-bone
            // regression behind a gain elsewhere.
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

        // The real-bone control nodes on the SAME physical chain as `bone`: its real-bone ancestors up to
        // (but not through) the nearest BRANCH POINT - a real ancestor with more than one real-bone child -
        // plus every real descendant below that point.
        //
        // Two sibling chains that only share a common static real ancestor (e.g. dark_willow's
        // CoattailBase_0, parent of BOTH Coattail_0_L and Coattail_0_R) must NOT be merged into one
        // candidate pool: climbing past the branch point would let a proxy vertex's "nearest 4" search draw
        // candidates from BOTH sides at once, so a left-side bone's fit picks up right-side vertices with a
        // spuriously large weight and its back-solved weighted centroid (vCenter) shifts 6-9 units off the
        // original. The original only mixes sides for vertices anchored directly AT the branch point
        // (Coattail_0_L/R), and even then with a small secondary weight; deeper joints have zero cross-side
        // contribution. Stopping the upward walk at the first branch point isolates each side to its own
        // chain; HairStrand_0/1 (whose ancestor "head" has one cloth descendant, never branches) are
        // unaffected.
        List<int> GetChainComponent(int bone)
        {
            var n = CtrlNames.Length;

            // realParent[i]: parent among real bones, or -1.
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

            // childCount[p]: number of real-bone nodes whose real parent is p - used to detect a branch
            // point (a shared ancestor of two or more distinct chains) that the upward walk must stop at.
            var childCount = new int[n];
            for (var i = 0; i < n; i++)
            {
                if (realParent[i] >= 0)
                {
                    childCount[realParent[i]]++;
                }
            }

            // A vertex whose OWN nearest real ancestor already IS a branch point (not just an ancestor
            // reached while climbing) must not be smeared across every sibling chain hanging off it either.
            // Verified on dark_willow: EVERY one of the 9 proxy vertices whose real anchor is CoattailBase_0
            // itself (the hub bone, not either coattail side) contributes to ZERO m_FitMatrices in the
            // original (not a small secondary weight either - genuinely absent from every fit) - the
            // original pins these hub-anchored vertices to the hub bone alone rather than distributing them
            // across its children.
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
