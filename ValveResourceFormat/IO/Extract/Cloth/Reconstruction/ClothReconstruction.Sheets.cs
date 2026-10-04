using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// A cloth proxy mesh (sheet) reconstructed from the FeModel surface, with its per-vertex paint.
    /// </summary>
    internal sealed class ProxyMesh
    {
        /// <summary>Gets the FeModel control node of each vertex.</summary>
        public required int[] NodeIndices { get; init; }
        public required Vector3[] Positions { get; init; }
        /// <summary>Gets the <c>cloth_enable</c> paint: 1 for simulated, 0 for pinned.</summary>
        public required float[] ClothEnable { get; init; }
        /// <summary>Gets the <c>cloth_goal_strength_v2</c> paint.</summary>
        public required float[] GoalStrength { get; init; }
        /// <summary>Gets the <c>cloth_goal_damping</c> paint.</summary>
        public required float[] GoalDamping { get; init; }
        /// <summary>Gets the <c>cloth_animation_force_attract</c> paint of <see cref="ClothReconstruction.RawGoalPaintNodes"/>, or empty.</summary>
        public float[] AnimationForceAttract { get; init; } = [];
        /// <summary>Gets the <c>cloth_animation_attract</c> paint, alongside <see cref="AnimationForceAttract"/>.</summary>
        public float[] AnimationAttract { get; init; } = [];
        public required float[] CollisionRadius { get; init; }
        public required float[] Friction { get; init; }
        public required float[] Drag { get; init; }
        public required float[] GroundCollision { get; init; }
        public required float[] GroundFriction { get; init; }
        /// <summary>Gets the <c>cloth_gravity</c> paint.</summary>
        public required float[] Gravity { get; init; }
        /// <summary>Gets the compiled <c>flAnimationVertexAttraction</c> of each vertex.</summary>
        public required float[] VertexAttraction { get; init; }
        public required SkinInfluence[][] SkinInfluences { get; init; }
        /// <summary>Gets the faces as quads and triangles of vertex indices.</summary>
        public required List<int[]> Faces { get; init; }
        /// <summary>Gets the named vertex selections covering this sheet, as a membership weight per vertex.</summary>
        public (string Name, float[] Weights)[] VertexMaps { get; init; } = [];
        /// <summary>
        /// Gets the <c>cloth_make_rods</c> paint, 1 where every element of the vertex was built into rods, or empty when the
        /// sheet has no such region.
        /// </summary>
        public float[] RodsDriven { get; init; } = [];
        public int SimulatedCount { get; init; }
        public int PinnedCount { get; init; }
        /// <summary>
        /// Gets whether the importer is expected to drop a vertex of this triangulated island, see <see cref="ClothReconstruction.ComputeDropRisk"/>.
        /// </summary>
        public bool IsDropRisk { get; init; }
        /// <summary>Gets whether <see cref="Faces"/> came from <c>m_SourceElems</c> rather than a triangulation.</summary>
        public bool UsesAuthoredFaces { get; init; }
        /// <summary>Gets whether no vertex of this sheet is driven by a real skeleton bone.</summary>
        public bool IsFreeFloating { get; init; }
    }

    internal sealed partial class ClothReconstruction
    {
        internal const string ProxyNamePrefix = "$cloth_m";

        /// <summary>The exclusive upper bound of a vertex slot <see cref="PadToAuthoredSlots"/> pads up to.</summary>
        private const int MaxProxySlots = 1 << 16;

        private Dictionary<int, int>? firstOffsetParentByNode;

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
                if (node < 0 || node >= Fe.CtrlNames.Length)
                {
                    return mesh;
                }

                var m = ParseProxyMeshIndex(Fe.CtrlNames[node]);
                var p = ParseProxyVertexIndex(Fe.CtrlNames[node]);
                if (m < 0 || p < 0 || p >= MaxProxySlots || (i > 0 && (m != meshIndex || p <= slots[i - 1])))
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
            var localToSlot = new int[n];
            for (var slot = 0; slot < total; slot++)
            {
                if (dummy[slot])
                {
                    clothEnable[slot] = 0f;
                }
                else
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
        /// index.
        /// </summary>
        internal List<ProxyMesh> BuildProxyMeshes()
        {
            var coveredNodes = new HashSet<int>();
            var pending = new List<ProxyMesh>();

            if (BuildProxyMesh() is { } merged)
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
                    var meshIndex = ProxyMeshIndexOf(merged.NodeIndices[v]);
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

            // Unnumbered proxy nodes hanging off an independent chain belong to that chain, not to a sheet
            var chainBoneNodes = IndependentChainJointNodes();
            if (chainBoneNodes.Count > 0)
            {
                for (var node = 0; node < Fe.CtrlNames.Length; node++)
                {
                    if (!IsProxyNodeName(Fe.CtrlNames[node]) || ProxyMeshIndexOf(node) >= 0)
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

            return [.. pending
                .OrderBy(p => { var m = ProxyMeshOriginalIndex(p); return m >= 0 ? m : int.MaxValue; })
                .ThenBy(p => p.NodeIndices.Length == 0 ? int.MaxValue : p.NodeIndices.Min())
                .Select(PadToAuthoredSlots)];
        }

        /// <summary>Finds the root of <paramref name="x"/> in a union-find forest, halving the path on the way.</summary>
        private static int FindRoot(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                x = parent[x] = parent[parent[x]];
            }

            return x;
        }

        /// <summary>Gets the smallest <c>$cloth_m&lt;N&gt;</c> index among the mesh's nodes, or -1.</summary>
        private int ProxyMeshOriginalIndex(ProxyMesh mesh) => mesh.NodeIndices
            .Select(ProxyMeshIndexOf)
            .Where(m => m >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        /// <summary>Merges two meshes of one <c>$cloth_m&lt;N&gt;</c> index into one, in authored vertex order.</summary>
        private ProxyMesh MergeSameIndexProxyMeshes(ProxyMesh a, ProxyMesh b)
        {
            var an = a.NodeIndices.Length;
            var bn = b.NodeIndices.Length;
            static float[] Weights(ProxyMesh mesh, string name)
                => Array.Find(mesh.VertexMaps, m => m.Name == name).Weights ?? new float[mesh.NodeIndices.Length];

            var both = new ProxyMesh
            {
                NodeIndices = [.. a.NodeIndices, .. b.NodeIndices],
                Positions = [.. a.Positions, .. b.Positions],
                ClothEnable = [.. a.ClothEnable, .. b.ClothEnable],
                GoalStrength = [.. a.GoalStrength, .. b.GoalStrength],
                GoalDamping = [.. a.GoalDamping, .. b.GoalDamping],
                AnimationForceAttract = [.. a.AnimationForceAttract, .. b.AnimationForceAttract],
                AnimationAttract = [.. a.AnimationAttract, .. b.AnimationAttract],
                CollisionRadius = [.. a.CollisionRadius, .. b.CollisionRadius],
                Friction = [.. a.Friction, .. b.Friction],
                Drag = [.. a.Drag, .. b.Drag],
                GroundCollision = [.. a.GroundCollision, .. b.GroundCollision],
                GroundFriction = [.. a.GroundFriction, .. b.GroundFriction],
                Gravity = [.. a.Gravity, .. b.Gravity],
                VertexAttraction = [.. a.VertexAttraction, .. b.VertexAttraction],
                SkinInfluences = [.. a.SkinInfluences, .. b.SkinInfluences],
                VertexMaps = [.. a.VertexMaps.Select(static m => m.Name).Union(b.VertexMaps.Select(static m => m.Name))
                    .Select(name => (name, (float[])[.. Weights(a, name), .. Weights(b, name)]))],
                Faces = [],
                RodsDriven = a.RodsDriven.Length == 0 && b.RodsDriven.Length == 0
                    ? []
                    : [.. a.RodsDriven.Length == 0 ? new float[an] : a.RodsDriven, .. b.RodsDriven.Length == 0 ? new float[bn] : b.RodsDriven],
                UsesAuthoredFaces = a.UsesAuthoredFaces,
            };

            var sourceOf = Enumerable.Range(0, an + bn).OrderBy(i => ParseProxyVertexIndex(Fe.CtrlNames[both.NodeIndices[i]])).ToArray();
            var slotOf = new int[sourceOf.Length];
            for (var slot = 0; slot < sourceOf.Length; slot++)
            {
                slotOf[sourceOf[slot]] = slot;
            }

            List<int[]> faces = [.. a.Faces.Select(f => f.Select(v => slotOf[v]).ToArray()),
                .. b.Faces.Select(f => f.Select(v => slotOf[an + v]).ToArray())];
            return GatherProxyMesh(both, sourceOf, GatherSlots(both.ClothEnable, sourceOf), faces,
                a.SimulatedCount + b.SimulatedCount, a.PinnedCount + b.PinnedCount,
                a.IsDropRisk || b.IsDropRisk, a.IsFreeFloating && b.IsFreeFloating);
        }

        private List<BoneChain> IndependentBoneChains()
            => [.. BuildBoneChains().Where(IsIndependentChain)];

        private HashSet<int> IndependentChainJointNodes()
            => IndependentBoneChains().SelectMany(static c => c.Joints).Select(static j => j.Node).ToHashSet();

        /// <summary>
        /// Gets whether a chain is exported as a standalone <c>ClothChain</c>: no joint is back-solved by a proxy sheet and
        /// the chain is not sheet-driven.
        /// </summary>
        internal bool IsIndependentChain(BoneChain chain)
            => !chain.Joints.Any(joint => ProxyFitMatrixNodes.Contains(joint.Node)) && !IsSheetDrivenChain(chain);

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

                if (joint.Node >= Fe.StaticNodeCount)
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
            for (var node = 0; node < Fe.CtrlNames.Length; node++)
            {
                var sheetVertex = ProxyMeshIndexOf(node) >= 0;
                var generatedRing = IsRingNode(node);
                if (!sheetVertex && !generatedRing)
                {
                    continue;
                }

                var parent = SkelParentOf(node);
                if (parent < 0 && !HasCompiledSkelParents && FirstOffsetParentByNode.TryGetValue(node, out var offsetParent))
                {
                    parent = offsetParent;
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

        /// <summary>Gets the parent of the first control offset of each child node.</summary>
        private Dictionary<int, int> FirstOffsetParentByNode => firstOffsetParentByNode ??= BuildFirstOffsetParentByNode();

        private Dictionary<int, int> BuildFirstOffsetParentByNode()
        {
            var parents = new Dictionary<int, int>(Fe.CtrlOffsets.Length);
            foreach (var off in Fe.CtrlOffsets)
            {
                parents.TryAdd(off.CtrlChild, off.CtrlParent);
            }

            return parents;
        }

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
                if (corner < 0 || corner >= Fe.CtrlNames.Length)
                {
                    return false;
                }

                if (IsFreeClothNode(corner))
                {
                    free = true;
                }
                else if (IsProxyNodeName(Fe.CtrlNames[corner]))
                {
                    return false;
                }
            }

            return free || !HasProxyMeshNodes;
        }

        /// <summary>
        /// Gets the compiled surface faces built from <c>ClothTri</c> and <c>ClothQuad</c> elements, quads before triangles,
        /// with corners in the compiled cycle order.
        /// </summary>
        internal List<int[]> GetAuthoredElementFaces()
            => [.. Fe.Quads.Concat(Fe.Tris).Where(face => !IsHingeFanFace(face) && IsAuthoredElementFace(face))];

        /// <summary>
        /// Reconstructs the proxy sheets from the FeModel surface as one merged mesh, or null when there is no surface,
        /// as on a cloth made only of chains.
        /// </summary>
        internal ProxyMesh? BuildProxyMesh()
        {
            if ((Fe.Quads.Length == 0 && Fe.Tris.Length == 0) || Fe.InitPosePositions.Length == 0)
            {
                return null;
            }

            var referenced = SurfaceFaceNodes();
            if (referenced.Count == 0)
            {
                return null;
            }

            var surfaceNodes = new HashSet<int>(referenced);
            var surfaceMeshes = surfaceNodes.Select(ProxyMeshIndexOf)
                .Where(static mesh => mesh >= 0).ToHashSet();
            var rodsFaces = AddRodRegionFaces(referenced, surfaceNodes, surfaceMeshes);
            var strays = rodsFaces.Count > 0 ? AddUncoveredSheetVertices(referenced, surfaceMeshes) : [];

            var nodeIndices = referenced.ToArray();
            SortByAuthoredVertexOrder(nodeIndices);
            var remap = new Dictionary<int, int>(nodeIndices.Length);
            for (var i = 0; i < nodeIndices.Length; i++)
            {
                remap[nodeIndices[i]] = i;
            }

            var vertices = ComputeProxyVertexArrays(nodeIndices);

            var faces = SurfaceFacesInLaneOrder(remap);
            RestoreStaticQuadCornerOrder(faces, [.. rodsFaces.Select(face => face.Select(corner => remap[corner]).ToArray())], nodeIndices);

            var surfaceFaceCount = faces.Count;
            rodsFaces.Reverse();
            foreach (var face in rodsFaces)
            {
                faces.Add([.. face.Select(corner => remap[corner])]);
            }

            var meshOf = Array.ConvertAll(nodeIndices, ProxyMeshIndexOf);
            foreach (var stray in strays)
            {
                AttachStrayToTriangle(remap[stray], vertices.Positions, faces, meshOf);
            }

            var declared = RotateQuadsToShippedMasses(
                ChooseFaceDeclarationOrder(faces, surfaceFaceCount, nodeIndices),
                surfaceFaceCount, nodeIndices, Fe.InitPosePositions, Fe.NodeInvMasses);

            var hasRodRegion = rodsFaces.Count > 0;
            float[] rodsDriven = hasRodRegion ? Array.ConvertAll(nodeIndices, node => surfaceNodes.Contains(node) ? 0f : 1f) : [];

            return AssembleProxyMesh(vertices, nodeIndices, declared, rodsDriven,
                usesAuthoredFaces: hasRodRegion, isDropRisk: false, isFreeFloating: false);
        }

        /// <summary>
        /// Gets the nodes of the solve elements that belong to a proxy sheet: every face except hinge fans, authored element
        /// faces and faces over independent chain joints.
        /// </summary>
        private SortedSet<int> SurfaceFaceNodes()
        {
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
                        if (n >= 0 && n < Fe.InitPosePositions.Length && n < Fe.CtrlNames.Length && !IsHingeRegeneratedProxy(n))
                        {
                            referenced.Add(n);
                        }
                    }
                }
            }

            Collect(Fe.Quads);
            Collect(Fe.Tris);
            return referenced;
        }

        /// <summary>
        /// Gets the source faces of the covered sheets that reach a node outside the solve elements, which make up the
        /// sheets' rod region, and adds their corners to <paramref name="referenced"/>.
        /// </summary>
        private List<int[]> AddRodRegionFaces(SortedSet<int> referenced, HashSet<int> surfaceNodes, HashSet<int> surfaceMeshes)
        {
            var rodsFaces = new List<int[]>();
            foreach (var face in Fe.SourceFaces)
            {
                if (face.Length < 3 || SpansProxyMeshes(face))
                {
                    continue;
                }

                var rodsRegion = false;
                var sheet = true;
                foreach (var corner in face)
                {
                    if (corner < 0 || corner >= Fe.InitPosePositions.Length || corner >= Fe.CtrlNames.Length || IsHingeRegeneratedProxy(corner)
                        || !surfaceMeshes.Contains(ProxyMeshIndexOf(corner)))
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

            return rodsFaces;
        }

        /// <summary>
        /// Gets the vertices of the covered sheets that no face and no rod reaches, and adds them to <paramref name="referenced"/>.
        /// </summary>
        private List<int> AddUncoveredSheetVertices(SortedSet<int> referenced, HashSet<int> surfaceMeshes)
        {
            var strays = new List<int>();
            var covered = new HashSet<int>(referenced);
            foreach (var rod in Fe.Rods)
            {
                covered.Add(rod.NodeA);
                covered.Add(rod.NodeB);
            }

            for (var node = 0; node < Fe.CtrlNames.Length && node < Fe.InitPosePositions.Length; node++)
            {
                if (!covered.Contains(node) && !IsHingeRegeneratedProxy(node)
                    && surfaceMeshes.Contains(ProxyMeshIndexOf(node)))
                {
                    strays.Add(node);
                    referenced.Add(node);
                }
            }

            return strays;
        }

        /// <summary>
        /// Gets the solve elements over the remapped nodes in SIMD lane order, quads first, with each split quad merged back
        /// in place of its first half.
        /// </summary>
        private List<int[]> SurfaceFacesInLaneOrder(Dictionary<int, int> remap)
        {
            var faces = new List<int[]>(Fe.Quads.Length + Fe.Tris.Length);
            bool Kept(int[] face) => Array.TrueForAll(face, corner => remap.ContainsKey(corner));

            foreach (var q in OrderFacesBySimdLanes(Fe.Quads, "m_SimdQuads"))
            {
                if (Kept(q))
                {
                    faces.Add([remap[q[0]], remap[q[1]], remap[q[2]], remap[q[3]]]);
                }
            }

            var (splitQuads, splitHalves) = MergeSplitQuads();

            foreach (var t in OrderFacesBySimdLanes(Fe.Tris, "m_SimdTris"))
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

            return faces;
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
            public SkinInfluence[][] SkinInfluences { get; } = new SkinInfluence[count][];
            public int Simulated { get; set; }
            public int Pinned { get; set; }
        }

        /// <summary>Fills the per-vertex arrays of the given nodes from their compiled per-node values.</summary>
        private ProxyVertexArrays ComputeProxyVertexArrays(int[] nodeIndices)
        {
            var arrays = new ProxyVertexArrays(nodeIndices.Length);
            for (var i = 0; i < nodeIndices.Length; i++)
            {
                var node = nodeIndices[i];
                var isSim = Simulates(node);
                var integrator = Fe.GetIntegrator(node);
                var rawGoal = node < RawGoalPaintNodes.Length && RawGoalPaintNodes[node];
                var (worldFriction, groundFriction) = Fe.GetWorldFriction(node);

                arrays.Positions[i] = Fe.InitPosePositions[node];
                arrays.ClothEnable[i] = isSim ? 1f : 0f;
                if (isSim)
                {
                    arrays.Simulated++;
                }
                else
                {
                    arrays.Pinned++;
                }

                arrays.SkinInfluences[i] = ProxyVertexSkinInfluences(node, isSim);
                arrays.GoalStrength[i] = rawGoal ? 0f : GoalStrengthPaint(integrator.ForceAttraction);
                arrays.GoalDamping[i] = rawGoal ? 0f : GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction);
                arrays.AnimationForceAttract[i] = rawGoal ? integrator.ForceAttraction / ClothRawGoalScale : 0f;
                arrays.AnimationAttract[i] = rawGoal ? integrator.VertexAttraction / ClothRawGoalScale : 0f;
                arrays.CollisionRadius[i] = Fe.GetCollisionRadius(node);
                arrays.Friction[i] = MathUtils.Saturate(Fe.GetNodeFriction(node));
                arrays.Drag[i] = MathF.Max(integrator.PointDamping / ClothDragPointDampingScale, 0f);
                arrays.GroundCollision[i] = Fe.IsWorldCollisionNode(node) && IsProxyMeshNode(node)
                    ? MathF.Max(1f - worldFriction, 1e-6f)
                    : 0f;
                arrays.GroundFriction[i] = groundFriction;
                arrays.Gravity[i] = integrator.Gravity;
                arrays.VertexAttraction[i] = integrator.VertexAttraction;
            }

            return arrays;
        }

        private SkinInfluence[] ProxyVertexSkinInfluences(int node, bool isSim)
        {
            if (RecoveredSkinWeights.TryGetValue(node, out var skinInfluences))
            {
                return skinInfluences;
            }

            if (isSim)
            {
                skinInfluences = BuildChainSkinInfluences(node);
            }
            else
            {
                var anchor = ResolveSkinBone(node);
                skinInfluences = anchor is not null ? [new(anchor, 1f)] : [];
            }

            return skinInfluences.Length == 0 && DeferredOffsetSkinWeights.TryGetValue(node, out var offsetWeights)
                ? offsetWeights
                : skinInfluences;
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

        /// <summary>Gets the mesh index of a <c>$cloth_m&lt;N&gt;p&lt;S&gt;</c> name, or -1.</summary>
        private static int ParseProxyMeshIndex(string name)
            => ProxyNameSeparator(name) is var p and >= 0
                && int.TryParse(name.AsSpan(ProxyNamePrefix.Length, p - ProxyNamePrefix.Length), out var index)
                    ? index
                    : -1;

        /// <summary>Gets the vertex slot of a <c>$cloth_m&lt;N&gt;p&lt;S&gt;</c> name, or <see cref="int.MaxValue"/>.</summary>
        private static int ParseProxyVertexIndex(string name)
            => ProxyNameSeparator(name) is var p and >= 0 && int.TryParse(name.AsSpan(p + 1), out var index)
                ? index
                : int.MaxValue;

        private static int ProxyNameSeparator(string name)
            => name.StartsWith(ProxyNamePrefix, StringComparison.Ordinal) ? name.IndexOf('p', ProxyNamePrefix.Length) : -1;

        /// <summary>Gets whether <paramref name="node"/> is a proxy-sheet vertex (<c>$cloth_m&lt;N&gt;p&lt;S&gt;</c>).</summary>
        internal bool IsProxyMeshNode(int node) => ProxyMeshIndexOf(node) >= 0;

        /// <summary>Gets the <c>$cloth_m&lt;N&gt;</c> mesh index of <paramref name="node"/>, or -1.</summary>
        private int ProxyMeshIndexOf(int node)
            => node >= 0 && node < Fe.CtrlNames.Length ? ParseProxyMeshIndex(Fe.CtrlNames[node]) : -1;

        /// <summary>Gets whether <paramref name="node"/> is a free cloth node (<c>$cloth_node_</c>).</summary>
        internal bool IsFreeClothNode(int node)
            => node >= 0 && node < Fe.CtrlNames.Length && Fe.CtrlNames[node].StartsWith(FreeClothNodePrefix, StringComparison.Ordinal);

        /// <summary>
        /// Reorders faces to their SIMD lane order, each in its lane's node order; faces without a lane follow in array order.
        /// </summary>
        private int[][] OrderFacesBySimdLanes(int[][] faces, string simdKey)
        {
            var simd = Fe.Data.GetArray(simdKey);
            if (simd is null || simd.Count == 0 || faces.Length == 0)
            {
                return faces;
            }

            var rows = faces[0].Length;

            var remaining = new Dictionary<string, List<int[]>>();
            foreach (var face in faces)
            {
                GetOrAdd(remaining, SurfaceElementKey(face)).Add(face);
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

                    if (remaining.TryGetValue(SurfaceElementKey(laneNodes), out var list) && list.Count > 0)
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
        private void SortByAuthoredVertexOrder(int[] nodeIndices)
        {
            Array.Sort(nodeIndices, (x, y) =>
            {
                var mx = ParseProxyMeshIndex(Fe.CtrlNames[x]);
                var my = ParseProxyMeshIndex(Fe.CtrlNames[y]);
                if (mx != my)
                {
                    return mx.CompareTo(my);
                }

                var px = ParseProxyVertexIndex(Fe.CtrlNames[x]);
                var py = ParseProxyVertexIndex(Fe.CtrlNames[y]);
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
                if (corner < 0 || corner >= Fe.CtrlNames.Length || !IsProxyNodeName(Fe.CtrlNames[corner]))
                {
                    continue;
                }

                var cornerMesh = ParseProxyMeshIndex(Fe.CtrlNames[corner]);
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
