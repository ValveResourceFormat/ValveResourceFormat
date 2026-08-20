using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// The auto-generated cloth proxy mesh (the cloth "sheet"), reconstructed from the FeModel surface
        /// topology. Vertices are the control nodes referenced by <see cref="Quads"/>/<see cref="Tris"/>
        /// (i.e. the <c>$cloth_*</c> proxy nodes); the real bone-chain nodes are intentionally excluded so a
        /// proxy-mesh recompile and a <c>ClothChain</c> recompile do not drive the same nodes twice.
        /// </summary>
        public sealed class ProxyMesh
        {
            /// <summary>Gets the original FeModel control-node index of each proxy vertex.</summary>
            public required int[] NodeIndices { get; init; }
            /// <summary>Gets the model-space rest position of each proxy vertex.</summary>
            public required Vector3[] Positions { get; init; }
            /// <summary>Gets the per-vertex <c>cloth_enable</c> flag (1 = simulated, 0 = pinned anchor).</summary>
            public required float[] ClothEnable { get; init; }
            /// <summary>
            /// Gets the per-vertex goal/force attraction toward the animated pose (recovered from the node
            /// integrator's <c>flAnimationForceAttraction</c>, clamped to the 0..1 paint range). Higher = the
            /// cloth hugs the body more tightly. Emitted as the modern <c>cloth_goal_strength_v2$0</c> paint.
            /// </summary>
            public required float[] GoalStrength { get; init; }
            /// <summary>
            /// Gets the per-vertex goal damping paint, recovered by inverting the vertex-attraction
            /// response (<see cref="GoalDampingFromAttraction"/>). Legacy models with out-of-range
            /// attraction clamp to the strongest reproducible damping.
            /// </summary>
            public required float[] GoalDamping { get; init; }
            /// <summary>Gets the per-vertex self-collision radius (recovered from <c>m_NodeCollisionRadii</c>).</summary>
            public required float[] CollisionRadius { get; init; }
            /// <summary>Gets the per-vertex friction (recovered from <c>m_DynNodeFriction</c>), 0..1 paint range.</summary>
            public required float[] Friction { get; init; }
            /// <summary>Gets the per-vertex air drag (recovered from the FeModel air-drag scalar), 0..1 paint range.</summary>
            public required float[] Drag { get; init; }
            /// <summary>Gets the per-vertex ground-collision weight (recovered where available), 0..1 paint range.</summary>
            public required float[] GroundCollision { get; init; }
            /// <summary>Gets the per-vertex ground friction of a world-colliding node (recovered from <c>m_WorldCollisionParams</c>), 0..1 paint range.</summary>
            public required float[] GroundFriction { get; init; }
            /// <summary>
            /// Gets the per-vertex gravity (the integrator's <c>flGravity</c>, verbatim - the
            /// <c>cloth_gravity$0</c> paint compiles into <c>flGravity</c> with no scaling, unlike the
            /// /360 <c>gravity_z</c> KV field on ClothNode/ClothChain joints). Without the stream the
            /// compiler defaults every vertex to 360, silently discarding authored variation -
            /// dark_willow's hair strands and paper lantern are authored nearly weightless (flGravity=1).
            /// </summary>
            public required float[] Gravity { get; init; }
            /// <summary>
            /// Gets the RAW compiled <c>flAnimationVertexAttraction</c> per vertex. Values above 1.0
            /// (legacy-era compiles: dark_willow ships 15/10.5/6/5.25) are a legacy platform ceiling:
            /// unreachable via the modern goal_strength/goal_damping pipeline, the direct
            /// <c>cloth_animation_attract$0</c> stream is ignored by the proxy importer, and no modern
            /// Valve model ships va &gt; 1. Kept as data for diagnostics, not re-authored.
            /// </summary>
            public required float[] VertexAttraction { get; init; }
            /// <summary>
            /// Gets the skeleton bone influences of each proxy vertex. Pinned anchors carry a single
            /// weight-1 influence on their anchor bone. Simulated vertices are SMOOTHLY weighted across the
            /// nearest joints of the anchor's chain: the compiler back-solves a bone with a proper fit
            /// matrix only when enough weighted vertices reference it - hard single-bone skinning degrades
            /// every chain joint to a point-driven rope (dark_willow: original has 8 fit matrices / 89 fit
            /// weights, hard skinning compiled to 8 ropes / 1 fit and a much denser rod network).
            /// </summary>
            public required (string Bone, float Weight)[][] SkinInfluences { get; init; }
            /// <summary>
            /// Gets the faces (proxy-vertex index quads and triangles) covering the sheet, preserving the
            /// original quad/tri split. Triangulating the quads instead makes the compiler re-derive a much
            /// denser quad/rod network (dark_willow: 15 quads/61 rods became 43 quads/163 rods) and the
            /// recompiled cloth turns rigid.
            /// </summary>
            public required List<int[]> Faces { get; init; }
            /// <summary>
            /// Gets the named vertex selections covering this sheet, as a per-vertex membership weight
            /// each. Painted back onto the sheet as one <c>cloth_vertex_set_&lt;name&gt;</c> stream per
            /// selection, which is how a proxy vertex joins one.
            /// </summary>
            public (string Name, float[] Weights)[] VertexMaps { get; init; } = [];
            /// <summary>
            /// Gets, per vertex, whether the compiled sheet drives it through distance constraints alone -
            /// the importer turned every authored element it belongs to into rods (<c>m_SourceElems</c>)
            /// instead of keeping it as a solve element (<c>m_Quads</c>/<c>m_Tris</c>). Empty on a sheet
            /// with no such region. Painted back as <c>cloth_make_rods</c>, which is what makes the compiler
            /// split the same sheet the same way (verified against yamato's authored proxy: a 0/1 paint over
            /// this exact vertex set reproduces its compile on every key, while the sheet's own gradient
            /// paint is not recoverable and does not need to be).
            /// </summary>
            public float[] RodsDriven { get; init; } = [];
            /// <summary>Gets the number of simulated (cloth_enable == 1) vertices.</summary>
            public int SimulatedCount { get; init; }
            /// <summary>Gets the number of pinned (cloth_enable == 0) vertices.</summary>
            public int PinnedCount { get; init; }
            /// <summary>
            /// Gets whether the cloth importer is expected to silently PRUNE one or more of this synthesised
            /// island's vertices, which would make any explicit <c>ClothSpring</c> (m_Rods) referencing a
            /// pruned vertex a hard "Cannot find node $cloth_mXpY" compile failure. Two importer behaviours
            /// were reverse-engineered as the cause (verified against real recompiles - see
            /// <see cref="ComputeDropRisk"/>): (1) a pinned vertex whose face-neighbours are ALL pinned (a
            /// fully-static mesh region the solver discards - mars), and (2) a near-coincident vertex pair
            /// the importer welds (chain-ribbon end caps - hoodwink). When true, <c>AddClothProxySprings</c>
            /// skips this island's explicit rods and lets the compiler auto-derive them from the surface
            /// instead (guaranteed to compile, at the cost of exact rod topology for this one island). False
            /// for cleanly-triangulated islands, which keep their exact reconstructed rods.
            /// </summary>
            public bool IsDropRisk { get; init; }
            /// <summary>
            /// Gets whether <see cref="Faces"/> came from the authored <c>m_SourceElems</c> topology rather
            /// than a synthesised triangulation. The compiler rebuilds the shipped rods from such a sheet on
            /// its own, so it is exported without rod-suppressing paints and without explicit springs.
            /// </summary>
            public bool UsesAuthoredFaces { get; init; }
            /// <summary>
            /// Gets whether no vertex of this sheet is driven by a real skeleton bone. Hand-authored
            /// proxies of this kind ship unskinned, and the compiler answers by generating its own static
            /// root node plus one <c>m_CtrlOffsets</c> entry per vertex. Skinning such a sheet to the
            /// synthetic per-vertex bones instead binds each node directly and loses both.
            /// </summary>
            public bool IsFreeFloating { get; init; }
        }

        /// <summary>
        /// Restores a proxy's original vertex NUMBERING when it has gaps. The compiler names control nodes
        /// "$cloth_m{N}p{SLOT}" by DMX vertex slot BEFORE dropping unfaced vertices, so an original whose
        /// source mesh had culled vertices ships non-contiguous p numbers (135 corpus models - mars,
        /// death_prophet, undying, warlock). Re-exporting only the survivors contiguously shifts every name
        /// after each gap, and the whole node set mis-pairs against the original. A pinned, unfaced dummy
        /// copy of the nearest real vertex fills each gap slot: the compiler drops it again (unfaced), and
        /// every real vertex keeps its original number.
        /// </summary>
        ProxyMesh PadToAuthoredSlots(ProxyMesh mesh)
        {
            var n = mesh.NodeIndices.Length;
            if (n == 0)
            {
                return mesh;
            }

            var slots = new int[n];
            var meshIndex = -1;
            for (var i = 0; i < n; i++)
            {
                var node = mesh.NodeIndices[i];
                if (node < 0 || node >= CtrlNames.Length)
                {
                    return mesh;
                }

                var m = ParseProxyMeshIndex(CtrlNames[node]);
                var p = ParseProxyVertexIndex(CtrlNames[node]);
                if (m < 0 || p < 0 || (i > 0 && (m != meshIndex || p <= slots[i - 1])))
                {
                    return mesh;
                }

                meshIndex = m;
                slots[i] = p;
            }

            var total = slots[n - 1] + 1;
            if (total == n && slots[0] == 0)
            {
                return mesh;
            }

            // Padded slot -> source vertex; a gap copies the nearest real vertex at or before it (a gap
            // below the first real slot copies the first).
            var srcOf = new int[total];
            var dummy = new bool[total];
            var src = 0;
            for (var slot = 0; slot < total; slot++)
            {
                if (src < n && slots[src] == slot)
                {
                    srcOf[slot] = src;
                    src++;
                }
                else
                {
                    srcOf[slot] = src > 0 ? src - 1 : 0;
                    dummy[slot] = true;
                }
            }

            T[] Pad<T>(T[] source) => [.. Enumerable.Range(0, total).Select(slot => source[srcOf[slot]])];

            var clothEnable = Pad(mesh.ClothEnable);
            for (var slot = 0; slot < total; slot++)
            {
                if (dummy[slot])
                {
                    clothEnable[slot] = 0f;
                }
            }

            var localToSlot = new int[n];
            for (var slot = 0; slot < total; slot++)
            {
                if (!dummy[slot])
                {
                    localToSlot[srcOf[slot]] = slot;
                }
            }

            // A dummy carries its copied neighbour's control-node index: every consumer that walks
            // NodeIndices stays valid, and the name map only covers faced vertices so the duplicate
            // never claims the real vertex's name.
            return new ProxyMesh
            {
                NodeIndices = Pad(mesh.NodeIndices),
                Positions = Pad(mesh.Positions),
                ClothEnable = clothEnable,
                GoalStrength = Pad(mesh.GoalStrength),
                GoalDamping = Pad(mesh.GoalDamping),
                CollisionRadius = Pad(mesh.CollisionRadius),
                Friction = Pad(mesh.Friction),
                Drag = Pad(mesh.Drag),
                GroundCollision = Pad(mesh.GroundCollision),
                GroundFriction = Pad(mesh.GroundFriction),
                Gravity = Pad(mesh.Gravity),
                VertexAttraction = Pad(mesh.VertexAttraction),
                SkinInfluences = Pad(mesh.SkinInfluences),
                VertexMaps = [.. mesh.VertexMaps.Select(m => (m.Name, Pad(m.Weights)))],
                Faces = [.. mesh.Faces.Select(f => f.Select(v => localToSlot[v]).ToArray())],
                RodsDriven = mesh.RodsDriven.Length == 0 ? [] : Pad(mesh.RodsDriven),
                SimulatedCount = mesh.SimulatedCount,
                PinnedCount = mesh.PinnedCount + (total - n),
                IsDropRisk = mesh.IsDropRisk,
                UsesAuthoredFaces = mesh.UsesAuthoredFaces,
                IsFreeFloating = mesh.IsFreeFloating,
            };
        }

        /// <summary>
        /// Reconstructs the cloth proxy sheets from the FeModel surface arrays, one per connected island.
        /// Original models ship each cloth piece as its OWN proxy mesh (the compiled node names encode it:
        /// <c>$cloth_m0p3</c> = mesh 0, point 3; dark_willow has m0/m1/m2), so a merged single sheet
        /// changes how the compiler numbers and groups the nodes. Returns an empty list when the FeModel
        /// has no surface - e.g. pure bone-chain cloth that only needs ClothChain.
        /// </summary>
        public List<ProxyMesh> BuildProxyMeshes()
        {
            var result = new List<ProxyMesh>();
            var coveredNodes = new HashSet<int>();
            var merged = BuildProxyMesh();

            // Built here unpadded and combined with any same-index rods-only remainder (below) before a
            // single PadToAuthoredSlots pass at the end - padding each half separately would gap-detect
            // against the wrong, incomplete slot range.
            var pending = new List<ProxyMesh>();

            if (merged is not null)
            {
                coveredNodes.UnionWith(merged.NodeIndices);

                // Union-find over the merged sheet's local vertex indices by face membership.
                var count = merged.NodeIndices.Length;
                var groupOf = Enumerable.Range(0, count).ToArray();
                int Find(int x) { while (groupOf[x] != x) { x = groupOf[x] = groupOf[groupOf[x]]; } return x; }
                foreach (var face in merged.Faces)
                {
                    for (var i = 1; i < face.Length; i++)
                    {
                        groupOf[Find(face[0])] = Find(face[i]);
                    }
                }

                // Also union vertices the compiler already assigned to the same "$cloth_m<N>" mesh: a
                // single authored ClothProxyMeshFile keeps every one of its vertices under ONE index
                // regardless of internal face connectivity (yamato's single authored proxy spans two
                // face-disconnected regions, yet compiles as one "$cloth_m0" - splitting it by
                // connectivity alone invents a second mesh index the original never had, renumbering
                // every $cloth_m reference on that side).
                var meshIndexRep = new Dictionary<int, int>();
                for (var v = 0; v < count; v++)
                {
                    var meshIndex = ParseProxyMeshIndex(CtrlNames[merged.NodeIndices[v]]);
                    if (meshIndex < 0)
                    {
                        continue;
                    }

                    if (meshIndexRep.TryGetValue(meshIndex, out var rep))
                    {
                        groupOf[Find(v)] = Find(rep);
                    }
                    else
                    {
                        meshIndexRep[meshIndex] = v;
                    }
                }

                var islands = Enumerable.Range(0, count).GroupBy(Find).ToList();

                if (islands.Count == 1)
                {
                    pending.Add(merged);
                }
                else
                {
                    foreach (var island in islands)
                    {
                        // Ascending MERGED index preserves the authored per-island vertex order the
                        // merged mesh was already sorted into (SortByAuthoredVertexOrder) - re-sorting
                        // by global node index here would undo it.
                        var vertices = island.OrderBy(v => v).ToArray();
                        var remap = new Dictionary<int, int>(vertices.Length);
                        for (var i = 0; i < vertices.Length; i++)
                        {
                            remap[vertices[i]] = i;
                        }

                        T[] Take<T>(T[] source) => [.. vertices.Select(v => source[v])];

                        pending.Add(new ProxyMesh
                        {
                            NodeIndices = Take(merged.NodeIndices),
                            Positions = Take(merged.Positions),
                            ClothEnable = Take(merged.ClothEnable),
                            GoalStrength = Take(merged.GoalStrength),
                            GoalDamping = Take(merged.GoalDamping),
                            CollisionRadius = Take(merged.CollisionRadius),
                            Friction = Take(merged.Friction),
                            Drag = Take(merged.Drag),
                            GroundCollision = Take(merged.GroundCollision),
                            GroundFriction = Take(merged.GroundFriction),
                            Gravity = Take(merged.Gravity),
                            VertexAttraction = Take(merged.VertexAttraction),
                            SkinInfluences = Take(merged.SkinInfluences),
                            VertexMaps = [.. merged.VertexMaps.Select(m => (m.Name, Take(m.Weights)))],
                            Faces = [.. merged.Faces.Where(f => remap.ContainsKey(f[0])).Select(f => f.Select(v => remap[v]).ToArray())],
                            RodsDriven = merged.RodsDriven.Length == 0 ? [] : Take(merged.RodsDriven),
                            SimulatedCount = vertices.Count(v => merged.ClothEnable[v] != 0f),
                            PinnedCount = vertices.Count(v => merged.ClothEnable[v] == 0f),
                            UsesAuthoredFaces = merged.UsesAuthoredFaces,
                        });
                    }
                }
            }

            // A $cc proxy node whose SKELETON PARENT is a reconstructed bone-chain joint is the compiler's
            // own auto-generated proxy of that ClothChain: on marci every real chain bone (BackpackStrapLwr_
            // K_R, GemRibbon_K_R, Ponytail_K, HairA_K ...) carries 1-2 "$cc<bone>_<n>" proxy nodes parented
            // straight to it. That chain is emitted as a ClothChain (see BuildBoneChains) and the compiler
            // regenerates these proxies FROM it, so rebuilding them here as a rod-only proxy mesh would both
            // double-drive the bones and - for a curved 2-wide ribbon - collapse in the compiler's 2D
            // cloth-mesh import (later rungs weld onto earlier ones, verts get pruned, every explicit
            // ClothSpring to a pruned vert becomes a fatal "Cannot find node $cloth_mXpY" orphan). Mark them
            // covered so the rod-only pass leaves them to the ClothChain. A $cc panel with no real chain
            // bones has no such parent link and is untouched.
            // A "$cloth_m<N>p<S>" vertex hanging off a chain that ALREADY carries "$cc" nodes of its own is
            // not part of that ring: the ClothChain regenerates the $cc nodes, and the sheet the vertex
            // belongs to is separate authored geometry that merely skins onto the same joints (prof_dynamo's
            // 854-vertex coat panel rides the coat chain's joints, which carry their own 104 $cc nodes).
            // Suppressing it deletes the panel. A chain with no $cc node anywhere along it IS ringed by its
            // $cloth_m vertices - Dota's arcana ribbons name their ring that way - and those stay covered.
            // Only chains emitted as an INDEPENDENT ClothChain get their proxies suppressed. A chain any of
            // whose joints is back-solved by a fit matrix (dark_willow's Coattail/HairStrand, legion's
            // Banner) is NOT emitted as a ClothChain - it is driven THROUGH its proxy mesh - so suppressing
            // that proxy would delete the cloth entirely (regressed legion_commander: "cloth lost after
            // recompile"). Same fit-matrix exclusion ModelExtract uses to pick independentChains.
            var independentChains = IndependentBoneChains();
            var chainBoneNodes = independentChains.SelectMany(static c => c.Joints).Select(static j => j.Node).ToHashSet();
            if (chainBoneNodes.Count > 0)
            {
                // Old-era compiles ship m_SkelParents empty; a ring vertex's anchor bone then comes from
                // its m_CtrlOffsets entry instead (the same fallback BuildBoneChains uses).
                Dictionary<int, int>? offsetParents = null;
                if (!HasCompiledSkelParents && CtrlOffsets.Length > 0)
                {
                    offsetParents = new Dictionary<int, int>(CtrlOffsets.Length);
                    foreach (var off in CtrlOffsets)
                    {
                        offsetParents[off.CtrlChild] = off.CtrlParent;
                    }
                }

                int ParentOf(int node)
                {
                    var parent = node < SkelParents.Length ? SkelParents[node] : -1;
                    return parent < 0 && offsetParents is not null
                        ? offsetParents.GetValueOrDefault(node, -1)
                        : parent;
                }

                var ringOwners = new HashSet<int>();
                for (var node = 0; node < CtrlNames.Length; node++)
                {
                    if (CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal))
                    {
                        var owner = ParentOf(node);
                        if (owner >= 0)
                        {
                            ringOwners.Add(owner);
                        }
                    }
                }

                var ringedJoints = independentChains
                    .Where(chain => chain.Joints.Any(joint => ringOwners.Contains(joint.Node)))
                    .SelectMany(static c => c.Joints).Select(static j => j.Node).ToHashSet();

                for (var node = 0; node < CtrlNames.Length; node++)
                {
                    if (!IsProxyNodeName(CtrlNames[node]))
                    {
                        continue;
                    }

                    var parent = ParentOf(node);
                    if (parent >= 0 && chainBoneNodes.Contains(parent)
                        && !(ParseProxyMeshIndex(CtrlNames[node]) >= 0 && ringedJoints.Contains(parent)))
                    {
                        coveredNodes.Add(node);
                    }
                }
            }

            // "$cloth_*" control nodes that carry no m_Quads/m_Tris of their own: a plain
            // ClothProxyMeshFile import compiles down to a bare distance-constraint (m_Rods) network,
            // discarding the authored surface (see MakeClothQuad remarks in ModelExtract.ValveModel.cs),
            // so these nodes would otherwise be silently dropped instead of round-tripping as a sheet.
            // A rods-only group that shares its ORIGINAL "$cloth_m<N>" mesh index with one of the
            // face-covered proxies above is that proxy's own remainder, not a separate authored piece
            // (meepo_naruto_set's jaket: one quad covers 4 of the mesh's 75 "$cloth_m0*" nodes, the other
            // 71 are rods-only - all one physical panel) - merge it in instead of exporting a second
            // proxy file, which would otherwise hand the compiler two mesh indices for what the original
            // numbers as one.
            foreach (var rodsOnly in BuildProxyMeshesFromRodsOnly(coveredNodes))
            {
                var meshIndex = ProxyMeshOriginalIndex(rodsOnly);
                var matchIndex = meshIndex >= 0 ? pending.FindIndex(p => ProxyMeshOriginalIndex(p) == meshIndex) : -1;
                if (matchIndex >= 0)
                {
                    pending[matchIndex] = MergeSameIndexProxyMeshes(pending[matchIndex], rodsOnly);
                }
                else
                {
                    pending.Add(rodsOnly);
                }
            }

            // Final order: by the ORIGINAL "$cloth_m<N>" mesh index the compiler already assigned
            // (smallest control-node index as a fallback tiebreak, or for a proxy with no such name), so
            // the order proxy nodes are emitted in - see EnqueueClothProxyMeshes in ModelExtract.Mesh.cs
            // - reproduces it: the compiler numbers $cloth_m<N> by ordinal name sort of the proxy nodes,
            // and those names sort in this list's order.
            result.AddRange(pending
                .OrderBy(p => { var m = ProxyMeshOriginalIndex(p); return m >= 0 ? m : int.MaxValue; })
                .ThenBy(p => p.NodeIndices.Length == 0 ? int.MaxValue : p.NodeIndices.Min())
                .Select(PadToAuthoredSlots));

            return result;
        }

        // The original "$cloth_m<N>" mesh index the compiler already assigned to a proxy vertex set (the
        // smallest parsed index among its nodes - every node of one physical proxy carries the same
        // index by construction, so this is exact, not a heuristic), or -1 if none of its nodes carry
        // that name (a rods-only "$cc" panel).
        int ProxyMeshOriginalIndex(ProxyMesh mesh) => mesh.NodeIndices
            .Select(node => ParseProxyMeshIndex(CtrlNames[node]))
            .Where(m => m >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        // Merges a face-covered proxy with a rods-only remainder the compiler numbers under the SAME
        // "$cloth_m<N>" mesh index into one, re-sorted into their shared original DMX vertex-slot order
        // so PadToAuthoredSlots's gap detection still applies to the combined set. Exporting them as two
        // separate proxy files would give the compiler two mesh indices where the original only had one.
        ProxyMesh MergeSameIndexProxyMeshes(ProxyMesh a, ProxyMesh b)
        {
            var an = a.NodeIndices.Length;
            var bn = b.NodeIndices.Length;
            var order = Enumerable.Range(0, an).Select(i => (FromB: false, Index: i))
                .Concat(Enumerable.Range(0, bn).Select(i => (FromB: true, Index: i)))
                .OrderBy(t => ParseProxyVertexIndex(CtrlNames[(t.FromB ? b : a).NodeIndices[t.Index]]))
                .ToArray();

            var n = order.Length;
            var remapA = new int[an];
            var remapB = new int[bn];
            for (var slot = 0; slot < n; slot++)
            {
                var (fromB, index) = order[slot];
                if (fromB) { remapB[index] = slot; } else { remapA[index] = slot; }
            }

            T[] Combine<T>(T[] fromA, T[] fromB)
            {
                var combined = new T[n];
                for (var i = 0; i < an; i++) { combined[remapA[i]] = fromA[i]; }
                for (var i = 0; i < bn; i++) { combined[remapB[i]] = fromB[i]; }
                return combined;
            }

            var vertexMapNames = a.VertexMaps.Select(m => m.Name).Union(b.VertexMaps.Select(m => m.Name)).ToArray();
            var vertexMaps = new (string Name, float[] Weights)[vertexMapNames.Length];
            for (var i = 0; i < vertexMapNames.Length; i++)
            {
                var name = vertexMapNames[i];
                var weights = new float[n];
                var fromA = Array.Find(a.VertexMaps, m => m.Name == name).Weights;
                var fromB = Array.Find(b.VertexMaps, m => m.Name == name).Weights;
                for (var j = 0; fromA is not null && j < an; j++) { weights[remapA[j]] = fromA[j]; }
                for (var j = 0; fromB is not null && j < bn; j++) { weights[remapB[j]] = fromB[j]; }
                vertexMaps[i] = (name, weights);
            }

            var faces = new List<int[]>(a.Faces.Count + b.Faces.Count);
            faces.AddRange(a.Faces.Select(f => f.Select(v => remapA[v]).ToArray()));
            faces.AddRange(b.Faces.Select(f => f.Select(v => remapB[v]).ToArray()));

            return new ProxyMesh
            {
                NodeIndices = Combine(a.NodeIndices, b.NodeIndices),
                Positions = Combine(a.Positions, b.Positions),
                ClothEnable = Combine(a.ClothEnable, b.ClothEnable),
                GoalStrength = Combine(a.GoalStrength, b.GoalStrength),
                GoalDamping = Combine(a.GoalDamping, b.GoalDamping),
                CollisionRadius = Combine(a.CollisionRadius, b.CollisionRadius),
                Friction = Combine(a.Friction, b.Friction),
                Drag = Combine(a.Drag, b.Drag),
                GroundCollision = Combine(a.GroundCollision, b.GroundCollision),
                GroundFriction = Combine(a.GroundFriction, b.GroundFriction),
                Gravity = Combine(a.Gravity, b.Gravity),
                VertexAttraction = Combine(a.VertexAttraction, b.VertexAttraction),
                SkinInfluences = Combine(a.SkinInfluences, b.SkinInfluences),
                VertexMaps = vertexMaps,
                Faces = faces,
                RodsDriven = a.RodsDriven.Length == 0 && b.RodsDriven.Length == 0
                    ? []
                    : Combine(a.RodsDriven.Length == 0 ? new float[an] : a.RodsDriven,
                              b.RodsDriven.Length == 0 ? new float[bn] : b.RodsDriven),
                SimulatedCount = a.SimulatedCount + b.SimulatedCount,
                PinnedCount = a.PinnedCount + b.PinnedCount,
                IsDropRisk = a.IsDropRisk || b.IsDropRisk,
                UsesAuthoredFaces = a.UsesAuthoredFaces,
                IsFreeFloating = a.IsFreeFloating && b.IsFreeFloating,
            };
        }

        /// <summary>
        /// Gets the bone chains that compile to a standalone <c>ClothChain</c> rather than being driven
        /// through a proxy mesh - the ones whose joints already get their own <c>stray_radius</c> (and the
        /// rest of <c>MakeClothJoint</c>'s KVs) from the model extractor, so no other recovery path should
        /// also claim their nodes.
        /// </summary>
        List<BoneChain> IndependentBoneChains()
            => [.. BuildBoneChains()
                .Where(chain => !chain.Joints.Any(joint => ProxyFitMatrixNodes.Contains(joint.Node)))];

        /// <summary>
        /// Reconstructs the cloth proxy mesh (sheet) from the FeModel surface arrays as ONE merged mesh.
        /// Returns null when the FeModel has no surface (no quads/tris) - e.g. a pure bone-chain cloth
        /// that only needs ClothChain.
        /// </summary>
        public ProxyMesh? BuildProxyMesh()
        {
            if ((Quads.Length == 0 && Tris.Length == 0) || InitPosePositions.Length == 0)
            {
                return null;
            }

            // Collect the control nodes actually used by the surface, in ascending order. These are the
            // proxy-mesh ("sheet") nodes; the bone-chain nodes never appear in a quad/tri.
            var referenced = new SortedSet<int>();
            void Collect(int[][] faces)
            {
                foreach (var face in faces)
                {
                    foreach (var n in face)
                    {
                        if (n >= 0 && n < InitPosePositions.Length && !IsHingeRegeneratedProxy(n))
                        {
                            referenced.Add(n);
                        }
                    }
                }
            }

            Collect(Quads);
            Collect(Tris);

            if (referenced.Count == 0)
            {
                return null;
            }

            // The same sheet's ROD region. m_SourceElems keeps the authored elements the importer turned
            // into distance constraints; m_Quads/m_Tris keep the ones that stayed solve elements. A sheet
            // painted "cloth_make_rods" over part of itself compiles to both at once (yamato: 350 of its 512
            // vertices carry a face, the other 162 only rods), and the rod region's vertices then belong to
            // no face of their own. Taking those elements back into this mesh is what lets the export hand
            // the compiler one sheet with the same split instead of a face-covered island plus a separately
            // triangulated remainder.
            var surfaceNodes = new HashSet<int>(referenced);
            var surfaceMeshes = surfaceNodes.Select(node => ParseProxyMeshIndex(CtrlNames[node]))
                .Where(static mesh => mesh >= 0).ToHashSet();
            var rodsFaces = new List<int[]>();
            foreach (var face in SourceFaces)
            {
                if (face.Length < 3 || SpansProxyMeshes(face))
                {
                    continue;
                }

                var rodsRegion = false;
                var sheet = true;
                foreach (var corner in face)
                {
                    // Only a "$cloth_m<N>" vertex of a mesh this surface already covers: a chain's own "$cc"
                    // ring elements, and a mesh with no surface at all, are the rod-only path's to rebuild.
                    if (corner < 0 || corner >= InitPosePositions.Length || IsHingeRegeneratedProxy(corner)
                        || !surfaceMeshes.Contains(ParseProxyMeshIndex(CtrlNames[corner])))
                    {
                        sheet = false;
                        break;
                    }

                    rodsRegion |= !surfaceNodes.Contains(corner);
                }

                if (sheet && rodsRegion)
                {
                    rodsFaces.Add(face);
                    referenced.UnionWith(face);
                }
            }

            // A sheet vertex the compile registers that neither kind of element covers and no rod reaches:
            // its authored face was trimmed to the corners that stayed a surface - a quad with a single rod
            // corner compiles to the triangle of the other three and leaves no m_SourceElems entry behind.
            // Putting it back as that triangle's fourth corner (below) is the only way an export gets the
            // compiler to register it. One a rod does reach is a rod-only island's, whatever else covers it.
            var strays = new List<int>();
            if (rodsFaces.Count > 0)
            {
                var covered = new HashSet<int>(referenced);
                foreach (var rod in Rods)
                {
                    covered.Add(rod.NodeA);
                    covered.Add(rod.NodeB);
                }

                for (var node = 0; node < CtrlNames.Length; node++)
                {
                    if (!covered.Contains(node) && !IsHingeRegeneratedProxy(node)
                        && surfaceMeshes.Contains(ParseProxyMeshIndex(CtrlNames[node])))
                    {
                        strays.Add(node);
                        referenced.Add(node);
                    }
                }
            }

            var nodeIndices = referenced.ToArray();
            SortByAuthoredVertexOrder(nodeIndices);
            var remap = new Dictionary<int, int>(nodeIndices.Length);
            for (var i = 0; i < nodeIndices.Length; i++)
            {
                remap[nodeIndices[i]] = i;
            }

            var nodeFriction = Data.GetFloatArray("m_DynNodeFriction");

            var positions = new Vector3[nodeIndices.Length];
            var clothEnable = new float[nodeIndices.Length];
            var goalStrength = new float[nodeIndices.Length];
            var goalDamping = new float[nodeIndices.Length];
            var collisionRadius = new float[nodeIndices.Length];
            var friction = new float[nodeIndices.Length];
            var drag = new float[nodeIndices.Length];
            var groundCollision = new float[nodeIndices.Length];
            var groundFriction = new float[nodeIndices.Length];
            var gravity = new float[nodeIndices.Length];
            var vertexAttraction = new float[nodeIndices.Length];
            var skinInfluences = new (string Bone, float Weight)[nodeIndices.Length][];
            var simulated = 0;
            var pinned = 0;

            for (var i = 0; i < nodeIndices.Length; i++)
            {
                var node = nodeIndices[i];
                positions[i] = InitPosePositions[node];

                var vertex = ComputeProxyVertexData(node, nodeFriction);
                clothEnable[i] = vertex.IsSim ? 1f : 0f;
                if (vertex.IsSim) { simulated++; } else { pinned++; }
                skinInfluences[i] = vertex.SkinInfluences;
                goalStrength[i] = vertex.GoalStrength;
                goalDamping[i] = vertex.GoalDamping;
                collisionRadius[i] = vertex.CollisionRadius;
                friction[i] = vertex.Friction;
                drag[i] = vertex.Drag;
                groundCollision[i] = vertex.GroundCollision;
                groundFriction[i] = vertex.GroundFriction;
                gravity[i] = vertex.Gravity;
                vertexAttraction[i] = vertex.VertexAttraction;
            }

            // Faces are fed in the ORIGINAL compile's SIMD lane order (with each face's node order taken
            // from its lane) instead of the compiled m_Quads/m_Tris arrays' own order: those arrays are
            // node-sorted on output, but the SIMD constraint packer consumes the authored DMX face order -
            // feeding the sorted arrays back packs DIFFERENT groups whose leftover lanes get padded with
            // LIVE full-weight replicas of real constraints (dark_willow: 9 of 26 tris solved 2-3x per
            // iteration - measurably stiffer cloth than the original). The lane-major expansion is the
            // closest recoverable stand-in for the authored face order.
            // A face reaching into a corner the sheet gave up (one a hinged chain rebuilds itself) is not
            // this sheet's to draw.
            var faces = new List<int[]>(Quads.Length + Tris.Length);
            bool Kept(int[] face) => Array.TrueForAll(face, corner => remap.ContainsKey(corner));

            foreach (var q in OrderFacesBySimdLanes(Quads, "m_SimdQuads"))
            {
                if (Kept(q))
                {
                    faces.Add([remap[q[0]], remap[q[1]], remap[q[2]], remap[q[3]]]);
                }
            }

            var (splitQuads, splitHalves) = MergeSplitQuads();

            foreach (var t in OrderFacesBySimdLanes(Tris, "m_SimdTris"))
            {
                if (!Kept(t))
                {
                    continue;
                }

                var key = SortedTriKey(t);
                if (splitHalves.Contains(key))
                {
                    continue;
                }

                if (splitQuads.TryGetValue(key, out var quad))
                {
                    faces.Add([remap[quad[0]], remap[quad[1]], remap[quad[2]], remap[quad[3]]]);
                    continue;
                }

                faces.Add([remap[t[0]], remap[t[1]], remap[t[2]]]);
            }

            var rodsDriven = new float[nodeIndices.Length];
            rodsFaces.Reverse();
            foreach (var face in rodsFaces)
            {
                faces.Add([.. face.Select(corner => remap[corner])]);
            }

            foreach (var stray in strays)
            {
                AttachStrayToTriangle(remap[stray], positions, faces);
            }

            for (var i = 0; i < nodeIndices.Length; i++)
            {
                rodsDriven[i] = surfaceNodes.Contains(nodeIndices[i]) ? 0f : 1f;
            }

            return new ProxyMesh
            {
                NodeIndices = nodeIndices,
                Positions = positions,
                ClothEnable = clothEnable,
                GoalStrength = goalStrength,
                GoalDamping = goalDamping,
                CollisionRadius = collisionRadius,
                Friction = friction,
                Drag = drag,
                GroundCollision = groundCollision,
                GroundFriction = groundFriction,
                Gravity = gravity,
                VertexAttraction = vertexAttraction,
                SkinInfluences = skinInfluences,
                VertexMaps = BuildVertexMapWeights(nodeIndices),
                Faces = faces,
                SimulatedCount = simulated,
                PinnedCount = pinned,
                RodsDriven = rodsFaces.Count > 0 ? rodsDriven : [],
                UsesAuthoredFaces = rodsFaces.Count > 0,
            };
        }

        /// <summary>
        /// The authored <c>quad_bend_tolerance</c> (ModelDoc <c>ClothParams</c>) the compiler measures the
        /// split against, which <c>MakeClothParams</c> re-emits. Every corpus model authors or defaults to
        /// it, spectre pinning its own value to [0.04974, 0.05026).
        /// </summary>
        const float QuadBendTolerance = 0.05f;

        static (int, int, int) SortedTriKey(int[] tri)
        {
            var (a, b, c) = (tri[0], tri[1], tri[2]);
            if (a > b) { (a, b) = (b, a); }
            if (b > c) { (b, c) = (c, b); }
            if (a > b) { (a, b) = (b, a); }
            return (a, b, c);
        }

        /// <summary>
        /// Pairs compiled <see cref="Tris"/> back into the authored quads the compiler split them from,
        /// keyed by <see cref="SortedTriKey"/>: the first returned map gives, for the half that stayed in
        /// place, the quad to export instead of it; the second names the half to drop.
        /// <para>
        /// Before building its surface arrays the compiler splits an authored quad whose two halves are not
        /// coplanar enough - the sine of the dihedral angle across the SHORTER diagonal above
        /// <see cref="QuadBendTolerance"/> - into the triangle <c>(n0,n1,n2)</c> in place plus
        /// <c>(n0,n2,n3)</c> appended at the end of the element array, and only when no corner of the quad
        /// is static. Re-exporting those two triangles instead of the quad they came from measures mass and
        /// element rest shape over five corner pairs where the original had six, so every node around such a
        /// quad compiles heavier (yamato: 113 of 572 inverse masses) and the discarded diagonal's bend rod
        /// is lost with it.
        /// </para>
        /// <para>
        /// The merge is accepted only where the compiler is predicted to re-split the recovered quad into
        /// exactly the pair at hand - same diagonal, same corner order, and the appended half later in the
        /// array - which on yamato recovers all 84 splits with no spurious merge and leaves the 2 triangles
        /// that were never halves of anything alone.
        /// </para>
        /// </summary>
        (Dictionary<(int, int, int), int[]> Quads, HashSet<(int, int, int)> Halves) MergeSplitQuads()
        {
            var quads = new Dictionary<(int, int, int), int[]>();
            var halves = new HashSet<(int, int, int)>();
            if (Tris.Length < 2 || InitPosePositions.Length == 0)
            {
                return (quads, halves);
            }

            bool Usable(int node) => node >= 0 && node < InitPosePositions.Length
                && node < NodeInvMasses.Length && !IsHingeRegeneratedProxy(node);

            var keys = new (int, int, int)[Tris.Length];
            var ambiguous = new HashSet<(int, int, int)>();
            var distinct = new HashSet<(int, int, int)>();
            var byEdge = new Dictionary<(int, int), List<int>>();

            for (var i = 0; i < Tris.Length; i++)
            {
                var tri = Tris[i];
                if (tri.Length != 3)
                {
                    continue;
                }

                keys[i] = SortedTriKey(tri);
                if (!distinct.Add(keys[i]))
                {
                    ambiguous.Add(keys[i]);
                }

                if (!Array.TrueForAll(tri, Usable))
                {
                    continue;
                }

                for (var a = 0; a < 3; a++)
                {
                    for (var b = a + 1; b < 3; b++)
                    {
                        var edge = tri[a] < tri[b] ? (tri[a], tri[b]) : (tri[b], tri[a]);
                        if (!byEdge.TryGetValue(edge, out var sharing))
                        {
                            byEdge[edge] = sharing = [];
                        }

                        sharing.Add(i);
                    }
                }
            }

            var pairs = new List<(int InPlace, int Appended, int[] Quad)>();
            foreach (var (edge, sharing) in byEdge)
            {
                for (var x = 0; x < sharing.Count; x++)
                {
                    for (var y = x + 1; y < sharing.Count; y++)
                    {
                        // The in-place half is emitted as (n0,n1,n2) and the appended one as (n0,n2,n3),
                        // so the shared edge sits at the first corner and the last of the earlier of the
                        // two, and at the first two corners of the later.
                        var (inPlace, appended) = (sharing[x], sharing[y]);
                        var first = Tris[inPlace];
                        var second = Tris[appended];
                        if (first[0] != second[0] || first[2] != second[1])
                        {
                            continue;
                        }

                        var quad = new[] { first[0], first[1], first[2], second[2] };
                        if (quad.Distinct().Count() != 4)
                        {
                            continue;
                        }

                        var corners = Array.ConvertAll(quad, node => InitPosePositions[node]);
                        var staticCorners = quad.Count(node => NodeInvMasses[node] == 0f);
                        var order = PredictQuadSplit(corners, staticCorners);
                        if (order is null)
                        {
                            continue;
                        }

                        var (d0, d1) = (quad[order[0]], quad[order[2]]);
                        if ((d0 < d1 ? (d0, d1) : (d1, d0)) != edge)
                        {
                            continue;
                        }

                        pairs.Add((inPlace, appended, quad));
                    }
                }
            }

            var claims = new Dictionary<int, int>();
            foreach (var (inPlace, appended, _) in pairs)
            {
                claims[inPlace] = claims.GetValueOrDefault(inPlace) + 1;
                claims[appended] = claims.GetValueOrDefault(appended) + 1;
            }

            foreach (var (inPlace, appended, quad) in pairs)
            {
                if (claims[inPlace] > 1 || claims[appended] > 1
                    || ambiguous.Contains(keys[inPlace]) || ambiguous.Contains(keys[appended]))
                {
                    continue;
                }

                quads[keys[inPlace]] = quad;
                halves.Add(keys[appended]);
            }

            return (quads, halves);
        }

        /// <summary>
        /// Whether the compiler splits the quad with the given rest corners, and in which corner order:
        /// the two triangles it emits are <c>(order[0], order[1], order[2])</c> and
        /// <c>(order[0], order[2], order[3])</c>. Null when the quad is kept whole.
        /// </summary>
        static int[]? PredictQuadSplit(Vector3[] corners, int staticCorners)
        {
            var order = staticCorners < 2 ? MaximalQuadPairing(corners) : [0, 1, 2, 3];
            if (staticCorners != 0)
            {
                return null;
            }

            if (Vector3.Distance(corners[order[0]], corners[order[2]])
                > Vector3.Distance(corners[order[1]], corners[order[3]]))
            {
                order = [order[1], order[2], order[3], order[0]];
            }

            var (a, b, c, d) = (corners[order[0]], corners[order[1]], corners[order[2]], corners[order[3]]);
            var n1 = Vector3.Cross(b - a, c - a);
            var n2 = Vector3.Cross(d - c, a - c);
            return Vector3.Cross(n1, n2).Length() > n1.Length() * n2.Length() * QuadBendTolerance
                ? order
                : null;
        }

        // Repairs an authored corner order that does not describe a simple quadrilateral, by taking the
        // pairing whose two diagonals span the largest cross product.
        static int[] MaximalQuadPairing(Vector3[] corners)
        {
            int[][] pairings = [[0, 1, 2, 3], [0, 2, 3, 1], [0, 3, 1, 2]];
            var best = pairings[0];
            var bestSpan = -1f;
            foreach (var pairing in pairings)
            {
                var span = Vector3.Cross(corners[pairing[3]] - corners[pairing[1]],
                    corners[pairing[2]] - corners[pairing[0]]).Length();
                if (span > bestSpan)
                {
                    (best, bestSpan) = (pairing, span);
                }
            }

            return best;
        }

        // Puts an uncovered sheet vertex back as the fourth corner of the triangle its authored quad was
        // trimmed to: the nearest one, with the corner it sits farthest from taken as the quad's diagonal,
        // which is the only arrangement a convex quad admits. The triangle is replaced rather than added to,
        // so the compiler still emits exactly one face there - it re-trims the quad to the same triangle
        // once the vertex is painted into the rod region.
        static void AttachStrayToTriangle(int stray, Vector3[] positions, List<int[]> faces)
        {
            var best = -1;
            var bestSpan = float.MaxValue;
            for (var i = 0; i < faces.Count; i++)
            {
                if (faces[i].Length != 3)
                {
                    continue;
                }

                var span = faces[i].Max(corner => Vector3.Distance(positions[stray], positions[corner]));
                if (span < bestSpan)
                {
                    (best, bestSpan) = (i, span);
                }
            }

            if (best < 0)
            {
                return;
            }

            var triangle = faces[best];
            var diagonal = 0;
            for (var i = 1; i < 3; i++)
            {
                if (Vector3.Distance(positions[stray], positions[triangle[i]])
                    > Vector3.Distance(positions[stray], positions[triangle[diagonal]]))
                {
                    diagonal = i;
                }
            }

            // Rotated so the diagonal corner sits between the two the stray shares an edge with, keeping the
            // triangle's own winding.
            var start = (diagonal + 2) % 3;
            faces[best] = [triangle[start], triangle[(start + 1) % 3], triangle[(start + 2) % 3], stray];
        }

        // The vertex selections that reach any of the given nodes, as a membership weight per node.
        (string Name, float[] Weights)[] BuildVertexMapWeights(IReadOnlyList<int> nodeIndices)
        {
            var maps = new List<(string, float[])>();
            foreach (var map in VertexMaps)
            {
                var weights = new float[nodeIndices.Count];
                var covers = false;
                for (var i = 0; i < nodeIndices.Count; i++)
                {
                    weights[i] = map.WeightOf(nodeIndices[i]);
                    covers |= weights[i] > 0f;
                }

                if (covers)
                {
                    maps.Add((map.Name, weights));
                }
            }

            return [.. maps];
        }

        // Per-node cloth paint values recovered from the FeModel solver data (goal attraction, damping,
        // collision/friction/drag, gravity, and skin influences), shared by every proxy-mesh
        // reconstruction path - quad/tri-driven (BuildProxyMesh) and rod-only (BuildProxyMeshFromNodeSet).
        readonly record struct ProxyVertexData(
            bool IsSim,
            float GoalStrength,
            float GoalDamping,
            float CollisionRadius,
            float Friction,
            float Drag,
            float Gravity,
            float VertexAttraction,
            float GroundCollision,
            float GroundFriction,
            (string Bone, float Weight)[] SkinInfluences);

        ProxyVertexData ComputeProxyVertexData(int node, float[] nodeFriction)
        {
            var isSim = node < NodeInvMasses.Length && NodeInvMasses[node] != 0f;

            // The authored weights recovered verbatim from the compiled back-solve data take priority -
            // they reproduce the original fit matrices exactly (see RecoveredSkinWeights remarks).
            // Otherwise: pinned anchors follow their animated anchor bone with full weight, and simulated
            // vertices get smooth inverse-distance weights across the anchor's chain joints (see
            // BuildChainSkinInfluences docs) so the compiler back-solves every chain joint with a
            // proper fit.
            if (!RecoveredSkinWeights.TryGetValue(node, out var skinInfluences))
            {
                if (isSim)
                {
                    skinInfluences = BuildChainSkinInfluences(node);
                }
                else
                {
                    var anchor = ResolveSkinBone(node);
                    skinInfluences = anchor is not null ? [(anchor, 1f)] : [];
                }
            }

            // Recover the per-node paint values. These are 0..1 paint sliders in the cloth editor, so
            // the recovered quantities are clamped into that range (the editor shows a blank/0 slider
            // for out-of-range values - this is what made the old cloth_goal_damping=6.0 paint break).
            var integrator = GetIntegrator(node);

            // The compiler CUBES the painted goal strength: flAnimationForceAttraction =
            // (cloth_goal_strength_v2)^3 (measured exact: 0.24^3=0.013824, 0.45^3=0.091125,
            // 0.75^3=0.421875). Paint the cube ROOT of the recovered force attraction so the compiler's
            // cubing reproduces the ORIGINAL attraction instead of one that is too weak by a cube (the
            // "loose vs tight" / clipping difference). cbrt of a 0..1 value stays in 0..1.
            //
            // goal_damping drives flAnimationVertexAttraction (va) through an exponential-saturation curve
            // that asymptotes to 1.0. Legacy-compiled nodes ship va > 1 (dark_willow 5.25..15, void_spirit
            // up to 21) - structurally impossible for the modern compiler and NOT reproducible: RE of both
            // vphysics2.dll (runtime) and physicsbuilder.dll (compiler) proved the legacy attraction solver
            // path (node-flag bits 9,10) was removed - goal_strength is hard-clamped to [0,1] at read
            // (fa<=1), the raw cloth_animation_attract / _force_attract inputs are dead registry entries no
            // consumer reads, and the 0x600 legacy flag mask is never produced anywhere in the compiler.
            // For va > 1, GoalDampingFromAttraction returns its 1.0 maximum (excess/ceiling >= 1), painting
            // the strongest attraction the modern path can express - the closest faithful reproduction of
            // the original's own compiled values, with no feel-calibrated constants (this saturates va to
            // ~0.98, which reads stiffer than the legacy snap-then-relax, an accepted platform ceiling).
            var goalStrength = MathF.Cbrt(Math.Clamp(integrator.ForceAttraction, 0f, 1f));
            var goalDamping = GoalDampingFromAttraction(integrator.ForceAttraction, integrator.VertexAttraction);

            var collisionRadius = GetCollisionRadius(node);

            // m_DynNodeFriction is indexed by dynamic node, like m_NodeCollisionRadii.
            var dynamicIndex = node - StaticNodeCount;
            var friction = dynamicIndex >= 0 && dynamicIndex < nodeFriction.Length
                ? Math.Clamp(nodeFriction[dynamicIndex], 0f, 1f)
                : 0f;

            // The cloth_drag paint compiles to flPointDamping = paint * 30 (measured exact: 0.2 -> 6.0,
            // 0.5 -> 15.0), so the paint is recovered as pd/30. This velocity damping is what keeps the
            // original cloth calm - a 0 paint leaves the sheet swinging undamped (dark_willow ships
            // pd=6 on every simulated sheet node).
            var drag = Math.Clamp(integrator.PointDamping / ClothDragPointDampingScale, 0f, 1f);

            // Per-vertex gravity: the cloth_gravity$0 paint compiles into flGravity VERBATIM (measured:
            // painting 0.002778 lands 0.002778, painting 1.0 lands 1 - no 360 scale, unlike the
            // gravity_z KV field on ClothNode/ClothChain joints). Without the stream the compiler
            // defaults every vertex to 360, silently discarding authored variation - dark_willow's hair
            // strands and paper lantern are authored nearly weightless (flGravity=1) while the coattail
            // is full weight (360); without the stream all of them compile at 360.
            var gravity = integrator.Gravity;

            // The raw compiled vertex attraction is NOT re-authorable: values above 1.0 (legacy-era
            // compiles: dark_willow ships 15/10.5/6/5.25) exceed the modern goal_strength/goal_damping
            // pipeline's structural ceiling, the cloth_animation_attract$0 paint stream is ignored by
            // the proxy-mesh importer (verified inert on a clean compile; the name belongs to
            // ClothMapFilter's map list, not the importer's), and no modern Valve model ships va > 1
            // (checked muerta/ringmaster/kez/primal_beast) - a genuine legacy platform ceiling.
            var vertexAttraction = integrator.VertexAttraction;

            // Per-vertex ground collision: ctrl+54 world_collision, the same node flag ClothChain joints
            // already emit via IsWorldCollisionNode (ModelExtract.ValveModel.cs MakeClothJoint). A proxy-mesh
            // vertex reads it through cloth_ground_collision$0 instead of a joint KV. The paint is not a
            // membership bit: any strictly positive value enrolls a dynamic vertex, and the compiler reads
            // the value itself as that node's world friction, 1 - paint, sorted into runs spanning at most
            // 0.1 whose minimum is the flWorldFriction m_WorldCollisionParams stores.
            //
            // Only a node the original compiled as a sheet vertex can be painted: the compiler names the
            // vertex's node itself, so on a stand-in sheet rebuilt over bone or free-ClothNode controls the
            // paint enrolls a fabricated "$cloth_m*" node the original has no counterpart for, on top of
            // the construct that recreates the control node and carries its own world_collision KV.
            var (worldFriction, groundFriction) = GetWorldFriction(node);
            var groundCollision = IsWorldCollisionNode(node) && IsProxyMeshNode(node)
                ? Math.Max(1f - worldFriction, 1e-6f)
                : 0f;

            return new ProxyVertexData(isSim, goalStrength, goalDamping, collisionRadius, friction, drag, gravity, vertexAttraction, groundCollision, groundFriction, skinInfluences);
        }

        // Extracts the mesh index the compiler already encodes in an auto-generated proxy control-node
        /// <summary>
        /// Whether the compiler named any control node as a proxy-mesh vertex, i.e. whether the original
        /// carries a cloth SHEET at all. Without one there is nothing for a fit matrix to drive a bone
        /// THROUGH, so a chain keeps its own ClothChain however many of its joints are back-solved.
        /// </summary>
        public bool HasProxyMeshNodes => Array.Exists(CtrlNames, static name => ParseProxyMeshIndex(name) >= 0);

        // name ("$cloth_m3p12" -> 3), or -1 if the name does not follow that convention.
        static int ParseProxyMeshIndex(string name)
        {
            const string Prefix = "$cloth_m";
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return -1;
            }

            var pIndex = name.IndexOf('p', Prefix.Length);
            if (pIndex < 0)
            {
                return -1;
            }

            return int.TryParse(name.AsSpan(Prefix.Length, pIndex - Prefix.Length), out var index) ? index : -1;
        }

        /// <summary>
        /// Whether the original compiled <paramref name="node"/> as a vertex of an authored cloth SHEET.
        /// Anything a reconstructed proxy mesh covers that this rejects is a stand-in the export builds
        /// over bone or free-<c>ClothNode</c> controls, so per-vertex sheet data recovered for it belongs
        /// to a different construct.
        /// </summary>
        public bool IsProxyMeshNode(int node)
            => node >= 0 && node < CtrlNames.Length && ParseProxyMeshIndex(CtrlNames[node]) >= 0;

        // Extracts the AUTHORED local vertex index from an auto-generated proxy control-node name
        // ("$cloth_m3p12" -> 12) - the compiler assigns p{N} as the vertex's position in the authored
        // DMX's own position array, so the original author's vertex ORDER survives compilation inside the
        // node names. int.MaxValue for non-proxy names.
        static int ParseProxyVertexIndex(string name)
        {
            const string Prefix = "$cloth_m";
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return int.MaxValue;
            }

            var pIndex = name.IndexOf('p', Prefix.Length);
            if (pIndex < 0)
            {
                return int.MaxValue;
            }

            return int.TryParse(name.AsSpan(pIndex + 1), out var index) ? index : int.MaxValue;
        }

        // Returns the given faces reordered to the original compile's SIMD lane-major order (first
        // occurrence of each face wins; padding replicas dedup away), with each face's own node order
        // taken from its first SIMD lane. Faces without a SIMD lane (shouldn't happen - the SIMD arrays
        // pack every logical face) keep their original array order at the end. See the call site in
        // BuildProxyMesh for why the order matters.
        int[][] OrderFacesBySimdLanes(int[][] faces, string simdKey)
        {
            var simd = Data.GetArray(simdKey);
            if (simd is null || simd.Count == 0 || faces.Length == 0)
            {
                return faces;
            }

            var rows = faces[0].Length;
            static string FaceKey(IEnumerable<int> nodes) => string.Join(',', nodes.Order());

            var remaining = new Dictionary<string, List<int[]>>();
            foreach (var face in faces)
            {
                var k = FaceKey(face);
                if (!remaining.TryGetValue(k, out var list))
                {
                    list = [];
                    remaining[k] = list;
                }

                list.Add(face);
            }

            var ordered = new List<int[]>(faces.Length);
            foreach (var entry in simd)
            {
                // nNode is rows x 4 lanes; the KV3 form may present it as nested arrays or one
                // flattened row-major array - handle both.
                if (!entry.TryGetValue("nNode", out var nNodeValue) || !nNodeValue.IsArray)
                {
                    return faces;
                }

                var flat = new List<int>(rows * 4);
                foreach (var row in nNodeValue.AsArraySpan())
                {
                    if (row.IsArray)
                    {
                        foreach (var lane in row.AsArraySpan())
                        {
                            flat.Add((int)(long)lane);
                        }
                    }
                    else
                    {
                        flat.Add((int)(long)row);
                    }
                }

                if (flat.Count < rows * 4)
                {
                    return faces; // unexpected shape - keep original order rather than guessing
                }

                for (var lane = 0; lane < 4; lane++)
                {
                    var laneNodes = new int[rows];
                    for (var r = 0; r < rows; r++)
                    {
                        laneNodes[r] = flat[r * 4 + lane];
                    }

                    var k = FaceKey(laneNodes);
                    if (remaining.TryGetValue(k, out var list) && list.Count > 0)
                    {
                        list.RemoveAt(list.Count - 1);
                        ordered.Add(laneNodes);
                    }
                }
            }

            foreach (var leftovers in remaining.Values)
            {
                ordered.AddRange(leftovers);
            }

            return [.. ordered];
        }

        // Orders proxy vertices by (mesh index, AUTHORED vertex index) recovered from their compiled
        // "$cloth_m{mesh}p{vertex}" names, instead of ascending global node index. The compiler's SIMD
        // constraint packing depends on the DMX's local vertex order: re-importing the same faces with a
        // DIFFERENT vertex order packs different SIMD groups and pads the leftover lanes with LIVE
        // (full-weight) replicas of real constraints - dark_willow got 9 of its 26 tris solved 2-3x per
        // iteration (stiffer cloth) purely from an ascending-global order. Restoring the authored order
        // reproduces the original packing and makes the recompile reassign identical "$cloth_mXpY" names.
        void SortByAuthoredVertexOrder(int[] nodeIndices)
        {
            Array.Sort(nodeIndices, (x, y) =>
            {
                var mx = ParseProxyMeshIndex(CtrlNames[x]);
                var my = ParseProxyMeshIndex(CtrlNames[y]);
                if (mx != my)
                {
                    return mx.CompareTo(my);
                }

                var px = ParseProxyVertexIndex(CtrlNames[x]);
                var py = ParseProxyVertexIndex(CtrlNames[y]);
                if (px != py)
                {
                    return px.CompareTo(py);
                }

                return x.CompareTo(y);
            });
        }

        /// <summary>
        /// Reconstructs proxy-mesh islands for "$cloth_*" control nodes that carry no <see cref="Quads"/>/
        /// <see cref="Tris"/> of their own. A plain (non-<c>ClothQuad</c>) <c>ClothProxyMeshFile</c> import
        /// compiles down to a bare distance-constraint (<c>m_Rods</c>) network, discarding the authored
        /// surface entirely (verified: no import setting preserves it - see the <c>MakeClothQuad</c> remarks
        /// in <c>ModelExtract.ValveModel.cs</c>), so these nodes are otherwise silently dropped instead of
        /// round-tripping as a sheet. Grouped by the "$cloth_m&lt;N&gt;p&lt;M&gt;" mesh index the compiler
        /// already encodes in the node name - one island per otherwise-uncovered index - with faces
        /// synthesised by 2D triangulation: the compiler re-derives its own rod network from whatever
        /// surface is imported anyway (same discarding behaviour), so an approximate triangulation is
        /// enough to recover working physics instead of the exact original faces.
        ///
        /// Coverage is checked per NODE, not per mesh index: a single authored proxy DMX can contain both
        /// a small quad/tri-covered patch AND many more vertices connected only by rods (verified on
        /// meepo_naruto_set's jaket proxy - 1 quad covering 4 of the mesh's 75 "$cloth_m0*" nodes, the
        /// other 71 rods-only). Skipping by mesh index there would drop all 71 just because 4 siblings
        /// already got a face-based island - the two groups end up as separate exported proxy files
        /// instead of one, but every node's physics data still round-trips instead of being silently lost.
        ///
        /// (A ClothNode/ClothSpring reconstruction reproduces m_Rods byte-exact for the constraint data
        /// itself, but ClothNode always creates an independent new goal-attraction point; it cannot
        /// back-solve an EXISTING named bone the way ClothProxyMeshFile's back_solve_joints does. Bone-chain
        /// cloth like legion_commander's banner needs exactly that back-solve - the render mesh is skinned to
        /// the real Banner_L/R bones, not to any node ClothNode could create - so the mesh-import path stays
        /// the only route there despite the topology being approximate.)
        /// </summary>
        List<ProxyMesh> BuildProxyMeshesFromRodsOnly(HashSet<int> coveredNodes)
        {
            var result = new List<ProxyMesh>();
            if (InitPosePositions.Length == 0)
            {
                return result;
            }

            // A still-uncovered "$..." control node is a rod-only proxy vertex (real skeleton bones are
            // handled by BuildBoneChains / the driven-bone path, not here).
            // A "$cloth_node_" ctrl is the exception: the compiler writes that prefix for an authored
            // free-standing ClothNode element, not for a sheet vertex, and AddFreeClothNodesAndSprings
            // re-authors it as one. Sweeping it into a synthesised sheet renames it "$cloth_m<N>p<M>" and
            // regenerates its rods from that sheet's faces instead of its own ClothSprings.
            var n = CtrlNames.Length;
            var isProxy = new bool[n];
            for (var node = 0; node < n && node < InitPosePositions.Length; node++)
            {
                isProxy[node] = IsProxyNodeName(CtrlNames[node]) && !string.IsNullOrEmpty(CtrlNames[node])
                    && !CtrlNames[node].StartsWith(FreeClothNodePrefix, StringComparison.Ordinal)
                    && !coveredNodes.Contains(node) && !IsHingeRegeneratedProxy(node);
            }

            // Group rod-only proxy vertices by ROD CONNECTIVITY, not by name. One authored cloth panel can
            // span several proxy-node name prefixes - kez_base's cape is "$ccCapeA".."$ccCapeE" plus
            // "$ccCapeLeafA..C", all wired into one sheet by rods - so grouping by name would split a
            // connected panel across multiple proxy meshes and orphan every rod that crosses the split
            // ("Cannot find node $cloth_mXpY", a hard compile failure). Union-find over rods whose BOTH
            // endpoints are uncovered proxy vertices yields exactly the original's per-panel meshes:
            // primal_beast's leg/back/neck chains and snapfire's two panels have no rods joining them, so
            // they stay separate exactly as before; meepo/dark_willow single-mesh islands are one component.
            var parent = new int[n];
            for (var i = 0; i < n; i++)
            {
                parent[i] = i;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    x = parent[x] = parent[parent[x]];
                }

                return x;
            }

            foreach (var rod in Rods)
            {
                if (rod.NodeA >= 0 && rod.NodeA < n && rod.NodeB >= 0 && rod.NodeB < n
                    && isProxy[rod.NodeA] && isProxy[rod.NodeB])
                {
                    parent[Find(rod.NodeA)] = Find(rod.NodeB);
                }
            }

            // The corners of an authored face are one surface by definition. Splitting them across islands
            // would drop that face from both (a face is only kept where all of its corners are), leaving
            // its vertices unfaced - and an unfaced vertex is never registered as a control node at all.
            // Only faces confined to a single compiled mesh count: a face spanning two of them describes
            // authored geometry the compiler itself chose to split, and merging on it would fuse two
            // separate sheets into one.
            foreach (var face in SourceFaces)
            {
                if (SpansProxyMeshes(face))
                {
                    continue;
                }

                var first = -1;
                foreach (var corner in face)
                {
                    if (corner < 0 || corner >= n || !isProxy[corner])
                    {
                        continue;
                    }

                    if (first < 0)
                    {
                        first = corner;
                    }
                    else
                    {
                        parent[Find(corner)] = Find(first);
                    }
                }
            }

            // Also union nodes the compiler already assigned to the same "$cloth_m{N}" mesh: that index is
            // an authoritative grouping the name encodes, and a single panel can contain a vertex with no
            // rod to the rest (meepo_naruto_set's jaket has one such isolated "$cloth_m0" node - grouping
            // by rod connectivity alone would strand it in a <3-vertex singleton and drop it, losing a node).
            // "$cc" names carry no mesh index (ParseProxyMeshIndex returns -1) and rely on rod connectivity.
            var meshIndexRep = new Dictionary<int, int>();
            for (var node = 0; node < n; node++)
            {
                if (!isProxy[node])
                {
                    continue;
                }

                var meshIndex = ParseProxyMeshIndex(CtrlNames[node]);
                if (meshIndex < 0)
                {
                    continue;
                }

                if (meshIndexRep.TryGetValue(meshIndex, out var rep))
                {
                    parent[Find(node)] = Find(rep);
                }
                else
                {
                    meshIndexRep[meshIndex] = node;
                }
            }

            var groups = new Dictionary<int, List<int>>();
            for (var node = 0; node < n; node++)
            {
                if (!isProxy[node])
                {
                    continue;
                }

                var root = Find(node);
                if (!groups.TryGetValue(root, out var nodes))
                {
                    groups[root] = nodes = [];
                }

                nodes.Add(node);
            }

            var nodeFriction = Data.GetFloatArray("m_DynNodeFriction");
            // Smallest member index first; BuildProxyMeshes (the only caller) re-sorts and pads its
            // combined final list by the original "$cloth_m<N>" mesh index itself, so this order is only
            // a deterministic default for groups it doesn't merge into another proxy.
            foreach (var (_, nodeIndices) in groups.OrderBy(static kv => kv.Value.Min()))
            {
                // Need at least a triangle's worth of points to synthesise a surface.
                if (nodeIndices.Count < 3)
                {
                    continue;
                }

                var mesh = BuildProxyMeshFromNodeSet(nodeIndices, nodeFriction);
                if (mesh is not null)
                {
                    result.Add(mesh);
                }
            }

            return result;
        }

        ProxyMesh? BuildProxyMeshFromNodeSet(List<int> nodeIndices, float[] nodeFriction)
        {
            // Same authored-vertex-order restoration as BuildProxyMesh - the SIMD constraint packing
            // (and the recompile's own "$cloth_mXpY" numbering) follows the DMX vertex order.
            var sorted = nodeIndices.ToArray();
            SortByAuthoredVertexOrder(sorted);
            nodeIndices = [.. sorted];

            var count = nodeIndices.Count;
            var positions = new Vector3[count];
            var clothEnable = new float[count];
            var goalStrength = new float[count];
            var goalDamping = new float[count];
            var collisionRadius = new float[count];
            var friction = new float[count];
            var drag = new float[count];
            var groundCollision = new float[count];
            var groundFriction = new float[count];
            var gravity = new float[count];
            var vertexAttraction = new float[count];
            var skinInfluences = new (string Bone, float Weight)[count][];
            var simulated = 0;
            var pinned = 0;

            for (var i = 0; i < count; i++)
            {
                var node = nodeIndices[i];
                positions[i] = InitPosePositions[node];

                var vertex = ComputeProxyVertexData(node, nodeFriction);
                clothEnable[i] = vertex.IsSim ? 1f : 0f;
                if (vertex.IsSim) { simulated++; } else { pinned++; }
                skinInfluences[i] = vertex.SkinInfluences;
                goalStrength[i] = vertex.GoalStrength;
                goalDamping[i] = vertex.GoalDamping;
                collisionRadius[i] = vertex.CollisionRadius;
                friction[i] = vertex.Friction;
                drag[i] = vertex.Drag;
                groundCollision[i] = vertex.GroundCollision;
                groundFriction[i] = vertex.GroundFriction;
                gravity[i] = vertex.Gravity;
                vertexAttraction[i] = vertex.VertexAttraction;
            }

            var localOf = new Dictionary<int, int>(count);
            for (var i = 0; i < count; i++)
            {
                localOf[nodeIndices[i]] = i;
            }

            var faces = TakeAuthoredFaces(localOf, nodeIndices);
            var usesAuthoredFaces = faces.Count > 0;
            if (!usesAuthoredFaces)
            {
                faces = TriangulateDominantPlane(positions);
                EnsureAllVerticesFaced(positions, faces);
            }

            if (faces.Count == 0)
            {
                return null;
            }

            // Authored faces are the original topology, so the compiler's own rod derivation from them
            // reproduces the shipped m_Rods - the island needs no drop-risk fallback and no explicit rods.
            var isDropRisk = !usesAuthoredFaces && ComputeDropRisk(positions, clothEnable, faces);

            return new ProxyMesh
            {
                NodeIndices = [.. nodeIndices],
                Positions = positions,
                ClothEnable = clothEnable,
                GoalStrength = goalStrength,
                GoalDamping = goalDamping,
                CollisionRadius = collisionRadius,
                Friction = friction,
                Drag = drag,
                GroundCollision = groundCollision,
                GroundFriction = groundFriction,
                Gravity = gravity,
                VertexAttraction = vertexAttraction,
                SkinInfluences = skinInfluences,
                VertexMaps = BuildVertexMapWeights(nodeIndices),
                Faces = faces,
                SimulatedCount = simulated,
                PinnedCount = pinned,
                IsDropRisk = isDropRisk,
                UsesAuthoredFaces = usesAuthoredFaces,
                IsFreeFloating = usesAuthoredFaces && HasGeneratedClothRoot
                    && skinInfluences.All(static v => v.All(static i => IsProxyNodeName(i.Bone))),
            };
        }

        // The authored faces wholly contained in one island, remapped to that island's local vertex
        // indices. A face straddling islands belongs to neither.
        //
        // The set is kept only when every rod the compiler would derive from it is a rod the model
        // actually ships. A surface that would invent constraints is rejected outright and the island
        // falls back to a synthesised triangulation with its rods declared explicitly, which is exact even
        // though the compiled quad/tri surface then differs (the mixed chain-plus-proxy heroes land here).
        List<int[]> TakeAuthoredFaces(Dictionary<int, int> localOf, List<int> nodeIndices)
        {
            var faces = new List<int[]>();
            foreach (var face in SourceFaces)
            {
                if (SpansProxyMeshes(face))
                {
                    continue;
                }

                var complete = true;
                foreach (var corner in face)
                {
                    if (!localOf.ContainsKey(corner))
                    {
                        complete = false;
                        break;
                    }
                }

                if (complete)
                {
                    faces.Add(face);
                }
            }

            if (faces.Count == 0)
            {
                return [];
            }

            var shipped = new HashSet<(int, int)>();
            foreach (var rod in Rods)
            {
                shipped.Add(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA));
            }

            // A rod between two pinned nodes constrains nothing, and the compiler leaves it out - so an
            // authored edge joining two of them is absent from m_Rods without the surface being wrong.
            foreach (var (a, b) in DeriveRodsFromFaces(faces))
            {
                if (!shipped.Contains((a, b)) && !(a < StaticNodeCount && b < StaticNodeCount))
                {
                    return [];
                }
            }

            // Every vertex has to land on a face: an unfaced one is never registered as a control node, so
            // a partial cover would silently lose nodes that the synthesised triangulation keeps.
            var covered = new HashSet<int>();
            foreach (var face in faces)
            {
                covered.UnionWith(face);
            }

            if (!nodeIndices.All(covered.Contains))
            {
                return [];
            }

            // The compiler records the imported faces back to front, so the authored order is the reverse
            // of the one m_SourceElems lists them in. Restoring it matters because a rod's endpoints are
            // kept in the order the face that first claimed them names them (m_Rods carries the two nodes
            // in that order, with flWeight0 measured from the first), and two faces sharing an edge name
            // it in opposite directions. Faces of unequal corner counts are grouped by count on the way
            // out, so their order relative to each other does not survive.
            faces.Reverse();
            return [.. faces.Select(face => face.Select(corner => localOf[corner]).ToArray())];
        }

        // Whether a face's corners come from more than one compiled proxy mesh. Such a face cannot have
        // been authored in any single proxy DMX, and the rods it would imply are not the ones the model
        // ships, so it is left out of the surface entirely.
        bool SpansProxyMeshes(int[] face)
        {
            var meshIndex = int.MinValue;
            foreach (var corner in face)
            {
                if (corner < 0 || corner >= CtrlNames.Length || !IsProxyNodeName(CtrlNames[corner]))
                {
                    continue;
                }

                var cornerMesh = ParseProxyMeshIndex(CtrlNames[corner]);
                if (meshIndex == int.MinValue)
                {
                    meshIndex = cornerMesh;
                }
                else if (cornerMesh != meshIndex)
                {
                    return true;
                }
            }

            return false;
        }

        // Projects a 3D point set onto its two dominant-extent axes (the same "biggest bounding-box
        // spread" heuristic ModelExtract.Mesh uses for proxy UVs) - good enough for the near-planar cloth
        // sheets these control-node islands represent.
        static Vector2[] ProjectToDominantPlane(Vector3[] positions)
        {
            var min = positions.Aggregate(Vector3.Min);
            var max = positions.Aggregate(Vector3.Max);
            var extent = max - min;
            Span<int> axes = [0, 1, 2];
            axes.Sort((a, b) => extent[b].CompareTo(extent[a]));
            var (axisU, axisV) = (axes[0], axes[1]);

            var projected = new Vector2[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                projected[i] = new Vector2(positions[i][axisU], positions[i][axisV]);
            }

            return projected;
        }

        // Incremental Bowyer-Watson Delaunay triangulation over the dominant-plane projection.
        //
        // A vertex not referenced by ANY face is NOT registered as a valid FeModel control node at all, and
        // every unfaced vertex then hard-fails to compile ("Cannot find Fx Bone"/"Cannot find node"). A
        // sparser cover is not an option even though the ORIGINAL's own compiled m_Tris/m_Quads for a 75-node
        // jaket proxy island add up to only 1 quad + 1 tri: the author's source DMX carries a much denser
        // authored topology than its OWN compiled output, which the importer collapses into m_Rods - not
        // reconstructable by feeding a sparser face set. A full triangulation (this function) is what gets
        // every vertex registered: compiles clean with m_nNodeCount exact (106/106) and m_Rods close (331 vs
        // 325), even though the resulting m_Quads/m_Tris don't match (the compiler's own quad-vs-rod-collapse
        // heuristic isn't reverse-engineered here). Do not "simplify" this back to a sparser cover without
        // re-verifying node count first.
        //
        // Do NOT thin this to a sparser cover: a high cloth_make_rods paint makes the compiler match
        // m_Quads/m_Tris exactly, but its auto-derived rods then STACK with AddClothProxySprings' own exact
        // m_Rods (inflating 331->515), and any sparser fan/set-cover either leaves vertices unfaced (they
        // fail to register: "Cannot find Fx Bone") or needs a per-model minimum-degree that isn't universal
        // (>=3 fixes meepo's jaket but breaks legion's island). Correct per-edge rod topology matters more
        // for simulated behaviour than the compiled quad/tri surface count, so keep the full Delaunay output.
        //
        // The compiler only registers a proxy vertex as an FeModel control node if it is referenced by at
        // least one face (see the TriangulateDominantPlane remarks). A Delaunay triangulation of a curved
        // or near-collinear rod-only island can still leave boundary vertices - or vertices that overlap
        // once projected to the dominant plane (snapfire's two curved panels) - out of every face. Those
        // vertices then can't be targeted by their m_Rods' ClothSprings ("Cannot find node"), so the whole
        // rod-only island's cloth is lost. Attach each still-unfaced vertex to its two nearest non-collinear
        // neighbours, guaranteeing every vertex registers and its rods survive. This is purely ADDITIVE:
        // fully-triangulated islands (dark_willow / meepo_naruto_set / legion_commander) have no unfaced
        // vertices, so no triangle is added and their compiled output stays byte-exact.
        static void EnsureAllVerticesFaced(Vector3[] positions, List<int[]> faces)
        {
            var n = positions.Length;
            if (n < 3)
            {
                return;
            }

            var faced = new HashSet<int>();
            foreach (var face in faces)
            {
                foreach (var v in face)
                {
                    faced.Add(v);
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (faced.Contains(i))
                {
                    continue;
                }

                // Nearest distinct-position vertex first, then the nearest one after it that is not
                // collinear with i and the first pick, so the synthesized triangle has real area.
                var ordered = Enumerable.Range(0, n)
                    .Where(j => j != i && positions[j] != positions[i])
                    .OrderBy(j => Vector3.DistanceSquared(positions[i], positions[j]))
                    .ToList();

                if (ordered.Count < 2)
                {
                    continue;
                }

                var a = ordered[0];
                var b = -1;
                for (var k = 1; k < ordered.Count; k++)
                {
                    var cross = Vector3.Cross(positions[a] - positions[i], positions[ordered[k]] - positions[i]);
                    if (cross.LengthSquared() > 1e-6f)
                    {
                        b = ordered[k];
                        break;
                    }
                }

                if (b < 0)
                {
                    continue;
                }

                faces.Add([i, a, b]);
                faced.Add(i);
                faced.Add(a);
                faced.Add(b);
            }
        }

        // Predicts whether the cloth mesh importer will silently prune any vertex of a synthesised rod-only
        // island (which would orphan an explicit ClothSpring referencing it - a hard compile failure). Two
        // importer prune behaviours (verified byte-exact on mars m3 and hoodwink m2/m4/m5):
        //   (1) ISOLATED-PINNED: a pinned (cloth_enable == 0) vertex whose face-neighbours are ALL pinned has
        //       no simulated neighbour, so it is a fully-static mesh region the solver has no use for and
        //       discards. (mars m3: the two spine_2-pinned tip verts p21/p22, surrounded only by other
        //       pinned verts, were dropped; the sibling tip vert p23 kept a simulated neighbour and survived.)
        //   (2) NEAR-COINCIDENT WELD: two vertices much closer than the island's typical edge length get
        //       welded into one by the importer, dropping the duplicate. (hoodwink's chain-ribbon end caps:
        //       the two sides converge to ~0.9-unit-apart pairs, ~0.18x the ~5-unit rod length.)
        // Either signal marks the whole island as drop-risk; the caller then omits its explicit rods and lets
        // the compiler auto-derive them (always compiles). Convex, uniformly-spaced islands trip neither and
        // keep their exact reconstructed rods. Both signals are scale-relative / topological - no value is
        // peeked from any specific model's source.
        static bool ComputeDropRisk(Vector3[] positions, float[] clothEnable, List<int[]> faces)
        {
            var n = positions.Length;
            if (n == 0)
            {
                return false;
            }

            // (1) isolated-pinned: build face adjacency, flag a pinned vertex with no simulated neighbour.
            var adjacency = new HashSet<int>[n];
            for (var i = 0; i < n; i++)
            {
                adjacency[i] = [];
            }

            foreach (var face in faces)
            {
                foreach (var a in face)
                {
                    foreach (var b in face)
                    {
                        if (a != b)
                        {
                            adjacency[a].Add(b);
                        }
                    }
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (clothEnable[i] != 0f)
                {
                    continue;
                }

                var hasSimulatedNeighbour = false;
                foreach (var nb in adjacency[i])
                {
                    if (clothEnable[nb] != 0f)
                    {
                        hasSimulatedNeighbour = true;
                        break;
                    }
                }

                if (!hasSimulatedNeighbour)
                {
                    return true;
                }
            }

            // (2) near-coincident weld: any vertex pair closer than a fraction of the island's median edge.
            var edges = new List<float>();
            foreach (var face in faces)
            {
                for (var a = 0; a < face.Length; a++)
                {
                    var b = (a + 1) % face.Length;
                    edges.Add(Vector3.Distance(positions[face[a]], positions[face[b]]));
                }
            }

            if (edges.Count > 0)
            {
                edges.Sort();
                var weldDistance = edges[edges.Count / 2] * 0.25f;
                for (var i = 0; i < n; i++)
                {
                    for (var j = i + 1; j < n; j++)
                    {
                        if (Vector3.Distance(positions[i], positions[j]) < weldDistance)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        static List<int[]> TriangulateDominantPlane(Vector3[] positions)
        {
            var faces = new List<int[]>();
            var n = positions.Length;
            if (n < 3)
            {
                return faces;
            }

            var points = ProjectToDominantPlane(positions);

            var min = points.Aggregate(Vector2.Min);
            var max = points.Aggregate(Vector2.Max);
            var center = (min + max) * 0.5f;
            var size = MathF.Max(max.X - min.X, max.Y - min.Y) * 10f + 1f;

            // Super-triangle enclosing every point, at indices n, n+1, n+2 (stripped out at the end).
            var allPoints = new Vector2[n + 3];
            Array.Copy(points, allPoints, n);
            allPoints[n] = center + new Vector2(0f, size * 2f);
            allPoints[n + 1] = center + new Vector2(-size * 2f, -size);
            allPoints[n + 2] = center + new Vector2(size * 2f, -size);

            var triangles = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };

            for (var p = 0; p < n; p++)
            {
                var bad = triangles.Where(tri => InCircumcircle(allPoints[tri.A], allPoints[tri.B], allPoints[tri.C], allPoints[p])).ToList();

                // The hole's boundary: edges of bad triangles that are not shared with another bad triangle.
                var polygon = new List<(int A, int B)>();
                foreach (var tri in bad)
                {
                    foreach (var edge in new[] { (tri.A, tri.B), (tri.B, tri.C), (tri.C, tri.A) })
                    {
                        var shared = false;
                        foreach (var other in bad)
                        {
                            if (!other.Equals(tri) && HasEdge(other, edge.Item1, edge.Item2))
                            {
                                shared = true;
                                break;
                            }
                        }

                        if (!shared)
                        {
                            polygon.Add(edge);
                        }
                    }
                }

                triangles.RemoveAll(bad.Contains);
                foreach (var (a, b) in polygon)
                {
                    triangles.Add((a, b, p));
                }
            }

            foreach (var tri in triangles)
            {
                if (tri.A < n && tri.B < n && tri.C < n)
                {
                    faces.Add([tri.A, tri.B, tri.C]);
                }
            }

            return faces;
        }

        static bool HasEdge((int A, int B, int C) tri, int a, int b)
            => (tri.A == a && tri.B == b) || (tri.A == b && tri.B == a)
            || (tri.B == a && tri.C == b) || (tri.B == b && tri.C == a)
            || (tri.C == a && tri.A == b) || (tri.C == b && tri.A == a);

        static bool InCircumcircle(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            var ax = a.X - p.X; var ay = a.Y - p.Y;
            var bx = b.X - p.X; var by = b.Y - p.Y;
            var cx = c.X - p.X; var cy = c.Y - p.Y;

            var det =
                (ax * ax + ay * ay) * (bx * cy - cx * by) -
                (bx * bx + by * by) * (ax * cy - cx * ay) +
                (cx * cx + cy * cy) * (ax * by - bx * ay);

            var area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            return area >= 0 ? det > 0 : det < 0;
        }
    }
}
