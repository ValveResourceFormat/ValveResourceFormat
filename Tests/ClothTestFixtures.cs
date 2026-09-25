using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace Tests
{
    /// <summary>Synthetic cloth fixtures shared by several cloth test classes.</summary>
    public abstract class ClothTestFixtures
    {
        protected static string OneWideRopeDocument(string nodeBase) => SyntheticCloth.Document(
            ["root", "j1", "$ccj1_0", "j2", "$ccj2_0", "j3", "$ccj3_0"], staticNodes: 1, parents: [-1, 0, 1, 1, 3, 3, 5],
            poses: [new(0f, 0f, 10f), new(0f, 0f, 0f), new(3f, 0f, 0f), new(0f, 0f, -10f), new(3f, 0f, -10f), new(0f, 0f, -20f),
                new(3f, 0f, -20f)],
            body: $$"""
                m_SourceElems = [ 0, 0, 0, 2, 1, 2, 4, 3, 3, 4, 6, 5 ]
                m_NodeBases = [ { {{nodeBase}} } ]
                """);

        private protected static FeModel.BoneChain TwistedRopeChain()
        {
            var chain = new FeModel.BoneChain { RootBone = "j1" };
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1 });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 3, Name = "j3", ParentNode = 2, InvMass = 1f });
            return chain;
        }

        protected static FeModel StretchlessChain(string rods) => SyntheticCloth.Model(
            ["j0", "j1", "j2", "j3"], staticNodes: 0, parents: [-1, 0, 1, 2],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f)],
            body: $$"""
                m_nFirstPositionDrivenNode = 4
                m_Rods =
                [
                    {{rods}}
                ]
                """);

        protected static string ExplicitMassChainText => SyntheticCloth.Fixture("cloth_chain_explicit_mass.kv3");

        protected static string SuspenderAtNaturalRelaxationText => SyntheticCloth.Fixture("cloth_chain_natural_suspender.kv3");

        protected static string VertexMapEntry(string name, uint hash, int offset, int vertexBase, int count, float volumetric = 0f)
            => $"{{ sName = \"{name}\" nNameHash = {hash} nVertexBase = {vertexBase} nVertexCount = {count} nMapOffset = {offset} "
                + $"vCenterOfMass = [ 0.0, 0.0, 0.0 ] flVolumetricSolveStrength = {SyntheticCloth.Num(volumetric)} nScaleSourceNode = -1 }},";

        protected static FeModel TwistPair(string toChild, string toRoot) => SyntheticCloth.Model(
            ["coattail_0_L", "coattail_1_L"], staticNodes: 1, parents: [-1, 0], body: $$"""
                m_Twists =
                [
                    { nNodeOrient = 0 nNodeEnd = 1 flTwistRelax = {{toChild}} flSwingRelax = 1.0 },
                    { nNodeOrient = 1 nNodeEnd = 0 flTwistRelax = {{toRoot}} flSwingRelax = 0.0 },
                ]
                """);

        protected static KVObject EffectParentNode(string bone, bool isStatic) => KVHelpers.MakeNode("ClothNode",
            ("name", bone), ("cloth_node_root_bone", bone), ("is_static_node", isStatic));

        protected static string TwoVersionTree(bool rootRingFirst, bool thinTipGrouped, bool wideLeafGrouped,
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

            var offsets = new List<string>();
            if (thinTipGrouped)
            {
                offsets.Add($"{{ vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = {At("a1")} nTargetNode = {At("$cca1_0")} }},");
            }

            if (wideLeafGrouped)
            {
                offsets.Add($"{{ vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = {At("b1")} nTargetNode = {At("$ccb1_0")} }},");
            }

            var rootFit = rootFitsFirstJoint
                ? "m_FitMatrices = [ { bone = [ 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 ] vCenter = [ 0.0, 0.0, 0.0 ] nEnd = 4 nNode = 0 nBeginDynamic = 4 } ]"
                    + " m_FitWeights = [ " + string.Concat("a0 $cca0_0 b0 $ccb0_0".Split(' ')
                        .Select(name => "{ flWeight = 1.0 nNode = " + At(name) + " nDummy = 0 }, ")) + "]"
                : string.Empty;

            return $$"""
                {
                    m_CtrlName = [ {{string.Join(", ", names.Select(static name => '"' + name + '"'))}} ]
                    m_SkelParents = [ {{string.Join(", ", nodes.Select(node => node.Parent is null ? -1 : At(node.Parent)))}} ]
                    m_nNodeCount = {{nodes.Count}}
                    m_nStaticNodes = 7
                    m_nRotLockStaticNodes = {{(rootRotationLocked ? 2 : 0)}}
                    m_NodeInvMasses = [ {{string.Join(", ", nodes.Select(static (_, i) => i < 7 ? "0.0" : "1.0"))}} ]
                    m_InitPose = [ {{string.Concat(nodes.Select(static node => SyntheticCloth.Pose(node.Position.X, node.Position.Y, node.Position.Z)))}} ]
                    m_LockToGoal = [ 0 ]
                    m_LockToParent =
                    [
                        { vOffset = [ 10.0, 0.0, 0.0 ] nCtrlParent = 0 nCtrlChild = {{At("a0")}} },
                        { vOffset = [ -10.0, 0.0, 0.0 ] nCtrlParent = 0 nCtrlChild = {{At("b0")}} },
                    ]
                    m_NodeBases = [ { nNode = {{At("a0")}} nNodeX0 = {{At("a0")}} nNodeX1 = {{At("$cca0_0")}} nNodeY0 = {{At("b0")}} nNodeY1 = {{At("$ccb0_0")}} } ]
                    {{rootFit}}
                    m_ReverseOffsets = [ {{string.Join(" ", offsets)}} ]
                }
                """;
        }

        /// <summary>
        /// Two proxy sheets and a tip bone fit over mesh 1's vertices; <paramref name="firstPositionDriven"/> holds
        /// <c>m_nFirstPositionDrivenNode</c>.
        /// </summary>
        protected static FeModel SheetFitOverTipBone(string firstPositionDriven) => SyntheticCloth.Model(
            ["bone_0", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m1p0", "$cloth_m1p1", "$cloth_m1p2", "tip"],
                staticNodes: 1, parents: [-1, 0, 0, 0, 7, 7, 7, 0],
            poses: [new(0f, 0f, 0f), new(4f, 0f, -10f), new(8f, 0f, -10f), new(4f, 0f, -20f), new(-4f, 0f, -10f),
                new(-8f, 0f, -10f), new(-4f, 0f, -20f), new(0f, 0f, -8f)],
            body: $$"""
                m_nRotLockStaticNodes = 1
                {{firstPositionDriven}}
                m_Tris = [ { nNode = [ 1, 2, 3 ] }, { nNode = [ 4, 5, 6 ] } ]
                m_CtrlOffsets =
                [
                    { vOffset = [ 4.0, 0.0, -10.0 ] nCtrlParent = 0 nCtrlChild = 1 },
                    { vOffset = [ 8.0, 0.0, -10.0 ] nCtrlParent = 0 nCtrlChild = 2 },
                    { vOffset = [ 4.0, 0.0, -20.0 ] nCtrlParent = 0 nCtrlChild = 3 },
                    { vOffset = [ -4.0, 0.0, -2.0 ] nCtrlParent = 7 nCtrlChild = 4 },
                    { vOffset = [ -8.0, 0.0, -2.0 ] nCtrlParent = 7 nCtrlChild = 5 },
                    { vOffset = [ -4.0, 0.0, -12.0 ] nCtrlParent = 7 nCtrlChild = 6 },
                ]
                m_FitMatrices = [ { nEnd = 3 nNode = 7 nBeginDynamic = 0 } ]
                m_FitWeights =
                [
                    { flWeight = 1.0 nNode = 4 nDummy = 0 },
                    { flWeight = 1.0 nNode = 5 nDummy = 0 },
                    { flWeight = 1.0 nNode = 6 nDummy = 0 },
                ]
                """);

        /// <summary>
        /// Three face-kept quads under a static top row and banded rods across the middle quad at <paramref
        /// name="weight"/>.
        /// </summary>
        protected static FeModel FoldedSheetModel(float weight) => SyntheticCloth.Model(
            ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7"],
                staticNodes: 2, invMasses: "0.0, 0.0, 0.1, 0.1, 0.08, 0.08, 0.05, 0.05",
            poses: [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 0f, -2f), new(1f, 0f, -2f), new(0f, 0f, -4f), new(1f, 0f, -4f),
                new(0f, 0f, -6f), new(1f, 0f, -6f)],
            body: $$"""
                m_Quads =
                [
                    { nNode = [ 0, 1, 3, 2 ] flSlack = 0.0 vShape = [ [ 0.0, 0.0, 0.0, 0.0 ], [ 0.0, 0.0, 0.0, 0.0 ], [ 0.0, 0.0, 0.0, 0.5 ], [ 0.0, 0.0, 0.0, 0.5 ] ] },
                    { nNode = [ 2, 3, 5, 4 ] flSlack = 0.0 vShape = [ [ 0.0, 0.0, 0.0, 0.25 ], [ 0.0, 0.0, 0.0, 0.25 ], [ 0.0, 0.0, 0.0, 0.25 ], [ 0.0, 0.0, 0.0, 0.25 ] ] },
                    { nNode = [ 4, 5, 7, 6 ] flSlack = 0.0 vShape = [ [ 0.0, 0.0, 0.0, 0.25 ], [ 0.0, 0.0, 0.0, 0.25 ], [ 0.0, 0.0, 0.0, 0.25 ], [ 0.0, 0.0, 0.0, 0.25 ] ] },
                ]
                m_Rods =
                [
                    { nNode = [ 2, 6 ] flMinDist = 3.5 flMaxDist = 4.0 flWeight0 = {{SyntheticCloth.Num(weight)}} flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 7 ] flMinDist = 3.5 flMaxDist = 4.0 flWeight0 = {{SyntheticCloth.Num(weight)}} flRelaxationFactor = 1.0 },
                ]
                """);
    }
}
