using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace Tests
{
    /// <summary>Reconstructing bone chains from a compiled FeModel: joints, rings, links, locks and the chain version evidence.</summary>
    public class ClothFeModelChainTest : ClothTestFixtures
    {
        /// <summary>
        /// Chains are ordered by the lowest simulated node their joints occupy, not by their root.
        /// </summary>
        [Test]
        public async Task ChainsAreOrderedByTheirLowestSimulatedNode()
        {
            var feModel = SyntheticCloth.Model(
                ["rootA", "rootB", "jB", "jA"], staticNodes: 2, parents: [-1, -1, 1, 0],
                poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 0f, -5f), new(0f, 0f, -5f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 3, 5f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 5f, 1f)}}
                    ]
                    """);

            var chains = feModel.BuildBoneChains();

            await Assert.That(chains.Count).IsEqualTo(2);

            using (Assert.Multiple())
            {
                await Assert.That(chains[0].RootBone).IsEqualTo("rootB");
                await Assert.That(chains[1].RootBone).IsEqualTo("rootA");

                await Assert.That(chains[0].Joints[0].Node).IsEqualTo(1);
                await Assert.That(chains[1].Joints[0].Node).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A ring suffix index that restarts starts a second declaration of the bone, splitting the chain in two.
        /// </summary>
        [Test]
        public async Task ARingSuffixRestartSplitsOneBoneIntoTwoDeclarations()
        {
            var split = RingDeclarationModel(
                "\"$ccroot_0\", \"$ccroot_1\", \"$ccroot_0\", \"$ccroot_1\"").BuildBoneChains();
            var single = RingDeclarationModel(
                "\"$ccroot_0\", \"$ccroot_1\", \"$ccroot_2\", \"$ccroot_3\"").BuildBoneChains();

            using (Assert.Multiple())
            {
                await Assert.That(split.Count).IsEqualTo(2);
                await Assert.That(split[0].RootBone).IsEqualTo("root");
                await Assert.That(split[1].RootBone).IsEqualTo("root");
                await Assert.That(split[0].DeclarationSuffix).IsNotEqualTo(split[1].DeclarationSuffix);
                await Assert.That(split[0].Joints[0].ProxyNode).IsEqualTo(1);
                await Assert.That(split[1].Joints[0].ProxyNode).IsEqualTo(3);
                await Assert.That(split[0].ExtrudeSides).IsEqualTo(2);

                await Assert.That(single.Count).IsEqualTo(1);
                await Assert.That(single[0].ExtrudeSides).IsEqualTo(4);
            }
        }

        /// <summary>
        /// Each declaration of a bone carries only the ring nodes it extruded.
        /// </summary>
        [Test]
        public async Task EachDeclarationOfABoneCarriesItsOwnRingNodes()
        {
            var split = RingDeclarationModel(
                "\"$ccroot_0\", \"$ccroot_1\", \"$ccroot_0\", \"$ccroot_1\"").BuildBoneChains();
            var single = RingDeclarationModel(
                "\"$ccroot_0\", \"$ccroot_1\", \"$ccroot_2\", \"$ccroot_3\"").BuildBoneChains();

            using (Assert.Multiple())
            {
                await Assert.That(split[0].Joints[0].RingNodes).IsEquivalentTo([1, 2]);
                await Assert.That(split[1].Joints[0].RingNodes).IsEquivalentTo([3, 4]);
                await Assert.That(single[0].Joints[0].RingNodes).IsEquivalentTo([1, 2, 3, 4]);
            }
        }

        private static FeModel RingDeclarationModel(string ringNames) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", {{ringNames}} ]
                m_SkelParents = [ -1, 0, 0, 0, 0 ]
                m_nNodeCount = 5
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 2f, 0f)}}
                    {{SyntheticCloth.Pose(0f, -2f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, 2f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -2f)}}
                ]
            }
            """);

        /// <summary>
        /// A chain joint's node base tells the version-1 bulk grade (reaching the parent's ring) from the version-2
        /// preset grade over the joint's own extrusion and its child's.
        /// </summary>
        [Test]
        public async Task AChainJointBasisNamingTheParentRingWasBulkGraded()
        {
            var bulk = OneWideRope("nNode = 3 nNodeX0 = 5 nNodeX1 = 2 nNodeY0 = 6 nNodeY1 = 1");
            var preset = OneWideRope("nNode = 3 nNodeX0 = 5 nNodeX1 = 4 nNodeY0 = 6 nNodeY1 = 3");

            using (Assert.Multiple())
            {
                await Assert.That(bulk.ChainBasesAreBulkGraded(bulk.BuildBoneChains()[0])).IsTrue();
                await Assert.That(preset.ChainBasesAreBulkGraded(preset.BuildBoneChains()[0])).IsFalse();
            }
        }

        /// <summary>
        /// A rope whose only node base is on a leaf joint decides no chain version.
        /// </summary>
        [Test]
        public async Task ALeafJointBasisDecidesNoChainVersion()
        {
            var leaf = OneWideRope("nNode = 5 nNodeX0 = 5 nNodeX1 = 2 nNodeY0 = 6 nNodeY1 = 3");

            await Assert.That(leaf.ChainBasesAreBulkGraded(leaf.BuildBoneChains()[0])).IsNull();
        }

        private static FeModel OneWideRope(string nodeBase) => SyntheticCloth.Parse(OneWideRopeDocument(nodeBase));

        /// <summary>
        /// A twisted joint's hint left at {joint, twist end, 0, 0} reads as version 0; a graded hint does not.
        /// </summary>
        [Test]
        public async Task AnUngradedTwistHintSaysTheChainCompiledAtVersionZero()
        {
            var ungraded = TwistedRope("nNodeX0 = 2 nNodeX1 = 1 nNodeY0 = 0 nNodeY1 = 0",
                "nNodeX0 = 3 nNodeX1 = 2 nNodeY0 = 0 nNodeY1 = 0");
            var graded = TwistedRope("nNodeX0 = 3 nNodeX1 = 1 nNodeY0 = 3 nNodeY1 = 3",
                "nNodeX0 = 3 nNodeX1 = 2 nNodeY0 = 3 nNodeY1 = 3");

            using (Assert.Multiple())
            {
                await Assert.That(ungraded.ChainHintsAreTwistWritten(TwistedRopeChain())).IsTrue();
                await Assert.That(graded.ChainHintsAreTwistWritten(TwistedRopeChain())).IsFalse();
            }
        }

        private static FeModel TwistedRope(string hint2, string hint3) => SyntheticCloth.Model(
            ["root", "j1", "j2", "j3"], staticNodes: 2, parents: [-1, 0, 1, 2],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f)],
            body: $$"""
                m_Twists =
                [
                    { nNodeOrient = 2 nNodeEnd = 1 flTwistRelax = 0.618 },
                    { nNodeOrient = 2 nNodeEnd = 3 flTwistRelax = 0.382 },
                    { nNodeOrient = 3 nNodeEnd = 2 flTwistRelax = 0.618 },
                ]
                m_DynNodeWindBases =
                [
                    { {{hint2}} },
                    { {{hint3}} },
                ]
                """);

        /// <summary>
        /// A stretchless chain's top link is read off the bend rod spanning its second joint, rooting the chain at that
        /// parent.
        /// </summary>
        [Test]
        public async Task ASpanningBendRodRootsAStretchlessChainAtTheParentItSpansTo()
        {
            var spanned = StretchlessChain(SyntheticCloth.RigidRod(0, 2, 20f, 1f)
                + SyntheticCloth.RigidRod(1, 3, 20f, 1f)).BuildBoneChains();
            var unspanned = StretchlessChain(SyntheticCloth.RigidRod(1, 3, 20f, 1f)).BuildBoneChains();

            using (Assert.Multiple())
            {
                await Assert.That(spanned.Count).IsEqualTo(1);
                await Assert.That(spanned[0].RootBone).IsEqualTo("j0");
                await Assert.That(spanned[0].Joints.Count).IsEqualTo(4);
                await Assert.That(unspanned.Find(chain => chain.Joints.Exists(joint => joint.Name == "j3"))!
                    .RootBone).IsEqualTo("j1");
            }
        }

        /// <summary>
        /// A thin chain tip without a reverse offset reads as an unstaged thin joint (version 0); with the offset it is
        /// staged. A second two-wide leaf is unstaged only when it also lacks its offset.
        /// </summary>
        [Test]
        public async Task AThinChainTipWithoutAReverseOffsetWasStagedAtVersionZero()
        {
            var versionZero = SyntheticCloth.Parse(ThinTipChain(tipRecord: false));
            var versionOne = SyntheticCloth.Parse(ThinTipChain(tipRecord: true));
            var stagedLeaf = SyntheticCloth.Parse(WithWideLeaf(ThinTipChain(tipRecord: false), leafRecord: true));
            var unstagedLeaf = SyntheticCloth.Parse(WithWideLeaf(ThinTipChain(tipRecord: false), leafRecord: false));
            var chainZero = versionZero.BuildBoneChains();
            var chainOne = versionOne.BuildBoneChains();
            var chainStaged = stagedLeaf.BuildBoneChains();
            var chainUnstaged = unstagedLeaf.BuildBoneChains();

            using (Assert.Multiple())
            {
                await Assert.That(chainZero[0].Joints.Count).IsEqualTo(4);
                await Assert.That(versionZero.ChainHasUnstagedThinJoint(chainZero[0])).IsTrue();
                await Assert.That(versionOne.ChainHasUnstagedThinJoint(chainOne[0])).IsFalse();
                await Assert.That(chainStaged[0].Joints.Count).IsEqualTo(5);
                await Assert.That(stagedLeaf.ChainHasUnstagedThinJoint(chainStaged[0])).IsFalse();
                await Assert.That(unstagedLeaf.ChainHasUnstagedThinJoint(chainUnstaged[0])).IsTrue();
            }
        }

        private static string WithWideLeaf(string text, bool leafRecord) => text
            .Replace("\"$cccoattail_end_L_0\" ]",
                "\"$cccoattail_end_L_0\", \"coattail_side_L\", \"$cccoattail_side_L_0\", \"$cccoattail_side_L_1\" ]",
                StringComparison.Ordinal)
            .Replace("m_SkelParents = [ -1, 0, 0, 2, 2, 4, 4, 6 ]", "m_SkelParents = [ -1, 0, 0, 2, 2, 4, 4, 6, 4, 8, 8 ]",
                StringComparison.Ordinal)
            .Replace("m_nNodeCount = 8", "m_nNodeCount = 11", StringComparison.Ordinal)
            .Replace("2.0, 2.0, 1.0, 1.0 ]", "2.0, 2.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]", StringComparison.Ordinal)
            .Replace("-19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],",
                "-19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ], "
                + SyntheticCloth.Pose(-14.8f, 13.0f, 45.0f) + " " + SyntheticCloth.Pose(-14.8f, 15.0f, 45.0f)
                + " " + SyntheticCloth.Pose(-14.8f, 11.0f, 45.0f), StringComparison.Ordinal)
            .Replace("{ nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },",
                "{ nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 }, "
                + SyntheticCloth.RigidRod(4, 8, 9.0f, 1f) + " " + SyntheticCloth.RigidRod(8, 9, 2f, 1f) + " "
                + SyntheticCloth.RigidRod(8, 10, 2f, 1f) + " " + SyntheticCloth.RigidRod(9, 10, 4f, 1f),
                StringComparison.Ordinal)
            .Replace("nBoneCtrl = 4 nTargetNode = 6 },",
                "nBoneCtrl = 4 nTargetNode = 6 }," + (leafRecord ? " { vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = 8 nTargetNode = 9 }," : ""),
                StringComparison.Ordinal);

        private static string ThinTipChain(bool tipRecord) => ExplicitMassChainText.Replace("m_Rods =", $$"""
            m_LockToGoal = [ 0 ]
                m_ReverseOffsets =
                [
                    { vOffset = [ 8.49993, 0.00006, 0.000001 ] nBoneCtrl = 2 nTargetNode = 4 },
                    { vOffset = [ 8.500038, -0.000026, 0.000008 ] nBoneCtrl = 4 nTargetNode = 6 },
                    {{(tipRecord ? "{ vOffset = [ -0.000001, 2.000001, -0.000001 ] nBoneCtrl = 6 nTargetNode = 7 }," : "")}}
                ]
                m_Rods =
            """, StringComparison.Ordinal);

        /// <summary>
        /// A static joint's parent lock reads as <c>lock_translation</c> only where the fit pass could not have written
        /// it: not on a free root's fit-owning joint below version 2, but under a covering root fit or a
        /// rotation-locked root.
        /// </summary>
        [Test]
        public async Task AStaticJointsFitGroupParentLockIsNotLockTranslation()
        {
            var free = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: true));
            var fitted = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: true,
                rootFitsFirstJoint: true));
            var locked = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: true,
                rootRotationLocked: true));

            using (Assert.Multiple())
            {
                await Assert.That(free.LocksTranslation(Array.IndexOf(free.CtrlNames, "a0"), chainVersion: 1)).IsFalse();
                await Assert.That(free.LocksTranslation(Array.IndexOf(free.CtrlNames, "a0"), chainVersion: 2)).IsTrue();
                await Assert.That(free.LocksTranslation(Array.IndexOf(free.CtrlNames, "b0"), chainVersion: 1)).IsTrue();
                await Assert.That(fitted.LocksTranslation(Array.IndexOf(fitted.CtrlNames, "a0"), chainVersion: 1)).IsTrue();
                await Assert.That(locked.LocksTranslation(Array.IndexOf(locked.CtrlNames, "a0"), chainVersion: 1)).IsTrue();
                await Assert.That(locked.LocksTranslation(Array.IndexOf(locked.CtrlNames, "b0"), chainVersion: 1)).IsTrue();
            }
        }

        /// <summary>
        /// A two-ring leaf without a reverse offset reads as unstaged, and with one as staged.
        /// </summary>
        [Test]
        public async Task ATwoRingLeafHoldsTwoFitTableEntries()
        {
            var ungrouped = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: true, thinTipGrouped: true, wideLeafGrouped: false));
            var grouped = SyntheticCloth.Parse(TwoVersionTree(rootRingFirst: true, thinTipGrouped: true, wideLeafGrouped: true));

            static FeModel.BoneChainJoint[] WideJoints(FeModel feModel) =>
            [
                .. feModel.BuildBoneChains().SelectMany(static chain => chain.Joints)
                    .Where(static joint => joint.Name is "b0" or "b1"),
            ];

            using (Assert.Multiple())
            {
                await Assert.That(ungrouped.ThinJointStagingOf(WideJoints(ungrouped))).IsEqualTo(FeModel.ThinJointStaging.Unstaged);
                await Assert.That(grouped.ThinJointStagingOf(WideJoints(grouped))).IsEqualTo(FeModel.ThinJointStaging.Staged);
            }
        }

        /// <summary>
        /// Below version 2 a parent fit holding a fit-owning static joint and its direct children reads
        /// <c>lock_translation</c>; one holding only the joint does not, except at version 2.
        /// </summary>
        [Test]
        public async Task AParentFitHoldingAFitJointsOneWideEntryIsLockTranslation()
        {
            var model = SyntheticCloth.Parse(OneWideEntryTree());

            using (Assert.Multiple())
            {
                await Assert.That(model.LocksTranslation(Array.IndexOf(model.CtrlNames, "a0"), chainVersion: 1)).IsTrue();
                await Assert.That(model.LocksTranslation(Array.IndexOf(model.CtrlNames, "b0"), chainVersion: 1)).IsFalse();
                await Assert.That(model.LocksTranslation(Array.IndexOf(model.CtrlNames, "b0"), chainVersion: 2)).IsTrue();
            }
        }

        private static string OneWideEntryTree()
        {
            List<(string Name, string? Parent, Vector3 Position)> nodes =
            [
                ("root", null, Vector3.Zero), ("a0", "root", new Vector3(10f, 0f, 0f)), ("b0", "root", new Vector3(-10f, 0f, 0f)),
                ("a1", "a0", new Vector3(10f, 0f, -10f)), ("b1", "b0", new Vector3(-10f, 0f, -10f)),
                ("a2", "a1", new Vector3(10f, 0f, -20f)), ("b2", "b1", new Vector3(-10f, 0f, -20f)),
            ];
            var names = nodes.ConvertAll(static node => node.Name);
            int At(string name) => names.IndexOf(name);

            var fits = new List<string>();
            var weights = new List<string>();
            foreach (var group in "root:a0 a1 b0|a0:a0 a1 a2|b0:b0 b1 b2".Split('|'))
            {
                var (owner, members) = (group.Split(':')[0], group.Split(':')[1]);
                weights.AddRange(members.Split(' ').Select(member => "{ flWeight = 1.0 nNode = " + At(member) + " nDummy = 0 },"));
                fits.Add("{ bone = [ 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 ] vCenter = [ 0.0, 0.0, 0.0 ] nEnd = " + weights.Count
                    + " nNode = " + At(owner) + " nBeginDynamic = " + weights.Count + " },");
            }

            return $$"""
                {
                    m_CtrlName = [ {{string.Join(", ", names.Select(static name => '"' + name + '"'))}} ]
                    m_SkelParents = [ {{string.Join(", ", nodes.Select(node => node.Parent is null ? -1 : At(node.Parent)))}} ]
                    m_nNodeCount = {{nodes.Count}}
                    m_nStaticNodes = 3
                    m_nRotLockStaticNodes = 0
                    m_NodeInvMasses = [ {{string.Join(", ", nodes.Select(static (_, i) => i < 3 ? "0.0" : "1.0"))}} ]
                    m_InitPose = [ {{string.Concat(nodes.Select(static node => SyntheticCloth.Pose(node.Position.X, node.Position.Y, node.Position.Z)))}} ]
                    m_LockToGoal = [ 0 ]
                    m_LockToParent =
                    [
                        { vOffset = [ 10.0, 0.0, 0.0 ] nCtrlParent = 0 nCtrlChild = {{At("a0")}} },
                        { vOffset = [ -10.0, 0.0, 0.0 ] nCtrlParent = 0 nCtrlChild = {{At("b0")}} },
                    ]
                    m_FitMatrices = [ {{string.Join(" ", fits)}} ]
                    m_FitWeights = [ {{string.Join(" ", weights)}} ]
                }
                """;
        }

        /// <summary>
        /// A one-wide joint's base naming the parent ring reads as bulk graded even when the scan does not predict it
        /// and with no skeleton parents; an entry inside the preset's candidates reads neither.
        /// </summary>
        [Test]
        public async Task AJointBasisNamingTheParentRingIsBulkGradedWithoutItsScan()
        {
            const string ParentRingEntry = "nNode = 3 nNodeX0 = 2 nNodeX1 = 1 nNodeY0 = 5 nNodeY1 = 6";
            var unpredicted = OneWideRope(ParentRingEntry);
            var unparented = SyntheticCloth.Parse(OneWideRopeDocument(ParentRingEntry).Replace(
                "m_SkelParents = [ -1, 0, 1, 1, 3, 3, 5 ]", string.Empty, StringComparison.Ordinal));
            var inside = OneWideRope("nNode = 3 nNodeX0 = 3 nNodeX1 = 6 nNodeY0 = 4 nNodeY1 = 5");

            var unparentedChain = new FeModel.BoneChain { RootBone = "j1", ExtrudeSides = 1 };
            unparentedChain.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1, InvMass = 1f, ExtrudeSides = 1, RingNodes = [2] });
            unparentedChain.Joints.Add(new FeModel.BoneChainJoint { Node = 3, Name = "j2", ParentNode = 1, InvMass = 1f, ExtrudeSides = 1, RingNodes = [4] });
            unparentedChain.Joints.Add(new FeModel.BoneChainJoint { Node = 5, Name = "j3", ParentNode = 3, InvMass = 1f, ExtrudeSides = 1, RingNodes = [6] });

            using (Assert.Multiple())
            {
                await Assert.That(unpredicted.ChainBasesAreBulkGraded(unpredicted.BuildBoneChains()[0])).IsTrue();
                await Assert.That(unparented.SkelParents.Length).IsEqualTo(0);
                await Assert.That(unparented.ChainBasesAreBulkGraded(unparentedChain)).IsTrue();
                await Assert.That(inside.ChainBasesAreBulkGraded(inside.BuildBoneChains()[0])).IsNull();
            }
        }

        /// <summary>
        /// A static joint whose chain stages it no fit group, or which the declaration simulates, holds its parent lock
        /// by <c>lock_translation</c>; a version-1 lone joint, a table of four, or no chain reads the lock as the fit
        /// pass's.
        /// </summary>
        [Test]
        public async Task AStaticJointItsChainGivesNoFitGroupHoldsItsParentLockByTheKey()
        {
            var ringed = ParentLockedTip(rings: true);
            var ringless = ParentLockedTip(rings: false);

            static FeModel.BoneChain Chain(FeModel feModel, bool withKid, bool tipSimulated = false)
            {
                var chain = new FeModel.BoneChain { RootBone = "tip" };
                var tip = Array.IndexOf(feModel.CtrlNames, "tip");
                chain.Joints.Add(new FeModel.BoneChainJoint { Node = tip, Name = "tip", ParentNode = -1, InvMass = tipSimulated ? 1f : 0f });
                if (withKid)
                {
                    chain.Joints.Add(new FeModel.BoneChainJoint
                    {
                        Node = Array.IndexOf(feModel.CtrlNames, "kid"),
                        Name = "kid",
                        ParentNode = tip,
                        ParentName = "tip",
                        InvMass = 1f,
                    });
                }

                return chain;
            }

            var ringedTip = Array.IndexOf(ringed.CtrlNames, "tip");
            var ringlessTip = Array.IndexOf(ringless.CtrlNames, "tip");

            using (Assert.Multiple())
            {
                await Assert.That(ringed.LocksTranslation(ringedTip, chainVersion: 0, chain: Chain(ringed, withKid: false))).IsTrue();
                await Assert.That(ringless.LocksTranslation(ringlessTip, chainVersion: 0, chain: Chain(ringless, withKid: true))).IsTrue();
                await Assert.That(ringed.LocksTranslation(ringedTip, chainVersion: 0, chain: Chain(ringed, withKid: true, tipSimulated: true))).IsTrue();
                await Assert.That(ringed.LocksTranslation(ringedTip, chainVersion: 1, chain: Chain(ringed, withKid: false))).IsFalse();
                await Assert.That(ringed.LocksTranslation(ringedTip, chainVersion: 0, chain: Chain(ringed, withKid: true))).IsFalse();
                await Assert.That(ringed.LocksTranslation(ringedTip, chainVersion: 0)).IsFalse();
            }
        }

        private static FeModel ParentLockedTip(bool rings)
        {
            List<(string Name, string? Parent, Vector3 Position)> nodes = [("root", null, Vector3.Zero)];
            if (rings)
            {
                nodes.Add(("$ccroot_0", "root", new Vector3(0f, 2f, 0f)));
            }

            nodes.Add(("tip", "root", new Vector3(10f, 0f, 0f)));
            if (rings)
            {
                nodes.Add(("$cctip_0", "tip", new Vector3(10f, 2f, 0f)));
            }

            nodes.Add(("kid", "tip", new Vector3(10f, 0f, -10f)));
            if (rings)
            {
                nodes.Add(("$cckid_0", "kid", new Vector3(10f, 2f, -10f)));
            }

            var names = nodes.ConvertAll(static node => node.Name);
            int At(string name) => names.IndexOf(name);
            var statics = At("kid");

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{string.Join(", ", names.Select(static name => '"' + name + '"'))}} ]
                    m_SkelParents = [ {{string.Join(", ", nodes.Select(node => node.Parent is null ? -1 : At(node.Parent)))}} ]
                    m_nNodeCount = {{nodes.Count}}
                    m_nStaticNodes = {{statics}}
                    m_nRotLockStaticNodes = 0
                    m_NodeInvMasses = [ {{string.Join(", ", nodes.Select((_, i) => i < statics ? "0.0" : "1.0"))}} ]
                    m_InitPose = [ {{string.Concat(nodes.Select(static node => SyntheticCloth.Pose(node.Position.X, node.Position.Y, node.Position.Z)))}} ]
                    m_LockToGoal = [ 0 ]
                    m_LockToParent = [ { vOffset = [ 10.0, 0.0, 0.0 ] nCtrlParent = 0 nCtrlChild = {{At("tip")}} } ]
                    m_NodeBases = [ { nNode = {{At("tip")}} nNodeX0 = {{At("tip")}} nNodeX1 = {{At("kid")}} nNodeY0 = 0 nNodeY1 = {{At("kid")}} } ]
                }
                """);
        }

        /// <summary>
        /// A reverse offset's version reading without <c>m_SkelParents</c> uses the chain's own rings and matches the
        /// parented rope's reading.
        /// </summary>
        [Test]
        public async Task AReverseOffsetIsReadOverTheChainsOwnRingsWithoutSkeletonParents()
        {
            const string JointBase = "nNode = 3 nNodeX0 = 2 nNodeX1 = 1 nNodeY0 = 5 nNodeY1 = 6";
            const string Offset = "m_ReverseOffsets = [ { vOffset = [ 0.0, 0.0, 0.0 ] nBoneCtrl = 3 nTargetNode = 4 } ]\n                m_SourceElems";
            var parentedDocument = OneWideRopeDocument(JointBase).Replace("m_SourceElems", Offset, StringComparison.Ordinal);
            var parented = SyntheticCloth.Parse(parentedDocument);
            var unparented = SyntheticCloth.Parse(parentedDocument.Replace(
                "m_SkelParents = [ -1, 0, 1, 1, 3, 3, 5 ]", string.Empty, StringComparison.Ordinal));

            var unparentedChain = new FeModel.BoneChain { RootBone = "j1", ExtrudeSides = 1 };
            unparentedChain.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1, InvMass = 1f, ExtrudeSides = 1, RingNodes = [2] });
            unparentedChain.Joints.Add(new FeModel.BoneChainJoint { Node = 3, Name = "j2", ParentNode = 1, InvMass = 1f, ExtrudeSides = 1, RingNodes = [4] });
            unparentedChain.Joints.Add(new FeModel.BoneChainJoint { Node = 5, Name = "j3", ParentNode = 3, InvMass = 1f, ExtrudeSides = 1, RingNodes = [6] });

            var reading = parented.ChainReverseOffsetsArePreset(parented.BuildBoneChains()[0]);

            using (Assert.Multiple())
            {
                await Assert.That(reading).IsNotNull();
                await Assert.That(unparented.ChainReverseOffsetsArePreset(unparentedChain)).IsEqualTo(reading);
            }
        }

        /// <summary>
        /// Without <c>m_SkelParents</c> a ring joint hangs under the joint its source elements join it to; with no such
        /// element it stays a chain root, and compiled parents stand as given.
        /// </summary>
        [Test]
        public async Task AnOldEraRingNoSourceElementJoinsToItsSkeletonParentHangsUnderTheRingItsElementsName()
        {
            static FeModel.BoneChainJoint Joint(FeModel model, string name)
                => model.BuildBoneChains().SelectMany(static chain => chain.Joints).First(joint => joint.Name == name);

            var tube = OldEraTube(compiledParents: false, kFace: true);
            var loose = OldEraTube(compiledParents: false, kFace: false);
            var compiled = OldEraTube(compiledParents: true, kFace: true);

            using (Assert.Multiple())
            {
                await Assert.That(tube.HasCompiledSkelParents).IsFalse();
                await Assert.That(Joint(tube, "k").ParentName).IsEqualTo("j3");
                await Assert.That(Joint(tube, "j3").ParentName).IsEqualTo("j2");
                await Assert.That(Joint(loose, "k").ParentNode).IsEqualTo(-1);
                await Assert.That(Joint(compiled, "k").ParentName).IsEqualTo("j2");
            }
        }

        /// <summary>
        /// Four simulated joints 10 apart down Z, each extruding a one-node ring 3 along X (rings anchored by <c>m_CtrlOffsets</c>),
        /// with k's skeleton parent j2, rods j1-j2, j2-j3 and j2-k between the rings, and source elements j1-j2, j2-j3 and, where
        /// <paramref name="kFace"/>, j3-k. <paramref name="compiledParents"/> adds <c>m_SkelParents</c> naming the skeleton parents.
        /// </summary>
        private static FeModel OldEraTube(bool compiledParents, bool kFace)
        {
            var parents = compiledParents ? "m_SkelParents = [ -1, 0, 0, 2, 2, 4, 2, 6 ]" : string.Empty;
            var faces = kFace ? "0, 0, 0, 3, 0, 1, 3, 2, 2, 3, 5, 4, 4, 5, 7, 6" : "0, 0, 0, 2, 0, 1, 3, 2, 2, 3, 5, 4";
            var model = SyntheticCloth.Model(
                ["j1", "$ccj1_0", "j2", "$ccj2_0", "j3", "$ccj3_0", "k", "$cck_0"], staticNodes: 0,
                poses: [new(0f, 0f, 0f), new(3f, 0f, 0f), new(0f, 0f, -10f), new(3f, 0f, -10f), new(0f, 0f, -20f),
                    new(3f, 0f, -20f), new(0f, 0f, -30f), new(3f, 0f, -30f)],
                body: $$"""
                    {{parents}}
                    m_CtrlOffsets =
                    [
                        { vOffset = [ 3.0, 0.0, 0.0 ] nCtrlParent = 0 nCtrlChild = 1 },
                        { vOffset = [ 3.0, 0.0, 0.0 ] nCtrlParent = 2 nCtrlChild = 3 },
                        { vOffset = [ 3.0, 0.0, 0.0 ] nCtrlParent = 4 nCtrlChild = 5 },
                        { vOffset = [ 3.0, 0.0, 0.0 ] nCtrlParent = 6 nCtrlChild = 7 },
                    ]
                    m_Rods = [ {{SyntheticCloth.RigidRod(1, 3, 10f, 1f)}} {{SyntheticCloth.RigidRod(3, 5, 10f, 1f)}} {{SyntheticCloth.RigidRod(3, 7, 20f, 1f)}} ]
                    m_SourceElems = [ {{faces}} ]
                    """);
            model.SkeletonBoneParents = new Dictionary<string, string?> { ["j1"] = null, ["j2"] = "j1", ["j3"] = "j2", ["k"] = "j2" };
            return model;
        }

        /// <summary>
        /// A rope with no rods between its joints reads as a chain with <c>stretch_spring</c> 0; without the rope the
        /// joints stay unlinked.
        /// </summary>
        [Test]
        public async Task ARopeWithNoRodsBetweenItsJointsIsAChainWithNoStretchSpring()
        {
            var roped = SyntheticCloth.Parse(RodlessTail("m_nRopeCount = 1\n                m_Ropes = [ 4, 0, 1, 2 ]"));
            var unroped = SyntheticCloth.Parse(RodlessTail(string.Empty));

            var chain = roped.BuildBoneChains().Find(static chain => chain.Joints.Count == 3);

            using (Assert.Multiple())
            {
                await Assert.That(chain).IsNotNull();
                await Assert.That(chain!.Joints[1].StretchStiffness).IsEqualTo(0f);
                await Assert.That(chain.Joints[2].StretchStiffness).IsEqualTo(0f);
                await Assert.That(unroped.BuildBoneChains().Exists(static chain => chain.Joints.Count > 1)).IsFalse();
            }
        }

        private static string RodlessTail(string ropes) => SyntheticCloth.Document(
            ["tail_0", "tail_1", "tail_2"], staticNodes: 1, parents: [-1, 0, 1],
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f)],
            body: $$"""
                {{ropes}}
                """);

        /// <summary>
        /// Raw-integrator flags with no goal bit, a follow link or a legacy stretch force mark an imported cloth;
        /// goal-integrator nodes, raw static nodes or a proxy sheet vertex do not.
        /// </summary>
        [Test]
        public async Task AnFxTableWithNoPairedColumnIsAnImportedCloth()
        {
            const string NoFields = "";
            const string Follow = "m_FollowNodes = [ { nParentNode = 0 nChildNode = 1 flWeight = 0.1 } ]";
            const string Legacy = "m_LegacyStretchForce = [ 0.0, 1.0, 1.0 ]";

            using (Assert.Multiple())
            {
                await Assert.That(FxTable(0xF00, 0x2F10, NoFields).IsImportedCloth).IsTrue();
                await Assert.That(FxTable(0x880, 0x2880, Follow).IsImportedCloth).IsTrue();
                await Assert.That(FxTable(0x880, 0x2880, Legacy).IsImportedCloth).IsTrue();
                await Assert.That(FxTable(0x880, 0x2880, NoFields).IsImportedCloth).IsFalse();
                await Assert.That(FxTable(0x200, 0x2080, NoFields).IsImportedCloth).IsFalse();
                await Assert.That(FxTable(0xF00, 0x2F10, NoFields, "$cloth_m0p2").IsImportedCloth).IsFalse();
            }
        }

        private static FeModel FxTable(uint staticFlags, uint dynamicFlags, string fields, string tip = "tail_2")
            => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "tail_0", "tail_1", "{{tip}}" ]
                m_nNodeCount = 3
                m_nStaticNodes = 1
                m_nStaticNodeFlags = {{staticFlags}}
                m_nDynamicNodeFlags = {{dynamicFlags}}
                m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -8.5f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -17f)}}
                ]
                m_Rods =
                [
                    {{SyntheticCloth.BandedRod(0, 1, 0.425f, 8.5f, 1f)}}
                    {{SyntheticCloth.BandedRod(1, 2, 0.425f, 8.5f, 1f)}}
                ]
                {{fields}}
            }
            """);

        /// <summary>
        /// Paired columns beside a ringed chain are an imported strip the chain reconstruction skips; without the ring
        /// the whole model is an imported cloth with no strip set.
        /// </summary>
        [Test]
        public async Task AStripBesideARingedChainIsAnImportedStripOfItsOwn()
        {
            var mixed = StripBesideChain(ringed: true);
            var alone = StripBesideChain(ringed: false);

            var joints = mixed.BuildBoneChains().SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();

            using (Assert.Multiple())
            {
                await Assert.That(mixed.IsImportedCloth).IsFalse();
                await Assert.That(mixed.ImportedStripNodes.Order()).IsEquivalentTo([0, 1, 2, 3]);
                await Assert.That(joints.Overlaps([0, 1, 2, 3])).IsFalse();
                await Assert.That(joints.Contains(5)).IsTrue();
                await Assert.That(alone.IsImportedCloth).IsTrue();
                await Assert.That(alone.ImportedStripNodes.Count).IsEqualTo(0);
            }
        }

        private static FeModel StripBesideChain(bool ringed) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "strip_r0c0", "strip_r0c1", "strip_r1c0", "strip_r1c1", "hat", "hat_end"{{(ringed ? ", \"$cchat_end_0\"" : string.Empty)}} ]
                m_SkelParents = [ -1, 0, 0, 2, -1, 4{{(ringed ? ", 5" : string.Empty)}} ]
                m_nNodeCount = {{(ringed ? 7 : 6)}}
                m_nStaticNodes = 3
                m_nStaticNodeFlags = 3840
                m_nDynamicNodeFlags = 12048
                m_NodeInvMasses = [ 0.0, 0.0, 0.0, 1.0, 1.0, 1.0{{(ringed ? ", 1.0" : string.Empty)}} ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, -20f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -8f)}}
                    {{SyntheticCloth.Pose(0f, -20f, -8f)}}
                    {{SyntheticCloth.Pose(40f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(40f, 0f, -8f)}}
                    {{(ringed ? SyntheticCloth.Pose(40f, 2f, -8f) : string.Empty)}}
                ]
                m_CtrlOsOffsets = [ { nCtrlParent = 0 nCtrlChild = 1 }, { nCtrlParent = 2 nCtrlChild = 3 } ]
                m_CtrlOffsets = [ {{(ringed ? "{ vOffset = [ 0.0, 2.0, 0.0 ] nCtrlParent = 5 nCtrlChild = 6 }" : string.Empty)}} ]
                m_Rods =
                [
                    {{SyntheticCloth.BandedRod(0, 2, 0.4f, 8f, 1f)}}
                    {{SyntheticCloth.BandedRod(1, 3, 0.4f, 8f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 3, 1f, 20f, 1f)}}
                    {{SyntheticCloth.RigidRod(4, 5, 8f, 1f)}}
                ]
            }
            """);

        /// <summary>
        /// A hinge ring perpendicular to its child ring tilts the recovered hinge vector along the quad's diagonals,
        /// the other way for the swapped corner order; an untied child ring leaves it as compiled.
        /// </summary>
        [Test]
        public async Task ATiedHingeFanQuadTiltsTheHingeVectorAlongItsDiagonals()
        {
            var forward = HingeFan("[ 1, 0, 3, 4 ]", 20f);
            var swapped = HingeFan("[ 1, 0, 4, 3 ]", 20f);
            var untied = HingeFan("[ 1, 0, 3, 4 ]", 22f);

            using (Assert.Multiple())
            {
                await Assert.That(forward.RigidHingeJoints[2].Z).IsGreaterThan(5e-5f);
                await Assert.That(swapped.RigidHingeJoints[2].Z).IsLessThan(-5e-5f);
                await Assert.That(untied.RigidHingeJoints[2]).IsEqualTo(new Vector3(0f, 10f, -1e-6f));
            }
        }

        private static FeModel HingeFan(string quad, float upperChildOffset) => SyntheticCloth.Model(
            ["$cchat_0", "$cchat_1", "hat", "$cchat_end_0", "$cchat_end_1", "hat_end"], staticNodes: 3,
                parents: [2, 2, -1, 5, 5, 2],
            poses: [new(0f, -10f, 0f), new(0f, 10f, 0f), new(0f, 0f, 0f), new(8f, 0f, -20f), new(8f, 0f, upperChildOffset),
                new(8f, 0f, 0f)],
            body: $$"""
                m_CtrlOffsets =
                [
                    { vOffset = [ 0.0, -10.0, 0.000001 ] nCtrlParent = 2 nCtrlChild = 0 },
                    { vOffset = [ 0.0, 10.0, -0.000001 ] nCtrlParent = 2 nCtrlChild = 1 },
                    { vOffset = [ 0.0, 0.0, -20.0 ] nCtrlParent = 5 nCtrlChild = 3 },
                    { vOffset = [ 0.0, 0.0, {{SyntheticCloth.Num(upperChildOffset)}} ] nCtrlParent = 5 nCtrlChild = 4 },
                ]
                m_Quads = [ { nNode = {{quad}} } ]
                m_Rods = [ {{SyntheticCloth.RigidRod(3, 4, 40f, 1f)}} ]
                """);

        /// <summary>
        /// A position-driven joint in a one-sided ring chain is not restated by its parent rod; one inside a two-sided
        /// ring is.
        /// </summary>
        [Test]
        public async Task AParentRodBesideAOneSidedRingIsNotARestatement()
        {
            var oneSided = SyntheticCloth.Load("cloth_chain_extrude_one_side.kv3");

            var twoSided = SyntheticCloth.Load("cloth_chain_extrude_two_sides.kv3");

            static string[] Restated(FeModel feModel)
                => [.. feModel.BuildBoneChains().SelectMany(static chain => chain.Joints).Where(static joint => joint.Restated).Select(static joint => joint.Name)];

            using (Assert.Multiple())
            {
                await Assert.That(Restated(oneSided)).IsEmpty();
                await Assert.That(Restated(twoSided)).IsNotEmpty();
            }
        }

        /// <summary>
        /// Goal-locked ringless static roots under one bone are one chain under that bone with a
        /// <c>child_sibling_spring</c>; unlocked roots or ringed chains are not merged.
        /// </summary>
        [Test]
        public async Task LockedRinglessRootsUnderOneBoneAreOneSprungChain()
        {
            var sprung = SiblingHubs(locked: true, rings: false);
            var unlocked = SiblingHubs(locked: false, rings: false);
            var ringed = SiblingHubs(locked: true, rings: true);

            var chains = sprung.BuildBoneChains();
            var merged = chains.Find(static chain => chain.RootBone == "hub");

            using (Assert.Multiple())
            {
                await Assert.That(chains.Count).IsEqualTo(1);
                await Assert.That(merged).IsNotNull();
                await Assert.That(merged!.Joints.Count).IsEqualTo(5);
                await Assert.That(merged.Joints.Find(static joint => joint.Name == "hub")!.ChildSiblingSpring)
                    .IsGreaterThan(0f);

                foreach (var name in (string[])["r1", "r2"])
                {
                    await Assert.That(merged.Joints.Find(joint => joint.Name == name)!.ParentName).IsEqualTo("hub");
                }

                await Assert.That(unlocked.BuildBoneChains().Exists(static chain => chain.RootBone == "hub"))
                    .IsFalse();
                await Assert.That(ringed.BuildBoneChains().Exists(static chain => chain.RootBone == "hub"))
                    .IsFalse();
            }
        }

        /// <summary>
        /// Two static roots under one bone, each carrying a simulated child, with no rod between the roots.
        /// <paramref name="locked"/> puts the roots in <c>m_LockToGoal</c> and
        /// <paramref name="rings"/> gives each root an extruded proxy ring.
        /// </summary>
        private static FeModel SiblingHubs(bool locked, bool rings)
        {
            var names = rings
                ? "\"hub\", \"r1\", \"r2\", \"c1\", \"c2\", \"$ccr1_0\", \"$ccr2_0\""
                : "\"hub\", \"r1\", \"r2\", \"c1\", \"c2\"";
            var model = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{names}} ]
                    m_SkelParents = [ -1, -1, -1, 1, 2{{(rings ? ", 1, 2" : string.Empty)}} ]
                    m_nNodeCount = {{(rings ? 7 : 5)}}
                    m_nStaticNodes = 3
                    m_NodeInvMasses = [ 0.0, 0.0, 0.0, 1.0, 1.0{{(rings ? ", 1.0, 1.0" : string.Empty)}} ]
                    m_LockToGoal = [ {{(locked ? "1, 2" : string.Empty)}} ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(-5f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(5f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(-5f, 0f, -20f)}}
                        {{SyntheticCloth.Pose(5f, 0f, -20f)}}
                        {{(rings ? SyntheticCloth.Pose(-2f, 0f, -10f) + SyntheticCloth.Pose(8f, 0f, -10f) : string.Empty)}}
                    ]
                    {{(rings
                        ? """
                          m_CtrlOffsets =
                          [
                              { vOffset = [ 3.0, 0.0, 0.0 ] nCtrlParent = 1 nCtrlChild = 5 },
                              { vOffset = [ 3.0, 0.0, 0.0 ] nCtrlParent = 2 nCtrlChild = 6 },
                          ]
                          """
                        : string.Empty)}}
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(1, 3, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(2, 4, 10f, 1f)}}
                    ]
                }
                """);
            model.SkeletonBoneParents = new Dictionary<string, string?>
            {
                ["hub"] = null,
                ["r1"] = "hub",
                ["r2"] = "hub",
                ["c1"] = "r1",
                ["c2"] = "r2",
                ["$ccr1_0"] = "r1",
                ["$ccr2_0"] = "r2",
            };
            return model;
        }

        /// <summary>
        /// An ungraded twist-written hint reads as version 0 only on a joint with a chain child, not on a leaf.
        /// </summary>
        [Test]
        public async Task ALeafsUngradedHintStatesNoVersion()
        {
            var model = TwistedRope("nNodeX0 = 2 nNodeX1 = 1 nNodeY0 = 0 nNodeY1 = 0",
                "nNodeX0 = 3 nNodeX1 = 2 nNodeY0 = 0 nNodeY1 = 0");

            var interior = new FeModel.BoneChain { RootBone = "j1" };
            interior.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1 });
            interior.Joints.Add(new FeModel.BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f });
            interior.Joints.Add(new FeModel.BoneChainJoint { Node = 3, Name = "j3", ParentNode = 2, InvMass = 1f });

            var leaf = new FeModel.BoneChain { RootBone = "j1" };
            leaf.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1 });
            leaf.Joints.Add(new FeModel.BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f });

            using (Assert.Multiple())
            {
                await Assert.That(model.ChainHintsAreTwistWritten(interior)).IsTrue();
                await Assert.That(model.ChainHintsAreTwistWritten(leaf)).IsFalse();
            }
        }

        /// <summary>
        /// A hinge's fan face is chain geometry with or without limits or an anchor, while only a plain hinge stays in
        /// the rigid hinge set; a face on a sheet vertex is no fan.
        /// </summary>
        [Test]
        public async Task AHingesFanIsChainGeometryWhetherOrNotItIsLimited()
        {
            int[] fan = [1, 0, 3, 4];
            int[] overSheet = [1, 0, 3, 6];

            var plain = HingeFanGate(limits: false, anchor: false);
            var limited = HingeFanGate(limits: true, anchor: false);
            var anchored = HingeFanGate(limits: false, anchor: true);
            var both = HingeFanGate(limits: true, anchor: true);

            using (Assert.Multiple())
            {
                await Assert.That(plain.IsHingeFanFace(fan)).IsTrue();
                await Assert.That(plain.RigidHingeJoints.ContainsKey(2)).IsTrue();
                await Assert.That(plain.IsHingeFanFace(overSheet)).IsFalse();

                await Assert.That(limited.RigidHingeJoints.ContainsKey(2)).IsFalse();
                await Assert.That(anchored.RigidHingeJoints.ContainsKey(2)).IsFalse();
                await Assert.That(both.RigidHingeJoints.ContainsKey(2)).IsFalse();

                await Assert.That(limited.IsHingeFanFace(fan)).IsTrue();
                await Assert.That(anchored.IsHingeFanFace(fan)).IsTrue();
                await Assert.That(both.IsHingeFanFace(fan)).IsTrue();
            }
        }

        /// <summary>
        /// The hinge fan of "hat" over "hat_end" with switchable limits and anchor; node 6 is a sheet vertex the fan
        /// never names.
        /// </summary>
        private static FeModel HingeFanGate(bool limits, bool anchor) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "$cchat_0", "$cchat_1", "hat", "$cchat_end_0", "$cchat_end_1", "hat_end",
                               "$cloth_m0p0"{{(anchor ? ", \"$ha_hat\"" : string.Empty)}} ]
                m_SkelParents = [ 2, 2, -1, 5, 5, 2, -1{{(anchor ? ", -1" : string.Empty)}} ]
                m_nNodeCount = {{(anchor ? 8 : 7)}}
                m_nStaticNodes = 3
                m_NodeInvMasses = [ 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, 1.0{{(anchor ? ", 0.0" : string.Empty)}} ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, -10f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 10f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(8f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(8f, 0f, 20f)}}
                    {{SyntheticCloth.Pose(8f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(16f, 0f, 0f)}}
                    {{(anchor ? SyntheticCloth.Pose(0f, 0f, 0f) : string.Empty)}}
                ]
                m_CtrlOffsets =
                [
                    { vOffset = [ 0.0, -10.0, 0.000001 ] nCtrlParent = 2 nCtrlChild = 0 },
                    { vOffset = [ 0.0, 10.0, -0.000001 ] nCtrlParent = 2 nCtrlChild = 1 },
                    { vOffset = [ 0.0, 0.0, -20.0 ] nCtrlParent = 5 nCtrlChild = 3 },
                    { vOffset = [ 0.0, 0.0, 20.0 ] nCtrlParent = 5 nCtrlChild = 4 },
                ]
                m_Quads = [ { nNode = [ 1, 0, 3, 4 ] } ]
                m_Rods = [ {{SyntheticCloth.RigidRod(3, 4, 40f, 1f)}} ]
                {{(limits ? "m_HingeLimits = [ { nNode = [ 0, 1, 2, 5, 2, 5 ] flAngleCenter = 0.0 flAngleExtents = 0.785398 } ]" : string.Empty)}}
            }
            """);

        /// <summary>
        /// A hinge over a ringless child fanning over a triangle is a rigid hinge link, as a quad is; a triangle over
        /// bones only, or no surface element, is not.
        /// </summary>
        [Test]
        public async Task AHingeOverARinglessChildFansOutOverATriangleAndIsStillARigidHingeLink()
        {
            var fan = HingeTriGate(tris: "[ { nNode = [ 2, 1, 4 ] } ]", quads: string.Empty);
            var authoredOverBones = HingeTriGate(tris: "[ { nNode = [ 3, 4, 5 ] } ]", quads: string.Empty);
            var noSurface = HingeTriGate(tris: string.Empty, quads: string.Empty);
            var quad = HingeTriGate(tris: string.Empty, quads: "[ { nNode = [ 2, 1, 4, 5 ] } ]");

            using (Assert.Multiple())
            {
                var chain = fan.BuildBoneChains()[0];
                await Assert.That(string.Join(",", chain.Joints.Select(static joint => joint.Name)))
                    .IsEqualTo("hat,hat_end,hat_tip");
                await Assert.That(fan.IsHingedJoint(3)).IsTrue();
                await Assert.That(fan.ProxyCountOf(4)).IsEqualTo(0);
                await Assert.That(fan.HasChainRods(chain)).IsTrue();

                await Assert.That(fan.HasRigidHingeLink(chain)).IsTrue();

                await Assert.That(authoredOverBones.HasRigidHingeLink(
                    authoredOverBones.BuildBoneChains()[0])).IsFalse();

                await Assert.That(noSurface.HasRigidHingeLink(noSurface.BuildBoneChains()[0])).IsFalse();

                await Assert.That(quad.HasRigidHingeLink(quad.BuildBoneChains()[0])).IsTrue();
            }
        }

        /// <summary>
        /// A limited hinge on the chain root "hat" over a ringless "hat_end", so its fan is a triangle.
        /// </summary>
        private static FeModel HingeTriGate(string tris, string quads) => SyntheticCloth.Model(
            ["$ha_hat", "$cchat_0", "$cchat_1", "hat", "hat_end", "hat_tip"], staticNodes: 4, parents: [3, 3, 3, -1, 3, 4],
            poses: [new(0f, 0f, 0f), new(0f, -10f, 0f), new(0f, 10f, 0f), new(0f, 0f, 0f), new(8f, 0f, 0f), new(16f, 0f, 0f)],
            body: $$"""
                m_CtrlOffsets =
                [
                    { vOffset = [ 0.0, -10.0, 0.000001 ] nCtrlParent = 3 nCtrlChild = 1 },
                    { vOffset = [ 0.0, 10.0, -0.000001 ] nCtrlParent = 3 nCtrlChild = 2 },
                ]
                m_Rods = [ {{SyntheticCloth.RigidRod(4, 5, 8f, 1f)}} ]
                {{(quads.Length > 0 ? "m_Quads = " + quads : string.Empty)}}
                {{(tris.Length > 0 ? "m_Tris = " + tris : string.Empty)}}
                m_HingeLimits = [ { nNode = [ 1, 2, 3, 4, 3, 4 ] flAngleCenter = 0.0 flAngleExtents = 0.785398 } ]
                """);

        /// <summary>
        /// A twist between two bones links them into a chain with or without <c>m_SkelParents</c>; without twists the
        /// run stays loose.
        /// </summary>
        [Test]
        public async Task ATwistBetweenTwoBonesIsAChainLink()
        {
            static bool Chained(FeModel model) => model.BuildBoneChains().Exists(chain => chain.Joints.Count == 3);

            using (Assert.Multiple())
            {
                await Assert.That(Chained(TwistedRun(skelParents: false, twisted: false))).IsFalse();
                await Assert.That(Chained(TwistedRun(skelParents: true, twisted: false))).IsFalse();

                await Assert.That(Chained(TwistedRun(skelParents: false, twisted: true))).IsTrue();
                await Assert.That(Chained(TwistedRun(skelParents: true, twisted: true))).IsTrue();
            }
        }

        /// <summary>
        /// A static w0 over w1 and w2 with no rods; <paramref name="twisted"/> adds four twist entries.
        /// </summary>
        private static FeModel TwistedRun(bool skelParents, bool twisted) => SyntheticCloth.Model(
            ["w0", "w1", "w2"], staticNodes: 1, poses: [new(0f, 0f, 0f), new(-8.5f, 0f, 0f), new(-17f, 0f, 0f)], body: $$"""
                {{(skelParents ? "m_SkelParents = [ -1, 0, 1 ]" : string.Empty)}}
                m_Twists = [ {{(twisted ? "{ nNodeOrient = 0 nNodeEnd = 1 flTwistRelax = 0.0 flSwingRelax = 1.0 }, { nNodeOrient = 1 nNodeEnd = 0 flTwistRelax = 0.618 flSwingRelax = 0.0 }, { nNodeOrient = 1 nNodeEnd = 2 flTwistRelax = 0.382 flSwingRelax = 0.5 }, { nNodeOrient = 2 nNodeEnd = 1 flTwistRelax = 0.618 flSwingRelax = 1.0 }" : string.Empty)}} ]
                """);

        /// <summary>
        /// <c>TwistRecords</c> and <c>NodeBaseRecords</c> keep every record in array order with its swing relaxation,
        /// while <c>NodeBases</c> keeps each node's last record and <c>TwistNodes</c> every named node.
        /// </summary>
        [Test]
        public async Task EveryTwistAndNodeBaseRecordIsKept()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                body: """
                    m_Twists =
                    [
                        { nNodeOrient = 1 nNodeEnd = 0 flTwistRelax = 0.618 flSwingRelax = 0.0 },
                        { nNodeOrient = 1 nNodeEnd = 2 flTwistRelax = 0.382 flSwingRelax = 1.0 },
                        { nNodeOrient = 1 nNodeEnd = 0 flTwistRelax = 0.2163 flSwingRelax = 0.5 },
                    ]
                    m_NodeBases =
                    [
                        { nNode = 1 nNodeX0 = 1 nNodeX1 = 2 nNodeY0 = 0 nNodeY1 = 2 },
                        { nNode = 1 nNodeX0 = 1 nNodeX1 = 0 nNodeY0 = 2 nNodeY1 = 0 },
                        { nNode = 2 nNodeX0 = 2 nNodeX1 = 1 nNodeY0 = 0 nNodeY1 = 1 },
                    ]
                    """);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.NodeBases.Count).IsEqualTo(2);
                await Assert.That(feModel.NodeBases[1]).IsEqualTo(new FeModel.NodeBasis(1, 0, 2, 0));
                await Assert.That(feModel.TwistNodes.Order()).IsEquivalentTo([0, 1, 2], CollectionOrdering.Matching);

                await Assert.That(feModel.TwistRecords).IsEquivalentTo(
                    [
                        new FeModel.TwistRecord(1, 0, 0.618f, 0f),
                        new FeModel.TwistRecord(1, 2, 0.382f, 1f),
                        new FeModel.TwistRecord(1, 0, 0.2163f, 0.5f),
                    ], CollectionOrdering.Matching);
                await Assert.That(feModel.NodeBaseRecords).IsEquivalentTo(
                    [
                        (1, new FeModel.NodeBasis(1, 2, 0, 2)),
                        (1, new FeModel.NodeBasis(1, 0, 2, 0)),
                        (2, new FeModel.NodeBasis(2, 1, 0, 1)),
                    ], CollectionOrdering.Matching);
            }
        }

        /// <summary>
        /// A rope run past a joint owning an end-effector centre does not link into a chain; without the centre it
        /// does.
        /// </summary>
        [Test]
        public async Task ARopeRunPastAnEndEffectorCentreIsNoChainLink()
        {
            static bool Joined(bool centre) => SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "w0", "w1", "w2", "k1", {{(centre ? "\"$ccw2_Ctr\"" : "\"k2\"")}} ]
                    m_SkelParents = [ -1, 0, 1, 2, {{(centre ? "2" : "-1")}} ]
                    m_nNodeCount = 5
                    m_nStaticNodes = 1
                    m_nFirstPositionDrivenNode = 5
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                        {{SyntheticCloth.Pose(0f, 5f, -20f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    ]
                    m_nRopeCount = 1
                    m_Ropes = [ 3, 2, 3 ]
                }
                """).BuildBoneChains().Exists(chain => chain.Joints.Exists(static j => j.Name == "w2")
                    && chain.Joints.Exists(static j => j.Name == "k1"));

            using (Assert.Multiple())
            {
                await Assert.That(Joined(centre: false)).IsTrue();

                await Assert.That(Joined(centre: true)).IsFalse();
            }
        }

        /// <summary>
        /// At version 2 a static joint the preset cannot take holds its parent lock without <c>lock_translation</c>; a
        /// preset joint, no chain, or a ringless joint over two ringless children read the key.
        /// </summary>
        [Test]
        public async Task AVersion2JointThePresetCannotTakeHoldsItsParentLockWithoutTheKey()
        {
            static FeModel.BoneChain Chain(FeModel feModel, bool withKid, int sides)
            {
                var chain = new FeModel.BoneChain { RootBone = "tip" };
                var tip = Array.IndexOf(feModel.CtrlNames, "tip");
                chain.Joints.Add(new FeModel.BoneChainJoint { Node = tip, Name = "tip", ParentNode = -1, InvMass = 0f, ExtrudeSides = sides });
                if (withKid)
                {
                    chain.Joints.Add(new FeModel.BoneChainJoint
                    {
                        Node = Array.IndexOf(feModel.CtrlNames, "kid"),
                        Name = "kid",
                        ParentNode = tip,
                        ParentName = "tip",
                        InvMass = 1f,
                        ExtrudeSides = sides,
                    });
                }

                return chain;
            }

            static FeModel.BoneChain TwoKids(FeModel feModel)
            {
                var chain = Chain(feModel, withKid: true, sides: 0);
                var tip = Array.IndexOf(feModel.CtrlNames, "tip");
                chain.Joints.Add(new FeModel.BoneChainJoint
                {
                    Node = Array.IndexOf(feModel.CtrlNames, "root"),
                    Name = "root",
                    ParentNode = tip,
                    ParentName = "tip",
                    InvMass = 1f,
                });
                return chain;
            }

            var ringed = ParentLockedTip(rings: true);
            var ringless = ParentLockedTip(rings: false);
            var ringedTip = Array.IndexOf(ringed.CtrlNames, "tip");
            var ringlessTip = Array.IndexOf(ringless.CtrlNames, "tip");

            using (Assert.Multiple())
            {
                await Assert.That(ringed.LocksTranslation(ringedTip, chainVersion: 2, chain: Chain(ringed, withKid: true, sides: 1))).IsTrue();
                await Assert.That(ringless.LocksTranslation(ringlessTip, chainVersion: 2)).IsTrue();
                await Assert.That(ringless.LocksTranslation(ringlessTip, chainVersion: 2, chain: TwoKids(ringless))).IsTrue();

                await Assert.That(ringless.LocksTranslation(ringlessTip, chainVersion: 2, chain: Chain(ringless, withKid: false, sides: 0))).IsFalse();
                await Assert.That(ringless.LocksTranslation(ringlessTip, chainVersion: 2, chain: Chain(ringless, withKid: true, sides: 0))).IsFalse();
            }
        }
    }
}
