using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>Declaring ClothChain joints, versions, locks and rest poses.</summary>
    public class ClothExtractChainTest : ClothTestFixtures
    {
        /// <summary>
        /// A volume-solved selection over chain joints is declared as a container listing every covered joint at its
        /// weight; a surface-solved one declares none.
        /// </summary>
        [Test]
        public async Task AVolumetricSelectionOverChainJointsListsThemInItsNodeTable()
        {
            var feModel = SyntheticCloth.Parse(SuspenderAtNaturalRelaxationText.Replace("m_Rods =",
                "m_VertexMaps = [ " + VertexMapEntry("vmap0", 4164734239, 0, 0, 8, 0.5f) + VertexMapEntry("surface", 5, 8, 2, 6)
                + " ]\nm_VertexMapValues = [ 255, 255, 255, 255, 128, 128, 255, 255, 255, 255, 255, 255, 255, 255 ]\nm_Rods =",
                StringComparison.Ordinal));
            var children = KVObject.Array();
            ClothExtract.AddClothChainVolumetricMaps(children, feModel, feModel.BuildBoneChains());

            await Assert.That(children.Count).IsEqualTo(1);
            var map = children.ElementAt(0).Value;
            var nodes = map.GetSubCollection("data").GetSubCollection("nodes");

            using (Assert.Multiple())
            {
                await Assert.That(map.GetStringProperty("name")).IsEqualTo("vmap0");
                await Assert.That(map.GetFloatProperty("volumetric_solve")).IsEqualTo(0.5f);
                await Assert.That(nodes.Select(static n => n.Key).ToArray()).IsEquivalentTo(VolumetricMembers, CollectionOrdering.Any);
                await Assert.That(nodes.GetSubCollection("coattail_2_L").GetFloatProperty("weight")).IsEqualTo(128f / 255f).Within(1e-5f);
            }
        }

        private static readonly string[] VolumetricMembers = ["coattail_0_L", "coattail_1_L", "coattail_2_L", "coattail_end_L"];

        /// <summary>
        /// A locked back-solved joint is declared as a <c>ClothJointLock</c>; generated nodes, unlocked joints and
        /// joints declared elsewhere are not.
        /// </summary>
        [Test]
        public async Task ALockedBackSolvedJointIsDeclaredAsAJointLock()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "$cloth_m0p0", "locked_goal", "locked_parent", "goal_with_parent", "declared"], staticNodes: 3,
                    parents: [-1, -1, -1, 2, 0, 4],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -1f), new(0f, 0f, -2f), new(0f, 0f, -3f), new(0f, 0f, -4f), new(0f, 0f, -5f)],
                body: """
                    m_LockToGoal = [ 1, 2 ]
                    m_LockToParent = [ { vOffset = [ 0.0, 0.0, -1.0 ] nCtrlParent = 2 nCtrlChild = 3 }, { vOffset = [ 0.0, 0.0, -1.0 ] nCtrlParent = 4 nCtrlChild = 5 } ]
                    """);
            var children = KVObject.Array();
            ClothExtract.AddClothJointLocks(children, feModel, static (_, name) => name != "declared");

            await Assert.That(children.Select(static c => c.Value.GetStringProperty("feeder_bone")).ToArray())
                .IsEquivalentTo(LockedJoints, CollectionOrdering.Matching);
        }

        private static readonly string[] LockedJoints = ["locked_goal", "locked_parent"];

        /// <summary>
        /// A static root re-declares the twist its relaxation-free link records at 1.0 and stays unsimulated.
        /// </summary>
        [Test]
        public async Task AStaticRootRedeclaresTheTwistItsLinkRecords()
        {
            var root = ClothExtract.MakeClothJoint(TwistPair("0.0", "0.0"), StaticRootJoint());
            var control = ClothExtract.MakeClothJoint(TwistPair("0.0", "0.309"), StaticRootJoint());

            using (Assert.Multiple())
            {
                await Assert.That(root.GetFloatProperty("twist_relax")).IsEqualTo(1f);
                await Assert.That(root.GetBooleanProperty("simulate")).IsFalse();
                await Assert.That(control.GetFloatProperty("twist_relax")).IsEqualTo(0f);
            }
        }

        private static FeModel.BoneChainJoint StaticRootJoint()
            => new() { Name = "coattail_0_L", Node = 0, ParentNode = -1, InvMass = 0f };

        /// <summary>
        /// A goal lock is declared as a <c>ClothRigidCloudCluster</c> over the locked joint's children, and the chain
        /// version reads such a lock beside preset-graded bases as 2.
        /// </summary>
        [Test]
        public async Task ALockBesidePresetBasesIsDeclaredAsARigidCloudCluster()
        {
            static FeModel Model(string locks) => SyntheticCloth.Model(
                ["coattail_0_L", "coattail_1_L", "coattail_1_R"], staticNodes: 1, parents: [-1, 0, 0],
                poses: [new(0f, 0f, 60f), new(0f, 4f, 52f), new(0f, -4f, 52f)],
                body: $$"""
                    m_LockToGoal = [ {{locks}} ]
                    """);
            var chain = new FeModel.BoneChain { RootBone = "coattail_0_L" };
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 0, Name = "coattail_0_L", ParentNode = -1 });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "coattail_1_L", ParentNode = 0, ParentName = "coattail_0_L", InvMass = 1f });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 2, Name = "coattail_1_R", ParentNode = 0, ParentName = "coattail_0_L", InvMass = 1f });

            var locked = Model("0");
            var lockedJoints = ClothExtract.LockedJointsWithChildren(locked, chain).ToArray();
            var cluster = ClothExtract.MakeClothRigidCloudCluster(lockedJoints[0].Joint.Name,
                lockedJoints[0].Children.Select(static child => child.Name));
            string[] members = [.. cluster.GetSubCollection("chain").GetArray("joints").Select(static joint => joint.GetStringProperty("joint_name"))];

            using (Assert.Multiple())
            {
                await Assert.That(lockedJoints.Length).IsEqualTo(1);
                await Assert.That(cluster.GetStringProperty("_class")).IsEqualTo("ClothRigidCloudCluster");
                await Assert.That(cluster.GetInt32Property("algorithm")).IsEqualTo(0);
                await Assert.That(cluster.GetStringProperty("parent_node")).IsEqualTo("coattail_0_L");
                await Assert.That(members).IsEquivalentTo(["coattail_1_L", "coattail_1_R"], CollectionOrdering.Matching);
                await Assert.That(ClothExtract.IsRigidCloudClusterLock(locked, chain)).IsFalse();
                await Assert.That(ClothExtract.LockedJointsWithChildren(Model(string.Empty), chain).Any()).IsFalse();

                await Assert.That(ClothExtract.ClothChainVersion(new ClothExtract.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: true, RigidCloudClusterLock: false, LocksJoints: false, BasesBulkGraded: true,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false))).IsEqualTo(1);
                await Assert.That(ClothExtract.ClothChainVersion(new ClothExtract.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: true, RigidCloudClusterLock: true, LocksJoints: false, BasesBulkGraded: false,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false))).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(new ClothExtract.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: true, RigidCloudClusterLock: false, LocksJoints: false, BasesBulkGraded: false,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false))).IsEqualTo(1);
            }
        }

        /// <summary>
        /// Sub-chains staged at versions 0 and 1 under one root are declared as two chains, the root's ring going to
        /// the sub-chain whose ring follows it; with both ungrouped the tree stays one version-0 chain.
        /// </summary>
        [Test]
        public async Task SubChainsStagedAtTwoVersionsAreDeclaredApart()
        {
            var split = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: true));
            var moved = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: false, thinTipGrouped: false, wideLeafGrouped: true));
            var locked = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: false, thinTipGrouped: false, wideLeafGrouped: true,
                rootRotationLocked: true));
            var flipped = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: true, thinTipGrouped: true, wideLeafGrouped: false));
            var merged = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: false));
            var splitChains = split.BuildBoneChains(VersionOf(split));
            var movedChains = moved.BuildBoneChains(VersionOf(moved));
            var lockedChains = locked.BuildBoneChains(VersionOf(locked));
            var flippedChains = flipped.BuildBoneChains(VersionOf(flipped));
            var mergedChains = merged.BuildBoneChains(VersionOf(merged));

            static Func<FeModel.BoneChain, int> VersionOf(FeModel feModel)
                => chain => ClothExtract.ClothChainVersion(feModel, chain);

            static FeModel.BoneChain Owning(List<FeModel.BoneChain> chains, string joint)
                => chains.First(chain => chain.Joints.Exists(j => j.Name == joint));

            using (Assert.Multiple())
            {
                await Assert.That(splitChains.Count).IsEqualTo(2);
                await Assert.That(Owning(splitChains, "a1").Joints.Select(static j => j.Name).ToArray())
                    .IsEquivalentTo(ThinSubChain, CollectionOrdering.Matching);
                await Assert.That(Owning(splitChains, "b1").Joints.Select(static j => j.Name).ToArray())
                    .IsEquivalentTo(WideSubChain, CollectionOrdering.Matching);
                await Assert.That(Owning(splitChains, "a1").Joints[0].RingNodes.Count).IsEqualTo(1);
                await Assert.That(Owning(splitChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(ClothExtract.ClothChainVersion(split, Owning(splitChains, "a1"))).IsEqualTo(0);
                await Assert.That(ClothExtract.ClothChainVersion(split, Owning(splitChains, "b1"))).IsEqualTo(1);
                await Assert.That(movedChains.Count).IsEqualTo(2);
                await Assert.That(Owning(movedChains, "a1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(Owning(movedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(1);
                await Assert.That(lockedChains.Count).IsEqualTo(2);
                await Assert.That(Owning(lockedChains, "a1").Joints[0].RingNodes.Count).IsEqualTo(1);
                await Assert.That(Owning(lockedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(flippedChains.Count).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(flipped, Owning(flippedChains, "a1"))).IsEqualTo(1);
                await Assert.That(ClothExtract.ClothChainVersion(flipped, Owning(flippedChains, "b1"))).IsEqualTo(0);
                await Assert.That(Owning(flippedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(mergedChains.Count).IsEqualTo(1);
                await Assert.That(mergedChains[0].Joints.Count).IsEqualTo(5);
                await Assert.That(ClothExtract.ClothChainVersion(merged, mergedChains[0])).IsEqualTo(0);
            }
        }

        private static readonly string[] ThinSubChain = ["root", "a0", "a1"];

        private static readonly string[] WideSubChain = ["root", "b0", "b1"];

        /// <summary>
        /// Reverse offsets naming each joint's preset-basis Y1 node read as version 2, offsets off the fit group as
        /// version 1, and no offsets as 2.
        /// </summary>
        [Test]
        public async Task AReverseOffsetOffItsPresetBasisY1IsBelowChainVersion2()
        {
            var preset = TwoWideRope(0);
            var fitted = TwoWideRope(1);
            var bare = TwoWideRope(null);
            var presetChain = preset.BuildBoneChains()[0];
            var fittedChain = fitted.BuildBoneChains()[0];
            var bareChain = bare.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(preset.ChainReverseOffsetsArePreset(presetChain)).IsTrue();
                await Assert.That(fitted.ChainReverseOffsetsArePreset(fittedChain)).IsFalse();
                await Assert.That(bare.ChainReverseOffsetsArePreset(bareChain)).IsNull();
                await Assert.That(ClothExtract.ClothChainVersion(preset, presetChain)).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(fitted, fittedChain)).IsEqualTo(1);
                await Assert.That(ClothExtract.ClothChainVersion(bare, bareChain)).IsEqualTo(2);
            }
        }

        /// <summary>
        /// A simulated rope of three joints 8.5 apart, each with a two-node ring across Y, whose reverse offsets name
        /// ring node <paramref name="ringNode"/> of their own ring, or none.
        /// </summary>
        private static FeModel TwoWideRope(int? ringNode)
        {
            var offsets = ringNode is { } side
                ? string.Concat(Enumerable.Range(0, 3).Select(joint => "{ vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = " + (joint * 3)
                    + " nTargetNode = " + ((joint * 3) + 1 + side) + " }, "))
                : string.Empty;

            return SyntheticCloth.Model(
                ["j0", "$ccj0_0", "$ccj0_1", "j1", "$ccj1_0", "$ccj1_1", "j2", "$ccj2_0", "$ccj2_1"], staticNodes: 0,
                    parents: [-1, 0, 0, 0, 3, 3, 3, 6, 6],
                poses: [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, -2f, 0f), new(-8.5f, 0f, 0f), new(-8.5f, 2f, 0f),
                    new(-8.5f, -2f, 0f), new(-17f, 0f, 0f), new(-17f, 2f, 0f), new(-17f, -2f, 0f)],
                body: $$"""
                    m_SourceElems = [ 1, 2, 5, 4, 4, 5, 8, 7 ]
                    m_ReverseOffsets = [ {{offsets}} ]
                    """);
        }

        /// <summary>A joint's <c>extrude_twist</c> carries its tie roll.</summary>
        [Test]
        public async Task AChainJointsExtrudeTwistCarriesItsTieRoll()
        {
            var feModel = TwistPair("0.0", "0.0");
            FeModel.BoneChainJoint RolledJoint()
                => new()
                {
                    Name = "coattail_0_L",
                    Node = 0,
                    ParentNode = -1,
                    InvMass = 0f,
                    ExtrudeSides = 2,
                    ExtrudeRadius = 2f,
                    ExtrudeTwist = 90f,
                    ExtrudeTwistTieNudge = -0.012008f,
                };

            var rolled = ClothExtract.MakeClothJoint(feModel, RolledJoint(), chainExtrudes: true);

            await Assert.That(rolled.GetFloatProperty("extrude_twist")).IsEqualTo(-0.012008f).Within(1e-6f);
        }

        /// <summary>
        /// A rope hint left at its X pair with Y (0, 0) reads as twist-written (version 0); a graded run or a foreign X
        /// pair does not, and a chain whose original locks no joints keeps version 2.
        /// </summary>
        [Test]
        public async Task AnUngradedRopeHintSaysTheChainCompiledAtVersionZero()
        {
            var ungraded = RopeHinted("nNodeX0 = 1 nNodeX1 = 3 nNodeY0 = 0 nNodeY1 = 0",
                "nNodeX0 = 3 nNodeX1 = 2 nNodeY0 = 0 nNodeY1 = 0");
            var graded = RopeHinted("nNodeX0 = 3 nNodeX1 = 1 nNodeY0 = 3 nNodeY1 = 3",
                "nNodeX0 = 3 nNodeX1 = 2 nNodeY0 = 3 nNodeY1 = 3");
            var foreign = RopeHinted("nNodeX0 = 3 nNodeX1 = 1 nNodeY0 = 0 nNodeY1 = 0",
                "nNodeX0 = 2 nNodeX1 = 3 nNodeY0 = 0 nNodeY1 = 0");

            using (Assert.Multiple())
            {
                await Assert.That(ungraded.ChainHintsAreTwistWritten(TwistedRopeChain())).IsTrue();
                await Assert.That(graded.ChainHintsAreTwistWritten(TwistedRopeChain())).IsFalse();
                await Assert.That(foreign.ChainHintsAreTwistWritten(TwistedRopeChain())).IsFalse();

                await Assert.That(ClothExtract.ClothChainVersion(new ClothExtract.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: false, RigidCloudClusterLock: false, LocksJoints: false, BasesBulkGraded: null,
                    HintsTwistWritten: true, HasUnstagedThinJoint: false))).IsEqualTo(0);
                await Assert.That(ClothExtract.ClothChainVersion(new ClothExtract.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: false, RigidCloudClusterLock: false, LocksJoints: true, BasesBulkGraded: null,
                    HintsTwistWritten: true, HasUnstagedThinJoint: false))).IsEqualTo(2);
            }
        }

        private static FeModel RopeHinted(string hint2, string hint3) => SyntheticCloth.Model(
            ["root", "j1", "j2", "j3"], staticNodes: 2, parents: [-1, 0, 1, 2],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f)],
            body: $$"""
                m_nRopeCount = 1
                m_Ropes = [ 4, 1, 2, 3 ]
                m_DynNodeWindBases =
                [
                    { {{hint2}} },
                    { {{hint3}} },
                ]
                """);

        /// <summary>
        /// A simulated two-sided leaf with no node base and no reverse offset reads version 0; a based, staged or
        /// zero-stretch leaf does not, and a chain whose original locks no joints keeps version 2.
        /// </summary>
        [Test]
        public async Task ATwoSidedLeafWithNoNodeBaseSaysTheChainCompiledAtVersionZero()
        {
            const string JointBase = "{ nNode = 3 nNodeX0 = 4 nNodeX1 = 5 nNodeY0 = 6 nNodeY1 = 3 }";
            var unbased = TwoSidedStrip(JointBase, string.Empty);
            var based = TwoSidedStrip(JointBase + ", { nNode = 6 nNodeX0 = 7 nNodeX1 = 8 nNodeY0 = 3 nNodeY1 = 6 }", string.Empty);
            var staged = TwoSidedStrip(JointBase, "{ vOffset = [ 0.0, 0.0, 0.0 ] nBoneCtrl = 6 nTargetNode = 7 }");
            var slack = TwoSidedStripChain();
            slack.Joints[2].StretchStiffness = 0f;

            using (Assert.Multiple())
            {
                await Assert.That(unbased.ChainHasUnbasedLeaf(TwoSidedStripChain())).IsTrue();
                await Assert.That(based.ChainHasUnbasedLeaf(TwoSidedStripChain())).IsFalse();
                await Assert.That(staged.ChainHasUnbasedLeaf(TwoSidedStripChain())).IsFalse();
                await Assert.That(unbased.ChainHasUnbasedLeaf(slack)).IsFalse();

                await Assert.That(ClothExtract.ClothChainVersion(new ClothExtract.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: false, LockedJoint: false, RigidCloudClusterLock: false, LocksJoints: false, BasesBulkGraded: null,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false, HasUnbasedLeaf: true))).IsEqualTo(0);
                await Assert.That(ClothExtract.ClothChainVersion(new ClothExtract.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: false, LockedJoint: false, RigidCloudClusterLock: false, LocksJoints: true, BasesBulkGraded: null,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false, HasUnbasedLeaf: true))).IsEqualTo(2);
            }
        }

        private static FeModel.BoneChain TwoSidedStripChain()
        {
            var chain = new FeModel.BoneChain { RootBone = "root", ExtrudeSides = 2 };
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 0, Name = "root", ParentNode = -1, ExtrudeSides = 2, RingNodes = [1, 2] });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 3, Name = "j1", ParentNode = 0, InvMass = 1f, ExtrudeSides = 2, RingNodes = [4, 5] });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 6, Name = "j2", ParentNode = 3, InvMass = 1f, ExtrudeSides = 2, RingNodes = [7, 8] });
            return chain;
        }

        private static FeModel TwoSidedStrip(string nodeBases, string reverseOffsets) => SyntheticCloth.Model(
            ["root", "$ccroot_0", "$ccroot_1", "j1", "$ccj1_0", "$ccj1_1", "j2", "$ccj2_0", "$ccj2_1"], staticNodes: 3,
                parents: [-1, 0, 0, 0, 3, 3, 3, 6, 6],
            poses: [new(0f, 0f, 0f), new(3f, 0f, 0f), new(-3f, 0f, 0f), new(0f, 0f, -10f), new(3f, 0f, -10f), new(-3f, 0f, -10f),
                new(0f, 0f, -20f), new(3f, 0f, -20f), new(-3f, 0f, -20f)],
            body: $$"""
                m_SourceElems = [ 0, 0, 0, 2, 1, 2, 5, 4, 4, 5, 8, 7 ]
                m_NodeBases = [ {{nodeBases}} ]
                m_ReverseOffsets = [ {{reverseOffsets}} ]
                """);

        /// <summary>
        /// Where the preset scan's handedness ties, reverse offsets on the swapped Y1 of a swapped base read version 2;
        /// without those bases, or with decided handedness, they read version 1.
        /// </summary>
        [Test]
        public async Task AReverseOffsetOnTheSwappedYNodeOfAHandednessTieIsChainVersion2()
        {
            var tied = SwappedYPairRope(alongZ: true, based: true);
            var unbased = SwappedYPairRope(alongZ: true, based: false);
            var decided = SwappedYPairRope(alongZ: false, based: true);
            var tiedChain = tied.BuildBoneChains()[0];
            var unbasedChain = unbased.BuildBoneChains()[0];
            var decidedChain = decided.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(tied.ChainReverseOffsetsArePreset(tiedChain)).IsTrue();
                await Assert.That(unbased.ChainReverseOffsetsArePreset(unbasedChain)).IsFalse();
                await Assert.That(decided.ChainReverseOffsetsArePreset(decidedChain)).IsFalse();
                await Assert.That(ClothExtract.ClothChainVersion(tied, tiedChain)).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(unbased, unbasedChain)).IsEqualTo(1);
                await Assert.That(ClothExtract.ClothChainVersion(decided, decidedChain)).IsEqualTo(1);
            }
        }

        /// <summary>
        /// A simulated rope of three joints 8.5 apart, each extruding a two-node ring across Z or across Y, whose j0 and j1
        /// reverse offsets name the Y0 node of their preset basis ($ccj1_1, $ccj2_1), and whose j0 and j1 may carry that basis
        /// with its Y pair swapped.
        /// </summary>
        private static FeModel SwappedYPairRope(bool alongZ, bool based)
        {
            string Pose(float x, float side) => alongZ ? SyntheticCloth.Pose(x, 0f, side) : SyntheticCloth.Pose(x, side, 0f);
            var bases = based
                ? "{ nNode = 0 nNodeX0 = 4 nNodeX1 = 2 nNodeY0 = 1 nNodeY1 = 5 }, { nNode = 3 nNodeX0 = 7 nNodeX1 = 5 nNodeY0 = 4 nNodeY1 = 8 }"
                : string.Empty;

            return SyntheticCloth.Model(
                ["j0", "$ccj0_0", "$ccj0_1", "j1", "$ccj1_0", "$ccj1_1", "j2", "$ccj2_0", "$ccj2_1"], staticNodes: 0,
                    parents: [-1, 0, 0, 0, 3, 3, 3, 6, 6],
                body: $$"""
                    m_InitPose =
                    [
                        {{Pose(0f, 0f)}}
                        {{Pose(0f, 2f)}}
                        {{Pose(0f, -2f)}}
                        {{Pose(-8.5f, 0f)}}
                        {{Pose(-8.5f, 2f)}}
                        {{Pose(-8.5f, -2f)}}
                        {{Pose(-17f, 0f)}}
                        {{Pose(-17f, 2f)}}
                        {{Pose(-17f, -2f)}}
                    ]
                    m_SourceElems = [ 1, 2, 5, 4, 4, 5, 8, 7 ]
                    m_NodeBases = [ {{bases}} ]
                    m_ReverseOffsets =
                    [
                        { vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = 0 nTargetNode = 5 },
                        { vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = 3 nTargetNode = 8 },
                        { vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = 6 nTargetNode = 7 },
                    ]
                    """);
        }

        /// <summary>
        /// A <c>ClothRigidCloudCluster</c> declares at least two members: a line's child and grandchild, a fork's two
        /// children, or a lone leaf paired with its locked parent.
        /// </summary>
        [Test]
        public async Task ARigidCloudClusterDeclaresAtLeastTwoMembers()
        {
            static FeModel.BoneChain Chain(params int[] parents)
            {
                FeModel.BoneChain chain = new() { RootBone = "joint_0" };
                for (int node = 0; node < parents.Length; node++)
                {
                    chain.Joints.Add(new FeModel.BoneChainJoint { Node = node, Name = $"joint_{node}", ParentNode = parents[node] });
                }

                return chain;
            }

            static List<string> Members(FeModel.BoneChain chain) => ClothExtract.RigidCloudClusterMembers(chain, chain.Joints[0]);

            FeModel.BoneChain line = Chain(-1, 0, 1, 2);
            FeModel.BoneChain fork = Chain(-1, 0, 0, 1);
            FeModel.BoneChain pair = Chain(-1, 0);
            KVObject cluster = ClothExtract.MakeClothRigidCloudCluster("joint_0", Members(line));
            string[] declared = [.. cluster.GetSubCollection("chain").GetArray("joints").Select(static joint => joint.GetStringProperty("joint_name"))];

            using (Assert.Multiple())
            {
                await Assert.That(Members(line)).IsEquivalentTo(["joint_1", "joint_2"], CollectionOrdering.Matching);
                await Assert.That(Members(fork)).IsEquivalentTo(["joint_1", "joint_2"], CollectionOrdering.Matching);
                await Assert.That(Members(pair)).IsEquivalentTo(["joint_0", "joint_1"], CollectionOrdering.Matching);
                await Assert.That(declared).IsEquivalentTo(["joint_1", "joint_2"], CollectionOrdering.Matching);
            }
        }

        /// <summary>
        /// A chain whose non-simulated joint has no ring on itself or its children locks no joint to its goal; giving
        /// the child a ring does.
        /// </summary>
        [Test]
        public async Task AChainJointWithNoRingOnItselfOrItsChildrenIsNoGoalLock()
        {
            FeModel model = SyntheticCloth.Model(
                ["root", "mid", "leaf", "$ccmid_0", "$ccmid_1", "$ccleaf_0", "$ccleaf_1"], staticNodes: 1,
                poses: [new(0f, 0f, 0f), new(0f, 0f, -8f), new(0f, 0f, -16f), new(0f, 1f, -8f), new(0f, -1f, -8f),
                    new(0f, 1f, -16f), new(0f, -1f, -16f)],
                body: """
                    m_nRotLockStaticNodes = 0
                    """);

            static FeModel.BoneChain Chain(IReadOnlyList<int> midRing)
            {
                var chain = new FeModel.BoneChain { RootBone = "root", ExtrudeSides = 2 };
                chain.Joints.Add(new FeModel.BoneChainJoint { Node = 0, Name = "root", ParentNode = -1 });
                chain.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "mid", ParentNode = 0, InvMass = 1f, ExtrudeSides = midRing.Count, RingNodes = midRing });
                chain.Joints.Add(new FeModel.BoneChainJoint { Node = 2, Name = "leaf", ParentNode = 1, InvMass = 1f, ExtrudeSides = 2, RingNodes = [5, 6] });
                return chain;
            }

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.ChainLocksJoints(model, Chain([]))).IsFalse();
                await Assert.That(ClothExtract.ChainLocksJoints(model, Chain([3, 4]))).IsTrue();
            }
        }

        /// <summary>
        /// A static ringed child with no rod to its root is a chain of its own, declared in its parent's local space; a
        /// tying rod keeps one chain.
        /// </summary>
        [Test]
        public async Task AStaticRingedChildNoRodTiesToItsRootIsAChainOfItsOwn()
        {
            static FeModel Model(string tie) => SyntheticCloth.Model(
                ["root", "$ccroot_0", "end", "$ccend_0", "a", "$cca_0", "b", "$ccb_0"], staticNodes: 4,
                    parents: [-1, 0, 0, 2, 0, 4, 0, 6],
                poses: [new(0f, 0f, 0f), new(0f, 2f, 0f), new(-10f, 0f, 0f), new(-10f, 2f, 0f), new(0f, 0f, -10f),
                    new(0f, 2f, -10f), new(5f, 0f, -10f), new(5f, 2f, -10f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 4, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 4, 10.198039f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 5, 10.198039f, 1f)}}
                        {{SyntheticCloth.RigidRod(4, 5, 2f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 6, 11.18034f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 6, 11.357817f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 7, 11.357817f, 1f)}}
                        {{SyntheticCloth.RigidRod(6, 7, 2f, 1f)}}
                        {{tie}}
                    ]
                    """);

            var loose = Model("").BuildBoneChains();
            var tied = Model(SyntheticCloth.RigidRod(0, 2, 10f, 1f)).BuildBoneChains();

            FeModel rotated = SyntheticCloth.Model(["parent", "child"], staticNodes: 2, parents: [-1, 0], body: """
                    m_InitPose =
                    [
                        [ 1.0, 2.0, 3.0, 1.0, 0.0, 0.0, 0.7071068, 0.7071068 ],
                        [ 1.0, 12.0, 3.0, 1.0, 0.0, 0.0, 0.7071068, 0.7071068 ],
                    ]
                    """);
            var (origin, rotation) = ClothExtract.ClothBoneLocalPose(rotated, 1, 0);

            using (Assert.Multiple())
            {
                await Assert.That(loose.Count).IsEqualTo(2);
                await Assert.That(loose.Exists(chain => chain.RootBone == "end" && chain.Joints.Count == 1)).IsTrue();
                await Assert.That(loose.Exists(chain => chain.RootBone == "root" && !chain.Joints.Exists(joint => joint.Name == "end"))).IsTrue();
                await Assert.That(tied.Count).IsEqualTo(1);
                await Assert.That(tied[0].Joints.Exists(joint => joint.Name == "end")).IsTrue();
                await Assert.That(origin.X).IsEqualTo(10f).Within(1e-4f);
                await Assert.That(origin.Y).IsEqualTo(0f).Within(1e-4f);
                await Assert.That(origin.Z).IsEqualTo(0f).Within(1e-4f);
                await Assert.That(MathF.Abs(rotation.W)).IsEqualTo(1f).Within(1e-4f);
            }
        }

        /// <summary>
        /// A static root with a one-way relaxation-free twist entry declares <c>twist_relax</c> 1 and stays
        /// unsimulated; a relaxed entry does not.
        /// </summary>
        [Test]
        public async Task AStaticRootStatesARelaxlessTwistFromItsOwnEntryAlone()
        {
            var oneWay = TwistOneWay("0.0");
            var relaxed = TwistOneWay("0.309");
            var root = ClothExtract.MakeClothJoint(oneWay, StaticRootJoint());
            var control = ClothExtract.MakeClothJoint(relaxed, StaticRootJoint());

            using (Assert.Multiple())
            {
                await Assert.That(oneWay.HasRelaxlessTwistLink(0)).IsFalse();
                await Assert.That(root.GetFloatProperty("twist_relax")).IsEqualTo(1f);
                await Assert.That(root.GetBooleanProperty("simulate")).IsFalse();
                await Assert.That(control.GetFloatProperty("twist_relax")).IsNotEqualTo(1f);
            }
        }

        private static FeModel TwistOneWay(string toChild) => SyntheticCloth.Model(
            ["coattail_0_L", "coattail_1_L"], staticNodes: 1, parents: [-1, 0], body: $$"""
                m_Twists =
                [
                    { nNodeOrient = 0 nNodeEnd = 1 flTwistRelax = {{toChild}} flSwingRelax = 1.0 },
                ]
                """);

        /// <summary>
        /// An interior static joint re-declares its relaxation-free twist at <c>twist_relax</c> 1 as a root does; a
        /// simulating joint does not.
        /// </summary>
        [Test]
        public async Task AnInteriorStaticJointRedeclaresItsRelaxlessTwistToo()
        {
            var interior = ClothExtract.MakeClothJoint(InteriorTwist("0.0"), InteriorStaticJoint(0f));
            var simulated = ClothExtract.MakeClothJoint(InteriorTwist("1.0"), InteriorStaticJoint(1f));

            using (Assert.Multiple())
            {
                await Assert.That(InteriorTwist("0.0").OrientsRelaxlessTwist(1)).IsTrue();
                await Assert.That(interior.GetFloatProperty("twist_relax")).IsEqualTo(1f);
                await Assert.That(interior.GetBooleanProperty("simulate")).IsFalse();
                await Assert.That(simulated.GetFloatProperty("twist_relax")).IsNotEqualTo(1f);
            }
        }

        private static FeModel.BoneChainJoint InteriorStaticJoint(float invMass)
            => new() { Name = "mid", Node = 1, ParentNode = 0, ParentName = "root", InvMass = invMass };

        /// <summary>A three-bone chain whose MIDDLE joint orients a twist the far end states nothing back for.</summary>
        private static FeModel InteriorTwist(string relax) => SyntheticCloth.Model(
            ["root", "mid", "tip"], staticNodes: 2, parents: [-1, 0, 1], body: $$"""
                m_Twists =
                [
                    { nNodeOrient = 1 nNodeEnd = 2 flTwistRelax = {{relax}} flSwingRelax = 1.0 },
                ]
                """);

        /// <summary>
        /// A ringless chain's rotation-locked root without a node base keeps version 2.
        /// </summary>
        [Test]
        public async Task ARinglessChainsAbsentRootBaseStatesNoFormat()
        {
            var ringless = SyntheticCloth.Model(
                ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                body: $$"""
                    m_nRotLockStaticNodes = 1
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    ]
                    """);

            var chain = ringless.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(chain.ExtrudeSides).IsLessThan(1);
                await Assert.That(ringless.AllowsRotation(chain.Joints[0].Node)).IsFalse();
                await Assert.That(ringless.NodeBases.ContainsKey(chain.Joints[0].Node)).IsFalse();
                await Assert.That(ClothExtract.ClothChainVersion(ringless, chain))
                    .IsEqualTo(2);
            }
        }

        /// <summary>
        /// A simulating joint whose child-ward twist carries no relaxation is first declared unsimulated; a relaxed
        /// entry or a doubled parent-ward entry keeps it simulating.
        /// </summary>
        [Test]
        public async Task ASimulatingJointsRelaxlessChildTwistStatesASecondDeclaration()
        {
            static FeModel Chain(string toChild, string extraToParent) => SyntheticCloth.Model(
                ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    ]
                    m_Twists =
                    [
                        { nNodeOrient = 1 nNodeEnd = 0 flTwistRelax = 0.4944 flSwingRelax = 0.0 },
                        { nNodeOrient = 1 nNodeEnd = 2 flTwistRelax = {{toChild}} flSwingRelax = 1.0 },
                        {{extraToParent}}
                    ]
                    """);

            static KVObject FirstDeclaration(FeModel feModel)
            {
                var chain = feModel.BuildBoneChains()[0];
                return ClothExtract.MakeClothJoint(feModel, chain.Joints.Find(static joint => joint.Name == "j1")!,
                    chain: chain);
            }

            var split = FirstDeclaration(Chain("0.0", string.Empty));
            var relaxed = FirstDeclaration(Chain("0.3056", string.Empty));
            var doubled = FirstDeclaration(Chain("0.0",
                "{ nNodeOrient = 1 nNodeEnd = 0 flTwistRelax = 0.2163 flSwingRelax = 0.0 },"));

            using (Assert.Multiple())
            {
                await Assert.That(split.GetBooleanProperty("simulate")).IsFalse();
                await Assert.That(relaxed.GetBooleanProperty("simulate")).IsTrue();
                await Assert.That(doubled.GetBooleanProperty("simulate")).IsTrue();
                await Assert.That(split.GetFloatProperty("twist_relax")).IsEqualTo(0.8f).Within(1e-3f);
            }
        }

        /// <summary>
        /// A chain's <c>attrs</c> state <c>extrude_twist</c> default 0 while <c>extrude_sides</c> and
        /// <c>extrude_radius</c> carry the chain's values; the joint key is the complement of the roll.
        /// </summary>
        [Test]
        public async Task AChainsAttrsStateTheExtrudeTwistSchemaDefault()
        {
            const float Roll = 70f;
            var extruding = ClothExtract.MakeClothChainAttrs(2, 1.5f);
            var rope = ClothExtract.MakeClothChainAttrs();

            using (Assert.Multiple())
            {
                await Assert.That(extruding.GetSubCollection("extrude_sides").GetFloatProperty("default"))
                    .IsEqualTo(2f);
                await Assert.That(extruding.GetSubCollection("extrude_radius").GetFloatProperty("default"))
                    .IsEqualTo(1.5f);
                await Assert.That(rope.GetSubCollection("extrude_twist").GetFloatProperty("default"))
                    .IsEqualTo(0f);
                await Assert.That(ClothExtract.ClothExtrudeTwistKey(Roll)).IsEqualTo(90f - Roll);
                await Assert.That(extruding.GetSubCollection("extrude_twist").GetFloatProperty("default"))
                    .IsEqualTo(0f);
            }
        }

        private static readonly string[] VoicedRunMembers = ["root", "a", "b", "c", "d"];

        /// <summary>
        /// A doubled twist run is marked for a second declaration under its topmost root despite a static intermediate,
        /// and each declaration states its own rank's <c>twist_relax</c>; a run both declarations simulated is not
        /// marked.
        /// </summary>
        [Test]
        public async Task AVoicedRunIsReDeclaredWholeAndEachDeclarationStatesItsOwnRank()
        {
            static FeModel Run(float firstChildWard) => SyntheticCloth.Model(
                ["root", "b", "a", "c", "d"], staticNodes: 2, parents: [-1, 2, 0, 1, 3],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -20f), new(0f, 0f, -10f), new(0f, 0f, -30f), new(0f, 0f, -40f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(2, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 3, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(3, 4, 10f, 1f)}}
                    ]
                    m_Twists =
                    [
                        { nNodeOrient = 2 nNodeEnd = 0 flTwistRelax = 0.4944 flSwingRelax = 0.0 },
                        { nNodeOrient = 2 nNodeEnd = 1 flTwistRelax = {{SyntheticCloth.Num(firstChildWard)}} flSwingRelax = 0.5 },
                        { nNodeOrient = 3 nNodeEnd = 1 flTwistRelax = 0.4944 flSwingRelax = 0.0 },
                        { nNodeOrient = 3 nNodeEnd = 4 flTwistRelax = {{SyntheticCloth.Num(firstChildWard)}} flSwingRelax = 0.5 },
                        { nNodeOrient = 2 nNodeEnd = 0 flTwistRelax = 0.2163 flSwingRelax = 0.0 },
                        { nNodeOrient = 2 nNodeEnd = 1 flTwistRelax = 0.1337 flSwingRelax = 0.25 },
                        { nNodeOrient = 3 nNodeEnd = 1 flTwistRelax = 0.2163 flSwingRelax = 0.0 },
                        { nNodeOrient = 3 nNodeEnd = 4 flTwistRelax = 0.1337 flSwingRelax = 0.25 },
                    ]
                    """);

            static IEnumerable<string> Roots(FeModel feModel)
                => feModel.BuildBoneChains()[0].Joints.Select(static joint =>
                    joint.SecondDeclarationRoot ?? "<unmarked>");

            static float Declared(FeModel feModel, string bone, bool second)
            {
                var chain = feModel.BuildBoneChains()[0];
                return ClothExtract.MakeClothJoint(feModel,
                    chain.Joints.Find(joint => joint.Name == bone)!,
                    chain: chain, secondDeclaration: second).GetFloatProperty("twist_relax");
            }

            var voiced = Run(0f);
            var bothSimulating = Run(0.3056f);

            using (Assert.Multiple())
            {
                await Assert.That(Roots(voiced)).IsEquivalentTo(
                    ["root", "root", "root", "root", "root"], CollectionOrdering.Matching);
                await Assert.That(voiced.BuildBoneChains()[0].Joints.Select(static joint => joint.Name))
                    .IsEquivalentTo(VoicedRunMembers, CollectionOrdering.Any);

                await Assert.That(Declared(voiced, "a", second: false)).IsEqualTo(0.8f).Within(1e-3f);
                await Assert.That(Declared(voiced, "a", second: true)).IsEqualTo(0.35f).Within(1e-3f);

                await Assert.That(Roots(bothSimulating)).IsEquivalentTo(
                    ["<unmarked>", "<unmarked>", "<unmarked>", "<unmarked>", "<unmarked>"],
                    CollectionOrdering.Matching);
            }
        }

        /// <summary>
        /// The version-2 preset scans rings in the importer's order, so a permuted rope's reverse offsets still read as
        /// preset (version 2); the ordered rope reads the same and a bare rope reads nothing.
        /// </summary>
        [Test]
        public async Task ThePresetScansItsListInTheImportersOrder()
        {
            var permuted = PermutedTwoWideRope(true);
            var bare = PermutedTwoWideRope(false);
            var ordered = TwoWideRope(0);
            var permutedChain = permuted.BuildBoneChains()[0];
            var bareChain = bare.BuildBoneChains()[0];
            var orderedChain = ordered.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(ordered.ChainReverseOffsetsArePreset(orderedChain)).IsTrue();
                await Assert.That(bare.ChainReverseOffsetsArePreset(bareChain)).IsNull();

                await Assert.That(permuted.ChainReverseOffsetsArePreset(permutedChain)).IsTrue();
                await Assert.That(ClothExtract.ClothChainVersion(permuted, permutedChain)).IsEqualTo(2);
            }
        }

        /// <summary><see cref="TwoWideRope"/> with j0's ring compiled after j1's.</summary>
        private static FeModel PermutedTwoWideRope(bool offsets) => SyntheticCloth.Model(
            ["j0", "$ccj1_0", "$ccj1_1", "j1", "$ccj0_0", "$ccj0_1", "j2", "$ccj2_0", "$ccj2_1"], staticNodes: 0,
                parents: [-1, 3, 3, 0, 0, 0, 3, 6, 6],
            poses: [new(0f, 0f, 0f), new(-8.5f, 2f, 0f), new(-8.5f, -2f, 0f), new(-8.5f, 0f, 0f), new(0f, 2f, 0f),
                new(0f, -2f, 0f), new(-17f, 0f, 0f), new(-17f, 2f, 0f), new(-17f, -2f, 0f)],
            body: $$"""
                m_SourceElems = [ 4, 5, 2, 1, 1, 2, 8, 7 ]
                m_ReverseOffsets = [ {{(offsets ? "{ vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = 0 nTargetNode = 4 }, { vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = 3 nTargetNode = 1 }, " : string.Empty)}} ]
                """);

        /// <summary>
        /// A fit matrix on a joint the version-2 preset would offset reads below version 2; no fit, or a fit on the
        /// leaf, keeps 2.
        /// </summary>
        [Test]
        public async Task AFitMatrixOnAJointThePresetWouldOffsetRulesOutVersion2()
        {
            var bare = TwoWideRopeFitting(null);
            var leaf = TwoWideRopeFitting(6);
            var fitted = TwoWideRopeFitting(3);
            var bareChain = bare.BuildBoneChains()[0];
            var leafChain = leaf.BuildBoneChains()[0];
            var fittedChain = fitted.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.ClothChainVersion(bare, bareChain)).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(leaf, leafChain)).IsEqualTo(2);

                await Assert.That(ClothExtract.ClothChainVersion(fitted, fittedChain)).IsLessThan(2);
            }
        }

        /// <summary>
        /// <see cref="TwoWideRope"/> without reverse offsets and, where given, a fit matrix on <paramref name="fitNode"/>
        /// over the ring nodes.
        /// </summary>
        private static FeModel TwoWideRopeFitting(int? fitNode) => SyntheticCloth.Model(
            ["j0", "$ccj0_0", "$ccj0_1", "j1", "$ccj1_0", "$ccj1_1", "j2", "$ccj2_0", "$ccj2_1"], staticNodes: 0,
                parents: [-1, 0, 0, 0, 3, 3, 3, 6, 6],
            poses: [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, -2f, 0f), new(-8.5f, 0f, 0f), new(-8.5f, 2f, 0f),
                new(-8.5f, -2f, 0f), new(-17f, 0f, 0f), new(-17f, 2f, 0f), new(-17f, -2f, 0f)],
            body: $$"""
                m_SourceElems = [ 1, 2, 5, 4, 4, 5, 8, 7 ]
                m_ReverseOffsets = [ ]
                {{(fitNode is { } node
                    ? "m_FitMatrices = [ { bone = [ 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 ] vCenter = [ 0.0, 0.0, 0.0 ] nEnd = 4 nNode = " + node + " nBeginDynamic = 0 } ] m_FitWeights = [ { flWeight = 0.25 nNode = 4 }, { flWeight = 0.25 nNode = 5 }, { flWeight = 0.25 nNode = 7 }, { flWeight = 0.25 nNode = 8 } ]"
                    : string.Empty)}}
                """);

        /// <summary>
        /// A static joint the original locks to its parent does not hold a fitted chain at version 2; without that lock
        /// the guard keeps version 2.
        /// </summary>
        [Test]
        public async Task AParentLockedJointDoesNotHoldAFittedChainAtVersion2()
        {
            var free = StaticRootRopeFitting(parentLocked: false);
            var locked = StaticRootRopeFitting(parentLocked: true);
            var freeChain = free.BuildBoneChains()[0];
            var lockedChain = locked.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.ClothChainVersion(free, freeChain)).IsEqualTo(2);

                await Assert.That(ClothExtract.ClothChainVersion(locked, lockedChain)).IsLessThan(2);
            }
        }

        /// <summary>
        /// <see cref="TwoWideRopeFitting"/> on node 3 with j0 and its ring static; <paramref name="parentLocked"/> locks
        /// j0 to j1.
        /// </summary>
        private static FeModel StaticRootRopeFitting(bool parentLocked) => SyntheticCloth.Model(
            ["j0", "$ccj0_0", "$ccj0_1", "j1", "$ccj1_0", "$ccj1_1", "j2", "$ccj2_0", "$ccj2_1"], staticNodes: 3,
                parents: [-1, 0, 0, 0, 3, 3, 3, 6, 6],
            poses: [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, -2f, 0f), new(-8.5f, 0f, 0f), new(-8.5f, 2f, 0f),
                new(-8.5f, -2f, 0f), new(-17f, 0f, 0f), new(-17f, 2f, 0f), new(-17f, -2f, 0f)],
            body: $$"""
                m_SourceElems = [ 1, 2, 5, 4, 4, 5, 8, 7 ]
                m_ReverseOffsets = [ ]
                m_FitMatrices = [ { bone = [ 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 ] vCenter = [ 0.0, 0.0, 0.0 ] nEnd = 4 nNode = 3 nBeginDynamic = 0 } ]
                m_FitWeights = [ { flWeight = 0.25 nNode = 4 }, { flWeight = 0.25 nNode = 5 }, { flWeight = 0.25 nNode = 7 }, { flWeight = 0.25 nNode = 8 } ]
                m_LockToParent = [ {{(parentLocked ? "{ vOffset = [ 8.5, 0.0, 0.0 ] nCtrlParent = 3 nCtrlChild = 0 }" : string.Empty)}} ]
                """);

        /// <summary>
        /// A bulk-graded chain whose static first joint hangs off a rotation-locked parent reads below version 2; a
        /// free parent keeps version 2.
        /// </summary>
        [Test]
        public async Task AGoalOnlyLockDoesNotHoldABulkGradedChainAtVersion2()
        {
            var goalLocked = StaticFirstJointRope(rootRotationLocked: true);
            var parentLocked = StaticFirstJointRope(rootRotationLocked: false);
            var goalChain = goalLocked.BuildBoneChains()[0];
            var parentChain = parentLocked.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(goalLocked.ChainBasesAreBulkGraded(goalChain)).IsTrue();
                await Assert.That(ClothExtract.ChainLocksJoints(goalLocked, goalChain)).IsTrue();

                await Assert.That(ClothExtract.ClothChainVersion(parentLocked, parentChain)).IsEqualTo(2);

                await Assert.That(ClothExtract.ClothChainVersion(goalLocked, goalChain)).IsLessThan(2);
            }
        }

        /// <summary>
        /// The bulk-graded one-wide rope with j1 a static first joint under a rotation-locked or free root.
        /// </summary>
        private static FeModel StaticFirstJointRope(bool rootRotationLocked) => SyntheticCloth.Parse(
            OneWideRopeDocument("nNode = 3 nNodeX0 = 5 nNodeX1 = 2 nNodeY0 = 6 nNodeY1 = 1")
                .Replace("m_nStaticNodes = 1", "m_nStaticNodes = 2" + (rootRotationLocked ? " m_nRotLockStaticNodes = 1" : string.Empty),
                    StringComparison.Ordinal)
                .Replace("m_NodeInvMasses = [ 0.0, 1.0,", "m_NodeInvMasses = [ 0.0, 0.0,", StringComparison.Ordinal));
    }
}
