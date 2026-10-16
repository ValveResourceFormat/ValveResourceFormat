using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Synthetic cloth models shared by several cloth test classes.</summary>
    public abstract class FeModelTestModels
    {
        /// <summary>A one-wide rope of three joints under a static root, each joint with a one-node ring and <paramref name="nodeBase"/>.</summary>
        private protected static FeModelBuilder OneWideRope(FeNodeBase nodeBase) => new()
        {
            Names = ["root", "j1", "$ccj1_0", "j2", "$ccj2_0", "j3", "$ccj3_0"],
            StaticNodes = 1,
            Parents = [-1, 0, 1, 1, 3, 3, 5],
            Positions = [new(0f, 0f, 10f), new(0f, 0f, 0f), new(3f, 0f, 0f), new(0f, 0f, -10f), new(3f, 0f, -10f), new(0f, 0f, -20f), new(3f, 0f, -20f)],
            SourceElems = [0, 0, 0, 2, 1, 2, 4, 3, 3, 4, 6, 5],
            NodeBases = [nodeBase],
        };

        private protected static BoneChain TwistedRopeChain()
        {
            var chain = new BoneChain { RootBone = "j1" };
            chain.Joints.Add(new BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1 });
            chain.Joints.Add(new BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f });
            chain.Joints.Add(new BoneChainJoint { Node = 3, Name = "j3", ParentNode = 2, InvMass = 1f });
            return chain;
        }

        /// <summary>A four-joint chain with no static node, every node position-driven, held by <paramref name="rods"/>.</summary>
        private protected static FeModelBuilder StretchlessChain(params FeRodConstraint[] rods) => new()
        {
            Names = ["j0", "j1", "j2", "j3"],
            Parents = [-1, 0, 1, 2],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f)],
            FirstPositionDrivenNode = 4,
            Rods = rods,
        };

        /// <summary>A coattail of four joints, each with a one-node extrude ring, the first joint and its ring static.</summary>
        private protected static FeModelBuilder Coattail => new()
        {
            Names = ["coattail_0_L", "$cccoattail_0_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_2_L", "$cccoattail_2_L_0", "coattail_end_L",
                "$cccoattail_end_L_0"],
            StaticNodes = 2,
            Parents = [-1, 0, 0, 2, 2, 4, 4, 6],
            Poses =
            [
                Pose(-8.915481f, 4.000124f, 65.447983f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-10.723646f, 4.561181f, 66.092773f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-11.695464f, 4.267121f, 57.419937f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-13.473376f, 4.824905f, 58.146507f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-14.808016f, 4.637866f, 49.519089f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-16.587204f, 5.195801f, 50.242416f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-17.907341f, 5.004471f, 41.612736f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-19.686529f, 5.562407f, 42.336063f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
            ],
            SourceElems = [0, 0, 0, 6, 5, 4, 6, 7, 4, 5, 7, 6, 3, 2, 4, 5, 2, 3, 5, 4, 1, 0, 2, 3, 0, 1, 3, 2],
        };

        /// <summary>
        /// <see cref="Coattail"/> with explicit masses 1, 2 and 1 down the chain and rod weights that state them.
        /// </summary>
        private protected static FeModelBuilder ExplicitMassChain => Coattail with
        {
            InvMasses = [0f, 0f, 1f, 1f, 2f, 2f, 1f, 1f],
            Rods =
            [
                RigidRod(0, 2, 8.499948f, 1f, 0f),
                RigidRod(0, 3, 8.646747f, 1f, 0f),
                RigidRod(1, 2, 8.732066f, 1f, 0f),
                RigidRod(1, 3, 8.412711f, 1f, 0f),
                RigidRod(2, 4, 8.499931f, 1f, 0.333333f),
                RigidRod(2, 5, 8.735466f, 1f, 0.333333f),
                RigidRod(3, 4, 8.732044f, 1f, 0.333333f),
                RigidRod(3, 5, 8.503419f, 1f, 0.333333f),
                RigidRod(2, 3, 2f),
                RigidRod(4, 6, 8.500037f, 1f, 0.666667f),
                RigidRod(4, 7, 8.732154f, 1f, 0.666667f),
                RigidRod(5, 6, 8.732168f, 1f, 0.666667f),
                RigidRod(5, 7, 8.500037f, 1f, 0.666667f),
                RigidRod(4, 5, 2.000001f),
                RigidRod(6, 7, 2.000001f),
            ],
        };

        /// <summary>
        /// <see cref="Coattail"/> with a suspender from each static node to every joint and ring, at its natural relaxation.
        /// </summary>
        private protected static FeModelBuilder SuspenderAtNaturalRelaxation => Coattail with
        {
            InvMasses = [0f, 0f, 0.002328f, 0.002343f, 0.001772f, 0.001774f, 0.00178f, 0.001781f],
            Rods =
            [
                RigidRod(0, 2, 8.499948f, 1f, 0f),
                RigidRod(0, 3, 8.646747f, 1f, 0f),
                RigidRod(0, 4, 16.995832f, 1f, 0f),
                RigidRod(0, 5, 17.073202f, 1f, 0f),
                RigidRod(0, 6, 25.49473f, 1f, 0f),
                RigidRod(0, 7, 25.546371f, 1f, 0f),
                RigidRod(1, 2, 8.732066f, 1f, 0f),
                RigidRod(1, 3, 8.412711f, 1f, 0f),
                RigidRod(1, 4, 17.06971f, 1f, 0f),
                RigidRod(1, 5, 16.912064f, 1f, 0f),
                RigidRod(1, 6, 25.516155f, 1f, 0f),
                RigidRod(1, 7, 25.410963f, 1f, 0f),
                RigidRod(2, 3, 2f),
                RigidRod(4, 2, 8.499931f),
                RigidRod(5, 2, 8.735466f),
                RigidRod(4, 3, 8.732044f),
                RigidRod(5, 3, 8.503419f),
                RigidRod(4, 5, 2.000001f),
                RigidRod(6, 4, 8.500037f),
                RigidRod(7, 4, 8.732154f),
                RigidRod(6, 5, 8.732168f),
                RigidRod(7, 5, 8.500037f),
                RigidRod(6, 7, 2.000001f),
                RigidRod(0, 2, 8.499948f, 1f, 0f),
                RigidRod(0, 3, 8.646747f, 1f, 0f),
                RigidRod(1, 2, 8.732066f, 1f, 0f),
                RigidRod(1, 3, 8.412711f, 1f, 0f),
            ],
        };

        /// <summary>A free 3x3 proxy sheet 10 units on a side, its four quads as source elements and no rods.</summary>
        private protected static FeModelBuilder ThreeByThreeSheet => new()
        {
            Names = [.. Enumerable.Range(0, 9).Select(static node => $"$cloth_m0p{node}")],
            Positions = [.. Enumerable.Range(0, 9).Select(static node => new Vector3((node % 3) * 10f, 0f, -(node / 3) * 10f))],
            SourceElems = [0, 0, 0, 4, 0, 1, 4, 3, 1, 2, 5, 4, 3, 4, 7, 6, 4, 5, 8, 7],
        };

        /// <summary>Two coattail joints, the first static, with a twist each way relaxed by <paramref name="toChild"/> and <paramref name="toRoot"/>.</summary>
        private protected static FeModelBuilder TwistPair(float toChild, float toRoot) => new()
        {
            Names = ["coattail_0_L", "coattail_1_L"],
            StaticNodes = 1,
            Parents = [-1, 0],
            Twists = [Twist(0, 1, toChild, 1f), Twist(1, 0, toRoot, 0f)],
        };

        /// <summary>A rope of a static root and two joints 10 apart, held by rigid rods.</summary>
        private protected static FeModelBuilder ThreeJointRope => new()
        {
            Names = ["root", "j1", "j2"],
            StaticNodes = 1,
            Parents = [-1, 0, 1],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
            Rods = [RigidRod(0, 1, 10f), RigidRod(1, 2, 10f)],
        };

        /// <summary>
        /// A coattail of four joints, each with a one-node extrude ring, the first joint and its ring static, and a free
        /// cloth node under <c>spine_2</c> held to <c>coattail_1_L</c> by a rod.
        /// </summary>
        private protected static FeModelBuilder FreeNodeSprungToJoint => new()
        {
            Names = ["coattail_0_L", "$cccoattail_0_L_0", "coattail_1_L", "$cccoattail_1_L_0", "$cloth_node_node_b", "coattail_2_L",
                "$cccoattail_2_L_0", "coattail_end_L", "$cccoattail_end_L_0", "spine_2"],
            StaticNodes = 2,
            InvMasses = [0f, 0f, 0.00227f, 0.003444f, 0.006724f, 0.003428f, 0.003427f, 0.0065f, 0.0065f, 1f],
            Parents = [-1, 0, 0, 2, 9, 2, 5, 5, 7, -1],
            Poses =
            [
                Pose(-8.915481f, 4.000124f, 65.447983f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-10.723646f, 4.561181f, 66.092773f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-11.695464f, 4.267121f, 57.419937f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-13.473376f, 4.824905f, 58.146507f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-2.814225f, -7.999896f, 68.199326f, -0.435361f, -0.55719f, -0.55719f, 0.435361f),
                Pose(-14.808016f, 4.637866f, 49.519089f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-16.587204f, 5.195801f, 50.242416f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-17.907341f, 5.004471f, 41.612736f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-19.686529f, 5.562407f, 42.336063f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-2.814222f, 0.000104f, 68.199318f, -0.435361f, -0.55719f, -0.55719f, 0.435361f),
            ],
            CtrlOffsets =
            [
                Offset(0, 1, 0f, 2.000001f, -0f),
                Offset(2, 3, -0f, 2f, 0f),
                Offset(9, 4, 0.000003f, 0.000001f, -8.000001f),
                Offset(5, 6, -0.000001f, 2.000001f, -0.000001f),
                Offset(7, 8, -0.000001f, 2.000001f, -0.000001f),
            ],
            Rods =
            [
                RigidRod(0, 2, 8.499948f, 1f, 0f),
                RigidRod(0, 3, 8.646747f, 1f, 0f),
                RigidRod(1, 2, 8.732066f, 1f, 0f),
                RigidRod(1, 3, 8.412711f, 1f, 0f),
                RigidRod(2, 3, 2f),
                RigidRod(2, 4, 18.58901f),
                RigidRod(2, 5, 8.499931f),
                RigidRod(2, 6, 8.735466f),
                RigidRod(3, 5, 8.732044f),
                RigidRod(3, 6, 8.503419f),
                RigidRod(5, 6, 2.000001f),
                RigidRod(5, 7, 8.500037f),
                RigidRod(5, 8, 8.732154f),
                RigidRod(6, 7, 8.732168f),
                RigidRod(6, 8, 8.500037f),
                RigidRod(7, 8, 2.000001f),
            ],
            SourceElems = [0, 1, 0, 6, 4, 2, 6, 5, 7, 8, 5, 6, 8, 7, 3, 2, 5, 6, 2, 3, 6, 5, 1, 0, 2, 3, 0, 1, 3, 2],
        };

        /// <summary>
        /// A gravity effect of strength (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>), recording
        /// <paramref name="node"/> where given.
        /// </summary>
        private protected static FeEffectDesc Gravity(string name, uint hash, int? node, float x, float y, float z)
        {
            var parameters = node is { } n
                ? Object(("Node", n), ("Strength", Floats(x, y, z)))
                : Object(("Strength", Floats(x, y, z)));
            return Effect(name, hash, 4, parameters);
        }

        /// <summary>A proxy mesh over <paramref name="nodes"/> with every other stream zero and no skin influences.</summary>
        private protected static ProxyMesh Proxy(int[] nodes, float[] clothEnable, List<int[]> faces, Vector3[]? positions = null)
            => new()
            {
                NodeIndices = nodes,
                Positions = positions ?? new Vector3[nodes.Length],
                ClothEnable = clothEnable,
                GoalStrength = new float[nodes.Length],
                GoalDamping = new float[nodes.Length],
                CollisionRadius = new float[nodes.Length],
                Friction = new float[nodes.Length],
                Drag = new float[nodes.Length],
                GroundCollision = new float[nodes.Length],
                GroundFriction = new float[nodes.Length],
                Gravity = new float[nodes.Length],
                VertexAttraction = new float[nodes.Length],
                SkinInfluences = [.. nodes.Select(static _ => Array.Empty<SkinInfluence>())],
                Faces = faces,
            };

        /// <summary>
        /// Resolves <paramref name="cloth"/> against a skeleton whose bones have <paramref name="boneParents"/> and are
        /// named <paramref name="boneNames"/>, every control-node name when not given.
        /// </summary>
        private protected static ClothReconstruction WithSkeleton(ClothReconstruction cloth, IReadOnlyDictionary<string, string?>? boneParents = null,
            IEnumerable<string>? boneNames = null)
            => new(cloth.Fe, new ClothSkeletonContext((boneNames ?? cloth.Fe.CtrlName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                boneParents ?? new Dictionary<string, string?>(), new HashSet<int>(), [], null));

        protected static KVObject EffectParentNode(string bone, bool isStatic) => KVHelpers.MakeNode("ClothNode",
            ("name", bone), ("cloth_node_root_bone", bone), ("is_static_node", isStatic));

        /// <summary>
        /// A static root carrying a one-wide arm <c>a</c> and a two-wide arm <c>b</c>, each two joints long, whose ring nodes
        /// are placed and grouped by the flags.
        /// </summary>
        private protected static FeModelBuilder TwoVersionTree(bool rootRingFirst, bool thinTipGrouped, bool wideLeafGrouped,
            bool rootRotationLocked = false, bool rootFitsFirstJoint = false)
        {
            List<(string Name, string? Parent, Vector3 Position)> nodes = [("root", null, Vector3.Zero)];
            (string Name, string? Parent, Vector3 Position) rootRing = ("$ccroot_0", "root", new Vector3(0f, 2f, 0f));
            if (rootRingFirst || rootRotationLocked)
            {
                nodes.Add(rootRing);
            }

            nodes.Add(("a0", "root", new Vector3(10f, 0f, 0f)));
            nodes.Add(("$cca0_0", "a0", new Vector3(10f, 2f, 0f)));
            if (!rootRingFirst && !rootRotationLocked)
            {
                nodes.Add(rootRing);
            }

            nodes.AddRange([
                ("$ccb0_0", "b0", new Vector3(-10f, 2f, 0f)), ("$ccb0_1", "b0", new Vector3(-10f, -2f, 0f)),
                ("b0", "root", new Vector3(-10f, 0f, 0f)), ("a1", "a0", new Vector3(10f, 0f, -10f)),
                ("$cca1_0", "a1", new Vector3(10f, 2f, -10f)), ("$ccb1_0", "b1", new Vector3(-10f, 2f, -10f)),
                ("$ccb1_1", "b1", new Vector3(-10f, -2f, -10f)), ("b1", "b0", new Vector3(-10f, 0f, -10f)),
            ]);

            var names = nodes.ConvertAll(static node => node.Name);
            int At(string name) => names.IndexOf(name);

            var offsets = new List<FeNodeReverseOffset>();
            if (thinTipGrouped)
            {
                offsets.Add(ReverseOffset(At("a1"), At("$cca1_0"), 0f, 2f, 0f));
            }

            if (wideLeafGrouped)
            {
                offsets.Add(ReverseOffset(At("b1"), At("$ccb1_0"), 0f, 2f, 0f));
            }

            return new FeModelBuilder
            {
                Names = [.. names],
                StaticNodes = 7,
                RotLockStaticNodes = rootRotationLocked ? 2 : 0,
                Parents = [.. nodes.Select(node => node.Parent is null ? -1 : At(node.Parent))],
                InvMasses = [.. nodes.Select(static (_, i) => i < 7 ? 0f : 1f)],
                Positions = [.. nodes.Select(static node => node.Position)],
                LockToGoal = [0],
                LockToParent = [Offset(0, At("a0"), 10f, 0f, 0f), Offset(0, At("b0"), -10f, 0f, 0f)],
                NodeBases = [NodeBase(At("a0"), At("a0"), At("$cca0_0"), At("b0"), At("$ccb0_0"))],
                FitMatrices = rootFitsFirstJoint ? [FitMatrix(0, 4, 4)] : null,
                FitWeights = rootFitsFirstJoint ? [.. "a0 $cca0_0 b0 $ccb0_0".Split(' ').Select(name => FitWeight(At(name)))] : null,
                ReverseOffsets = [.. offsets],
            };
        }

        /// <summary>
        /// Two proxy sheets and a tip bone fit over mesh 1's vertices; <paramref name="firstPositionDriven"/> is
        /// <c>m_nFirstPositionDrivenNode</c>.
        /// </summary>
        private protected static FeModelBuilder SheetFitOverTipBone(int? firstPositionDriven) => new()
        {
            Names = ["bone_0", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m1p0", "$cloth_m1p1", "$cloth_m1p2", "tip"],
            StaticNodes = 1,
            RotLockStaticNodes = 1,
            FirstPositionDrivenNode = firstPositionDriven,
            Parents = [-1, 0, 0, 0, 7, 7, 7, 0],
            Positions = [new(0f, 0f, 0f), new(4f, 0f, -10f), new(8f, 0f, -10f), new(4f, 0f, -20f), new(-4f, 0f, -10f), new(-8f, 0f, -10f),
                new(-4f, 0f, -20f), new(0f, 0f, -8f)],
            Tris = [Tri(1, 2, 3), Tri(4, 5, 6)],
            CtrlOffsets =
            [
                Offset(0, 1, 4f, 0f, -10f),
                Offset(0, 2, 8f, 0f, -10f),
                Offset(0, 3, 4f, 0f, -20f),
                Offset(7, 4, -4f, 0f, -2f),
                Offset(7, 5, -8f, 0f, -2f),
                Offset(7, 6, -4f, 0f, -12f),
            ],
            FitMatrices = [FitMatrix(7, 3, 0)],
            FitWeights = [FitWeight(4), FitWeight(5), FitWeight(6)],
        };

        /// <summary>
        /// Three face-kept quads under a static top row and banded rods across the middle quad at <paramref name="weight"/>.
        /// </summary>
        private protected static FeModelBuilder FoldedSheetModel(float weight) => new()
        {
            Names = ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7"],
            StaticNodes = 2,
            InvMasses = [0f, 0f, 0.1f, 0.1f, 0.08f, 0.08f, 0.05f, 0.05f],
            Positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 0f, -2f), new(1f, 0f, -2f), new(0f, 0f, -4f), new(1f, 0f, -4f),
                new(0f, 0f, -6f), new(1f, 0f, -6f)],
            Quads =
            [
                Quad(0, 1, 3, 2, 0f, [new(0f, 0f, 0f, 0f), new(0f, 0f, 0f, 0f), new(0f, 0f, 0f, 0.5f), new(0f, 0f, 0f, 0.5f)]),
                Quad(2, 3, 5, 4, 0f, [new(0f, 0f, 0f, 0.25f), new(0f, 0f, 0f, 0.25f), new(0f, 0f, 0f, 0.25f), new(0f, 0f, 0f, 0.25f)]),
                Quad(4, 5, 7, 6, 0f, [new(0f, 0f, 0f, 0.25f), new(0f, 0f, 0f, 0.25f), new(0f, 0f, 0f, 0.25f), new(0f, 0f, 0f, 0.25f)]),
            ],
            Rods = [Rod(2, 6, 3.5f, 4f, weight), Rod(3, 7, 3.5f, 4f, weight)],
        };
    }
}
