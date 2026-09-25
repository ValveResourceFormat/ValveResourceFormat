using System.Linq;
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
        /// Reconstructs the cloth proxy sheets, one per connected island and <c>$cloth_m&lt;N&gt;</c> index, ordered by that
        /// index. Empty when the FeModel has no sheet.
        /// </summary>
        public List<ProxyMesh> BuildProxyMeshes()
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
                int Find(int x) { while (groupOf[x] != x) { x = groupOf[x] = groupOf[groupOf[x]]; } return x; }
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

            var independentChains = IndependentBoneChains();
            var chainBoneNodes = independentChains.SelectMany(static c => c.Joints).Select(static j => j.Node).ToHashSet();
            if (chainBoneNodes.Count > 0)
            {
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
        int ProxyMeshOriginalIndex(ProxyMesh mesh) => mesh.NodeIndices
            .Select(node => ParseProxyMeshIndex(CtrlNames[node]))
            .Where(m => m >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        /// <summary>Merges two meshes of one <c>$cloth_m&lt;N&gt;</c> index into one, in authored vertex order.</summary>
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
        /// Gets the bone chains exported as a standalone <c>ClothChain</c>: no joint is back-solved by a proxy sheet and the
        /// chain is not sheet-driven.
        /// </summary>
        List<BoneChain> IndependentBoneChains()
            => [.. BuildBoneChains()
                .Where(chain => !chain.Joints.Any(joint => ProxyFitMatrixNodes.Contains(joint.Node))
                    && !IsSheetDrivenChain(chain))];

        /// <summary>
        /// Gets whether a proxy sheet drives this chain's bones: every dynamic joint is position-driven, a <c>$cloth_m</c>
        /// vertex hangs off one of them, and no <c>$cc</c> ring does.
        /// </summary>
        public bool IsSheetDrivenChain(BoneChain chain)
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
        HashSet<int> IndependentChainJointNodes()
            => [.. IndependentBoneChains().SelectMany(static chain => chain.Joints)
                .Select(static joint => joint.Node)];

        /// <summary>Gets whether every corner of a face is a joint of an independent chain.</summary>
        static bool IsChainJointFace(int[] face, HashSet<int> chainJoints)
            => face.Length >= 3 && chainJoints.Count > 0
            && Array.TrueForAll(face, chainJoints.Contains);

        /// <summary>
        /// Gets whether a face comes from a <c>ClothTri</c> or <c>ClothQuad</c> declaration rather than a proxy sheet.
        /// </summary>
        bool IsAuthoredElementFace(int[] face)
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
        public List<int[]> GetAuthoredElementFaces()
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
        public ProxyMesh? BuildProxyMesh()
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

                var vertex = ComputeProxyVertexData(node);
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
        /// Gets the <c>quad_bend_tolerance</c> the compiler split quads against: 0.05, unless a split quad bends by less,
        /// then the largest bend among the dynamic quads kept whole, or 0.
        /// </summary>
        public float QuadBendTolerance => quadBendTolerance ??= ComputeQuadBendTolerance();

        private float? quadBendTolerance;

        const float DefaultQuadBendTolerance = 0.05f;

        float ComputeQuadBendTolerance()
        {
            bool Dynamic(int node) => node >= 0 && node < InitPosePositions.Length && node < NodeInvMasses.Length
                && NodeInvMasses[node] != 0f;

            var rigid = new HashSet<(int, int)>();
            foreach (var rod in Rods)
            {
                if (rod.MinDist == rod.MaxDist)
                {
                    rigid.Add(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA));
                }
            }

            var inPlace = new Dictionary<(int, int), List<int[]>>();
            var lowestSplit = float.MaxValue;
            foreach (var tri in Tris)
            {
                if (tri.Length != 3)
                {
                    continue;
                }

                if (inPlace.TryGetValue((tri[0], tri[1]), out var firsts))
                {
                    foreach (var first in firsts)
                    {
                        int[] quad = [first[0], first[1], first[2], tri[2]];
                        var far = quad[1] < quad[3] ? (quad[1], quad[3]) : (quad[3], quad[1]);
                        if (quad.Distinct().Count() == 4 && Array.TrueForAll(quad, Dynamic) && rigid.Contains(far))
                        {
                            lowestSplit = Math.Min(lowestSplit, QuadBendSine(Array.ConvertAll(quad, node => InitPosePositions[node])));
                        }
                    }
                }

                if (!inPlace.TryGetValue((tri[0], tri[2]), out var halves))
                {
                    inPlace[(tri[0], tri[2])] = halves = [];
                }

                halves.Add(tri);
            }

            if (lowestSplit > DefaultQuadBendTolerance)
            {
                return DefaultQuadBendTolerance;
            }

            var highestKept = 0f;
            foreach (var quad in Quads)
            {
                if (quad.Length == 4 && quad.Distinct().Count() == 4 && Array.TrueForAll(quad, Dynamic))
                {
                    highestKept = Math.Max(highestKept, QuadBendSine(Array.ConvertAll(quad, node => InitPosePositions[node])));
                }
            }

            return highestKept < lowestSplit ? highestKept : DefaultQuadBendTolerance;
        }

        static (int, int, int) SortedTriKey(int[] tri)
        {
            var (a, b, c) = (tri[0], tri[1], tri[2]);
            if (a > b) { (a, b) = (b, a); }
            if (b > c) { (b, c) = (c, b); }
            if (a > b) { (a, b) = (b, a); }
            return (a, b, c);
        }

        /// <summary>
        /// Pairs the <see cref="Tris"/> the compiler split from bent quads back into those quads, keyed by
        /// <see cref="SortedTriKey"/>: the quad to export instead of the half that stayed in place, and the appended half to
        /// drop.
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
                        var order = PredictQuadSplit(corners, staticCorners, QuadBendTolerance);
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
        /// Gets the rods the compiler builds across the discarded diagonal of every fully dynamic quad it splits.
        /// </summary>
        internal static HashSet<(int, int)> BentQuadRodsFromFaces(IEnumerable<int[]> faces,
            Vector3[] positions, Func<int, bool> isStatic, float tolerance)
        {
            var rods = new HashSet<(int, int)>();
            foreach (var face in faces)
            {
                if (face.Length != 4 || face.Distinct().Count() != 4
                    || Array.Exists(face, node => node < 0 || node >= positions.Length || isStatic(node)))
                {
                    continue;
                }

                if (PredictQuadSplit(Array.ConvertAll(face, node => positions[node]), 0, tolerance) is not { } order)
                {
                    continue;
                }

                var (a, b) = (face[order[1]], face[order[3]]);
                rods.Add(a < b ? (a, b) : (b, a));
            }

            return rods;
        }

        /// <summary>
        /// Whether the compiler splits the quad with the given rest corners at the given
        /// <c>quad_bend_tolerance</c>, and in which corner order: the two triangles it emits are
        /// <c>(order[0], order[1], order[2])</c> and <c>(order[0], order[2], order[3])</c>. Null when the quad is
        /// kept whole.
        /// </summary>
        static int[]? PredictQuadSplit(Vector3[] corners, int staticCorners, float tolerance)
        {
            if (staticCorners != 0)
            {
                return null;
            }

            var bend = QuadBend(corners, MaximalQuadPairing(corners));
            return bend.Cross > bend.Normals * tolerance ? bend.Order : null;
        }

        /// <summary>
        /// The quad rotated onto its shorter diagonal, with the two lengths the compiler's bend test compares: the
        /// cross product of the two half normals, and the product of their lengths.
        /// </summary>
        static (int[] Order, float Cross, float Normals) QuadBend(Vector3[] corners, int[] order)
        {
            if (Vector3.Distance(corners[order[0]], corners[order[2]])
                > Vector3.Distance(corners[order[1]], corners[order[3]]))
            {
                order = [order[1], order[2], order[3], order[0]];
            }

            var (a, b, c, d) = (corners[order[0]], corners[order[1]], corners[order[2]], corners[order[3]]);
            var n1 = Vector3.Cross(b - a, c - a);
            var n2 = Vector3.Cross(d - c, a - c);
            return (order, Vector3.Cross(n1, n2).Length(), n1.Length() * n2.Length());
        }

        /// <summary>The sine of a fully dynamic quad's bend across its shorter diagonal, zero when degenerate.</summary>
        static float QuadBendSine(Vector3[] corners)
        {
            var bend = QuadBend(corners, MaximalQuadPairing(corners));
            return bend.Normals > 0f ? bend.Cross / bend.Normals : 0f;
        }

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
        /// Rotates each quad with one or two adjacent static corners into the declared corner order whose predicted bend
        /// rods best match the model's rods.
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

        /// <summary>
        /// Gets the corner orders a quad with one or two adjacent static corners can be declared in: its own, then the
        /// ones with its static corners in the middle.
        /// </summary>
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
        /// Chooses the order the surface faces are declared in so the importer creates the sheet's nodes in the order the
        /// shipped node array numbers them, and returns that order with its corners rotated.
        /// </summary>
        internal List<int[]> ChooseFaceDeclarationOrder(List<int[]> faces, int surfaceFaceCount, IReadOnlyList<int> nodeIndices)
        {
            var rotationLockedCount = RotationLockedStaticNodeCount;
            bool IsStatic(int local) => local >= 0 && local < nodeIndices.Count
                && nodeIndices[local] < NodeInvMasses.Length && NodeInvMasses[nodeIndices[local]] == 0f;
            static int[] Corners(int[] face) => face.Length > 4 ? face[..4] : face;

            bool SimulatedAscend(List<int[]> order)
            {
                var created = new List<int>();
                var seen = new HashSet<int>();
                var neighbours = new Dictionary<int, HashSet<int>>();
                foreach (var face in order)
                {
                    foreach (var local in face)
                    {
                        if (!IsStatic(local) && seen.Add(local))
                        {
                            created.Add(local);
                        }
                    }

                    if (!Array.TrueForAll(face, IsStatic))
                    {
                        foreach (var local in face)
                        {
                            if (!neighbours.TryGetValue(local, out var set))
                            {
                                neighbours[local] = set = [];
                            }

                            set.UnionWith(face);
                        }
                    }
                }

                var level = new Dictionary<int, int>();
                var queue = new Queue<int>();
                foreach (var local in neighbours.Keys.Where(IsStatic))
                {
                    level[local] = 0;
                    queue.Enqueue(local);
                }

                while (queue.Count > 0)
                {
                    var a = queue.Dequeue();
                    foreach (var b in neighbours[a])
                    {
                        if (level.TryAdd(b, level[a] + 1))
                        {
                            queue.Enqueue(b);
                        }
                    }
                }

                var last = new Dictionary<int, int>();
                foreach (var local in created)
                {
                    var rank = level.GetValueOrDefault(local, int.MaxValue);
                    if (last.TryGetValue(rank, out var previous) && nodeIndices[local] < previous)
                    {
                        return false;
                    }

                    last[rank] = nodeIndices[local];
                }

                return true;
            }

            bool PinsAscend(List<int[]> order)
            {
                var created = new HashSet<int>();
                int[] last = [-1, -1];
                foreach (var face in order)
                {
                    var corners = Corners(face);
                    if (Array.TrueForAll(corners, IsStatic))
                    {
                        continue;
                    }

                    var introduced = new List<int>();
                    foreach (var local in corners)
                    {
                        if (IsStatic(local) && created.Add(nodeIndices[local]))
                        {
                            introduced.Add(nodeIndices[local]);
                        }
                    }

                    introduced.Sort();
                    foreach (var node in introduced)
                    {
                        var group = node < rotationLockedCount ? 0 : 1;
                        if (node < last[group])
                        {
                            return false;
                        }

                        last[group] = node;
                    }
                }

                return true;
            }

            var slots = new List<int>();
            for (var i = 0; i < surfaceFaceCount && i < faces.Count; i++)
            {
                var corners = Corners(faces[i]);
                if (Array.Exists(corners, IsStatic) && !Array.TrueForAll(corners, IsStatic))
                {
                    slots.Add(i);
                }
            }

            var carriers = slots.Select(i => faces[i])
                .OrderBy(face => Corners(face).Where(IsStatic).Min(local => nodeIndices[local]))
                .ToList();
            var byLowestPin = new List<int[]>(faces);
            for (var k = 0; k < slots.Count; k++)
            {
                byLowestPin[slots[k]] = carriers[k];
            }

            var head = Math.Min(surfaceFaceCount, faces.Count);
            var byShippedNodes = faces.Take(head)
                .OrderBy(face => face.Select(local => nodeIndices[local]).Order().ToArray(), ShippedNodeComparer)
                .Concat(faces.Skip(head))
                .ToList();

            List<int[]>[] candidates = [new List<int[]>(faces), byShippedNodes, byLowestPin];
            foreach (var order in candidates)
            {
                DeclareFacesInStaticNodeOrder(order, surfaceFaceCount, nodeIndices);
            }

            return Array.Find(candidates, order => PinsAscend(order) && SimulatedAscend(order))
                ?? Array.Find([candidates[0], candidates[2], candidates[1]], PinsAscend)
                ?? candidates[0];
        }

        static readonly Comparer<int[]> ShippedNodeComparer = Comparer<int[]>.Create(static (x, y) =>
        {
            for (var i = 0; i < x.Length && i < y.Length; i++)
            {
                if (x[i] != y[i])
                {
                    return x[i].CompareTo(y[i]);
                }
            }

            return x.Length.CompareTo(y.Length);
        });

        /// <summary>
        /// Rotates fully dynamic surface quads one corner back where that makes the compiler's mass pass reproduce the
        /// shipped inverse masses bit for bit, and returns the faces with those rotations.
        /// </summary>
        internal static List<int[]> RotateQuadsToShippedMasses(List<int[]> faces, int surfaceFaceCount,
            IReadOnlyList<int> nodeIndices, IReadOnlyList<Vector3> positions, IReadOnlyList<float> invMasses)
        {
            if (faces.Count != surfaceFaceCount)
            {
                return faces;
            }

            bool IsStatic(int local) => nodeIndices[local] >= invMasses.Count || invMasses[nodeIndices[local]] == 0f;
            float SquaredDistance(int a, int b)
            {
                var d = positions[nodeIndices[b]] - positions[nodeIndices[a]];
                return d.X * d.X + d.Y * d.Y + d.Z * d.Z;
            }

            bool Flippable(int[] face) => face.Length == 4 && face.Distinct().Count() == 4
                && !Array.Exists(face, IsStatic) && SquaredDistance(face[0], face[2]) < SquaredDistance(face[1], face[3]);

            List<int> Created(List<int[]> order)
            {
                var seen = new HashSet<int>();
                var created = new List<int>();
                foreach (var face in order)
                {
                    foreach (var local in face)
                    {
                        if (!IsStatic(local) && seen.Add(local))
                        {
                            created.Add(local);
                        }
                    }
                }

                return created;
            }

            HashSet<int> Missed(List<int[]> order)
            {
                var mass = new float[nodeIndices.Count];
                foreach (var face in order)
                {
                    var corners = face.Length > 4 ? face[..4] : face;
                    if (corners.Length < 3 || Array.TrueForAll(corners, IsStatic))
                    {
                        continue;
                    }

                    var cycle = CompilerCornerCycle(corners, IsStatic);
                    int[] element = cycle.Length == 4 ? cycle : [cycle[0], cycle[1], cycle[2], cycle[2]];
                    for (var k = 1; k < 4; k++)
                    {
                        for (var j = 0; j < k; j++)
                        {
                            if (element[j] != element[k])
                            {
                                var term = MathF.Sqrt(SquaredDistance(element[j], element[k])) * 4f;
                                mass[element[k]] += term;
                                mass[element[j]] += term;
                            }
                        }
                    }
                }

                var missed = new HashSet<int>();
                foreach (var local in Created(order))
                {
                    if (BitConverter.SingleToInt32Bits(1f / mass[local]) != BitConverter.SingleToInt32Bits(invMasses[nodeIndices[local]]))
                    {
                        missed.Add(local);
                    }
                }

                return missed;
            }

            var missed = Missed(faces);
            var reachable = faces.Where(Flippable).SelectMany(static face => face).ToHashSet();
            if (missed.Count == 0 || !missed.All(reachable.Contains))
            {
                return faces;
            }

            var creation = Created(faces);
            var current = new List<int[]>(faces);
            var flipped = new bool[faces.Count];
            for (var changed = true; changed && missed.Count > 0;)
            {
                changed = false;
                for (var i = 0; i < current.Count && missed.Count > 0; i++)
                {
                    var face = current[i];
                    if (flipped[i] || !Flippable(face) || !Array.Exists(face, missed.Contains))
                    {
                        continue;
                    }

                    var trial = new List<int[]>(current);
                    trial[i] = [face[3], face[0], face[1], face[2]];
                    if (!Created(trial).SequenceEqual(creation))
                    {
                        continue;
                    }

                    var trialMissed = Missed(trial);
                    if (trialMissed.Count < missed.Count)
                    {
                        (current, missed, flipped[i], changed) = (trial, trialMissed, true, true);
                    }
                }
            }

            return missed.Count == 0 ? current : faces;
        }

        /// <summary>
        /// Rotates each surface face's declared corner order so the sheet hands the compiler its static
        /// vertices in the order the shipped node array numbers them.
        /// </summary>
        void DeclareFacesInStaticNodeOrder(List<int[]> faces, int rotatableFaceCount, IReadOnlyList<int> nodeIndices)
        {
            bool IsStatic(int local) => local >= 0 && local < nodeIndices.Count
                && nodeIndices[local] < NodeInvMasses.Length && NodeInvMasses[nodeIndices[local]] == 0f;

            var created = new HashSet<int>();
            for (var i = 0; i < faces.Count; i++)
            {
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

        /// <summary>
        /// Gets the rotation of a face that introduces its new static corners in ascending node order without changing its
        /// <see cref="CompilerCornerCycle"/>, or null when there is none or none is needed.
        /// </summary>
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
            }

            return cycle;
        }

        /// <summary>
        /// The corner order the compiler pairs bend rods in for a FIXED declaration: the passes
        /// <see cref="CompilerCornerCycle"/> models, and then the convexity swap it applies to a quad whose
        /// two leading corners are static.
        /// </summary>
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
        /// Gets the bend rods <c>add_stiffness_rods</c> derives from faces in their declared corner order, over their first
        /// four corners.
        /// </summary>
        internal static HashSet<(int, int)> BendRodsFromSurface(IEnumerable<int[]> faces, Func<int, bool> isStatic)
            => PredictBendRods([.. faces.Select(static face => face.Length > 4 ? face[..4] : face)], isStatic);

        /// <summary>
        /// The bend rods the compiler derives from faces a document declares, each taken in the corner order it reaches the
        /// compiler's element array in (see <see cref="CompilerCornerCycle"/>), which decides the corners a hinge pairs.
        /// </summary>
        internal static HashSet<(int, int)> BendRodsFromDeclaredFaces(IEnumerable<int[]> faces, Func<int, bool> isStatic)
            => PredictBendRods([.. faces.Select(face => CompilerCornerCycle(face.Length > 4 ? face[..4] : face, isStatic))], isStatic);

        static HashSet<(int, int)> PredictBendRods(List<int[]> elements, Func<int, bool> isStatic)
        {
            var rods = new HashSet<(int, int)>();
            foreach (var (_, nodeA, nodeB) in BendRodGenerators(elements))
            {
                if (nodeA != nodeB && !(isStatic(nodeA) && isStatic(nodeB)))
                {
                    rods.Add(nodeA < nodeB ? (nodeA, nodeB) : (nodeB, nodeA));
                }
            }

            return rods;
        }

        /// <summary>
        /// Gets every bend rod the compiler derives from a surface as its hinge edge and far corners. An edge pairs with the
        /// earliest open element listing it, same direction first; a third element listing it opens it again.
        /// </summary>
        internal static IEnumerable<((int, int) Hinge, int NodeA, int NodeB)> BendRodGenerators(IEnumerable<int[]> elements)
        {
            var open = new Dictionary<(int, int), (int Near, int Far)>();
            foreach (var e in elements.Select(static element => element.Length > 4 ? element[..4] : element))
            {
                var n = e.Length;
                for (var j = 0; j < n; j++)
                {
                    var n0 = e[j];
                    var n1 = e[(j + 1) % n];
                    var n2 = n == 3 ? e[(j + 2) % 3] : e[(j + 2) % 4];
                    var n3 = n == 3 ? n2 : e[(j + 3) % 4];
                    var hinge = n0 < n1 ? (n0, n1) : (n1, n0);
                    if (open.Remove((n0, n1), out var same))
                    {
                        yield return (hinge, n2, same.Near);
                        yield return (hinge, n3, same.Far);
                    }
                    else if (open.Remove((n1, n0), out var opposite))
                    {
                        yield return (hinge, n2, opposite.Far);
                        yield return (hinge, n3, opposite.Near);
                    }
                    else
                    {
                        open[(n0, n1)] = (n2, n3);
                    }
                }
            }
        }

        /// <summary>
        /// Makes an uncovered sheet vertex the fourth corner of the nearest same-mesh triangle, diagonal to the corner it
        /// is farthest from.
        /// </summary>
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

            var start = (diagonal + 2) % 3;
            faces[best] = [triangle[start], triangle[(start + 1) % 3], triangle[(start + 2) % 3], stray];
        }

        /// <summary>
        /// The weight a selection is painted at on a node <c>m_DynNodeVertexSet</c> puts in its set while its compiled
        /// weight there is 0; it rounds back to 0.
        /// </summary>
        internal const float SubQuantumMembershipWeight = 0.001f;

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
        bool InRecordedVertexSet(int node, uint nameHash)
        {
            var dynamic = node - StaticNodeCount;
            return dynamic >= 0 && dynamic < DynNodeVertexSet.Length
                && DynNodeVertexSet[dynamic] < VertexSetNames.Length
                && VertexSetNames[DynNodeVertexSet[dynamic]] == nameHash;
        }

        /// <summary>
        /// Gets the nodes compiled on the raw integrator, or empty when re-authoring the rest as goal-damped would change
        /// whether the dynamic nodes hold both integrator kinds.
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

        /// <summary>The per-node paint values of one proxy vertex.</summary>
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

        ProxyVertexData ComputeProxyVertexData(int node)
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

        /// <summary>Gets whether <paramref name="node"/> is a proxy-sheet vertex (<c>$cloth_m&lt;N&gt;p&lt;S&gt;</c>).</summary>
        public bool IsProxyMeshNode(int node)
            => node >= 0 && node < CtrlNames.Length && ParseProxyMeshIndex(CtrlNames[node]) >= 0;

        /// <summary>Gets the vertex slot of a <c>$cloth_m&lt;N&gt;p&lt;S&gt;</c> name, or <see cref="int.MaxValue"/>.</summary>
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

        /// <summary>
        /// Reorders faces to their SIMD lane order, each in its lane's node order; faces without a lane follow in array order.
        /// </summary>
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
        /// Reconstructs sheets for the uncovered proxy nodes that no solve element covers, grouped by rods, source faces and
        /// mesh index, with authored faces where they fit and a triangulation otherwise.
        /// </summary>
        List<ProxyMesh> BuildProxyMeshesFromRodsOnly(HashSet<int> coveredNodes)
        {
            var result = new List<ProxyMesh>();
            if (InitPosePositions.Length == 0)
            {
                return result;
            }

            var n = CtrlNames.Length;
            var isProxy = new bool[n];
            for (var node = 0; node < n && node < InitPosePositions.Length; node++)
            {
                isProxy[node] = IsProxyNodeName(CtrlNames[node]) && !string.IsNullOrEmpty(CtrlNames[node])
                    && !CtrlNames[node].StartsWith(FreeClothNodePrefix, StringComparison.Ordinal)
                    && !coveredNodes.Contains(node) && !IsHingeRegeneratedProxy(node);
            }

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
                if (rod.NodeA < n && rod.NodeB < n
                    && isProxy[rod.NodeA] && isProxy[rod.NodeB])
                {
                    parent[Find(rod.NodeA)] = Find(rod.NodeB);
                }
            }

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

            foreach (var (_, nodeIndices) in groups.OrderBy(static kv => kv.Value.Min()))
            {
                if (nodeIndices.Count < 3)
                {
                    continue;
                }

                var mesh = BuildProxyMeshFromNodeSet(nodeIndices);
                if (mesh is not null)
                {
                    result.Add(mesh);
                }
            }

            return result;
        }

        ProxyMesh? BuildProxyMeshFromNodeSet(List<int> nodeIndices)
        {
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

                var vertex = ComputeProxyVertexData(node);
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

            foreach (var node in usesAuthoredFaces ? truncatedTail : [])
            {
                clothEnable[localOf[node]] = 1f;
            }

            if (faces.Count == 0)
            {
                return null;
            }

            DeclareFacesInStaticNodeOrder(faces, faces.Count, nodeIndices);

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

            foreach (var (a, b) in FaceEdges(faces))
            {
                if (!shipped.Contains((a, b)) && InverseMassOf(a) + InverseMassOf(b) > RodMassFloor)
                {
                    return [];
                }
            }

            var covered = new HashSet<int>();
            foreach (var face in faces)
            {
                covered.UnionWith(face);
            }

            if (!AppendTruncatedCorners(faces, nodeIndices, covered, shipped, truncatedTail))
            {
                return [];
            }

            faces.Reverse();
            MergeFacesInNodeCreationOrder(faces, triangles);
            return [.. faces.Select(face => face.Select(corner => localOf[corner]).ToArray())];
        }

        /// <summary>
        /// The inverse-mass sum at or below which the rod importer drops a rod outright.
        /// </summary>
        const float RodMassFloor = 1e-6f;

        /// <summary>
        /// The edges of the given faces: each face's consecutive corner pairs in its declared cycle. A
        /// quad's two diagonals are not among them.
        /// </summary>
        static HashSet<(int, int)> FaceEdges(IEnumerable<int[]> faces)
        {
            var edges = new HashSet<(int, int)>();
            foreach (var face in faces)
            {
                for (var k = 0; k < face.Length; k++)
                {
                    var (a, b) = (face[k], face[(k + 1) % face.Length]);
                    if (a != b)
                    {
                        edges.Add(a < b ? (a, b) : (b, a));
                    }
                }
            }

            return edges;
        }

        /// <summary>
        /// Interleaves the quad run and the triangle run of a recovered surface the way the authored sheet
        /// declared them, placing an already-faced node as an extra corner where the sheet's own wider
        /// polygon named it early.
        /// </summary>
        void MergeFacesInNodeCreationOrder(List<int[]> faces, int triangleCount)
        {
            var quadCount = faces.Count - triangleCount;
            if (quadCount <= 0 || triangleCount <= 0)
            {
                return;
            }

            var rank = SurfaceNodeRanks;
            if (IntroducesInCompiledOrder(faces, rank))
            {
                return;
            }

            var merged = MergeRuns(faces, quadCount, rank, true, out var extras);
            if (extras > 0 && !IntroducesInCompiledOrder(merged, rank))
            {
                merged = MergeRuns(faces, quadCount, rank, false, out _);
            }

            faces.Clear();
            faces.AddRange(merged);
        }

        Dictionary<int, List<int>> CompiledRunsByRank(IEnumerable<int[]> faces, int[] rank)
        {
            var runs = new Dictionary<int, List<int>>();
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
                (runs.TryGetValue(rank[node], out var run) ? run : runs[rank[node]] = []).Add(node);
            }

            return runs;
        }

        /// <summary>
        /// The pinned nodes the surface introduces, in the order the compiled node array numbers them.
        /// </summary>
        List<int> CompiledPinnedRun(List<int[]> faces)
        {
            var covered = new SortedSet<int>();
            foreach (var face in faces)
            {
                foreach (var corner in PinnedRunCorners(face))
                {
                    covered.Add(corner);
                }
            }

            return [.. covered];
        }

        IEnumerable<int> PinnedRunCorners(int[] face)
        {
            var corners = face.Length > 4 ? face[..4] : face;
            return Array.TrueForAll(corners, static corner => corner < 0)
                || Array.TrueForAll(corners, corner => corner < RotationLockedStaticNodeCount)
                ? []
                : corners.Where(corner => corner >= 0 && corner < RotationLockedStaticNodeCount);
        }

        /// <summary>
        /// Whether walking the faces in order introduces the nodes in the order the compiled node array
        /// numbers them, in BOTH of the compiler's walks: the simulated nodes per rank, taking each face's
        /// own corners as a set, and the rotation-locked pinned nodes in one run.
        /// </summary>
        bool IntroducesInCompiledOrder(List<int[]> faces, int[] rank)
        {
            var pending = CompiledRunsByRank(faces, rank);
            var pinned = CompiledPinnedRun(faces);
            var seen = new HashSet<int>();
            var seenPinned = new HashSet<int>();
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

                var cursor = new Dictionary<int, int>(2);
                foreach (var node in fresh)
                {
                    var run = pending[rank[node]];
                    var from = cursor.GetValueOrDefault(rank[node]);
                    if (from >= run.Count || run[from] != node)
                    {
                        return false;
                    }

                    cursor[rank[node]] = from + 1;
                }

                var freshPinned = PinnedRunCorners(face).Where(corner => seenPinned.Add(corner)).ToList();
                if (freshPinned.Count > 0 && (pinned.Count < freshPinned.Count
                    || !pinned.Take(freshPinned.Count).ToHashSet().SetEquals(freshPinned)))
                {
                    return false;
                }

                foreach (var corner in freshPinned)
                {
                    pinned.Remove(corner);
                }

                foreach (var corner in fresh)
                {
                    seen.Add(corner);
                    pending[rank[corner]].Remove(corner);
                }
            }

            return true;
        }

        List<int[]> MergeRuns(List<int[]> faces, int quadCount, int[] rank, bool allowExtraCorners,
            out int extraCorners)
        {
            var pending = CompiledRunsByRank(faces, rank);
            var pinned = CompiledPinnedRun(faces);
            var created = new HashSet<int>();
            var createdPinned = new HashSet<int>();
            var placedAt = new Dictionary<int, int>();
            var merged = new List<int[]>(faces.Count);
            var quad = 0;
            var triangle = quadCount;
            var lastWide = -1;
            extraCorners = 0;

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

            List<int> IntroducedPinned(int face)
                => [.. PinnedRunCorners(faces[face]).Where(corner => !createdPinned.Contains(corner)).Distinct()];

            bool IsNextPinned(int face)
            {
                var fresh = IntroducedPinned(face);
                return fresh.Count == 0 || (pinned.Count >= fresh.Count
                    && pinned.Take(fresh.Count).ToHashSet().SetEquals(fresh));
            }

            List<int>? Preceding(List<int> fresh)
            {
                var cursor = new Dictionary<int, int>(2);
                var missing = new List<int>();
                foreach (var node in fresh)
                {
                    if (missing.Contains(node))
                    {
                        continue;
                    }

                    if (!pending.TryGetValue(rank[node], out var run))
                    {
                        return null;
                    }

                    var from = cursor.GetValueOrDefault(rank[node]);
                    var at = run.IndexOf(node, from);
                    if (at < 0)
                    {
                        return null;
                    }

                    if (at > from && lastWide < placedAt.GetValueOrDefault(rank[node], -1))
                    {
                        return null;
                    }

                    for (var k = from; k < at; k++)
                    {
                        missing.Add(run[k]);
                    }

                    cursor[rank[node]] = at + 1;
                }

                return missing;
            }

            void Create(int node, int face)
            {
                created.Add(node);
                pending[rank[node]].Remove(node);
                placedAt[rank[node]] = face;
            }

            while (quad < quadCount || triangle < faces.Count)
            {
                var heads = quad < quadCount
                    ? (triangle < faces.Count ? (int[])[0, 1] : [0])
                    : (int[])[1];
                int Face(int run) => run == 0 ? quad : triangle;

                var taken = -1;
                var fewest = int.MaxValue;
                foreach (var head in heads)
                {
                    var fresh = Introduced(Face(head));
                    if (fresh.Count > 0 && fresh.Count < fewest && IsNextPinned(Face(head))
                        && Preceding(fresh) is { Count: 0 })
                    {
                        taken = head;
                        fewest = fresh.Count;
                    }
                }

                if (taken < 0)
                {
                    foreach (var head in heads)
                    {
                        if (Introduced(Face(head)).Count == 0 && IsNextPinned(Face(head)))
                        {
                            taken = head;
                            break;
                        }
                    }
                }

                if (taken < 0 && allowExtraCorners && lastWide >= 0)
                {
                    List<int>? shortest = null;
                    foreach (var head in heads)
                    {
                        var fresh = Introduced(Face(head));
                        if (fresh.Count == 0 || !IsNextPinned(Face(head)))
                        {
                            continue;
                        }

                        var ahead = Preceding(fresh);
                        if (ahead is not null && ahead.Count > 0
                            && (shortest is null || ahead.Count < shortest.Count))
                        {
                            shortest = ahead;
                            taken = head;
                        }
                    }

                    if (shortest is not null)
                    {
                        merged[lastWide] = [.. merged[lastWide], .. shortest];
                        foreach (var node in shortest)
                        {
                            Create(node, lastWide);
                        }

                        extraCorners += shortest.Count;
                    }
                }

                taken = taken < 0 ? heads[0] : taken;
                var take = Face(taken);
                foreach (var corner in Introduced(take))
                {
                    Create(corner, merged.Count);
                }

                foreach (var corner in IntroducedPinned(take))
                {
                    createdPinned.Add(corner);
                    pinned.Remove(corner);
                }

                merged.Add(faces[take]);
                if (faces[take].Length >= 4)
                {
                    lastWide = merged.Count - 1;
                }

                if (taken == 0)
                {
                    quad++;
                }
                else
                {
                    triangle++;
                }
            }

            return merged;
        }

        /// <summary>
        /// Each node's BFS layer from the static set over the surface, which is the <c>nRank</c> the builder
        /// lays the dynamic node block out by.
        /// </summary>
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
        /// Appends each unfaced, unrodded vertex past the fourth corner of the nearest quad, the corners the compiler
        /// truncates from a larger polygon. Returns false when a vertex cannot be placed that way.
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

        /// <summary>Adds a triangle to the two nearest non-collinear vertices for each vertex no face covers.</summary>
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

        /// <summary>
        /// Gets whether a pinned vertex has no simulated neighbour, or two vertices lie closer than a quarter of the
        /// median edge.
        /// </summary>
        static bool ComputeDropRisk(Vector3[] positions, float[] clothEnable, List<int[]> faces)
        {
            var n = positions.Length;
            if (n == 0)
            {
                return false;
            }

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

        /// <summary>Triangulates the positions by Delaunay over their two widest axes.</summary>
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

            var allPoints = new Vector2[n + 3];
            Array.Copy(points, allPoints, n);
            allPoints[n] = center + new Vector2(0f, size * 2f);
            allPoints[n + 1] = center + new Vector2(-size * 2f, -size);
            allPoints[n + 2] = center + new Vector2(size * 2f, -size);

            var triangles = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };

            for (var p = 0; p < n; p++)
            {
                var bad = triangles.Where(tri => InCircumcircle(allPoints[tri.A], allPoints[tri.B], allPoints[tri.C], allPoints[p])).ToList();

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
