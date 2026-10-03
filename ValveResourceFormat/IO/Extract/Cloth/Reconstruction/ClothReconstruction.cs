using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace ValveResourceFormat.IO
{
    /// <summary>The model skeleton a cloth reconstruction is resolved against.</summary>
    /// <param name="BoneNames">The skeleton's bone names together with the culled control-node bone names.</param>
    /// <param name="BoneParents">Each skeleton bone's parent bone name.</param>
    /// <param name="CulledNodes">The control nodes of <paramref name="CulledBones"/>.</param>
    /// <param name="CulledBones">The control nodes named after bones the compiled skeleton does not contain.</param>
    /// <param name="ChainExtrudeOrigins">The bind position a chain joint's ring is measured from, for bones a scaled proxy skeleton moved.</param>
    internal sealed record ClothSkeletonContext(
        IReadOnlySet<string> BoneNames,
        IReadOnlyDictionary<string, string?> BoneParents,
        IReadOnlySet<int> CulledNodes,
        IReadOnlyList<CulledBone> CulledBones,
        IReadOnlyDictionary<string, Vector3>? ChainExtrudeOrigins);

    /// <summary>A control node named after a bone the compiled skeleton does not contain.</summary>
    internal readonly record struct CulledBone(int Node, string Name);

    /// <summary>The bone transforms that put the skeleton back on the cloth rest pose.</summary>
    /// <param name="BonePositions">The parent-space bone positions written into the vmdl and render-mesh DMX files.</param>
    /// <param name="ProxyBonePositions">The parent-space bone positions written into the proxy DMX joint lists.</param>
    /// <param name="ProxyBoneRotations">The parent-space bone rotations written beside <paramref name="ProxyBonePositions"/>.</param>
    internal sealed record ClothRestPose(
        Dictionary<string, Vector3> BonePositions,
        Dictionary<string, Vector3> ProxyBonePositions,
        Dictionary<string, Quaternion> ProxyBoneRotations);

    /// <summary>One bone's skin weight on a cloth vertex.</summary>
    internal readonly record struct SkinInfluence(string Bone, float Weight);

    /// <summary>Every collision shape recovered from the cloth, and the bones they hang off.</summary>
    internal sealed record ClothCollisionShapes(IReadOnlyList<CollisionCapsule> Capsules, IReadOnlyList<CollisionSphere> Spheres,
        IReadOnlyList<CollisionBox> Boxes, IReadOnlyList<CollisionCapsule> PlanarizedCapsules,
        IReadOnlyList<CollisionBox> PlanarizedBoxes, IReadOnlySet<string> ParentBones);

    /// <summary>
    /// The authoring facts recovered from a compiled <see cref="FeModel"/> for one export, resolved against the model's
    /// skeleton.
    /// </summary>
    internal sealed partial class ClothReconstruction
    {
        /// <summary>Initializes a reconstruction of <paramref name="fe"/>, resolved against <paramref name="context"/> when given.</summary>
        internal ClothReconstruction(FeModel fe, ClothSkeletonContext? context = null)
        {
            Fe = fe;
            Context = context;
            HasCompiledSkelParents = fe.SkelParents.Length > 0;
            SkelParents = HasCompiledSkelParents ? fe.SkelParents : BuildRopeParents(fe.Data, RopeRuns);
            HasCompiledFirstPositionDrivenNode = fe.Data.ContainsKey("m_nFirstPositionDrivenNode");
            FirstPositionDrivenNode = HasCompiledFirstPositionDrivenNode
                ? fe.FirstPositionDrivenNode
                : DeriveFirstPositionDrivenNode(fe.Data, fe.CtrlNames, fe.NodeCount, fe.StaticNodeCount);

            VertexMaps = fe.VertexMaps;
            ZeroVertexSelectionNames = [.. fe.VertexMaps
                .Where(static map => map.VertexCount == 0 && map.Name.Length > 0)
                .Select(static map => map.Name)];
            if (VertexMaps.Count == 0)
            {
                VertexMaps = BuildVertexMapsFromSets();
                vertexMapsFromSets = VertexMaps.Count > 0;
            }

            ReadTwistLinks(fe.TwistRecords);

            if (context is not null)
            {
                SetSkeletonParents(context.BoneParents);
            }
        }

        /// <summary>
        /// Reconstructs <paramref name="fe"/> against <paramref name="skeleton"/>: the culled control-node bones, the
        /// skeleton parents, the vertex sets the model does not redeclare and the rest pose.
        /// </summary>
        /// <param name="fe">The compiled cloth.</param>
        /// <param name="skeleton">The model's skeleton.</param>
        /// <param name="modelFileName">The model's file name without directory or extension.</param>
        internal static ClothReconstruction ForModel(FeModel fe, Skeleton skeleton, string modelFileName)
        {
            var boneNames = skeleton.Bones
                .Select(static bone => bone.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var culledBones = GetCulledBoneCtrls(fe, boneNames);
            var culledNodes = culledBones.Select(static c => c.Node).ToHashSet();
            foreach (var (_, culledName) in culledBones)
            {
                boneNames.Add(culledName);
            }

            var boneParents = skeleton.Bones
                .GroupBy(static bone => bone.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.First().Parent?.Name, StringComparer.OrdinalIgnoreCase);

            var context = new ClothSkeletonContext(boneNames, boneParents, culledNodes, culledBones, null);
            var cloth = new ClothReconstruction(fe, context);
            cloth.DropModelNameVertexSet(modelFileName);
            cloth.DropUnnamedVertexSet();
            cloth.Context = context with { ChainExtrudeOrigins = cloth.BuildClothRestBonePositions(skeleton) };
            return cloth;
        }

        /// <summary>Gets the compiled cloth this reconstruction reads.</summary>
        internal FeModel Fe { get; }

        /// <summary>Gets the skeleton this reconstruction is resolved against, or null when it has none.</summary>
        internal ClothSkeletonContext? Context { get; private set; }

        /// <summary>Gets the bone transforms that put the skeleton back on the cloth rest pose.</summary>
        internal ClothRestPose RestPose { get; } = new(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase),
            new(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Gets the per-node parent node index, or -1 for a root: the compiled <c>m_SkelParents</c>, or where the compile
        /// carries none the parents read off the ropes, follow nodes and twists, or else off the skeleton.
        /// </summary>
        internal int[] SkelParents { get; private set; }

        /// <summary>Gets whether the compile carries <c>m_SkelParents</c>.</summary>
        internal bool HasCompiledSkelParents { get; }

        /// <summary>
        /// Gets the index of the first position-driven node, or <see cref="FeModel.NodeCount"/> when there is none. Derived from the
        /// compiled arrays when the compile omits <c>m_nFirstPositionDrivenNode</c>.
        /// </summary>
        internal int FirstPositionDrivenNode { get; }

        /// <summary>Gets whether the compile wrote <c>m_nFirstPositionDrivenNode</c> itself.</summary>
        internal bool HasCompiledFirstPositionDrivenNode { get; }

        /// <summary>
        /// Gets the named vertex selections the cloth carries: <c>m_VertexMaps</c>, or where it has none the selections
        /// rebuilt from the vertex sets.
        /// </summary>
        internal IReadOnlyList<VertexMap> VertexMaps { get; private set; }

        /// <summary>Gets the names of the skeleton's real bones, used to tell generated nodes without a <c>$</c> prefix apart.</summary>
        internal IReadOnlySet<string>? SkeletonBoneNames => Context?.BoneNames;

        /// <summary>Gets the culled control-node bones, whose names <see cref="SkeletonBoneNames"/> includes.</summary>
        internal IReadOnlySet<int>? CulledBoneCtrlNodes => Context?.CulledNodes;

        /// <summary>Gets each skeleton bone's parent bone name.</summary>
        internal IReadOnlyDictionary<string, string?>? SkeletonBoneParents => Context?.BoneParents;

        /// <summary>Gets the bind position a chain joint's ring is measured from, for bones a scaled proxy skeleton moved.</summary>
        internal IReadOnlyDictionary<string, Vector3>? ChainExtrudeOrigins => Context?.ChainExtrudeOrigins;

        /// <summary>
        /// Builds the bone chains with the <c>ClothChain</c> version each was authored at. The versions read while building
        /// see the sibling hubs of <see cref="BuildBoneChains()"/> when it ran first, and none otherwise.
        /// </summary>
        internal List<BoneChain> BuildDeclaredBoneChains()
        {
            if (declaredChains is null)
            {
                declaredChains = BuildBoneChains(chain => ClothChainVersion(this, chain));
                declaredChainVersions = [];
            }

            return declaredChains;
        }

        private List<BoneChain>? declaredChains;

        private Dictionary<BoneChain, int>? declaredChainVersions;

        /// <summary>Gets the <c>ClothChain</c> version <paramref name="chain"/> was authored at.</summary>
        internal int ChainVersionOf(BoneChain chain)
        {
            if (declaredChainVersions is null)
            {
                return ClothChainVersion(this, chain);
            }

            if (!declaredChainVersions.TryGetValue(chain, out var version))
            {
                version = ClothChainVersion(this, chain);
                declaredChainVersions[chain] = version;
            }

            return version;
        }

        /// <summary>Gets every collision shape recovered from the cloth.</summary>
        internal ClothCollisionShapes CollisionShapes => collisionShapes ??= BuildCollisionShapes();

        private ClothCollisionShapes? collisionShapes;

        private ClothCollisionShapes BuildCollisionShapes()
        {
            var capsules = BuildCollisionCapsules();
            var planarizedCapsules = BuildPlanarizeCapsules();
            var planarizedBoxes = BuildPlanarizeBoxes();
            var spheres = BuildCollisionSpheres();
            var boxes = BuildCollisionBoxes();
            HashSet<string> parentBones = [.. capsules.Select(static c => c.ParentBone)
                .Concat(planarizedCapsules.Select(static c => c.ParentBone))
                .Concat(planarizedBoxes.Select(static b => b.ParentBone))
                .Concat(spheres.Select(static s => s.ParentBone))
                .Concat(boxes.Select(static b => b.ParentBone))
                .OfType<string>()];
            return new ClothCollisionShapes([.. capsules], [.. spheres], [.. boxes], [.. planarizedCapsules], [.. planarizedBoxes],
                parentBones);
        }

        /// <summary>
        /// Gets the control nodes named after bones the skeleton does not contain, excluding generated ring and strip members.
        /// </summary>
        private static List<CulledBone> GetCulledBoneCtrls(FeModel fe, HashSet<string> skeletonBoneNames)
        {
            var result = new List<CulledBone>();
            var generatedChildren = new HashSet<int>();
            foreach (var offset in fe.CtrlOffsets)
            {
                generatedChildren.Add(offset.CtrlChild);
            }

            foreach (var pair in fe.CtrlOsOffsets)
            {
                generatedChildren.Add(pair.CtrlChild);
            }

            for (var node = 0; node < fe.CtrlNames.Length; node++)
            {
                var name = fe.CtrlNames[node];
                if (IsProxyNodeName(name) || skeletonBoneNames.Contains(name)
                    || generatedChildren.Contains(node) || node >= fe.InitPosePositions.Length)
                {
                    continue;
                }

                result.Add(new(node, name));
            }

            return result;
        }

        /// <summary>
        /// Rebuilds <see cref="SkelParents"/> from the bone hierarchy when the compile carries none: each node takes its
        /// nearest ancestor bone that is a control node. A walk stops after as many steps as there are bones.
        /// </summary>
        private void SetSkeletonParents(IReadOnlyDictionary<string, string?> boneParents)
        {
            if (SkelParents.Length > 0 || Fe.CtrlNames.Length == 0 || Fe.NodeCount <= 0)
            {
                return;
            }

            foreach (var name in Fe.CtrlNames)
            {
                if (IsProxyNodeName(name))
                {
                    return;
                }
            }

            var nodeByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var node = 0; node < Fe.CtrlNames.Length && node < Fe.NodeCount; node++)
            {
                nodeByName.TryAdd(Fe.CtrlNames[node], node);
            }

            var parents = new int[Fe.NodeCount];
            Array.Fill(parents, -1);
            var parented = false;

            foreach (var (name, node) in nodeByName)
            {
                var ancestor = boneParents.GetValueOrDefault(name);
                for (var steps = 0; ancestor is not null && steps <= boneParents.Count; steps++)
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

        private HashSet<(int, int)>? animRodPairs;

        /// <summary>
        /// Gets the unordered node pairs of <c>m_SimdRodsAnim</c>, the rods of chain joints declared with
        /// <c>animated_length</c>. These rods appear nowhere else in the file.
        /// </summary>
        private IReadOnlySet<(int, int)> AnimRodPairs => animRodPairs ??= [.. Fe.AnimRods.Select(static rod => UnorderedPair(rod.NodeA, rod.NodeB))];

        /// <summary>Gets every node an <see cref="AnimRodPairs"/> rod ends on.</summary>
        private IReadOnlySet<int> AnimRodNodes => animRodNodes ??= [.. AnimRodPairs.SelectMany(static pair => (int[])[pair.Item1, pair.Item2])];

        private HashSet<int>? animRodNodes;

        /// <summary>Gets whether <c>m_SourceElems</c> records a two-corner element on the pair, in either order.</summary>
        internal bool IsSourceSpring(int a, int b) => SourceSpringSet.Contains((a, b)) || SourceSpringSet.Contains((b, a));

        /// <summary>Gets whether <c>m_SourceElems</c> records a two-corner element from <paramref name="a"/> to <paramref name="b"/>.</summary>
        internal bool HasDirectedSourceSpring(int a, int b) => SourceSpringSet.Contains((a, b));

        private HashSet<(int, int)> SourceSpringSet => sourceSpringSet ??= [.. Fe.SourceSprings];

        private HashSet<(int, int)>? sourceSpringSet;

        /// <summary>Gets every node a quad corner names.</summary>
        private IReadOnlySet<int> QuadNodes => quadNodes ??= [.. Fe.Quads.SelectMany(static quad => quad)];

        private HashSet<int>? quadNodes;

        /// <summary>Gets the <see cref="FeModel.KelagerBends"/> keyed by their bent node, in array order.</summary>
        private Dictionary<int, List<KelagerBend>> KelagerBendsByMidNode
        {
            get
            {
                if (kelagerBendsByMidNode is null)
                {
                    kelagerBendsByMidNode = [];
                    foreach (var bend in Fe.KelagerBends)
                    {
                        GetOrAdd(kelagerBendsByMidNode, bend.MidNode).Add(bend);
                    }
                }

                return kelagerBendsByMidNode;
            }
        }

        private Dictionary<int, List<KelagerBend>>? kelagerBendsByMidNode;

        /// <summary>
        /// Gets the subset of <see cref="FeModel.FitMatrixNodes"/> whose fit covers a proxy sheet vertex
        /// (<c>$cloth_m&lt;N&gt;p&lt;S&gt;</c>), i.e. the bones a proxy sheet back-solves.
        /// </summary>
        internal IReadOnlySet<int> ProxyFitMatrixNodes => proxyFitMatrixNodes ??= ReadProxyFitMatrixNodes();

        private HashSet<int>? proxyFitMatrixNodes;

        private HashSet<int> ReadProxyFitMatrixNodes()
        {
            var nodes = new HashSet<int>();
            var fitWeights = Fe.Data.GetArray("m_FitWeights") ?? [];
            var begin = 0;
            foreach (var fit in Fe.Data.GetArray("m_FitMatrices") ?? [])
            {
                var end = fit.GetInt32Property("nEnd");
                for (var i = begin; i < end && i < fitWeights.Count; i++)
                {
                    if (IsProxyMeshNode(fitWeights[i].GetInt32Property("nNode")))
                    {
                        nodes.Add(fit.GetInt32Property("nNode"));
                    }
                }

                begin = end;
            }

            return nodes;
        }

        /// <summary>
        /// Gets the authored skin weights of back-solved proxy-sheet vertices, keyed by control node, recovered from
        /// <c>m_FitWeights</c>, <c>m_CtrlOffsets</c> and <c>m_CtrlSoftOffsets</c>.
        /// </summary>
        internal IReadOnlyDictionary<int, SkinInfluence[]> RecoveredSkinWeights => SkinWeights.Recovered;

        /// <summary>
        /// Gets the offset-network skin weights of the proxy-sheet vertices <see cref="RecoveredSkinWeights"/> leaves out,
        /// keyed by control node.
        /// </summary>
        internal IReadOnlyDictionary<int, SkinInfluence[]> DeferredOffsetSkinWeights => SkinWeights.Deferred;

        /// <summary>
        /// Gets the proxy mesh indices compiled without back-solving: no <c>m_FitWeights</c> range names their vertices, and
        /// every position-driven bone their simulated vertices bind to is fit over another mesh.
        /// </summary>
        internal IReadOnlySet<int> UnbackSolvedProxyMeshes => SkinWeights.UnbackSolvedMeshes;

        private (Dictionary<int, SkinInfluence[]> Recovered, Dictionary<int, SkinInfluence[]> Deferred,
            HashSet<int> UnbackSolvedMeshes) SkinWeights
        {
            get
            {
                if (skinWeights is null)
                {
                    var recovered = RecoverAuthoredSkinWeights(out var deferred, out var unbackSolvedMeshes);
                    skinWeights = (recovered, deferred, unbackSolvedMeshes);
                }

                return skinWeights.Value;
            }
        }

        private (Dictionary<int, SkinInfluence[]>, Dictionary<int, SkinInfluence[]>, HashSet<int>)? skinWeights;

        /// <summary>Gets the unordered node pairs a twist constraint spans.</summary>
        private HashSet<(int, int)> TwistLinks { get; } = [];

        /// <summary>
        /// Gets the <c>flTwistRelax</c> of each directed (<c>nNodeOrient</c>, <c>nNodeEnd</c>) pair; the last entry wins
        /// where a pair has several.
        /// </summary>
        private Dictionary<(int Orient, int End), float> TwistRelaxByLink { get; } = [];

        /// <summary>
        /// Gets every <c>flTwistRelax</c> of each directed pair in array order, one per chain declaration that wrote it.
        /// </summary>
        private Dictionary<(int Orient, int End), List<float>> TwistRelaxCopies { get; } = [];

        /// <summary>Fills the twist links, the relaxless twist sets and the orient fallback from <paramref name="records"/>.</summary>
        private void ReadTwistLinks(IReadOnlyList<TwistRecord> records)
        {
            foreach (var (orient, end, relax, _) in records)
            {
                TwistLinks.Add(UnorderedPair(orient, end));
                TwistRelaxByLink[(orient, end)] = relax;
                twistOrientFallback.TryAdd(orient, relax);

                GetOrAdd(TwistRelaxCopies, (orient, end)).Add(relax);
            }

            foreach (var ((orient, end), relax) in TwistRelaxByLink)
            {
                if (relax != 0f)
                {
                    continue;
                }

                if (!TwistRelaxByLink.TryGetValue((end, orient), out var back))
                {
                    relaxlessTwistOrients.Add(orient);
                }
                else if (back == 0f)
                {
                    relaxlessTwistNodes.Add(orient);
                    relaxlessTwistNodes.Add(end);
                }
            }
        }

        private static int[] BuildRopeParents(KVObject data, IReadOnlyList<int[]> ropeRuns)
        {
            var nodeCount = data.GetInt32Property("m_nNodeCount");
            if (nodeCount <= 0)
            {
                return [];
            }

            var parents = new int[nodeCount];
            Array.Fill(parents, -1);
            var parented = false;

            void Adopt(int node, int parent)
            {
                if (node >= 0 && node < nodeCount && parent >= 0 && parent < nodeCount
                    && node != parent && parents[node] < 0 && !Descends(parent, node))
                {
                    parents[node] = parent;
                    parented = true;
                }
            }

            bool Descends(int node, int ancestor)
            {
                for (var p = parents[node]; p >= 0; p = parents[p])
                {
                    if (p == ancestor)
                    {
                        return true;
                    }
                }

                return false;
            }

            foreach (var run in ropeRuns)
            {
                for (var i = 1; i < run.Length; i++)
                {
                    Adopt(run[i], run[i - 1]);
                }
            }

            foreach (var follow in data.GetArray("m_FollowNodes") ?? [])
            {
                Adopt(follow.GetInt32Property("nChildNode"), follow.GetInt32Property("nParentNode"));
            }

            var names = data.GetArray<string>("m_CtrlName") ?? [];
            var twists = data.GetArray("m_Twists") ?? [];
            for (var k = 0; k < twists.Count; k++)
            {
                var orient = twists[k].GetInt32Property("nNodeOrient");
                var end = twists[k].GetInt32Property("nNodeEnd");
                var paired = k + 1 < twists.Count && twists[k + 1].GetInt32Property("nNodeOrient") == end
                    && twists[k + 1].GetInt32Property("nNodeEnd") == orient;
                if (orient >= 0 && end >= 0 && orient < names.Length && end < names.Length
                    && !IsProxyNodeName(names[orient]) && !IsProxyNodeName(names[end]))
                {
                    if (paired)
                    {
                        Adopt(end, orient);
                    }
                    else
                    {
                        Adopt(orient, end);
                    }
                }

                if (paired)
                {
                    k++;
                }
            }

            return parented ? parents : [];
        }

        /// <summary>Gets the pair with its lower node first.</summary>
        internal static (int, int) UnorderedPair(int a, int b) => a < b ? (a, b) : (b, a);

        /// <summary>Gets the value under <paramref name="key"/>, adding an empty one where there is none.</summary>
        internal static TValue GetOrAdd<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key)
            where TKey : notnull
            where TValue : class, new()
        {
            ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out _);
            return value ??= new TValue();
        }

        /// <summary>Gets the <see cref="SkelParents"/> entry of <paramref name="node"/>, or -1 when it has none.</summary>
        private int SkelParentOf(int node) => node >= 0 && node < SkelParents.Length ? SkelParents[node] : -1;

        /// <summary>Gets whether <paramref name="node"/> carries a non-zero inverse mass.</summary>
        private bool Simulates(int node) => InverseMassOf(node) != 0f;

        /// <summary>Gets whether <paramref name="node"/> is a generated <c>$cc</c> chain ring node.</summary>
        private bool IsRingNode(int node) => node >= 0 && node < Fe.CtrlNames.Length && Fe.CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal);

        /// <summary>Gets whether the node is position-driven (back-solved rather than simulated).</summary>
        internal bool IsPositionDriven(int node) => node >= FirstPositionDrivenNode;

        private static int DeriveFirstPositionDrivenNode(KVObject data, string[] ctrlNames, int nodeCount, int staticNodes)
        {
            var driven = new HashSet<int>();

            foreach (var fit in data.GetArray("m_FitMatrices") ?? [])
            {
                driven.Add(fit.GetInt32Property("nNode"));
            }

            foreach (var offset in data.GetArray("m_ReverseOffsets") ?? [])
            {
                driven.Add(offset.GetInt32Property("nBoneCtrl"));
            }

            var ringSides = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in ctrlNames)
            {
                if (!name.StartsWith("$cc", StringComparison.Ordinal))
                {
                    continue;
                }

                var split = name.LastIndexOf('_');
                if (split <= 3 || !int.TryParse(name.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                var owner = name[3..split];
                ringSides[owner] = ringSides.GetValueOrDefault(owner) + 1;
            }

            foreach (var (owner, sides) in ringSides)
            {
                var joint = sides >= 2 ? Array.IndexOf(ctrlNames, owner) : -1;
                if (joint >= 0)
                {
                    driven.Add(joint);
                }
            }

            var first = nodeCount;
            while (first > staticNodes && driven.Contains(first - 1))
            {
                first--;
            }

            return first;
        }

        /// <summary>
        /// Gets the parent control node of <paramref name="node"/>, or -1 for a root: its skeleton parent,
        /// or on an original that ships no <c>m_SkelParents</c> the parent its ctrl offset names.
        /// </summary>
        private int ParentNodeOf(int node)
        {
            var parent = SkelParentOf(node);
            if (parent >= 0 || HasCompiledSkelParents)
            {
                return parent;
            }

            if (offsetParentByNode is null)
            {
                offsetParentByNode = new Dictionary<int, int>(Fe.CtrlOffsets.Length);
                foreach (var off in Fe.CtrlOffsets)
                {
                    offsetParentByNode[off.CtrlChild] = off.CtrlParent;
                }
            }

            return offsetParentByNode.GetValueOrDefault(node, -1);
        }

        private Dictionary<int, int>? offsetParentByNode;

        private float InverseMassOf(int node)
            => node >= 0 && node < Fe.NodeInvMasses.Length ? Fe.NodeInvMasses[node] : 0f;

        /// <summary>The maximum length a rod that is not length-limited at all is given.</summary>
        internal const float UnboundedRodDistance = 16384f;

        /// <summary>The prefix of a control node created for an authored free-standing <c>ClothNode</c>.</summary>
        internal const string FreeClothNodePrefix = "$cloth_node_";

        /// <summary>
        /// Gets, per control node, whether its goal values are exported through the raw attraction paints instead of the
        /// goal-strength pair.
        /// </summary>
        private bool[] RawGoalPaintNodes => rawGoalPaintNodes ??= BuildRawGoalPaintNodes();

        private bool[]? rawGoalPaintNodes;

        /// <summary>
        /// Gets each node's parent along the <c>m_Ropes</c> runs alone, without the <c>m_FollowNodes</c> fallback of
        /// <see cref="BuildRopeParents"/>.
        /// </summary>
        internal IReadOnlyDictionary<int, int> RopeRunParents => ropeRunParents ??= BuildRopeRunParents();

        private Dictionary<int, int>? ropeRunParents;

        private Dictionary<int, int> BuildRopeRunParents()
        {
            var parents = new Dictionary<int, int>();
            foreach (var run in RopeRuns)
            {
                for (var i = 1; i < run.Length; i++)
                {
                    parents.TryAdd(run[i], run[i - 1]);
                }
            }

            return parents;
        }

        /// <summary>
        /// Gets the node runs of <c>m_Ropes</c>, whose first <c>m_nRopeCount</c> entries are the runs' exclusive end offsets.
        /// </summary>
        private IReadOnlyList<int[]> RopeRuns => ropeRuns ??= ReadRopeRuns(Fe.Data);

        private List<int[]>? ropeRuns;

        private static List<int[]> ReadRopeRuns(KVObject data)
        {
            var runs = new List<int[]>();
            var ropeCount = data.GetInt32Property("m_nRopeCount");
            var ropes = data.GetIntegerArray("m_Ropes");
            if (ropeCount <= 0 || ropes.Length <= ropeCount)
            {
                return runs;
            }

            var begin = ropeCount;
            for (var rope = 0; rope < ropeCount; rope++)
            {
                var end = Math.Min((int)ropes[rope], ropes.Length);
                var run = new int[Math.Max(end - begin, 0)];
                for (var i = 0; i < run.Length; i++)
                {
                    run[i] = (int)ropes[begin + i];
                }

                runs.Add(run);
                begin = end;
            }

            return runs;
        }
    }
}
