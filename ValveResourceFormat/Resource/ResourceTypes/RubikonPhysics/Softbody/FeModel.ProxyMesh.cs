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
            /// <summary>
            /// Gets the per-vertex <c>cloth_animation_force_attract</c> paint: the node integrator's
            /// <c>flAnimationForceAttraction</c> at 1/30 scale, for the vertices
            /// <see cref="RawGoalPaintNodes"/> keeps on the raw integrator, and 0 for the rest. Empty when
            /// no vertex of the sheet needs it.
            /// </summary>
            public float[] AnimationForceAttract { get; init; } = [];
            /// <summary>
            /// Gets the per-vertex <c>cloth_animation_attract</c> paint: the node integrator's
            /// <c>flAnimationVertexAttraction</c> at 1/30 scale, on the same vertices as
            /// <see cref="AnimationForceAttract"/>.
            /// </summary>
            public float[] AnimationAttract { get; init; } = [];
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
            /// compiler defaults every vertex to 360, silently discarding authored per-vertex variation.
            /// </summary>
            public required float[] Gravity { get; init; }
            /// <summary>
            /// Gets the RAW compiled <c>flAnimationVertexAttraction</c> per vertex. The
            /// goal_strength/goal_damping pair caps it at 1.0; legacy-era compiles above that value are
            /// re-authored through <see cref="AnimationAttract"/> instead.
            /// </summary>
            public required float[] VertexAttraction { get; init; }
            /// <summary>
            /// Gets the skeleton bone influences of each proxy vertex. Pinned anchors carry a single
            /// weight-1 influence on their anchor bone. Simulated vertices are SMOOTHLY weighted across the
            /// nearest joints of the anchor's chain: the compiler back-solves a bone with a proper fit
            /// matrix only when enough weighted vertices reference it - hard single-bone skinning degrades
            /// every chain joint to a point-driven rope with a much denser rod network.
            /// </summary>
            public required (string Bone, float Weight)[][] SkinInfluences { get; init; }
            /// <summary>
            /// Gets the faces (proxy-vertex index quads and triangles) covering the sheet, preserving the
            /// original quad/tri split. Triangulating the quads instead makes the compiler re-derive a much
            /// denser quad/rod network and the recompiled cloth turns rigid.
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
            /// with no such region. Painted back as <c>cloth_make_rods</c>, which makes the compiler split
            /// the same sheet the same way; the sheet's own gradient paint is not recoverable and does not
            /// need to be.
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
            /// cause this (see <see cref="ComputeDropRisk"/>): (1) a pinned vertex whose face-neighbours are
            /// ALL pinned (a fully-static mesh region the solver discards), and (2) a near-coincident vertex
            /// pair the importer welds. When true, <c>AddClothProxySprings</c> skips this island's explicit
            /// rods and lets the compiler auto-derive them from the surface instead (guaranteed to compile,
            /// at the cost of exact rod topology for this one island). False for cleanly-triangulated
            /// islands, which keep their exact reconstructed rods.
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
        /// source mesh had culled vertices ships non-contiguous p numbers. Re-exporting only the survivors
        /// contiguously shifts every name after each gap, and the whole node set mis-pairs against the
        /// original. A pinned, unfaced dummy copy of the nearest real vertex fills each gap slot: the
        /// compiler drops it again (unfaced), and every real vertex keeps its original number.
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
                AnimationForceAttract = Pad(mesh.AnimationForceAttract),
                AnimationAttract = Pad(mesh.AnimationAttract),
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
        /// <c>$cloth_m0p3</c> = mesh 0, point 3), so a merged single sheet changes how the compiler numbers
        /// and groups the nodes. Returns an empty list when the FeModel has no surface - e.g. pure
        /// bone-chain cloth that only needs ClothChain.
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
                // regardless of internal face connectivity, even when they span multiple
                // face-disconnected regions - splitting it by connectivity alone invents a second mesh
                // index the original never had, renumbering every $cloth_m reference on that side.
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
                            AnimationForceAttract = Take(merged.AnimationForceAttract),
                            AnimationAttract = Take(merged.AnimationAttract),
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
            // own auto-generated proxy of that ClothChain, carrying 1-2 "$cc<bone>_<n>" proxy nodes parented
            // straight to each real chain bone. That chain is emitted as a ClothChain (see BuildBoneChains)
            // and the compiler regenerates these proxies FROM it, so rebuilding them here as a rod-only
            // proxy mesh would both double-drive the bones and - for a curved 2-wide ribbon - collapse in
            // the compiler's 2D cloth-mesh import (later rungs weld onto earlier ones, verts get pruned,
            // every explicit ClothSpring to a pruned vert becomes a fatal "Cannot find node $cloth_mXpY"
            // orphan). Mark them covered so the rod-only pass leaves them to the ClothChain. A $cc panel
            // with no real chain bones has no such parent link and is untouched.
            // A "$cloth_m<N>p<S>" vertex hanging off a chain that ALREADY carries "$cc" nodes of its own is
            // not part of that ring: the ClothChain regenerates the $cc nodes, and the sheet the vertex
            // belongs to is separate authored geometry that merely skins onto the same joints. Suppressing
            // it deletes the panel. A "$cloth_m<N>p<S>" vertex is never suppressed: the name records an
            // authored DMX sheet slot, and an emitted ClothChain regenerates only "$cc" rings, so a
            // suppressed sheet vertex is simply lost from the recompile.
            // Only chains emitted as an INDEPENDENT ClothChain get their proxies suppressed. A chain any of
            // whose joints is back-solved by a fit matrix is NOT emitted as a ClothChain - it is driven
            // THROUGH its proxy mesh - so suppressing that proxy would delete the cloth entirely. Same
            // fit-matrix exclusion ModelExtract uses to pick independentChains.
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

                for (var node = 0; node < CtrlNames.Length; node++)
                {
                    if (!IsProxyNodeName(CtrlNames[node]) || ParseProxyMeshIndex(CtrlNames[node]) >= 0)
                    {
                        continue;
                    }

                    var parent = ParentOf(node);
                    if (parent >= 0 && chainBoneNodes.Contains(parent))
                    {
                        coveredNodes.Add(node);
                    }
                }
            }

            // "$cloth_*" control nodes that carry no m_Quads/m_Tris of their own: a plain
            // ClothProxyMeshFile import compiles down to a bare distance-constraint (m_Rods) network,
            // discarding the authored surface, so these nodes would otherwise be silently dropped instead
            // of round-tripping as a sheet.
            // A rods-only group that shares its ORIGINAL "$cloth_m<N>" mesh index with one of the
            // face-covered proxies above is that proxy's own remainder, not a separate authored piece -
            // merge it in instead of exporting a second proxy file, which would otherwise hand the
            // compiler two mesh indices for what the original numbers as one.
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
                AnimationForceAttract = Combine(a.AnimationForceAttract, b.AnimationForceAttract),
                AnimationAttract = Combine(a.AnimationAttract, b.AnimationAttract),
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
                .Where(chain => !chain.Joints.Any(joint => ProxyFitMatrixNodes.Contains(joint.Node))
                    && !IsSheetDrivenChain(chain))];

        /// <summary>
        /// Whether the original drives this chain's bones THROUGH a proxy sheet instead of simulating
        /// them: every dynamic-band joint sits in the position-driven band (the compiler puts back-solved
        /// bones there, fit-matrix and CtrlOffsets-driven alike) and at least one "$cloth_m" sheet vertex
        /// hangs off one of the joints. Emitting a ClothChain for such a chain simulates bones the
        /// original back-solves, and alongside the re-created sheet the double drive access-violates the
        /// compiler; the sheet with <c>back_solve_joints</c> reproduces the original band exactly.
        /// </summary>
        public bool IsSheetDrivenChain(BoneChain chain)
        {
            var anyPositionDriven = false;
            foreach (var joint in chain.Joints)
            {
                // A joint bone the compiled skeleton culled cannot be skinned to from the proxy DMX
                // (its jointList is the compiled skeleton), so the sheet cannot back-solve it.
                if (CulledBoneCtrlNodes?.Contains(joint.Node) == true)
                {
                    return false;
                }

                if (joint.Node >= StaticNodeCount)
                {
                    if (!IsPositionDriven(joint.Node))
                    {
                        return false;
                    }

                    anyPositionDriven = true;
                }
            }

            if (!anyPositionDriven)
            {
                return false;
            }

            var jointNodes = chain.Joints.Select(static j => j.Node).ToHashSet();
            var sheetDriven = false;
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                var sheetVertex = ParseProxyMeshIndex(CtrlNames[node]) >= 0;
                var generatedRing = CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal);
                if (!sheetVertex && !generatedRing)
                {
                    continue;
                }

                var parent = node < SkelParents.Length ? SkelParents[node] : -1;
                if (parent < 0 && !HasCompiledSkelParents)
                {
                    foreach (var off in CtrlOffsets)
                    {
                        if (off.CtrlChild == node)
                        {
                            parent = off.CtrlParent;
                            break;
                        }
                    }
                }

                if (parent < 0 || !jointNodes.Contains(parent))
                {
                    continue;
                }

                // The chain carries generated "$cc" proxies of its own, and only an emitted ClothChain
                // recreates those, so dropping the chain for the sheet would delete them.
                if (generatedRing)
                {
                    return false;
                }

                sheetDriven = true;
            }

            return sheetDriven;
        }

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
            // proxy-mesh ("sheet") nodes; a rigid hinge's fan is chain geometry the ClothChain rebuilds.
            var referenced = new SortedSet<int>();
            void Collect(int[][] faces)
            {
                foreach (var face in faces)
                {
                    if (IsRigidHingeFace(face))
                    {
                        continue;
                    }

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
            // painted "cloth_make_rods" over part of itself compiles to both at once, and the rod region's
            // vertices then belong to no face of their own. Taking those elements back into this mesh is
            // what lets the export hand the compiler one sheet with the same split instead of a
            // face-covered island plus a separately triangulated remainder.
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

                for (var node = 0; node < CtrlNames.Length && node < InitPosePositions.Length; node++)
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
            var animationForceAttract = new float[nodeIndices.Length];
            var animationAttract = new float[nodeIndices.Length];
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
                animationForceAttract[i] = vertex.AnimationForceAttract;
                animationAttract[i] = vertex.AnimationAttract;
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
            // LIVE full-weight replicas of real constraints, solving some elements multiple times per
            // iteration for measurably stiffer cloth. The lane-major expansion is the closest recoverable
            // stand-in for the authored face order.
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

            RestoreStaticQuadCornerOrder(faces, rodsFaces.Select(face => face.Select(corner => remap[corner]).ToArray()).ToList(), nodeIndices);

            var rodsDriven = new float[nodeIndices.Length];
            var surfaceFaceCount = faces.Count;
            rodsFaces.Reverse();
            foreach (var face in rodsFaces)
            {
                faces.Add([.. face.Select(corner => remap[corner])]);
            }

            var meshOf = Array.ConvertAll(nodeIndices, node => ParseProxyMeshIndex(CtrlNames[node]));
            foreach (var stray in strays)
            {
                AttachStrayToTriangle(remap[stray], positions, faces, meshOf);
            }

            DeclareFacesInStaticNodeOrder(faces, surfaceFaceCount, nodeIndices);

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
                AnimationForceAttract = animationForceAttract,
                AnimationAttract = animationAttract,
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
        /// split against, which <c>MakeClothParams</c> re-emits.
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
        /// quad compiles heavier and the discarded diagonal's bend rod is lost with it.
        /// </para>
        /// <para>
        /// The merge is accepted only where the compiler is predicted to re-split the recovered quad into
        /// exactly the pair at hand - same diagonal, same corner order, and the appended half later in the
        /// array.
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

        /// <summary>
        /// Rotates the corner order of every quad with one or two adjacent static corners into the form the
        /// model's bend rods say it was authored in.
        /// <para>
        /// The compiler builds a bend rod across each edge two elements share, joining the far corners at the
        /// same end of that edge, and reads the elements in the corner order the DMX declares - after moving
        /// the static corners of a quad to the front when its declared order does not already lead with them.
        /// A quad declared with its static corners in the middle of the order is thereby turned into a bow
        /// tie: two of its edges become diagonals and the far corners across the other two pair crosswise. The
        /// compiled quad shows only the convex order the compiler restores afterwards, so the export declares
        /// that order unless the rods the model ships are the ones the bow tie produces.
        /// </para>
        /// </summary>
        void RestoreStaticQuadCornerOrder(List<int[]> faces, List<int[]> rodFaces, int[] nodeIndices)
        {
            bool IsStatic(int local) => local >= 0 && local < nodeIndices.Length
                && nodeIndices[local] < NodeInvMasses.Length && NodeInvMasses[nodeIndices[local]] == 0f;

            var choices = new List<(int Face, int[][] Orders)>();
            for (var i = 0; i < faces.Count; i++)
            {
                var orders = StaticQuadCornerOrders(faces[i], IsStatic);
                if (orders.Length > 1)
                {
                    choices.Add((i, orders));
                }
            }

            if (choices.Count == 0)
            {
                return;
            }

            var localOf = new Dictionary<int, int>(nodeIndices.Length);
            for (var i = 0; i < nodeIndices.Length; i++)
            {
                localOf[nodeIndices[i]] = i;
            }

            var shipped = new HashSet<(int, int)>();
            foreach (var rod in Rods)
            {
                if (localOf.TryGetValue(rod.NodeA, out var a) && localOf.TryGetValue(rod.NodeB, out var b) && a != b)
                {
                    shipped.Add(a < b ? (a, b) : (b, a));
                }
            }

            var elements = new List<int[]>(faces.Count + rodFaces.Count);
            elements.AddRange(faces.Select(face => CompilerCornerCycle(face, IsStatic)));
            elements.AddRange(rodFaces);

            int Disagreement()
            {
                var predicted = PredictBendRods(elements, IsStatic);
                var count = 0;
                foreach (var pair in predicted)
                {
                    if (!shipped.Contains(pair))
                    {
                        count++;
                    }
                }

                foreach (var pair in shipped)
                {
                    if (!predicted.Contains(pair))
                    {
                        count++;
                    }
                }

                return count;
            }

            var chosen = new int[faces.Count];
            for (var pass = 0; pass < 4; pass++)
            {
                var changed = false;
                foreach (var (face, orders) in choices)
                {
                    var best = chosen[face];
                    var bestScore = int.MaxValue;
                    for (var c = 0; c < orders.Length; c++)
                    {
                        elements[face] = CompilerCornerCycle(orders[c], IsStatic);
                        var score = Disagreement();
                        if (score < bestScore || (score == bestScore && c == chosen[face]))
                        {
                            (best, bestScore) = (c, score);
                        }
                    }

                    elements[face] = CompilerCornerCycle(orders[best], IsStatic);
                    if (best != chosen[face])
                    {
                        chosen[face] = best;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            foreach (var (face, orders) in choices)
            {
                faces[face] = orders[chosen[face]];
            }
        }

        // The corner orders a quad with one or two adjacent static corners can be declared in: the order at
        // hand (the convex one the compiled quad carries, static corners first), then the ones that put the
        // static corners in the middle of the declaration. Any other face has only the order it has.
        static int[][] StaticQuadCornerOrders(int[] face, Func<int, bool> isStatic)
        {
            if (face.Length != 4)
            {
                return [face];
            }

            var statics = face.Count(isStatic);
            if (statics is 0 or > 2)
            {
                return [face];
            }

            var start = -1;
            for (var k = 0; k < 4; k++)
            {
                if (isStatic(face[k]) && !isStatic(face[(k + 3) % 4]))
                {
                    start = k;
                }
            }

            if (start < 0 || (statics == 2 && !isStatic(face[(start + 1) % 4])))
            {
                return [face];
            }

            var proper = new[] { face[start], face[(start + 1) % 4], face[(start + 2) % 4], face[(start + 3) % 4] };
            return statics == 2
                ? [face, [proper[3], proper[0], proper[1], proper[2]]]
                : [face, [proper[3], proper[0], proper[1], proper[2]], [proper[2], proper[3], proper[0], proper[1]]];
        }

        /// <summary>
        /// Rotates each surface face's declared corner order so the sheet hands the compiler its static
        /// vertices in the order the shipped node array numbers them.
        /// </summary>
        /// <remarks>
        /// The cloth importer creates a control node for every simulated vertex first, walking the mesh's
        /// corner index array, and then walks the faces again and creates one for each remaining corner of
        /// a face that carries a simulated corner. The builder orders the static block by that creation
        /// order, so which of a face's static corners is numbered first follows from the corner the face is
        /// declared from - a choice the compiled elements do not record. Only rotations that leave
        /// <see cref="CompilerCornerCycle"/> unchanged are taken, so the elements and the bend rods derived
        /// from them are the same either way. Only the first <paramref name="rotatableFaceCount"/> faces are
        /// rotated; the rod-region tail a mixed sheet appends keeps the corner order it was read in.
        /// </remarks>
        void DeclareFacesInStaticNodeOrder(List<int[]> faces, int rotatableFaceCount, IReadOnlyList<int> nodeIndices)
        {
            bool IsStatic(int local) => local >= 0 && local < nodeIndices.Count
                && nodeIndices[local] < NodeInvMasses.Length && NodeInvMasses[nodeIndices[local]] == 0f;

            var created = new HashSet<int>();
            for (var i = 0; i < faces.Count; i++)
            {
                // The importer keeps a face's first four corners and skips a face with no simulated corner.
                var face = faces[i];
                var corners = face.Length > 4 ? face[..4] : face;
                if (Array.TrueForAll(corners, IsStatic))
                {
                    continue;
                }

                if (i < rotatableFaceCount && corners.Length == face.Length)
                {
                    var rotated = RotateToStaticNodeOrder(face, created, nodeIndices, IsStatic);
                    if (rotated is not null)
                    {
                        faces[i] = corners = rotated;
                    }
                }

                foreach (var corner in corners)
                {
                    if (IsStatic(corner))
                    {
                        created.Add(corner);
                    }
                }
            }
        }

        // The rotation of a face that introduces its as-yet unnumbered static corners in ascending node
        // index, or null when the declaration at hand already does or no rotation the corner cycle survives
        // can. Statics on opposite sides of the rotation-lock boundary are left alone: the builder groups
        // those before it orders them by creation.
        int[]? RotateToStaticNodeOrder(int[] face, HashSet<int> created, IReadOnlyList<int> nodeIndices, Func<int, bool> isStatic)
        {
            var introduced = new List<int>(2);
            foreach (var corner in face)
            {
                if (isStatic(corner) && !created.Contains(corner) && !introduced.Contains(corner))
                {
                    introduced.Add(corner);
                }
            }

            if (introduced.Count < 2)
            {
                return null;
            }

            var rotationLocked = RotationLockedStaticNodeCount;
            if (introduced.Exists(corner => nodeIndices[corner] < rotationLocked != (nodeIndices[introduced[0]] < rotationLocked)))
            {
                return null;
            }

            var wanted = introduced.OrderBy(corner => nodeIndices[corner]).ToArray();
            if (introduced.SequenceEqual(wanted))
            {
                return null;
            }

            var cycle = CompilerCornerCycle(face, isStatic);
            for (var start = 1; start < face.Length; start++)
            {
                var rotated = new int[face.Length];
                for (var k = 0; k < face.Length; k++)
                {
                    rotated[k] = face[(start + k) % face.Length];
                }

                if (!CompilerCornerCycle(rotated, isStatic).SequenceEqual(cycle))
                {
                    continue;
                }

                if (Array.FindAll(rotated, corner => introduced.Contains(corner)).Distinct().SequenceEqual(wanted))
                {
                    return rotated;
                }
            }

            return null;
        }

        /// <summary>
        /// The corner order a declared face reaches the compiler's own element array in: the import
        /// canonicalisation and the mass pass's static-first partition.
        /// </summary>
        /// <remarks>
        /// At import a quad whose two simulated corners are DIAGONAL takes a single transposition, and every
        /// other face is rotated so the trailing static run leads. Inside the mass pass a face that still has
        /// a static corner past its leading run is stable-partitioned, non-simulated first. This is the model
        /// a SEARCH over candidate declarations scores against; the order the compiler finally pairs bend
        /// rods in is <see cref="CompiledElementOrder"/>, one pass further on.
        /// </remarks>
        static int[] CompilerCornerCycle(int[] face, Func<int, bool> isStatic)
        {
            var n = face.Length;


            if (n == 4 && face[2] != face[3] && face[1] != face[0])
            {
                if (!isStatic(face[0]) && !isStatic(face[2]) && isStatic(face[1]) && isStatic(face[3]))
                {
                    return [face[3], face[1], face[2], face[0]];
                }

                if (!isStatic(face[1]) && !isStatic(face[3]) && isStatic(face[0]) && isStatic(face[2]))
                {
                    return [face[0], face[2], face[1], face[3]];
                }
            }

            var start = 0;
            for (var k = n - 1; k >= 0; k--)
            {
                if (!isStatic(face[k]))
                {
                    start = (k + 1) % n;
                    break;
                }
            }

            var cycle = new int[n];
            for (var k = 0; k < n; k++)
            {
                cycle[k] = face[(start + k) % n];
            }

            var leading = 0;
            while (leading < n && isStatic(cycle[leading]))
            {
                leading++;
            }

            var trailingStatic = false;
            for (var k = leading; k < n && !trailingStatic; k++)
            {
                trailingStatic = isStatic(cycle[k]);
            }

            if (trailingStatic)
            {
                cycle = [.. cycle.Where(isStatic), .. cycle.Where(corner => !isStatic(corner))];
                leading = cycle.Count(isStatic);
            }

            return cycle;
        }

        /// <summary>
        /// The corner order the compiler pairs bend rods in for a FIXED declaration: the passes
        /// <see cref="CompilerCornerCycle"/> models, and then the convexity swap it applies to a quad whose
        /// two leading corners are static.
        /// </summary>
        /// <remarks>
        /// The swap runs between the mass pass and the edge-descriptor walk, so only a prediction of what an
        /// already-chosen declaration compiles to may model it. A search that picks a declaration must not:
        /// scoring candidates through the swap makes it choose one that anticipates the swap, which the
        /// compiler then applies on top.
        /// </remarks>
        static int[] CompiledElementOrder(int[] face, Func<int, bool> isStatic, Func<int, Vector3> positionOf)
        {
            var cycle = CompilerCornerCycle(face, isStatic);
            if (cycle.Length != 4 || !isStatic(cycle[0]) || !isStatic(cycle[1])
                || isStatic(cycle[2]) || isStatic(cycle[3]))
            {
                return cycle;
            }

            var edge0 = positionOf(cycle[1]) - positionOf(cycle[0]);
            var edge2 = positionOf(cycle[2]) - positionOf(cycle[3]);
            if (Vector3.Dot(edge2, edge0) < 0f)
            {
                (cycle[2], cycle[3]) = (cycle[3], cycle[2]);
            }

            return cycle;
        }

        /// <summary>
        /// The bend rods <c>add_stiffness_rods</c> makes the compiler derive from a surface, in whatever
        /// node indices the faces are given in: the pairs read off the edges the faces share, in the corner
        /// order they are declared in. A face with more than four corners is one the compiler keeps only
        /// the first four of (see <see cref="AppendTruncatedCorners"/>), and the rest have no bearing on
        /// the network.
        /// </summary>
        internal static HashSet<(int, int)> BendRodsFromSurface(IEnumerable<int[]> faces, Func<int, bool> isStatic)
            => PredictBendRods([.. faces.Select(static face => face.Length > 4 ? face[..4] : face)], isStatic);

        // The bend rods the compiler derives from the given elements, one pair of far corners per edge two of
        // them share. Elements are walked in order and an edge is paired with the earliest unpaired element
        // that lists it, in the same direction first; the far corners then pair by position when the
        // directions agree and crosswise when they oppose. A rod between two static corners is never built.
        static HashSet<(int, int)> PredictBendRods(List<int[]> elements, Func<int, bool> isStatic)
        {
            var open = new Dictionary<(int, int), (int Near, int Far)>();
            var rods = new HashSet<(int, int)>();
            void Add(int a, int b)
            {
                if (a != b && !(isStatic(a) && isStatic(b)))
                {
                    rods.Add(a < b ? (a, b) : (b, a));
                }
            }

            foreach (var e in elements)
            {
                var n = e.Length;
                for (var j = 0; j < n; j++)
                {
                    var n0 = e[j];
                    var n1 = e[(j + 1) % n];
                    var n2 = n == 3 ? e[(j + 2) % 3] : e[(j + 2) % 4];
                    var n3 = n == 3 ? n2 : e[(j + 3) % 4];
                    if (open.Remove((n0, n1), out var same))
                    {
                        Add(n2, same.Near);
                        Add(n3, same.Far);
                    }
                    else if (open.Remove((n1, n0), out var opposite))
                    {
                        Add(n2, opposite.Far);
                        Add(n3, opposite.Near);
                    }
                    else
                    {
                        open[(n0, n1)] = (n2, n3);
                    }
                }
            }

            return rods;
        }

        // Puts an uncovered sheet vertex back as the fourth corner of the triangle its authored quad was
        // trimmed to: the nearest one, with the corner it sits farthest from taken as the quad's diagonal,
        // which is the only arrangement a convex quad admits. The triangle is replaced rather than added to,
        // so the compiler still emits exactly one face there - it re-trims the quad to the same triangle
        // once the vertex is painted into the rod region.
        static void AttachStrayToTriangle(int stray, Vector3[] positions, List<int[]> faces, int[] meshOf)
        {
            var best = -1;
            var bestSpan = float.MaxValue;
            for (var i = 0; i < faces.Count; i++)
            {
                if (faces[i].Length != 3 || Array.Exists(faces[i], corner => meshOf[corner] != meshOf[stray]))
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

        /// <summary>
        /// Decides which nodes the export re-authors through the raw goal paint - see
        /// <see cref="RawGoalPaintNodes"/>. A node whose original compiled on the raw integrator keeps it
        /// only where the sheet carries that node, so the answer is dropped whole when re-authoring the
        /// remaining raw nodes as goal-damped ones would flip whether the dynamic band holds both kinds:
        /// that is what decides whether the compiler writes <c>m_GoalDampedSpringIntegrators</c> at all.
        /// </summary>
        bool[] BuildRawGoalPaintNodes()
        {
            var raw = new bool[NodeCount];
            var any = false;
            for (var node = 0; node < NodeCount; node++)
            {
                raw[node] = !UsesGoalDampedIntegrator(node);
                any |= raw[node];
            }

            if (!any)
            {
                return [];
            }

            var wasGoal = false;
            var wasRaw = false;
            var staysGoal = false;
            var staysRaw = false;
            for (var node = StaticNodeCount; node < NodeCount; node++)
            {
                var integrator = GetIntegrator(node);
                if (!raw[node])
                {
                    wasGoal |= integrator.ForceAttraction > 0f;
                    staysGoal |= integrator.ForceAttraction > 0f;
                }
                else if (integrator.ForceAttraction != 0f || integrator.VertexAttraction != 0f)
                {
                    wasRaw = true;
                    if (IsProxyMeshNode(node))
                    {
                        staysRaw = true;
                    }
                    else
                    {
                        staysGoal |= integrator.ForceAttraction > 0f;
                    }
                }
            }

            return (staysGoal && staysRaw) == (wasGoal && wasRaw) ? raw : [];
        }

        // Per-node cloth paint values recovered from the FeModel solver data (goal attraction, damping,
        // collision/friction/drag, gravity, and skin influences), shared by every proxy-mesh
        // reconstruction path - quad/tri-driven (BuildProxyMesh) and rod-only (BuildProxyMeshFromNodeSet).
        readonly record struct ProxyVertexData(
            bool IsSim,
            float GoalStrength,
            float GoalDamping,
            float AnimationForceAttract,
            float AnimationAttract,
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
            // the recovered quantities are clamped into that range - the editor shows a blank/0 slider
            // for any out-of-range value.
            var integrator = GetIntegrator(node);

            // The compiler CUBES the painted goal strength: flAnimationForceAttraction =
            // (cloth_goal_strength_v2)^3. Paint the cube ROOT of the recovered force attraction so the
            // compiler's cubing reproduces the ORIGINAL attraction instead of one that is too weak by a
            // cube. cbrt of a 0..1 value stays in 0..1.
            //
            // goal_damping drives flAnimationVertexAttraction (va) through an exponential-saturation curve
            // that asymptotes to 1.0, so this pair cannot express the va > 1 some legacy models ship -
            // GoalDampingFromAttraction returns its 1.0 maximum there, saturating va short of the original.
            //
            // A node the original compiled on the RAW integrator is painted through the raw channels
            // instead: they carry flAnimationForceAttraction/flAnimationVertexAttraction verbatim at 1/30
            // scale, reaching the values above 1.0 the goal pair cannot, and a vertex the raw paint reaches
            // keeps the raw integrator while the rest of the sheet stays goal-damped.
            var rawGoal = node < RawGoalPaintNodes.Length && RawGoalPaintNodes[node];
            var goalStrength = rawGoal ? 0f : GoalStrengthFromAttraction(integrator.ForceAttraction);
            var goalDamping = rawGoal ? 0f : GoalDampingFromAttraction(integrator.ForceAttraction, integrator.VertexAttraction);
            var animationForceAttract = rawGoal ? integrator.ForceAttraction / ClothRawGoalScale : 0f;
            var animationAttract = rawGoal ? integrator.VertexAttraction / ClothRawGoalScale : 0f;

            var collisionRadius = GetCollisionRadius(node);

            // m_DynNodeFriction is indexed by dynamic node, like m_NodeCollisionRadii.
            var friction = Math.Clamp(DynamicNodeValue(nodeFriction, node), 0f, 1f);

            // The cloth_drag paint compiles to flPointDamping = paint * 30, so the paint is recovered as
            // pd/30. This velocity damping is what keeps the original cloth calm - a 0 paint leaves the
            // sheet swinging undamped.
            var drag = Math.Clamp(integrator.PointDamping / ClothDragPointDampingScale, 0f, 1f);

            // Per-vertex gravity: the cloth_gravity$0 paint compiles into flGravity VERBATIM, with no
            // 360 scale, unlike the gravity_z KV field on ClothNode/ClothChain joints. Without the
            // stream the compiler defaults every vertex to 360, silently discarding authored per-vertex
            // variation.
            var gravity = integrator.Gravity;

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

            return new ProxyVertexData(isSim, goalStrength, goalDamping, animationForceAttract, animationAttract,
                collisionRadius, friction, drag, gravity, vertexAttraction, groundCollision, groundFriction, skinInfluences);
        }

        // Extracts the mesh index the compiler already encodes in an auto-generated proxy control-node
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
        // (full-weight) replicas of real constraints, stiffening the recompiled cloth. Restoring the
        // authored order reproduces the original packing and makes the recompile reassign identical
        // "$cloth_mXpY" names.
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
        /// <see cref="Tris"/> of their own. A <c>ClothProxyMeshFile</c> import compiles down to a bare
        /// distance-constraint (<c>m_Rods</c>) network, discarding the authored
        /// surface entirely, so these nodes are otherwise silently dropped instead of
        /// round-tripping as a sheet. Grouped by the "$cloth_m&lt;N&gt;p&lt;M&gt;" mesh index the compiler
        /// already encodes in the node name - one island per otherwise-uncovered index - with faces
        /// synthesised by 2D triangulation: the compiler re-derives its own rod network from whatever
        /// surface is imported anyway (same discarding behaviour), so an approximate triangulation is
        /// enough to recover working physics instead of the exact original faces.
        ///
        /// Coverage is checked per NODE, not per mesh index: a single authored proxy DMX can contain both
        /// a small quad/tri-covered patch AND many more vertices connected only by rods. Skipping by mesh
        /// index there would drop the rods-only vertices just because some siblings already got a
        /// face-based island - the two groups end up as separate exported proxy files instead of one, but
        /// every node's physics data still round-trips instead of being silently lost.
        ///
        /// (A ClothNode/ClothSpring reconstruction reproduces m_Rods byte-exact for the constraint data
        /// itself, but ClothNode always creates an independent new goal-attraction point; it cannot
        /// back-solve an EXISTING named bone the way ClothProxyMeshFile's back_solve_joints does. Bone-chain
        /// cloth whose render mesh is skinned to real bones rather than any node ClothNode could create
        /// needs exactly that back-solve, so the mesh-import path stays the only route there despite the
        /// topology being approximate.)
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
            // span several proxy-node name prefixes, all wired into one sheet by rods - so grouping by name
            // would split a connected panel across multiple proxy meshes and orphan every rod that crosses
            // the split ("Cannot find node $cloth_mXpY", a hard compile failure). Union-find over rods
            // whose BOTH endpoints are uncovered proxy vertices yields exactly the original's per-panel
            // meshes: unconnected panels with no rods joining them stay separate, while a single-mesh
            // island is one component.
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
            // rod to the rest - grouping by rod connectivity alone would strand it in a <3-vertex singleton
            // and drop it, losing a node.
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
            var animationForceAttract = new float[count];
            var animationAttract = new float[count];
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
                animationForceAttract[i] = vertex.AnimationForceAttract;
                animationAttract[i] = vertex.AnimationAttract;
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

            var faces = TakeAuthoredFaces(localOf, nodeIndices, out var truncatedTail);
            var usesAuthoredFaces = faces.Count > 0;
            if (!usesAuthoredFaces)
            {
                faces = TriangulateDominantPlane(positions);
                EnsureAllVerticesFaced(positions, faces);
            }

            // A corner the compiler truncated out of its element registers only through the simulated
            // path, whatever the mass it ended up with: the original painted it enabled and it came back
            // massless because no element was left to weigh it (see AppendTruncatedCorners).
            foreach (var node in usesAuthoredFaces ? truncatedTail : [])
            {
                clothEnable[localOf[node]] = 1f;
            }

            if (faces.Count == 0)
            {
                return null;
            }

            DeclareFacesInStaticNodeOrder(faces, faces.Count, nodeIndices);

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
                AnimationForceAttract = animationForceAttract,
                AnimationAttract = animationAttract,
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
        List<int[]> TakeAuthoredFaces(Dictionary<int, int> localOf, List<int> nodeIndices,
            out List<int> truncatedTail)
        {
            truncatedTail = [];
            var faces = new List<int[]>();
            var triangleElements = SourceTriangleElementCount;
            var triangles = 0;
            for (var i = 0; i < SourceFaces.Length; i++)
            {
                var face = SourceFaces[i];
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
                    if (i < triangleElements)
                    {
                        triangles++;
                    }
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

            if (!AppendTruncatedCorners(faces, nodeIndices, covered, shipped, truncatedTail))
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
            MergeFacesInNodeCreationOrder(faces, triangles);
            return [.. faces.Select(face => face.Select(corner => localOf[corner]).ToArray())];
        }

        /// <summary>
        /// Interleaves the quad run and the triangle run of a recovered surface the way the authored sheet
        /// declared them.
        /// </summary>
        /// <remarks>
        /// <c>m_SourceElems</c> groups its elements by corner count, so the recovered list is every
        /// triangle and then every quad while the authored one interleaves the two; each run is already in
        /// the authored order, which makes the interleaving a merge rather than a sort. The builder numbers
        /// a simulated node when a face corner first names it and lays the dynamic block out by
        /// <c>(rank, creation index)</c>, so the merge takes at each step the run whose head introduces the
        /// nodes the compiled node array numbers next. A head that introduces nothing settles no question
        /// and the quad run goes first, as it does when neither head fits.
        /// <para>
        /// The corners of one face are checked as a SET per rank: which of them is named first is decided
        /// afterwards by <see cref="DeclareFacesInStaticNodeOrder"/>, not here.
        /// </para>
        /// </remarks>
        void MergeFacesInNodeCreationOrder(List<int[]> faces, int triangleCount)
        {
            var quadCount = faces.Count - triangleCount;
            if (quadCount <= 0 || triangleCount <= 0)
            {
                return;
            }

            var rank = SurfaceNodeRanks;
            var pending = new Dictionary<int, List<int>>();
            var covered = new SortedSet<int>();
            foreach (var face in faces)
            {
                foreach (var corner in face)
                {
                    if (corner >= StaticNodeCount && corner < rank.Length)
                    {
                        covered.Add(corner);
                    }
                }
            }

            foreach (var node in covered)
            {
                (pending.TryGetValue(rank[node], out var run) ? run : pending[rank[node]] = []).Add(node);
            }

            var walked = new Dictionary<int, List<int>>(pending.Count);
            foreach (var (key, run) in pending)
            {
                walked[key] = [.. run];
            }

            var seen = new HashSet<int>();
            var consistent = true;
            foreach (var face in faces)
            {
                var fresh = new List<int>(4);
                foreach (var corner in face)
                {
                    if (corner >= StaticNodeCount && corner < rank.Length
                        && !seen.Contains(corner) && !fresh.Contains(corner))
                    {
                        fresh.Add(corner);
                    }
                }

                foreach (var group in fresh.GroupBy(node => rank[node]))
                {
                    var wanted = group.ToHashSet();
                    var run = walked[group.Key];
                    if (run.Count < wanted.Count || !run.Take(wanted.Count).ToHashSet().SetEquals(wanted))
                    {
                        consistent = false;
                    }
                }

                foreach (var corner in fresh)
                {
                    seen.Add(corner);
                    walked[rank[corner]].Remove(corner);
                }

                if (!consistent)
                {
                    break;
                }
            }

            if (consistent)
            {
                return;
            }

            var created = new HashSet<int>();
            var merged = new List<int[]>(faces.Count);
            var quad = 0;
            var triangle = quadCount;

            List<int> Introduced(int face)
            {
                var fresh = new List<int>(4);
                foreach (var corner in faces[face])
                {
                    if (corner >= StaticNodeCount && corner < rank.Length
                        && !created.Contains(corner) && !fresh.Contains(corner))
                    {
                        fresh.Add(corner);
                    }
                }

                return fresh;
            }

            bool IsNext(List<int> fresh)
            {
                foreach (var group in fresh.GroupBy(node => rank[node]))
                {
                    if (!pending.TryGetValue(group.Key, out var run))
                    {
                        return false;
                    }

                    var wanted = group.ToHashSet();
                    if (run.Count < wanted.Count || !run.Take(wanted.Count).ToHashSet().SetEquals(wanted))
                    {
                        return false;
                    }
                }

                return true;
            }

            while (quad < quadCount || triangle < faces.Count)
            {
                var heads = quad < quadCount
                    ? (triangle < faces.Count ? (int[])[0, 1] : [0])
                    : (int[])[1];
                int Face(int run) => run == 0 ? quad : triangle;

                var taken = -1;
                foreach (var head in heads)
                {
                    var fresh = Introduced(Face(head));
                    if (fresh.Count > 0 && IsNext(fresh))
                    {
                        taken = head;
                        break;
                    }
                }

                if (taken < 0)
                {
                    foreach (var head in heads)
                    {
                        if (Introduced(Face(head)).Count == 0)
                        {
                            taken = head;
                            break;
                        }
                    }
                }

                taken = taken < 0 ? heads[0] : taken;
                var take = Face(taken);
                foreach (var corner in Introduced(take))
                {
                    created.Add(corner);
                    pending[rank[corner]].Remove(corner);
                }

                merged.Add(faces[take]);
                if (taken == 0)
                {
                    quad++;
                }
                else
                {
                    triangle++;
                }
            }

            faces.Clear();
            faces.AddRange(merged);
        }

        /// <summary>
        /// Each node's BFS layer from the static set over the surface, which is the <c>nRank</c> the builder
        /// lays the dynamic node block out by.
        /// </summary>
        /// <remarks>
        /// The walk crosses the solve elements and the source elements, springs included, and NOT
        /// <c>m_Rods</c>: the compiler builds most of those after it has already assigned the ranks, so a
        /// rod edge shortens a path it never had. A node the static set cannot reach ranks after every one
        /// it can.
        /// </remarks>
        int[] SurfaceNodeRanks => surfaceNodeRanks ??= BuildSurfaceNodeRanks();
        int[]? surfaceNodeRanks;

        int[] BuildSurfaceNodeRanks()
        {
            var count = CtrlNames.Length;
            var neighbours = new HashSet<int>[count];

            void Link(int a, int b)
            {
                if (a == b || a < 0 || b < 0 || a >= count || b >= count)
                {
                    return;
                }

                (neighbours[a] ??= []).Add(b);
                (neighbours[b] ??= []).Add(a);
            }

            void LinkFace(int[] face)
            {
                for (var i = 0; i < face.Length; i++)
                {
                    for (var j = i + 1; j < face.Length; j++)
                    {
                        Link(face[i], face[j]);
                    }
                }
            }

            foreach (var quad in Quads)
            {
                LinkFace(quad);
            }

            foreach (var tri in Tris)
            {
                LinkFace(tri);
            }

            foreach (var face in SourceFaces)
            {
                LinkFace(face);
            }

            foreach (var (a, b) in SourceSprings)
            {
                Link(a, b);
            }

            var rank = new int[count];
            Array.Fill(rank, int.MaxValue);
            var queue = new Queue<int>();
            for (var node = 0; node < StaticNodeCount && node < count; node++)
            {
                rank[node] = 0;
                queue.Enqueue(node);
            }

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var next in neighbours[node] ?? [])
                {
                    if (rank[next] == int.MaxValue)
                    {
                        rank[next] = rank[node] + 1;
                        queue.Enqueue(next);
                    }
                }
            }

            return rank;
        }

        // How many of SourceFaces came from m_SourceElems' arity-3 block. That array holds four leading
        // counts, one per arity, and then the elements grouped by arity, so the block boundary is where the
        // recovered surface splits into its two runs. An element whose corners repeat is not recovered.
        int SourceTriangleElementCount
        {
            get
            {
                var elems = Data.GetIntegerArray("m_SourceElems");
                if (elems.Length < 4)
                {
                    return 0;
                }

                var read = 4 + (int)elems[0] + 2 * (int)elems[1];
                var recovered = 0;
                for (var i = 0; i < (int)elems[2] && read + 2 < elems.Length; i++, read += 3)
                {
                    if (elems[read] != elems[read + 1] && elems[read] != elems[read + 2]
                        && elems[read + 1] != elems[read + 2])
                    {
                        recovered++;
                    }
                }

                return recovered;
            }
        }

        /// <summary>
        /// Puts the island's remaining unfaced vertices back on a face by appending them past a quad's
        /// fourth corner, returning false when that cannot account for all of them and the authored
        /// surface has to be given up.
        /// <para>
        /// <c>m_SourceElems</c> holds at most four corners per element, so the compiler truncates a
        /// bigger authored polygon there while still registering every corner as a node - which is the
        /// only way a proxy vertex reaches the compiled file with no element naming it and no rod of its
        /// own. A polygon larger than a quad has its recorded element hold the first four corners, and the
        /// rest are exactly the vertices nothing else mentions. Appending a corner past the fourth leaves
        /// the recorded element and the rods derived from it untouched, so the vertex costs nothing to
        /// re-register. A leftover vertex that carries a rod of its own was not truncated away and still
        /// rejects the surface, as does one with no quad to append to.
        /// </para>
        /// </summary>
        bool AppendTruncatedCorners(List<int[]> faces, List<int> nodeIndices, HashSet<int> covered,
            HashSet<(int, int)> shipped, List<int> truncatedTail)
        {
            var unfaced = nodeIndices.FindAll(node => !covered.Contains(node));
            if (unfaced.Count == 0)
            {
                return true;
            }

            var roddedNodes = new HashSet<int>();
            foreach (var (a, b) in shipped)
            {
                roddedNodes.Add(a);
                roddedNodes.Add(b);
            }

            var quads = faces.FindAll(static face => face.Length == 4);
            if (quads.Count == 0)
            {
                return false;
            }

            var centre = quads.ConvertAll(face =>
            {
                var sum = Vector3.Zero;
                foreach (var corner in face)
                {
                    sum += InitPosePositions[corner];
                }

                return sum / face.Length;
            });

            var appended = new Dictionary<int[], List<int>>();
            foreach (var node in unfaced)
            {
                if (roddedNodes.Contains(node) || node >= InitPosePositions.Length)
                {
                    return false;
                }

                var nearest = 0;
                for (var i = 1; i < quads.Count; i++)
                {
                    if (Vector3.DistanceSquared(InitPosePositions[node], centre[i])
                        < Vector3.DistanceSquared(InitPosePositions[node], centre[nearest]))
                    {
                        nearest = i;
                    }
                }

                (appended.TryGetValue(quads[nearest], out var extra) ? extra : appended[quads[nearest]] = [])
                    .Add(node);
                truncatedTail.Add(node);
            }

            for (var i = 0; i < faces.Count; i++)
            {
                if (appended.TryGetValue(faces[i], out var extra))
                {
                    faces[i] = [.. faces[i], .. extra];
                }
            }

            return true;
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
        // sparser cover is not an option even where the ORIGINAL's own compiled m_Tris/m_Quads for a proxy
        // island add up to far fewer elements than its vertex count: the author's source DMX carries a much
        // denser authored topology than its OWN compiled output, which the importer collapses into m_Rods -
        // not reconstructable by feeding a sparser face set. A full triangulation (this function) is what
        // gets every vertex registered, even though the resulting m_Quads/m_Tris don't match the original
        // (the compiler's own quad-vs-rod-collapse heuristic isn't reverse-engineered here).
        //
        // A sparser fan or set-cover instead of the full triangulation either leaves vertices unfaced (they
        // fail to register: "Cannot find Fx Bone") or needs a per-model minimum-degree that isn't universal.
        // A high cloth_make_rods paint also makes the compiler match m_Quads/m_Tris exactly, but its
        // auto-derived rods then STACK with AddClothProxySprings' own exact m_Rods, over-constraining the
        // sheet. Correct per-edge rod topology matters more for simulated behaviour than the compiled
        // quad/tri surface count, so the full Delaunay output is kept.
        //
        // The compiler only registers a proxy vertex as an FeModel control node if it is referenced by at
        // least one face (see the TriangulateDominantPlane remarks). A Delaunay triangulation of a curved
        // or near-collinear rod-only island can still leave boundary vertices - or vertices that overlap
        // once projected to the dominant plane - out of every face. Those vertices then can't be targeted by
        // their m_Rods' ClothSprings ("Cannot find node"), so the whole rod-only island's cloth is lost.
        // Attach each still-unfaced vertex to its two nearest non-collinear neighbours, guaranteeing every
        // vertex registers and its rods survive. This is purely ADDITIVE: a fully-triangulated island has no
        // unfaced vertices, so no triangle is added and its compiled output stays byte-exact.
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
        // importer prune behaviours:
        //   (1) ISOLATED-PINNED: a pinned (cloth_enable == 0) vertex whose face-neighbours are ALL pinned has
        //       no simulated neighbour, so it is a fully-static mesh region the solver has no use for and
        //       discards.
        //   (2) NEAR-COINCIDENT WELD: two vertices much closer than the island's typical edge length get
        //       welded into one by the importer, dropping the duplicate.
        // Either signal marks the whole island as drop-risk; the caller then omits its explicit rods and lets
        // the compiler auto-derive them (always compiles). Convex, uniformly-spaced islands trip neither and
        // keep their exact reconstructed rods. Both signals are scale-relative / topological.
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
