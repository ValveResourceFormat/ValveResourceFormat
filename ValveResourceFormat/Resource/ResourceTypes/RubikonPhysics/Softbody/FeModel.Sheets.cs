using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// A cloth proxy mesh (sheet) reconstructed from the FeModel surface, with its per-vertex paint.
        /// </summary>
        public sealed class ProxyMesh
        {
            /// <summary>Gets the original FeModel control-node index of each proxy vertex.</summary>
            public required int[] NodeIndices { get; init; }
            /// <summary>Gets the model-space rest position of each proxy vertex.</summary>
            public required Vector3[] Positions { get; init; }
            /// <summary>Gets the per-vertex <c>cloth_enable</c> flag (1 = simulated, 0 = pinned anchor).</summary>
            public required float[] ClothEnable { get; init; }
            /// <summary>Gets the per-vertex <c>cloth_goal_strength_v2</c> paint.</summary>
            public required float[] GoalStrength { get; init; }
            /// <summary>Gets the per-vertex <c>cloth_goal_damping</c> paint.</summary>
            public required float[] GoalDamping { get; init; }
            /// <summary>
            /// Gets the per-vertex <c>cloth_animation_force_attract</c> paint for the <see cref="RawGoalPaintNodes"/>, or empty.
            /// </summary>
            public float[] AnimationForceAttract { get; init; } = [];
            /// <summary>Gets the per-vertex <c>cloth_animation_attract</c> paint, alongside <see cref="AnimationForceAttract"/>.</summary>
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
            /// <summary>Gets the per-vertex <c>cloth_gravity</c> paint (the integrator's <c>flGravity</c>).</summary>
            public required float[] Gravity { get; init; }
            /// <summary>Gets the compiled <c>flAnimationVertexAttraction</c> of each vertex.</summary>
            public required float[] VertexAttraction { get; init; }
            /// <summary>Gets the skeleton bone influences of each vertex.</summary>
            public required (string Bone, float Weight)[][] SkinInfluences { get; init; }
            /// <summary>Gets the faces as proxy-vertex index quads and triangles.</summary>
            public required List<int[]> Faces { get; init; }
            /// <summary>Gets the named vertex selections covering this sheet, as a membership weight per vertex.</summary>
            public (string Name, float[] Weights)[] VertexMaps { get; init; } = [];
            /// <summary>
            /// Gets the per-vertex <c>cloth_make_rods</c> paint: 1 where every element of the vertex was built into rods.
            /// Empty when the sheet has no such region.
            /// </summary>
            public float[] RodsDriven { get; init; } = [];
            /// <summary>Gets the number of simulated (cloth_enable == 1) vertices.</summary>
            public int SimulatedCount { get; init; }
            /// <summary>Gets the number of pinned (cloth_enable == 0) vertices.</summary>
            public int PinnedCount { get; init; }
            /// <summary>
            /// Gets whether the importer is expected to drop a vertex of this synthesised island (see <see cref="ComputeDropRisk"/>).
            /// </summary>
            public bool IsDropRisk { get; init; }
            /// <summary>Gets whether <see cref="Faces"/> came from the authored <c>m_SourceElems</c> rather than a triangulation.</summary>
            public bool UsesAuthoredFaces { get; init; }
            /// <summary>
            /// Gets whether no vertex of this sheet is driven by a real skeleton bone.
            /// </summary>
            public bool IsFreeFloating { get; init; }
        }

        /// <summary>
        /// Fills the gaps in a proxy's <c>$cloth_m{N}p{SLOT}</c> numbering with pinned, unfaced copies of the nearest
        /// preceding vertex, so every vertex sits at its original slot.
        /// </summary>
        private ProxyMesh PadToAuthoredSlots(ProxyMesh mesh)
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

            var clothEnable = GatherSlots(mesh.ClothEnable, srcOf);
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

            return GatherProxyMesh(mesh, srcOf, clothEnable,
                [.. mesh.Faces.Select(f => f.Select(v => localToSlot[v]).ToArray())],
                mesh.SimulatedCount, mesh.PinnedCount + (total - n), mesh.IsDropRisk, mesh.IsFreeFloating);
        }

        private static T[] GatherSlots<T>(T[] source, int[] sourceOf) => [.. sourceOf.Select(index => source[index])];

        /// <summary>
        /// Builds a proxy mesh whose vertex <c>i</c> copies vertex <c>sourceOf[i]</c> of <paramref name="source"/>.
        /// </summary>
        private static ProxyMesh GatherProxyMesh(ProxyMesh source, int[] sourceOf, float[] clothEnable, List<int[]> faces,
            int simulatedCount, int pinnedCount, bool isDropRisk, bool isFreeFloating)
            => new()
            {
                NodeIndices = GatherSlots(source.NodeIndices, sourceOf),
                Positions = GatherSlots(source.Positions, sourceOf),
                ClothEnable = clothEnable,
                GoalStrength = GatherSlots(source.GoalStrength, sourceOf),
                GoalDamping = GatherSlots(source.GoalDamping, sourceOf),
                AnimationForceAttract = GatherSlots(source.AnimationForceAttract, sourceOf),
                AnimationAttract = GatherSlots(source.AnimationAttract, sourceOf),
                CollisionRadius = GatherSlots(source.CollisionRadius, sourceOf),
                Friction = GatherSlots(source.Friction, sourceOf),
                Drag = GatherSlots(source.Drag, sourceOf),
                GroundCollision = GatherSlots(source.GroundCollision, sourceOf),
                GroundFriction = GatherSlots(source.GroundFriction, sourceOf),
                Gravity = GatherSlots(source.Gravity, sourceOf),
                VertexAttraction = GatherSlots(source.VertexAttraction, sourceOf),
                SkinInfluences = GatherSlots(source.SkinInfluences, sourceOf),
                VertexMaps = [.. source.VertexMaps.Select(m => (m.Name, GatherSlots(m.Weights, sourceOf)))],
                Faces = faces,
                RodsDriven = source.RodsDriven.Length == 0 ? [] : GatherSlots(source.RodsDriven, sourceOf),
                SimulatedCount = simulatedCount,
                PinnedCount = pinnedCount,
                IsDropRisk = isDropRisk,
                UsesAuthoredFaces = source.UsesAuthoredFaces,
                IsFreeFloating = isFreeFloating,
            };

        /// <summary>
        /// Reconstructs the cloth proxy sheets, one per connected island and <c>$cloth_m&lt;N&gt;</c> index, ordered by that
        /// index. Empty when the FeModel has no sheet.
        /// </summary>
        internal List<ProxyMesh> BuildProxyMeshes()
        {
            var result = new List<ProxyMesh>();
            var coveredNodes = new HashSet<int>();
            var merged = BuildProxyMesh();

            var pending = new List<ProxyMesh>();

            if (merged is not null)
            {
                coveredNodes.UnionWith(merged.NodeIndices);

                var count = merged.NodeIndices.Length;
                var groupOf = Enumerable.Range(0, count).ToArray();
                int Find(int x) => FindRoot(groupOf, x);
                foreach (var face in merged.Faces)
                {
                    for (var i = 1; i < face.Length; i++)
                    {
                        groupOf[Find(face[0])] = Find(face[i]);
                    }
                }

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
                        var vertices = island.OrderBy(v => v).ToArray();
                        var remap = new Dictionary<int, int>(vertices.Length);
                        for (var i = 0; i < vertices.Length; i++)
                        {
                            remap[vertices[i]] = i;
                        }

                        pending.Add(GatherProxyMesh(merged, vertices, GatherSlots(merged.ClothEnable, vertices),
                            [.. merged.Faces.Where(f => remap.ContainsKey(f[0])).Select(f => f.Select(v => remap[v]).ToArray())],
                            vertices.Count(v => merged.ClothEnable[v] != 0f), vertices.Count(v => merged.ClothEnable[v] == 0f),
                            isDropRisk: false, isFreeFloating: false));
                    }
                }
            }

            var independentChains = IndependentBoneChains();
            var chainBoneNodes = independentChains.SelectMany(static c => c.Joints).Select(static j => j.Node).ToHashSet();
            if (chainBoneNodes.Count > 0)
            {
                for (var node = 0; node < CtrlNames.Length; node++)
                {
                    if (!IsProxyNodeName(CtrlNames[node]) || ParseProxyMeshIndex(CtrlNames[node]) >= 0)
                    {
                        continue;
                    }

                    var parent = ParentNodeOf(node);
                    if (parent >= 0 && chainBoneNodes.Contains(parent))
                    {
                        coveredNodes.Add(node);
                    }
                }
            }

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

            result.AddRange(pending
                .OrderBy(p => { var m = ProxyMeshOriginalIndex(p); return m >= 0 ? m : int.MaxValue; })
                .ThenBy(p => p.NodeIndices.Length == 0 ? int.MaxValue : p.NodeIndices.Min())
                .Select(PadToAuthoredSlots));

            return result;
        }

        /// <summary>Gets the smallest <c>$cloth_m&lt;N&gt;</c> index among the mesh's nodes, or -1.</summary>
        /// <summary>Finds the root of <paramref name="x"/> in a union-find forest, halving the path on the way.</summary>
        private static int FindRoot(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                x = parent[x] = parent[parent[x]];
            }

            return x;
        }

        private int ProxyMeshOriginalIndex(ProxyMesh mesh) => mesh.NodeIndices
            .Select(node => ParseProxyMeshIndex(CtrlNames[node]))
            .Where(m => m >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        /// <summary>Merges two meshes of one <c>$cloth_m&lt;N&gt;</c> index into one, in authored vertex order.</summary>
        private ProxyMesh MergeSameIndexProxyMeshes(ProxyMesh a, ProxyMesh b)
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
        /// Gets the bone chains exported as a standalone <c>ClothChain</c>: no joint is back-solved by a proxy sheet and the
        /// chain is not sheet-driven.
        /// </summary>
        private List<BoneChain> IndependentBoneChains()
            => [.. BuildBoneChains()
                .Where(chain => !chain.Joints.Any(joint => ProxyFitMatrixNodes.Contains(joint.Node))
                    && !IsSheetDrivenChain(chain))];

        /// <summary>
        /// Gets whether a proxy sheet drives this chain's bones: every dynamic joint is position-driven, a <c>$cloth_m</c>
        /// vertex hangs off one of them, and no <c>$cc</c> ring does.
        /// </summary>
        internal bool IsSheetDrivenChain(BoneChain chain)
        {
            var anyPositionDriven = false;
            foreach (var joint in chain.Joints)
            {
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

                if (generatedRing)
                {
                    return false;
                }

                sheetDriven = true;
            }

            return sheetDriven;
        }

        /// <summary>
        /// The control nodes of every bone chain the export emits as a standalone <c>ClothChain</c>.
        /// </summary>
        private HashSet<int> IndependentChainJointNodes()
            => [.. IndependentBoneChains().SelectMany(static chain => chain.Joints)
                .Select(static joint => joint.Node)];

        /// <summary>Gets whether every corner of a face is a joint of an independent chain.</summary>
        private static bool IsChainJointFace(int[] face, HashSet<int> chainJoints)
            => face.Length >= 3 && chainJoints.Count > 0
            && Array.TrueForAll(face, chainJoints.Contains);

        /// <summary>
        /// Gets whether a face comes from a <c>ClothTri</c> or <c>ClothQuad</c> declaration rather than a proxy sheet.
        /// </summary>
        private bool IsAuthoredElementFace(int[] face)
        {
            if (face.Length < 3)
            {
                return false;
            }

            var free = false;
            foreach (var corner in face)
            {
                if (corner < 0 || corner >= CtrlNames.Length)
                {
                    return false;
                }

                var name = CtrlNames[corner];
                if (name.StartsWith(FreeClothNodePrefix, StringComparison.Ordinal))
                {
                    free = true;
                }
                else if (IsProxyNodeName(name))
                {
                    return false;
                }
            }

            return free || !HasProxyMeshNodes;
        }

        /// <summary>
        /// Gets the compiled surface faces the original built from a <c>ClothTri</c> or <c>ClothQuad</c>
        /// element over already-declared cloth nodes, quads before triangles. Corners are control-node
        /// indices in the compiled cycle order.
        /// </summary>
        internal List<int[]> GetAuthoredElementFaces()
        {
            var faces = new List<int[]>();
            foreach (var face in Quads)
            {
                if (!IsHingeFanFace(face) && IsAuthoredElementFace(face))
                {
                    faces.Add(face);
                }
            }

            foreach (var face in Tris)
            {
                if (!IsHingeFanFace(face) && IsAuthoredElementFace(face))
                {
                    faces.Add(face);
                }
            }

            return faces;
        }

        /// <summary>
        /// Reconstructs the cloth proxy mesh (sheet) from the FeModel surface arrays as ONE merged mesh.
        /// Returns null when the FeModel has no surface (no quads/tris) - e.g. a pure bone-chain cloth
        /// that only needs ClothChain.
        /// </summary>
        internal ProxyMesh? BuildProxyMesh()
        {
            if ((Quads.Length == 0 && Tris.Length == 0) || InitPosePositions.Length == 0)
            {
                return null;
            }

            var chainJoints = IndependentChainJointNodes();
            var referenced = new SortedSet<int>();
            void Collect(int[][] faces)
            {
                foreach (var face in faces)
                {
                    if (IsHingeFanFace(face) || IsAuthoredElementFace(face)
                        || IsChainJointFace(face, chainJoints))
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

            var vertices = ComputeProxyVertexArrays(nodeIndices);
            var positions = vertices.Positions;

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

            var declared = RotateQuadsToShippedMasses(
                ChooseFaceDeclarationOrder(faces, surfaceFaceCount, nodeIndices),
                surfaceFaceCount, nodeIndices, InitPosePositions, NodeInvMasses);
            faces.Clear();
            faces.AddRange(declared);

            for (var i = 0; i < nodeIndices.Length; i++)
            {
                rodsDriven[i] = surfaceNodes.Contains(nodeIndices[i]) ? 0f : 1f;
            }

            return AssembleProxyMesh(vertices, nodeIndices, faces, rodsFaces.Count > 0 ? rodsDriven : [],
                usesAuthoredFaces: rodsFaces.Count > 0, isDropRisk: false, isFreeFloating: false);
        }

        /// <summary>
        /// Makes an uncovered sheet vertex the fourth corner of the nearest same-mesh triangle, diagonal to the corner it
        /// is farthest from.
        /// </summary>
        private static void AttachStrayToTriangle(int stray, Vector3[] positions, List<int[]> faces, int[] meshOf)
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

            var start = (diagonal + 2) % 3;
            faces[best] = [triangle[start], triangle[(start + 1) % 3], triangle[(start + 2) % 3], stray];
        }

        /// <summary>The per-node paint values of one proxy vertex.</summary>
        /// <summary>The per-vertex arrays of a proxy mesh under construction.</summary>
        private sealed class ProxyVertexArrays(int count)
        {
            public Vector3[] Positions { get; } = new Vector3[count];
            public float[] ClothEnable { get; } = new float[count];
            public float[] GoalStrength { get; } = new float[count];
            public float[] GoalDamping { get; } = new float[count];
            public float[] AnimationForceAttract { get; } = new float[count];
            public float[] AnimationAttract { get; } = new float[count];
            public float[] CollisionRadius { get; } = new float[count];
            public float[] Friction { get; } = new float[count];
            public float[] Drag { get; } = new float[count];
            public float[] GroundCollision { get; } = new float[count];
            public float[] GroundFriction { get; } = new float[count];
            public float[] Gravity { get; } = new float[count];
            public float[] VertexAttraction { get; } = new float[count];
            public (string Bone, float Weight)[][] SkinInfluences { get; } = new (string Bone, float Weight)[count][];
            public int Simulated { get; set; }
            public int Pinned { get; set; }
        }

        /// <summary>Fills the per-vertex arrays of the given nodes from <see cref="ComputeProxyVertexData"/>.</summary>
        private ProxyVertexArrays ComputeProxyVertexArrays(IReadOnlyList<int> nodeIndices)
        {
            var arrays = new ProxyVertexArrays(nodeIndices.Count);
            for (var i = 0; i < nodeIndices.Count; i++)
            {
                var node = nodeIndices[i];
                arrays.Positions[i] = InitPosePositions[node];

                var vertex = ComputeProxyVertexData(node);
                arrays.ClothEnable[i] = vertex.IsSim ? 1f : 0f;
                if (vertex.IsSim)
                {
                    arrays.Simulated++;
                }
                else
                {
                    arrays.Pinned++;
                }

                arrays.SkinInfluences[i] = vertex.SkinInfluences;
                arrays.GoalStrength[i] = vertex.GoalStrength;
                arrays.GoalDamping[i] = vertex.GoalDamping;
                arrays.AnimationForceAttract[i] = vertex.AnimationForceAttract;
                arrays.AnimationAttract[i] = vertex.AnimationAttract;
                arrays.CollisionRadius[i] = vertex.CollisionRadius;
                arrays.Friction[i] = vertex.Friction;
                arrays.Drag[i] = vertex.Drag;
                arrays.GroundCollision[i] = vertex.GroundCollision;
                arrays.GroundFriction[i] = vertex.GroundFriction;
                arrays.Gravity[i] = vertex.Gravity;
                arrays.VertexAttraction[i] = vertex.VertexAttraction;
            }

            return arrays;
        }

        private ProxyMesh AssembleProxyMesh(ProxyVertexArrays vertices, int[] nodeIndices, List<int[]> faces, float[] rodsDriven,
            bool usesAuthoredFaces, bool isDropRisk, bool isFreeFloating)
            => new()
            {
                NodeIndices = nodeIndices,
                Positions = vertices.Positions,
                ClothEnable = vertices.ClothEnable,
                GoalStrength = vertices.GoalStrength,
                GoalDamping = vertices.GoalDamping,
                AnimationForceAttract = vertices.AnimationForceAttract,
                AnimationAttract = vertices.AnimationAttract,
                CollisionRadius = vertices.CollisionRadius,
                Friction = vertices.Friction,
                Drag = vertices.Drag,
                GroundCollision = vertices.GroundCollision,
                GroundFriction = vertices.GroundFriction,
                Gravity = vertices.Gravity,
                VertexAttraction = vertices.VertexAttraction,
                SkinInfluences = vertices.SkinInfluences,
                VertexMaps = BuildVertexMapWeights(nodeIndices),
                Faces = faces,
                SimulatedCount = vertices.Simulated,
                PinnedCount = vertices.Pinned,
                RodsDriven = rodsDriven,
                IsDropRisk = isDropRisk,
                UsesAuthoredFaces = usesAuthoredFaces,
                IsFreeFloating = isFreeFloating,
            };

        private readonly record struct ProxyVertexData(
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

        private ProxyVertexData ComputeProxyVertexData(int node)
        {
            var isSim = node < NodeInvMasses.Length && NodeInvMasses[node] != 0f;

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

                if (skinInfluences.Length == 0
                    && DeferredOffsetSkinWeights.TryGetValue(node, out var offsetWeights))
                {
                    skinInfluences = offsetWeights;
                }
            }

            var integrator = GetIntegrator(node);

            var rawGoal = node < RawGoalPaintNodes.Length && RawGoalPaintNodes[node];
            var goalStrength = rawGoal ? 0f : GoalStrengthPaint(integrator.ForceAttraction);
            var goalDamping = rawGoal ? 0f : GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction);
            var animationForceAttract = rawGoal ? integrator.ForceAttraction / ClothRawGoalScale : 0f;
            var animationAttract = rawGoal ? integrator.VertexAttraction / ClothRawGoalScale : 0f;

            var collisionRadius = GetCollisionRadius(node);

            var friction = Math.Clamp(GetNodeFriction(node), 0f, 1f);

            var drag = Math.Max(integrator.PointDamping / ClothDragPointDampingScale, 0f);

            var gravity = integrator.Gravity;

            var vertexAttraction = integrator.VertexAttraction;

            var (worldFriction, groundFriction) = GetWorldFriction(node);
            var groundCollision = IsWorldCollisionNode(node) && IsProxyMeshNode(node)
                ? Math.Max(1f - worldFriction, 1e-6f)
                : 0f;

            return new ProxyVertexData(isSim, goalStrength, goalDamping, animationForceAttract, animationAttract,
                collisionRadius, friction, drag, gravity, vertexAttraction, groundCollision, groundFriction, skinInfluences);
        }

        /// <summary>Gets the mesh index of a <c>$cloth_m&lt;N&gt;p&lt;S&gt;</c> name, or -1.</summary>
        private static int ParseProxyMeshIndex(string name)
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

        /// <summary>Gets whether <paramref name="node"/> is a proxy-sheet vertex (<c>$cloth_m&lt;N&gt;p&lt;S&gt;</c>).</summary>
        internal bool IsProxyMeshNode(int node)
            => node >= 0 && node < CtrlNames.Length && ParseProxyMeshIndex(CtrlNames[node]) >= 0;

        /// <summary>Gets the vertex slot of a <c>$cloth_m&lt;N&gt;p&lt;S&gt;</c> name, or <see cref="int.MaxValue"/>.</summary>
        private static int ParseProxyVertexIndex(string name)
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

        /// <summary>
        /// Reorders faces to their SIMD lane order, each in its lane's node order; faces without a lane follow in array order.
        /// </summary>
        private int[][] OrderFacesBySimdLanes(int[][] faces, string simdKey)
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
                if (!entry.TryGetValue("nNode", out var nNodeValue) || !nNodeValue.IsArray)
                {
                    return faces;
                }

                var flat = FlattenSimdNodes(nNodeValue, rows * 4);
                if (flat.Count < rows * 4)
                {
                    return faces;
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

        /// <summary>Sorts nodes by mesh index, then vertex slot, then node index.</summary>
        /// <summary>
        /// Flattens a SIMD <c>nNode</c> block, stored either as rows of four lanes or as one row-major array.
        /// </summary>
        private static List<int> FlattenSimdNodes(KVObject nNode, int capacity)
        {
            var flat = new List<int>(capacity);
            foreach (var row in nNode.AsArraySpan())
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

            return flat;
        }

        private void SortByAuthoredVertexOrder(int[] nodeIndices)
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

        private bool SpansProxyMeshes(int[] face)
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
    }
}
