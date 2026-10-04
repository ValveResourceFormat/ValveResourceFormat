using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// The decompile-side views of a <see cref="FeModel"/>: lookups, filtered and de-duplicated records, and per-node
    /// values resolved against the static node count.
    /// </summary>
    internal sealed class FeModelIndex
    {
        /// <summary>The collision mask of a node that declares none: all sixteen layers.</summary>
        internal const int DefaultNodeCollisionMask = 0xFFFF;

        /// <summary>The number of element arities <c>m_SourceElems</c> leads with a count for.</summary>
        private const int SourceElemArities = 4;

        private readonly FeModel fe;
        private readonly HashSet<int> lockedToParent;
        private readonly HashSet<int> lockedToGoal;
        private List<AnimRod>? animRods;
        private HashSet<int>? twistNodes;

        internal FeModelIndex(FeModel fe)
        {
            this.fe = fe;

            var initPose = fe.InitPose;
            InitPosePositions = [.. initPose.Select(static p => p.Position)];
            InitPoseRotations = [.. initPose.Select(static p => p.Orientation)];

            Quads = ReadNodeIndexArray(fe.Quads.Select(static q => q.Nodes), 4);
            Tris = ReadNodeIndexArray(fe.Tris.Select(static t => t.Nodes), 3);
            (SourceFaces, SourceSprings, SourceTriangleCount) = ReadSourceElems(fe.SourceElems);

            KelagerBends = ReadKelagerBends(fe);
            VertexMaps = ReadVertexMaps(fe);
            Rods = ReadRods(fe);

            (NodeCollisionMasks, WorldCollisionNodes, WorldCollisionFriction) = ReadWorldCollision(fe, NodeCount - fe.StaticNodes);
            AnimStrayRadii = ReadAnimStrayRadii(fe);
            (FitMatrixNodes, FitMatrixTargets) = ReadFitMatrices(fe);

            NodeBaseRecords = [.. fe.NodeBases.Select(static b => (b.Node, new NodeBasis(b.NodeX0, b.NodeX1, b.NodeY0, b.NodeY1)))];
            var nodeBases = new Dictionary<int, NodeBasis>();
            foreach (var (node, basis) in NodeBaseRecords)
            {
                nodeBases[node] = basis;
            }

            NodeBases = nodeBases;

            lockedToParent = [.. fe.LockToParent.Select(static link => link.CtrlChild)];
            lockedToGoal = [.. fe.LockToGoal];

            var followNodeLinks = new Dictionary<int, (int Parent, float Weight)>();
            foreach (var follow in fe.FollowNodes)
            {
                followNodeLinks.TryAdd(follow.ChildNode, (follow.ParentNode, follow.Weight));
            }

            FollowNodeLinks = followNodeLinks;
        }

        /// <summary>A distance constraint between two control nodes (from <c>m_Rods</c>).</summary>
        /// <param name="NodeA">First endpoint control-node index.</param>
        /// <param name="NodeB">Second endpoint control-node index.</param>
        /// <param name="MinDist">Minimum allowed distance.</param>
        /// <param name="MaxDist">Maximum allowed distance.</param>
        /// <param name="Weight0">Share of <paramref name="NodeA"/> in the correction.</param>
        /// <param name="RelaxationFactor">Relaxation factor.</param>
        internal readonly record struct Rod(int NodeA, int NodeB, float MinDist, float MaxDist, float Weight0, float RelaxationFactor)
        {
            /// <summary>Gets whether the rod's minimum and maximum distance differ by more than a relative 1e-4.</summary>
            internal bool IsBanded => MathF.Abs(MinDist - MaxDist) > 1e-4f * MathF.Max(1f, MathF.Abs(MaxDist));

            /// <summary>Gets the rod's two nodes, the lower first.</summary>
            internal (int, int) Pair => ClothReconstruction.UnorderedPair(NodeA, NodeB);

            /// <summary>Gets whether <paramref name="length"/> matches the rest distance <paramref name="rest"/>.</summary>
            internal static bool IsAtRestLength(float length, float rest) => MathF.Abs(length - rest) <= MathF.Max(1e-3f, 1e-4f * rest);
        }

        /// <summary>A rod of <c>m_SimdRodsAnim</c>.</summary>
        /// <param name="NodeA">First node of the lane.</param>
        /// <param name="NodeB">Second node of the lane.</param>
        /// <param name="Weight0">Share of <paramref name="NodeA"/> in the correction, as in <see cref="Rod.Weight0"/>.</param>
        internal readonly record struct AnimRod(int NodeA, int NodeB, float Weight0)
        {
            /// <summary>Gets the rod's two nodes, the lower first.</summary>
            internal (int, int) Pair => ClothReconstruction.UnorderedPair(NodeA, NodeB);
        }

        /// <summary>A node's explicit orientation basis (from <c>m_NodeBases</c>).</summary>
        /// <param name="NodeX0">First control node of the local X axis.</param>
        /// <param name="NodeX1">Second control node of the local X axis.</param>
        /// <param name="NodeY0">First control node of the local Y axis.</param>
        /// <param name="NodeY1">Second control node of the local Y axis.</param>
        internal readonly record struct NodeBasis(int NodeX0, int NodeX1, int NodeY0, int NodeY1);

        /// <summary>A three-node bend constraint (from <c>m_KelagerBends</c>).</summary>
        /// <param name="MidNode">The bent node.</param>
        /// <param name="End0">The first node the bend measures against.</param>
        /// <param name="End1">The second node the bend measures against.</param>
        /// <param name="MidWeight">Solver share of <paramref name="MidNode"/>.</param>
        /// <param name="End0Weight">Solver share of <paramref name="End0"/>.</param>
        /// <param name="End1Weight">Solver share of <paramref name="End1"/>.</param>
        /// <param name="Height">Allowed distance from the bent node to the centroid of the three nodes.</param>
        internal readonly record struct KelagerBend(int MidNode, int End0, int End1,
            float MidWeight, float End0Weight, float End1Weight, float Height);

        /// <summary>A named vertex selection, used to target cloth effects and joint vertex maps.</summary>
        /// <param name="Name">The selection name.</param>
        /// <param name="NameHash">The selection name hash.</param>
        /// <param name="VertexBase">The first control node the selection covers.</param>
        /// <param name="VertexCount">How many consecutive control nodes it covers.</param>
        /// <param name="CenterOfMass">The selection's center of mass.</param>
        /// <param name="Weights">Membership weight of each covered node, 0 to 1, indexed from <paramref name="VertexBase"/>.</param>
        /// <param name="VolumetricSolveStrength">How strongly the selection is solved as a volume.</param>
        /// <param name="ScaleSourceNode">The control node whose scale the selection follows, or -1.</param>
        internal readonly record struct VertexMap(string Name, uint NameHash, int VertexBase, int VertexCount,
            Vector3 CenterOfMass, float[] Weights, float VolumetricSolveStrength = 0f, int ScaleSourceNode = -1)
        {
            /// <summary>Gets how strongly <paramref name="node"/> belongs to this selection, 0 when it does not.</summary>
            internal float WeightOf(int node)
            {
                var index = node - VertexBase;
                return index >= 0 && index < Weights.Length ? Weights[index] : 0f;
            }
        }

        /// <summary>Gets <c>m_nNodeCount</c>, or 0 where it is stored negative.</summary>
        internal int NodeCount => Math.Max(fe.NodeCount, 0);

        /// <summary>Gets <c>m_flDefaultGravityScale</c>, 1 when absent.</summary>
        internal float DefaultGravityScale => fe.DefaultGravityScale ?? 1.0f;

        /// <summary>
        /// Gets the factor the compiler scales rod relaxations by: <c>exp(-m_flDefaultSurfaceStretch)</c>, or 1 where the
        /// stretch is not positive.
        /// </summary>
        internal float SurfaceStretchScale => StretchScale(fe.DefaultSurfaceStretch);

        /// <summary>
        /// Gets the factor the compiler scales <c>m_AnimStrayRadii</c> relaxations by: <c>exp(-m_flDefaultThreadStretch)</c>,
        /// or 1 where the stretch is not positive.
        /// </summary>
        internal float ThreadStretchScale => StretchScale(fe.DefaultThreadStretch);

        /// <summary>Gets the per-node rest positions in model space (from <c>m_InitPose</c>).</summary>
        internal Vector3[] InitPosePositions { get; }

        /// <summary>Gets the per-node rest orientations (from <c>m_InitPose</c>).</summary>
        internal Quaternion[] InitPoseRotations { get; }

        /// <summary>Gets the distance between two nodes' <see cref="InitPosePositions"/>.</summary>
        internal float RestDistance(int a, int b) => Vector3.Distance(InitPosePositions[a], InitPosePositions[b]);

        /// <summary>Gets the cloth surface quads, each as four control-node indices.</summary>
        internal int[][] Quads { get; }

        /// <summary>Gets the cloth surface triangles, each as three control-node indices.</summary>
        internal int[][] Tris { get; }

        /// <summary>Gets whether the cloth has quad or triangle solve elements.</summary>
        internal bool HasSurfaceElements => Quads.Length > 0 || Tris.Length > 0;

        /// <summary>
        /// Gets the authored proxy-mesh faces (from <c>m_SourceElems</c>), as control-node indices in winding order.
        /// </summary>
        internal int[][] SourceFaces { get; }

        /// <summary>Gets how many of <see cref="SourceFaces"/> are triangles.</summary>
        internal int SourceTriangleCount { get; }

        /// <summary>Gets the two-node elements of <c>m_SourceElems</c>, one per authored <c>ClothSpring</c>.</summary>
        internal (int, int)[] SourceSprings { get; }

        /// <summary>
        /// Gets the distance constraints between control nodes (<c>m_Rods</c>). Rods with a missing end or both ends on
        /// one node are left out.
        /// </summary>
        internal Rod[] Rods { get; }

        /// <summary>
        /// Gets the distinct rods of <c>m_SimdRodsAnim</c>. Lanes repeated to fill a SIMD block collapse into one, and
        /// lanes with both ends on one node are left out.
        /// </summary>
        internal IReadOnlyList<AnimRod> AnimRods => animRods ??= BuildAnimRods();

        /// <summary>
        /// Gets the explicit orientation basis of nodes that have one (<c>m_NodeBases</c>), keyed by control-node index.
        /// Where a node has several records, the last one is kept.
        /// </summary>
        internal IReadOnlyDictionary<int, NodeBasis> NodeBases { get; }

        /// <summary>
        /// Gets every <c>m_NodeBases</c> record in array order. Older models can carry several records for one node.
        /// </summary>
        internal IReadOnlyList<(int Node, NodeBasis Basis)> NodeBaseRecords { get; }

        /// <summary>
        /// Gets the per-dynamic-node collision masks: the leading entries of <c>m_TreeCollisionMasks</c>, whose remainder
        /// holds the OR of each subtree. Empty when that array is absent or not <c>2 * dynamicNodes - 1</c> long.
        /// </summary>
        internal int[] NodeCollisionMasks { get; }

        /// <summary>Gets the control nodes that collide with the world (<c>m_WorldCollisionNodes</c>).</summary>
        internal IReadOnlySet<int> WorldCollisionNodes { get; }

        /// <summary>Gets the world and ground friction of each world-colliding node (<c>m_WorldCollisionParams</c>).</summary>
        internal IReadOnlyDictionary<int, (float World, float Ground)> WorldCollisionFriction { get; }

        /// <summary>
        /// Gets the maximum distance each constrained node may stray from its animated position, with its relaxation
        /// factor (<c>m_AnimStrayRadii</c>).
        /// </summary>
        internal IReadOnlyDictionary<int, (float MaxDistance, float RelaxationFactor)> AnimStrayRadii { get; }

        /// <summary>Gets the control nodes driven by a fit matrix (<c>m_FitMatrices</c>).</summary>
        internal IReadOnlySet<int> FitMatrixNodes { get; }

        /// <summary>Gets the control nodes each fit matrix is fit over, keyed by the bone it drives.</summary>
        internal IReadOnlyDictionary<int, int[]> FitMatrixTargets { get; }

        /// <summary>Gets the control nodes named by any twist constraint (<c>m_Twists</c>).</summary>
        internal IReadOnlySet<int> TwistNodes => twistNodes ??= fe.Twists.SelectMany(static twist => (int[])[twist.NodeOrient, twist.NodeEnd]).ToHashSet();

        /// <summary>Gets whether the cloth carries per-node local force or rotation values.</summary>
        internal bool HasPerNodeLocalForce => fe.LocalForces.Length > 0 || fe.LocalRotations.Length > 0;

        /// <summary>Gets whether the cloth carries axial bend edges, which the rigid edge hinge option produces.</summary>
        internal bool HasAxialEdges => fe.AxialEdges.Length > 0;

        /// <summary>Gets the three-node bend constraints (<c>m_KelagerBends</c>), built for chain joints with a stiff hinge.</summary>
        internal IReadOnlyList<KelagerBend> KelagerBends { get; }

        /// <summary>Gets the named vertex selections (<c>m_VertexMaps</c>) with their weights from <c>m_VertexMapValues</c>.</summary>
        internal IReadOnlyList<VertexMap> VertexMaps { get; }

        /// <summary>Gets each follower node's leader and follow weight (<c>m_FollowNodes</c>); the first record of a node wins.</summary>
        internal IReadOnlyDictionary<int, (int Parent, float Weight)> FollowNodeLinks { get; }

        /// <summary>Returns whether a control-node name is a generated cloth proxy node rather than a skeleton bone.</summary>
        internal static bool IsProxyNodeName(string? name)
            => string.IsNullOrEmpty(name) || name.StartsWith('$');

        /// <summary>Returns whether <paramref name="node"/> is a static (pinned) node, with zero inverse mass.</summary>
        internal bool IsStatic(int node)
            => node >= 0 && node < fe.NodeInvMasses.Length && fe.NodeInvMasses[node] == 0f;

        /// <summary>Gets the integrator parameters for <paramref name="node"/>, or a zeroed struct when absent.</summary>
        internal FeModel.FeNodeIntegrator GetIntegrator(int node)
            => node >= 0 && node < fe.NodeIntegrator.Length ? fe.NodeIntegrator[node] : default;

        /// <summary>Gets the world-collision radius for control node <paramref name="node"/>, or 0 when absent.</summary>
        internal float GetCollisionRadius(int node) => DynamicNodeValue(fe.NodeCollisionRadii, node, 0f);

        /// <summary>
        /// Gets the collision mask of control node <paramref name="node"/>, or <see cref="DefaultNodeCollisionMask"/>
        /// when the model records none.
        /// </summary>
        internal int GetNodeCollisionMask(int node) => DynamicNodeValue(NodeCollisionMasks, node, DefaultNodeCollisionMask);

        /// <summary>Gets the world and ground friction for <paramref name="node"/>, or zero for both.</summary>
        internal (float World, float Ground) GetWorldFriction(int node)
            => WorldCollisionFriction.GetValueOrDefault(node);

        /// <summary>Returns whether <paramref name="node"/> collides with the world.</summary>
        internal bool IsWorldCollisionNode(int node) => WorldCollisionNodes.Contains(node);

        /// <summary>Gets the stray radius for <paramref name="node"/>, or 0 when unconstrained.</summary>
        internal float GetStrayRadius(int node) => AnimStrayRadii.GetValueOrDefault(node).MaxDistance;

        /// <summary>
        /// Gets whether <paramref name="node"/> keeps its rotation free. Static nodes are ordered rotation-locked first,
        /// so only the nodes below <see cref="FeModel.RotLockStaticNodes"/> are locked.
        /// </summary>
        internal bool AllowsRotation(int node) => node >= fe.RotLockStaticNodes;

        /// <summary>Gets whether <paramref name="node"/> is held at a fixed offset from its parent (<c>m_LockToParent</c>).</summary>
        internal bool IsLockedToParent(int node) => lockedToParent.Contains(node);

        /// <summary>
        /// Gets whether <paramref name="node"/> is held at its animated goal (<c>m_LockToGoal</c>), the lock a
        /// non-simulated node without a parent takes.
        /// </summary>
        internal bool IsLockedToGoal(int node) => lockedToGoal.Contains(node);

        /// <summary>Gets the friction painted on <paramref name="node"/>, or 0 when it has none.</summary>
        internal float GetNodeFriction(int node) => DynamicNodeValue(fe.DynNodeFriction, node, 0f);

        /// <summary>Gets the local force multiplier of control node <paramref name="node"/>, or 0 when it has none.</summary>
        internal float GetLocalForce(int node) => DynamicNodeValue(fe.LocalForces, node, 0f);

        /// <summary>Gets the local rotation multiplier of control node <paramref name="node"/>, or 0 when it has none.</summary>
        internal float GetLocalRotation(int node) => DynamicNodeValue(fe.LocalRotations, node, 0f);

        /// <summary>Flattens a SIMD <c>nNode</c> block, indexed [row][lane], into one row-major list.</summary>
        internal static List<int> FlattenSimdNodes(int[][] nodes)
        {
            var flat = new List<int>();
            foreach (var row in nodes)
            {
                flat.AddRange(row);
            }

            return flat;
        }

        private static float StretchScale(float stretch) => stretch > 0f ? MathF.Exp(-stretch) : 1f;

        /// <summary>Reads a per-dynamic-node array, which starts past the static nodes, at control node <paramref name="node"/>.</summary>
        private T DynamicNodeValue<T>(T[] values, int node, T fallback)
        {
            var dynamicIndex = node - fe.StaticNodes;
            return dynamicIndex >= 0 && dynamicIndex < values.Length ? values[dynamicIndex] : fallback;
        }

        private List<AnimRod> BuildAnimRods()
        {
            var rods = new List<AnimRod>();
            foreach (var entry in fe.SimdRodsAnim)
            {
                var flat = FlattenSimdNodes(entry.Nodes);
                if (flat.Count < 8)
                {
                    continue;
                }

                var weights = entry.Weight0;
                for (var lane = 0; lane < 4; lane++)
                {
                    var rod = new AnimRod(flat[lane], flat[4 + lane], lane < weights.Length ? weights[lane] : 0.5f);
                    if (rod.NodeA != rod.NodeB && !rods.Contains(rod))
                    {
                        rods.Add(rod);
                    }
                }
            }

            return rods;
        }

        private static List<KelagerBend> ReadKelagerBends(FeModel fe)
        {
            var kelagerBends = new List<KelagerBend>();
            foreach (var bend in fe.KelagerBends)
            {
                var nodes = bend.Nodes;
                var weights = bend.Weights;
                if (nodes.Length >= 3 && weights.Length >= 3)
                {
                    kelagerBends.Add(new KelagerBend(nodes[0], nodes[1], nodes[2],
                        weights[0], weights[1], weights[2], bend.Height0));
                }
            }

            return kelagerBends;
        }

        private static List<VertexMap> ReadVertexMaps(FeModel fe)
        {
            var mapValues = fe.VertexMapValues;
            var vertexMaps = new List<VertexMap>();
            foreach (var map in fe.VertexMaps)
            {
                var count = Math.Max(map.VertexCount, 0);
                var offset = map.MapOffset;
                var weights = new float[count];
                for (var i = 0; i < count && offset + i < mapValues.Length; i++)
                {
                    weights[i] = mapValues[offset + i] / 255f;
                }

                vertexMaps.Add(new VertexMap(map.Name, map.NameHash, map.VertexBase, count, map.CenterOfMass, weights,
                    map.VolumetricSolveStrength, map.ScaleSourceNode));
            }

            return vertexMaps;
        }

        private static Rod[] ReadRods(FeModel fe)
            => [.. fe.Rods.Select(static o =>
            {
                var nodes = o.Nodes;
                return new Rod(
                    nodes.Length > 0 ? nodes[0] : -1,
                    nodes.Length > 1 ? nodes[1] : -1,
                    o.MinDist,
                    o.MaxDist,
                    o.Weight0,
                    o.RelaxationFactor);
            }).Where(static r => r.NodeA >= 0 && r.NodeB >= 0 && r.NodeA != r.NodeB)];

        private static (int[] Masks, HashSet<int> Nodes, Dictionary<int, (float World, float Ground)> Friction) ReadWorldCollision(
            FeModel fe, int dynamicNodeCount)
        {
            var treeMasks = fe.TreeCollisionMasks;
            int[] nodeCollisionMasks = dynamicNodeCount > 0 && treeMasks.Length == (2 * dynamicNodeCount) - 1
                ? treeMasks[..dynamicNodeCount]
                : [];
            var worldCollisionOrder = fe.WorldCollisionNodes;

            var worldFriction = new Dictionary<int, (float World, float Ground)>();
            foreach (var entry in fe.WorldCollisionParams)
            {
                var begin = entry.ListBegin;
                var end = Math.Min(entry.ListEnd, worldCollisionOrder.Length);
                var frictions = (entry.WorldFriction, entry.GroundFriction);
                for (var i = Math.Max(begin, 0); i < end; i++)
                {
                    worldFriction[worldCollisionOrder[i]] = frictions;
                }
            }

            return (nodeCollisionMasks, worldCollisionOrder.ToHashSet(), worldFriction);
        }

        /// <summary>Reads the single-node records of <c>m_AnimStrayRadii</c>, keyed by node.</summary>
        private static Dictionary<int, (float MaxDistance, float RelaxationFactor)> ReadAnimStrayRadii(FeModel fe)
        {
            var strayRadii = new Dictionary<int, (float MaxDistance, float RelaxationFactor)>();
            foreach (var entry in fe.AnimStrayRadii)
            {
                var nodes = entry.Nodes;
                if (nodes.Length >= 2 && nodes[0] == nodes[1])
                {
                    strayRadii[nodes[0]] = (entry.MaxDist, entry.RelaxationFactor);
                }
            }

            return strayRadii;
        }

        /// <summary>Reads the fit-matrix bones and the nodes each fit is taken over, from its <c>m_FitWeights</c> range.</summary>
        private static (HashSet<int> Nodes, Dictionary<int, int[]> Targets) ReadFitMatrices(FeModel fe)
        {
            var fitMatrixNodes = new HashSet<int>();
            var fitTargets = new Dictionary<int, int[]>();
            var fitWeights = fe.FitWeights;
            var rangeBegin = 0;
            foreach (var fit in fe.FitMatrices)
            {
                var bone = fit.Node;
                var rangeEnd = fit.End;
                var targets = new List<int>();
                for (var i = rangeBegin; i < rangeEnd && i < fitWeights.Length; i++)
                {
                    targets.Add(fitWeights[i].Node);
                }

                fitMatrixNodes.Add(bone);
                fitTargets[bone] = [.. targets];
                rangeBegin = rangeEnd;
            }

            return (fitMatrixNodes, fitTargets);
        }

        private static int[][] ReadNodeIndexArray(IEnumerable<int[]> elements, int expectedLength)
        {
            var faces = new List<int[]>();
            foreach (var nodes in elements)
            {
                if (nodes.Length >= expectedLength)
                {
                    faces.Add(nodes[..expectedLength]);
                }
            }

            return [.. faces];
        }

        /// <summary>
        /// Reads <c>m_SourceElems</c>: a count per arity, then that many elements of each arity in turn. Returns nothing
        /// when the counts do not add up to the array length.
        /// </summary>
        private static (int[][] Faces, (int, int)[] Springs, int Triangles) ReadSourceElems(int[] elems)
        {
            if (elems.Length < SourceElemArities)
            {
                return ([], [], 0);
            }

            var counted = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                var count = elems[arity - 1];
                if (count < 0 || count > elems.Length)
                {
                    return ([], [], 0);
                }

                counted += arity * count;
            }

            if (counted != elems.Length)
            {
                return ([], [], 0);
            }

            var faces = new List<int[]>();
            var springs = new List<(int, int)>();
            var triangles = 0;
            var read = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                for (var remaining = elems[arity - 1]; remaining > 0; remaining--, read += arity)
                {
                    if (arity == 1)
                    {
                        continue;
                    }

                    if (arity == 2)
                    {
                        var a = elems[read];
                        var b = elems[read + 1];
                        if (a != b)
                        {
                            springs.Add((a, b));
                        }

                        continue;
                    }

                    // Collapse repeated corners, so a degenerate quad reads as a triangle
                    var corners = new List<int>(arity);
                    for (var c = 0; c < arity; c++)
                    {
                        var node = elems[read + c];
                        if (!corners.Contains(node))
                        {
                            corners.Add(node);
                        }
                    }

                    if (corners.Count >= 3)
                    {
                        faces.Add([.. corners]);

                        if (arity == 3)
                        {
                            triangles++;
                        }
                    }
                }
            }

            return ([.. faces], [.. springs], triangles);
        }
    }
}
