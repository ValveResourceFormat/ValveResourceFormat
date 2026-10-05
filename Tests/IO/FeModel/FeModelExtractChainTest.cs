using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Declaring ClothChain joints, versions, locks and rest poses.</summary>
    public class FeModelExtractChainTest : FeModelTestModels
    {
        /// <summary>
        /// A volume-solved selection over chain joints is declared as a container listing every covered joint at its
        /// weight; a surface-solved one declares none.
        /// </summary>
        [Test]
        public async Task AVolumetricSelectionOverChainJointsListsThemInItsNodeTable()
        {
            var cloth = (SuspenderAtNaturalRelaxation with
            {
                VertexMaps = [VertexMap("vmap0", 4164734239, 0, 0, 8, 0.5f), VertexMap("surface", 5, 8, 2, 6)],
                VertexMapValues = [255, 255, 255, 255, 128, 128, 255, 255, 255, 255, 255, 255, 255, 255],
            }).Reconstruct();
            var children = KVObject.Array();
            ClothExtract.AddClothChainVolumetricMaps(children, cloth, cloth.BuildBoneChains());

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
            var cloth = new FeModelBuilder
            {
                Names = ["root", "$cloth_m0p0", "locked_goal", "locked_parent", "goal_with_parent", "declared"],
                StaticNodes = 3,
                Parents = [-1, -1, -1, 2, 0, 4],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -1f), new(0f, 0f, -2f), new(0f, 0f, -3f), new(0f, 0f, -4f), new(0f, 0f, -5f)],
                LockToGoal = [1, 2],
                LockToParent = [Offset(2, 3, 0f, 0f, -1f), Offset(4, 5, 0f, 0f, -1f)],
            }.Reconstruct();
            var children = KVObject.Array();
            ClothExtract.AddClothJointLocks(children, cloth, static (_, name) => name != "declared");

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
            var root = ClothExtract.MakeClothJoint(TwistPair(0f, 0f).Reconstruct(), StaticRootJoint());
            var control = ClothExtract.MakeClothJoint(TwistPair(0f, 0.309f).Reconstruct(), StaticRootJoint());

            using (Assert.Multiple())
            {
                await Assert.That(root.GetFloatProperty("twist_relax")).IsEqualTo(1f);
                await Assert.That(root.GetBooleanProperty("simulate")).IsFalse();
                await Assert.That(control.GetFloatProperty("twist_relax")).IsEqualTo(0f);
            }
        }

        private static BoneChainJoint StaticRootJoint()
            => new() { Name = "coattail_0_L", Node = 0, ParentNode = -1, InvMass = 0f };

        /// <summary>
        /// A goal lock is declared as a <c>ClothRigidCloudCluster</c> over the locked joint's children, and the chain
        /// version reads such a lock beside preset-graded bases as 2.
        /// </summary>
        [Test]
        public async Task ALockBesidePresetBasesIsDeclaredAsARigidCloudCluster()
        {
            var chain = new BoneChain { RootBone = "coattail_0_L" };
            chain.Joints.Add(new BoneChainJoint { Node = 0, Name = "coattail_0_L", ParentNode = -1 });
            chain.Joints.Add(new BoneChainJoint { Node = 1, Name = "coattail_1_L", ParentNode = 0, ParentName = "coattail_0_L", InvMass = 1f });
            chain.Joints.Add(new BoneChainJoint { Node = 2, Name = "coattail_1_R", ParentNode = 0, ParentName = "coattail_0_L", InvMass = 1f });

            var locked = (CoattailFork with { LockToGoal = [0] }).Reconstruct();
            var unlocked = (CoattailFork with { LockToGoal = [] }).Reconstruct();
            var lockedJoints = ClothReconstruction.LockedJointsWithChildren(locked, chain).ToArray();
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
                await Assert.That(ClothReconstruction.IsRigidCloudClusterLock(locked, chain)).IsFalse();
                await Assert.That(ClothReconstruction.LockedJointsWithChildren(unlocked, chain).Any()).IsFalse();

                await Assert.That(ClothReconstruction.ClothChainVersion(new ClothReconstruction.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: true, RigidCloudClusterLock: false, LocksJoints: false, BasesBulkGraded: true,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false))).IsEqualTo(1);
                await Assert.That(ClothReconstruction.ClothChainVersion(new ClothReconstruction.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: true, RigidCloudClusterLock: true, LocksJoints: false, BasesBulkGraded: false,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false))).IsEqualTo(2);
                await Assert.That(ClothReconstruction.ClothChainVersion(new ClothReconstruction.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: true, RigidCloudClusterLock: false, LocksJoints: false, BasesBulkGraded: false,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false))).IsEqualTo(1);
            }
        }

        /// <summary>A static coattail root forking into a left and a right joint.</summary>
        private static FeModelBuilder CoattailFork => new()
        {
            Names = ["coattail_0_L", "coattail_1_L", "coattail_1_R"],
            StaticNodes = 1,
            Parents = [-1, 0, 0],
            Positions = [new(0f, 0f, 60f), new(0f, 4f, 52f), new(0f, -4f, 52f)],
        };

        /// <summary>
        /// Sub-chains staged at versions 0 and 1 under one root are declared as two chains, the root's ring going to
        /// the sub-chain whose ring follows it; with both ungrouped the tree stays one version-0 chain.
        /// </summary>
        [Test]
        public async Task SubChainsStagedAtTwoVersionsAreDeclaredApart()
        {
            var split = TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: true).Reconstruct();
            var moved = TwoVersionTree(rootRingFirst: false, thinTipGrouped: false, wideLeafGrouped: true).Reconstruct();
            var locked = TwoVersionTree(rootRingFirst: false, thinTipGrouped: false, wideLeafGrouped: true,
                rootRotationLocked: true).Reconstruct();
            var flipped = TwoVersionTree(rootRingFirst: true, thinTipGrouped: true, wideLeafGrouped: false).Reconstruct();
            var merged = TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: false).Reconstruct();
            var splitChains = split.BuildBoneChains(VersionOf(split));
            var movedChains = moved.BuildBoneChains(VersionOf(moved));
            var lockedChains = locked.BuildBoneChains(VersionOf(locked));
            var flippedChains = flipped.BuildBoneChains(VersionOf(flipped));
            var mergedChains = merged.BuildBoneChains(VersionOf(merged));

            static Func<BoneChain, int> VersionOf(ClothReconstruction cloth)
                => chain => ClothReconstruction.ClothChainVersion(cloth, chain);

            static BoneChain Owning(List<BoneChain> chains, string joint)
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
                await Assert.That(ClothReconstruction.ClothChainVersion(split, Owning(splitChains, "a1"))).IsEqualTo(0);
                await Assert.That(ClothReconstruction.ClothChainVersion(split, Owning(splitChains, "b1"))).IsEqualTo(1);
                await Assert.That(movedChains.Count).IsEqualTo(2);
                await Assert.That(Owning(movedChains, "a1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(Owning(movedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(1);
                await Assert.That(lockedChains.Count).IsEqualTo(2);
                await Assert.That(Owning(lockedChains, "a1").Joints[0].RingNodes.Count).IsEqualTo(1);
                await Assert.That(Owning(lockedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(flippedChains.Count).IsEqualTo(2);
                await Assert.That(ClothReconstruction.ClothChainVersion(flipped, Owning(flippedChains, "a1"))).IsEqualTo(1);
                await Assert.That(ClothReconstruction.ClothChainVersion(flipped, Owning(flippedChains, "b1"))).IsEqualTo(0);
                await Assert.That(Owning(flippedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(mergedChains.Count).IsEqualTo(1);
                await Assert.That(mergedChains[0].Joints.Count).IsEqualTo(5);
                await Assert.That(ClothReconstruction.ClothChainVersion(merged, mergedChains[0])).IsEqualTo(0);
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
                await Assert.That(ClothReconstruction.ClothChainVersion(preset, presetChain)).IsEqualTo(2);
                await Assert.That(ClothReconstruction.ClothChainVersion(fitted, fittedChain)).IsEqualTo(1);
                await Assert.That(ClothReconstruction.ClothChainVersion(bare, bareChain)).IsEqualTo(2);
            }
        }

        /// <summary>
        /// A simulated rope of three joints 8.5 apart, each with a two-node ring across Y, whose reverse offsets name
        /// ring node <paramref name="ringNode"/> of their own ring, or none.
        /// </summary>
        private static ClothReconstruction TwoWideRope(int? ringNode) => (TwoWideRopeBase with
        {
            ReverseOffsets = ringNode is { } side
                ? [.. Enumerable.Range(0, 3).Select(joint => ReverseOffset(joint * 3, (joint * 3) + 1 + side, 0f, 2f, 0f))]
                : [],
        }).Reconstruct();

        /// <summary>A simulated rope of three joints 8.5 apart, each with a two-node ring across Y.</summary>
        private static FeModelBuilder TwoWideRopeBase => new()
        {
            Names = ["j0", "$ccj0_0", "$ccj0_1", "j1", "$ccj1_0", "$ccj1_1", "j2", "$ccj2_0", "$ccj2_1"],
            Parents = [-1, 0, 0, 0, 3, 3, 3, 6, 6],
            Positions = [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, -2f, 0f), new(-8.5f, 0f, 0f), new(-8.5f, 2f, 0f),
                new(-8.5f, -2f, 0f), new(-17f, 0f, 0f), new(-17f, 2f, 0f), new(-17f, -2f, 0f)],
            SourceElems = [1, 2, 5, 4, 4, 5, 8, 7],
        };

        /// <summary>A joint's <c>extrude_twist</c> carries its tie roll.</summary>
        [Test]
        public async Task AChainJointsExtrudeTwistCarriesItsTieRoll()
        {
            var cloth = TwistPair(0f, 0f).Reconstruct();
            static BoneChainJoint RolledJoint()
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

            var rolled = ClothExtract.MakeClothJoint(cloth, RolledJoint(), chainExtrudes: true);

            await Assert.That(rolled.GetFloatProperty("extrude_twist")).IsEqualTo(-0.012008f).Within(1e-6f);
        }

        /// <summary>
        /// A rope hint left at its X pair with Y (0, 0) reads as twist-written (version 0); a graded run or a foreign X
        /// pair does not, and a chain whose original locks no joints keeps version 2.
        /// </summary>
        [Test]
        public async Task AnUngradedRopeHintSaysTheChainCompiledAtVersionZero()
        {
            var ungraded = (HintedRope with { DynNodeWindBases = [new(1, 3, 0, 0), new(3, 2, 0, 0)] }).Reconstruct();
            var graded = (HintedRope with { DynNodeWindBases = [new(3, 1, 3, 3), new(3, 2, 3, 3)] }).Reconstruct();
            var foreign = (HintedRope with { DynNodeWindBases = [new(3, 1, 0, 0), new(2, 3, 0, 0)] }).Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(ungraded.ChainHintsAreTwistWritten(TwistedRopeChain())).IsTrue();
                await Assert.That(graded.ChainHintsAreTwistWritten(TwistedRopeChain())).IsFalse();
                await Assert.That(foreign.ChainHintsAreTwistWritten(TwistedRopeChain())).IsFalse();

                await Assert.That(ClothReconstruction.ClothChainVersion(new ClothReconstruction.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: false, RigidCloudClusterLock: false, LocksJoints: false, BasesBulkGraded: null,
                    HintsTwistWritten: true, HasUnstagedThinJoint: false))).IsEqualTo(0);
                await Assert.That(ClothReconstruction.ClothChainVersion(new ClothReconstruction.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: true, LockedJoint: false, RigidCloudClusterLock: false, LocksJoints: true, BasesBulkGraded: null,
                    HintsTwistWritten: true, HasUnstagedThinJoint: false))).IsEqualTo(2);
            }
        }

        /// <summary>A rope of two static nodes and two simulated joints, with one <c>m_Ropes</c> run over j1 to j3.</summary>
        private static FeModelBuilder HintedRope => new()
        {
            Names = ["root", "j1", "j2", "j3"],
            StaticNodes = 2,
            Parents = [-1, 0, 1, 2],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f)],
            RopeCount = 1,
            Ropes = [4, 1, 2, 3],
        };

        /// <summary>
        /// A simulated two-sided leaf with no node base and no reverse offset reads version 0; a based, staged or
        /// zero-stretch leaf does not, and a chain whose original locks no joints keeps version 2.
        /// </summary>
        [Test]
        public async Task ATwoSidedLeafWithNoNodeBaseSaysTheChainCompiledAtVersionZero()
        {
            var unbased = TwoSidedStrip.Reconstruct();
            var based = (TwoSidedStrip with { NodeBases = [.. TwoSidedStrip.NodeBases!, NodeBase(6, 7, 8, 3, 6)] }).Reconstruct();
            var staged = (TwoSidedStrip with { ReverseOffsets = [ReverseOffset(6, 7, 0f, 0f, 0f)] }).Reconstruct();
            var slack = TwoSidedStripChain();
            slack.Joints[2].StretchStiffness = 0f;

            using (Assert.Multiple())
            {
                await Assert.That(unbased.ChainHasUnbasedLeaf(TwoSidedStripChain())).IsTrue();
                await Assert.That(based.ChainHasUnbasedLeaf(TwoSidedStripChain())).IsFalse();
                await Assert.That(staged.ChainHasUnbasedLeaf(TwoSidedStripChain())).IsFalse();
                await Assert.That(unbased.ChainHasUnbasedLeaf(slack)).IsFalse();

                await Assert.That(ClothReconstruction.ClothChainVersion(new ClothReconstruction.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: false, LockedJoint: false, RigidCloudClusterLock: false, LocksJoints: false, BasesBulkGraded: null,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false, HasUnbasedLeaf: true))).IsEqualTo(0);
                await Assert.That(ClothReconstruction.ClothChainVersion(new ClothReconstruction.ChainVersionEvidence(RootAllowsRotation: true,
                    RootHasBase: false, LockedJoint: false, RigidCloudClusterLock: false, LocksJoints: true, BasesBulkGraded: null,
                    HintsTwistWritten: false, HasUnstagedThinJoint: false, HasUnbasedLeaf: true))).IsEqualTo(2);
            }
        }

        private static BoneChain TwoSidedStripChain()
        {
            var chain = new BoneChain { RootBone = "root", ExtrudeSides = 2 };
            chain.Joints.Add(new BoneChainJoint { Node = 0, Name = "root", ParentNode = -1, ExtrudeSides = 2, RingNodes = [1, 2] });
            chain.Joints.Add(new BoneChainJoint { Node = 3, Name = "j1", ParentNode = 0, InvMass = 1f, ExtrudeSides = 2, RingNodes = [4, 5] });
            chain.Joints.Add(new BoneChainJoint { Node = 6, Name = "j2", ParentNode = 3, InvMass = 1f, ExtrudeSides = 2, RingNodes = [7, 8] });
            return chain;
        }

        /// <summary>
        /// A static ringed root and two simulated joints, each with a two-node ring across X, j1 alone carrying a node base.
        /// </summary>
        private static FeModelBuilder TwoSidedStrip => new()
        {
            Names = ["root", "$ccroot_0", "$ccroot_1", "j1", "$ccj1_0", "$ccj1_1", "j2", "$ccj2_0", "$ccj2_1"],
            StaticNodes = 3,
            Parents = [-1, 0, 0, 0, 3, 3, 3, 6, 6],
            Positions = [new(0f, 0f, 0f), new(3f, 0f, 0f), new(-3f, 0f, 0f), new(0f, 0f, -10f), new(3f, 0f, -10f), new(-3f, 0f, -10f),
                new(0f, 0f, -20f), new(3f, 0f, -20f), new(-3f, 0f, -20f)],
            SourceElems = [0, 0, 0, 2, 1, 2, 5, 4, 4, 5, 8, 7],
            NodeBases = [NodeBase(3, 4, 5, 6, 3)],
            ReverseOffsets = [],
        };

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
                await Assert.That(ClothReconstruction.ClothChainVersion(tied, tiedChain)).IsEqualTo(2);
                await Assert.That(ClothReconstruction.ClothChainVersion(unbased, unbasedChain)).IsEqualTo(1);
                await Assert.That(ClothReconstruction.ClothChainVersion(decided, decidedChain)).IsEqualTo(1);
            }
        }

        /// <summary>
        /// A simulated rope of three joints 8.5 apart, each extruding a two-node ring across Z or across Y, whose j0 and j1
        /// reverse offsets name the Y0 node of their preset basis ($ccj1_1, $ccj2_1), and whose j0 and j1 may carry that basis
        /// with its Y pair swapped.
        /// </summary>
        private static ClothReconstruction SwappedYPairRope(bool alongZ, bool based) => (TwoWideRopeBase with
        {
            Positions = alongZ
                ? [.. TwoWideRopeBase.Positions!.Select(static position => new Vector3(position.X, position.Z, position.Y))]
                : TwoWideRopeBase.Positions,
            NodeBases = based ? [NodeBase(0, 4, 2, 1, 5), NodeBase(3, 7, 5, 4, 8)] : [],
            ReverseOffsets = [ReverseOffset(0, 5, 0f, 2f, 0f), ReverseOffset(3, 8, 0f, 2f, 0f), ReverseOffset(6, 7, 0f, 2f, 0f)],
        }).Reconstruct();

        /// <summary>
        /// A <c>ClothRigidCloudCluster</c> declares at least two members: a line's child and grandchild, a fork's two
        /// children, or a lone leaf paired with its locked parent.
        /// </summary>
        [Test]
        public async Task ARigidCloudClusterDeclaresAtLeastTwoMembers()
        {
            static BoneChain Chain(params int[] parents)
            {
                BoneChain chain = new() { RootBone = "joint_0" };
                for (var node = 0; node < parents.Length; node++)
                {
                    chain.Joints.Add(new BoneChainJoint { Node = node, Name = $"joint_{node}", ParentNode = parents[node] });
                }

                return chain;
            }

            static List<string> Members(BoneChain chain) => ClothExtract.RigidCloudClusterMembers(chain, chain.Joints[0]);

            var line = Chain(-1, 0, 1, 2);
            var fork = Chain(-1, 0, 0, 1);
            var pair = Chain(-1, 0);
            var cluster = ClothExtract.MakeClothRigidCloudCluster("joint_0", Members(line));
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
            var model = new FeModelBuilder
            {
                Names = ["root", "mid", "leaf", "$ccmid_0", "$ccmid_1", "$ccleaf_0", "$ccleaf_1"],
                StaticNodes = 1,
                RotLockStaticNodes = 0,
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -8f), new(0f, 0f, -16f), new(0f, 1f, -8f), new(0f, -1f, -8f),
                    new(0f, 1f, -16f), new(0f, -1f, -16f)],
            }.Reconstruct();

            static BoneChain Chain(IReadOnlyList<int> midRing)
            {
                var chain = new BoneChain { RootBone = "root", ExtrudeSides = 2 };
                chain.Joints.Add(new BoneChainJoint { Node = 0, Name = "root", ParentNode = -1 });
                chain.Joints.Add(new BoneChainJoint { Node = 1, Name = "mid", ParentNode = 0, InvMass = 1f, ExtrudeSides = midRing.Count, RingNodes = midRing });
                chain.Joints.Add(new BoneChainJoint { Node = 2, Name = "leaf", ParentNode = 1, InvMass = 1f, ExtrudeSides = 2, RingNodes = [5, 6] });
                return chain;
            }

            using (Assert.Multiple())
            {
                await Assert.That(ClothReconstruction.ChainLocksJoints(model, Chain([]))).IsFalse();
                await Assert.That(ClothReconstruction.ChainLocksJoints(model, Chain([3, 4]))).IsTrue();
            }
        }

        /// <summary>
        /// A static ringed child with no rod to its root is a chain of its own, declared in its parent's local space; a
        /// tying rod keeps one chain.
        /// </summary>
        [Test]
        public async Task AStaticRingedChildNoRodTiesToItsRootIsAChainOfItsOwn()
        {
            var loose = RingedChildren.Reconstruct().BuildBoneChains();
            var tied = (RingedChildren with { Rods = [.. RingedChildren.Rods!, RigidRod(0, 2, 10f)] }).Reconstruct().BuildBoneChains();

            var rotated = new FeModelBuilder
            {
                Names = ["parent", "child"],
                StaticNodes = 2,
                Parents = [-1, 0],
                Poses = [Pose(1f, 2f, 3f, 0f, 0f, 0.7071068f, 0.7071068f), Pose(1f, 12f, 3f, 0f, 0f, 0.7071068f, 0.7071068f)],
            }.Reconstruct();
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
        /// A static ringed root with a static ringed child <c>end</c> and two simulated ringed children <c>a</c> and <c>b</c>,
        /// rods tying the root to <c>a</c> and <c>b</c> only.
        /// </summary>
        private static FeModelBuilder RingedChildren => new()
        {
            Names = ["root", "$ccroot_0", "end", "$ccend_0", "a", "$cca_0", "b", "$ccb_0"],
            StaticNodes = 4,
            Parents = [-1, 0, 0, 2, 0, 4, 0, 6],
            Positions = [new(0f, 0f, 0f), new(0f, 2f, 0f), new(-10f, 0f, 0f), new(-10f, 2f, 0f), new(0f, 0f, -10f),
                new(0f, 2f, -10f), new(5f, 0f, -10f), new(5f, 2f, -10f)],
            Rods =
            [
                RigidRod(0, 4, 10f),
                RigidRod(1, 4, 10.198039f),
                RigidRod(0, 5, 10.198039f),
                RigidRod(4, 5, 2f),
                RigidRod(0, 6, 11.18034f),
                RigidRod(1, 6, 11.357817f),
                RigidRod(0, 7, 11.357817f),
                RigidRod(6, 7, 2f),
            ],
        };

        /// <summary>
        /// A static root with a one-way relaxation-free twist entry declares <c>twist_relax</c> 1 and stays
        /// unsimulated; a relaxed entry does not.
        /// </summary>
        [Test]
        public async Task AStaticRootStatesARelaxlessTwistFromItsOwnEntryAlone()
        {
            var oneWay = TwistOneWay(0f);
            var relaxed = TwistOneWay(0.309f);
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

        private static ClothReconstruction TwistOneWay(float toChild)
            => (TwistPair(0f, 0f) with { Twists = [Twist(0, 1, toChild, 1f)] }).Reconstruct();

        /// <summary>
        /// An interior static joint re-declares its relaxation-free twist at <c>twist_relax</c> 1 as a root does; a
        /// simulating joint does not.
        /// </summary>
        [Test]
        public async Task AnInteriorStaticJointRedeclaresItsRelaxlessTwistToo()
        {
            var interior = ClothExtract.MakeClothJoint(InteriorTwist(0f), InteriorStaticJoint(0f));
            var simulated = ClothExtract.MakeClothJoint(InteriorTwist(1f), InteriorStaticJoint(1f));

            using (Assert.Multiple())
            {
                await Assert.That(InteriorTwist(0f).OrientsRelaxlessTwist(1)).IsTrue();
                await Assert.That(interior.GetFloatProperty("twist_relax")).IsEqualTo(1f);
                await Assert.That(interior.GetBooleanProperty("simulate")).IsFalse();
                await Assert.That(simulated.GetFloatProperty("twist_relax")).IsNotEqualTo(1f);
            }
        }

        private static BoneChainJoint InteriorStaticJoint(float invMass)
            => new() { Name = "mid", Node = 1, ParentNode = 0, ParentName = "root", InvMass = invMass };

        /// <summary>A three-bone chain whose MIDDLE joint orients a twist the far end states nothing back for.</summary>
        private static ClothReconstruction InteriorTwist(float relax) => new FeModelBuilder
        {
            Names = ["root", "mid", "tip"],
            StaticNodes = 2,
            Parents = [-1, 0, 1],
            Twists = [Twist(1, 2, relax, 1f)],
        }.Reconstruct();

        /// <summary>
        /// A ringless chain's rotation-locked root without a node base keeps version 2.
        /// </summary>
        [Test]
        public async Task ARinglessChainsAbsentRootBaseStatesNoFormat()
        {
            var ringless = (ThreeJointRope with { RotLockStaticNodes = 1 }).Reconstruct();

            var chain = ringless.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(chain.ExtrudeSides).IsLessThan(1);
                await Assert.That(ringless.Index.AllowsRotation(chain.Joints[0].Node)).IsFalse();
                await Assert.That(ringless.Index.NodeBases.ContainsKey(chain.Joints[0].Node)).IsFalse();
                await Assert.That(ClothReconstruction.ClothChainVersion(ringless, chain))
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
            static ClothReconstruction Chain(float toChild, params FeTwistConstraint[] extraToParent) => (ThreeJointRope with
            {
                Twists = [Twist(1, 0, 0.4944f, 0f), Twist(1, 2, toChild, 1f), .. extraToParent],
            }).Reconstruct();

            static KVObject FirstDeclaration(ClothReconstruction cloth)
            {
                var chain = cloth.BuildBoneChains()[0];
                return ClothExtract.MakeClothJoint(cloth, chain.Joints.Find(static joint => joint.Name == "j1")!,
                    chain: chain);
            }

            var split = FirstDeclaration(Chain(0f));
            var relaxed = FirstDeclaration(Chain(0.3056f));
            var doubled = FirstDeclaration(Chain(0f, Twist(1, 0, 0.2163f, 0f)));

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
            static ClothReconstruction Run(float firstChildWard) => new FeModelBuilder
            {
                Names = ["root", "b", "a", "c", "d"],
                StaticNodes = 2,
                Parents = [-1, 2, 0, 1, 3],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -20f), new(0f, 0f, -10f), new(0f, 0f, -30f), new(0f, 0f, -40f)],
                Rods = [RigidRod(0, 2, 10f), RigidRod(2, 1, 10f), RigidRod(1, 3, 10f), RigidRod(3, 4, 10f)],
                Twists =
                [
                    Twist(2, 0, 0.4944f, 0f),
                    Twist(2, 1, firstChildWard, 0.5f),
                    Twist(3, 1, 0.4944f, 0f),
                    Twist(3, 4, firstChildWard, 0.5f),
                    Twist(2, 0, 0.2163f, 0f),
                    Twist(2, 1, 0.1337f, 0.25f),
                    Twist(3, 1, 0.2163f, 0f),
                    Twist(3, 4, 0.1337f, 0.25f),
                ],
            }.Reconstruct();

            static IEnumerable<string> Roots(ClothReconstruction cloth)
                => cloth.BuildBoneChains()[0].Joints.Select(static joint =>
                    joint.SecondDeclarationRoot ?? "<unmarked>");

            static float Declared(ClothReconstruction cloth, string bone, bool second)
            {
                var chain = cloth.BuildBoneChains()[0];
                return ClothExtract.MakeClothJoint(cloth,
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
                await Assert.That(ClothReconstruction.ClothChainVersion(permuted, permutedChain)).IsEqualTo(2);
            }
        }

        /// <summary><see cref="TwoWideRope"/> with j0's ring compiled after j1's.</summary>
        private static ClothReconstruction PermutedTwoWideRope(bool offsets) => new FeModelBuilder
        {
            Names = ["j0", "$ccj1_0", "$ccj1_1", "j1", "$ccj0_0", "$ccj0_1", "j2", "$ccj2_0", "$ccj2_1"],
            Parents = [-1, 3, 3, 0, 0, 0, 3, 6, 6],
            Positions = [new(0f, 0f, 0f), new(-8.5f, 2f, 0f), new(-8.5f, -2f, 0f), new(-8.5f, 0f, 0f), new(0f, 2f, 0f),
                new(0f, -2f, 0f), new(-17f, 0f, 0f), new(-17f, 2f, 0f), new(-17f, -2f, 0f)],
            SourceElems = [4, 5, 2, 1, 1, 2, 8, 7],
            ReverseOffsets = offsets ? [ReverseOffset(0, 4, 0f, 2f, 0f), ReverseOffset(3, 1, 0f, 2f, 0f)] : [],
        }.Reconstruct();

        /// <summary>
        /// A fit matrix on a joint the version-2 preset would offset reads below version 2; no fit, or a fit on the
        /// leaf, keeps 2.
        /// </summary>
        [Test]
        public async Task AFitMatrixOnAJointThePresetWouldOffsetRulesOutVersion2()
        {
            var bare = TwoWideRopeFitting(null).Reconstruct();
            var leaf = TwoWideRopeFitting(6).Reconstruct();
            var fitted = TwoWideRopeFitting(3).Reconstruct();
            var bareChain = bare.BuildBoneChains()[0];
            var leafChain = leaf.BuildBoneChains()[0];
            var fittedChain = fitted.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(ClothReconstruction.ClothChainVersion(bare, bareChain)).IsEqualTo(2);
                await Assert.That(ClothReconstruction.ClothChainVersion(leaf, leafChain)).IsEqualTo(2);

                await Assert.That(ClothReconstruction.ClothChainVersion(fitted, fittedChain)).IsLessThan(2);
            }
        }

        /// <summary>
        /// <see cref="TwoWideRope"/> without reverse offsets and, where given, a fit matrix on <paramref name="fitNode"/>
        /// over the ring nodes.
        /// </summary>
        private static FeModelBuilder TwoWideRopeFitting(int? fitNode) => TwoWideRopeBase with
        {
            ReverseOffsets = [],
            FitMatrices = fitNode is { } node ? [FitMatrix(node, 4, 0)] : null,
            FitWeights = fitNode is not null ? [FitWeight(4, 0.25f), FitWeight(5, 0.25f), FitWeight(7, 0.25f), FitWeight(8, 0.25f)] : null,
        };

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
                await Assert.That(ClothReconstruction.ClothChainVersion(free, freeChain)).IsEqualTo(2);

                await Assert.That(ClothReconstruction.ClothChainVersion(locked, lockedChain)).IsLessThan(2);
            }
        }

        /// <summary>
        /// <see cref="TwoWideRopeFitting"/> on node 3 with j0 and its ring static; <paramref name="parentLocked"/> locks
        /// j0 to j1.
        /// </summary>
        private static ClothReconstruction StaticRootRopeFitting(bool parentLocked) => (TwoWideRopeFitting(3) with
        {
            StaticNodes = 3,
            LockToParent = parentLocked ? [Offset(3, 0, 8.5f, 0f, 0f)] : [],
        }).Reconstruct();

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
                await Assert.That(ClothReconstruction.ChainLocksJoints(goalLocked, goalChain)).IsTrue();

                await Assert.That(ClothReconstruction.ClothChainVersion(parentLocked, parentChain)).IsEqualTo(2);

                await Assert.That(ClothReconstruction.ClothChainVersion(goalLocked, goalChain)).IsLessThan(2);
            }
        }

        /// <summary>
        /// The bulk-graded one-wide rope with j1 a static first joint under a rotation-locked or free root.
        /// </summary>
        private static ClothReconstruction StaticFirstJointRope(bool rootRotationLocked) => (OneWideRope(NodeBase(3, 5, 2, 6, 1)) with
        {
            StaticNodes = 2,
            RotLockStaticNodes = rootRotationLocked ? 1 : null,
        }).Reconstruct();
    }
}
