using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Reconstructing bone chains from a compiled FeModel: joints, rings, links, locks and the chain version evidence.</summary>
    public class FeModelReconstructionChainTest : FeModelTestModels
    {
        /// <summary>
        /// Chains are ordered by the lowest simulated node their joints occupy, not by their root.
        /// </summary>
        [Test]
        public async Task ChainsAreOrderedByTheirLowestSimulatedNode()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["rootA", "rootB", "jB", "jA"],
                StaticNodes = 2,
                Parents = [-1, -1, 1, 0],
                Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 0f, -5f), new(0f, 0f, -5f)],
                Rods = [RigidRod(0, 3, 5f), RigidRod(1, 2, 5f)],
            }.Reconstruct();

            var chains = cloth.BuildBoneChains();

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
            var split = RingDeclarationModel("$ccroot_0", "$ccroot_1", "$ccroot_0", "$ccroot_1").BuildBoneChains();
            var single = RingDeclarationModel("$ccroot_0", "$ccroot_1", "$ccroot_2", "$ccroot_3").BuildBoneChains();

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
            var split = RingDeclarationModel("$ccroot_0", "$ccroot_1", "$ccroot_0", "$ccroot_1").BuildBoneChains();
            var single = RingDeclarationModel("$ccroot_0", "$ccroot_1", "$ccroot_2", "$ccroot_3").BuildBoneChains();

            using (Assert.Multiple())
            {
                await Assert.That(split[0].Joints[0].RingNodes).IsEquivalentTo([1, 2]);
                await Assert.That(split[1].Joints[0].RingNodes).IsEquivalentTo([3, 4]);
                await Assert.That(single[0].Joints[0].RingNodes).IsEquivalentTo([1, 2, 3, 4]);
            }
        }

        private static ClothReconstruction RingDeclarationModel(params string[] ringNames) => new FeModelBuilder
        {
            Names = ["root", .. ringNames],
            StaticNodes = 1,
            Parents = [-1, 0, 0, 0, 0],
            Positions = [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, -2f, 0f), new(0f, 0f, 2f), new(0f, 0f, -2f)],
        }.Reconstruct();

        /// <summary>
        /// A chain joint's node base tells the version-1 bulk grade (reaching the parent's ring) from the version-2
        /// preset grade over the joint's own extrusion and its child's.
        /// </summary>
        [Test]
        public async Task AChainJointBasisNamingTheParentRingWasBulkGraded()
        {
            var bulk = OneWideRope(NodeBase(3, 5, 2, 6, 1)).Reconstruct();
            var preset = OneWideRope(NodeBase(3, 5, 4, 6, 3)).Reconstruct();

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
            var leaf = OneWideRope(NodeBase(5, 5, 2, 6, 3)).Reconstruct();

            await Assert.That(leaf.ChainBasesAreBulkGraded(leaf.BuildBoneChains()[0])).IsNull();
        }

        /// <summary>
        /// A twisted joint's hint left at {joint, twist end, 0, 0} reads as version 0; a graded hint does not.
        /// </summary>
        [Test]
        public async Task AnUngradedTwistHintSaysTheChainCompiledAtVersionZero()
        {
            var ungraded = TwistedRope(new FeNodeWindBase(2, 1, 0, 0), new FeNodeWindBase(3, 2, 0, 0));
            var graded = TwistedRope(new FeNodeWindBase(3, 1, 3, 3), new FeNodeWindBase(3, 2, 3, 3));

            using (Assert.Multiple())
            {
                await Assert.That(ungraded.ChainHintsAreTwistWritten(TwistedRopeChain())).IsTrue();
                await Assert.That(graded.ChainHintsAreTwistWritten(TwistedRopeChain())).IsFalse();
            }
        }

        private static ClothReconstruction TwistedRope(FeNodeWindBase hint2, FeNodeWindBase hint3) => new FeModelBuilder
        {
            Names = ["root", "j1", "j2", "j3"],
            StaticNodes = 2,
            Parents = [-1, 0, 1, 2],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f)],
            Twists = [Twist(2, 1, 0.618f, 0f), Twist(2, 3, 0.382f, 0f), Twist(3, 2, 0.618f, 0f)],
            DynNodeWindBases = [hint2, hint3],
        }.Reconstruct();

        /// <summary>
        /// A stretchless chain's top link is read off the bend rod spanning its second joint, rooting the chain at that
        /// parent.
        /// </summary>
        [Test]
        public async Task ASpanningBendRodRootsAStretchlessChainAtTheParentItSpansTo()
        {
            var spanned = StretchlessChain(RigidRod(0, 2, 20f), RigidRod(1, 3, 20f)).Reconstruct().BuildBoneChains();
            var unspanned = StretchlessChain(RigidRod(1, 3, 20f)).Reconstruct().BuildBoneChains();

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
            var versionZero = ThinTipChain(tipRecord: false).Reconstruct();
            var versionOne = ThinTipChain(tipRecord: true).Reconstruct();
            var stagedLeaf = WithWideLeaf(ThinTipChain(tipRecord: false), leafRecord: true).Reconstruct();
            var unstagedLeaf = WithWideLeaf(ThinTipChain(tipRecord: false), leafRecord: false).Reconstruct();
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

        /// <summary>
        /// <paramref name="chain"/> with a two-ring side leaf under its third joint, staged by a reverse offset when
        /// <paramref name="leafRecord"/>.
        /// </summary>
        private static FeModelBuilder WithWideLeaf(FeModelBuilder chain, bool leafRecord) => chain with
        {
            Names = [.. chain.Names, "coattail_side_L", "$cccoattail_side_L_0", "$cccoattail_side_L_1"],
            Parents = [.. chain.Parents!, 4, 8, 8],
            InvMasses = [.. chain.InvMasses!, 1f, 1f, 1f],
            Poses = [.. chain.Poses!, Pose(-14.8f, 13f, 45f), Pose(-14.8f, 15f, 45f), Pose(-14.8f, 11f, 45f)],
            Rods = [.. chain.Rods!, RigidRod(4, 8, 9f), RigidRod(8, 9, 2f), RigidRod(8, 10, 2f), RigidRod(9, 10, 4f)],
            ReverseOffsets = leafRecord
                ? [.. chain.ReverseOffsets![..2], ReverseOffset(8, 9, 0f, 2f, 0f), .. chain.ReverseOffsets[2..]]
                : chain.ReverseOffsets,
        };

        /// <summary>
        /// <see cref="FeModelTestModels.ExplicitMassChain"/> locked to its goal at the root, with reverse offsets on its
        /// first two links and, when <paramref name="tipRecord"/>, on its tip ring.
        /// </summary>
        private static FeModelBuilder ThinTipChain(bool tipRecord) => ExplicitMassChain with
        {
            LockToGoal = [0],
            ReverseOffsets =
            [
                ReverseOffset(2, 4, 8.49993f, 0.00006f, 0.000001f),
                ReverseOffset(4, 6, 8.500038f, -0.000026f, 0.000008f),
                .. tipRecord ? [ReverseOffset(6, 7, -0.000001f, 2.000001f, -0.000001f)] : Array.Empty<FeNodeReverseOffset>(),
            ],
        };

        /// <summary>
        /// A static joint's parent lock reads as <c>lock_translation</c> only where the fit pass could not have written
        /// it: not on a free root's fit-owning joint below version 2, but under a covering root fit or a
        /// rotation-locked root.
        /// </summary>
        [Test]
        public async Task AStaticJointsFitGroupParentLockIsNotLockTranslation()
        {
            var free = TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: true).Reconstruct();
            var fitted = TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: true,
                rootFitsFirstJoint: true).Reconstruct();
            var locked = TwoVersionTree(rootRingFirst: true, thinTipGrouped: false, wideLeafGrouped: true,
                rootRotationLocked: true).Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(free.LocksTranslation(Array.IndexOf(free.Fe.CtrlName, "a0"), chainVersion: 1)).IsFalse();
                await Assert.That(free.LocksTranslation(Array.IndexOf(free.Fe.CtrlName, "a0"), chainVersion: 2)).IsTrue();
                await Assert.That(free.LocksTranslation(Array.IndexOf(free.Fe.CtrlName, "b0"), chainVersion: 1)).IsTrue();
                await Assert.That(fitted.LocksTranslation(Array.IndexOf(fitted.Fe.CtrlName, "a0"), chainVersion: 1)).IsTrue();
                await Assert.That(locked.LocksTranslation(Array.IndexOf(locked.Fe.CtrlName, "a0"), chainVersion: 1)).IsTrue();
                await Assert.That(locked.LocksTranslation(Array.IndexOf(locked.Fe.CtrlName, "b0"), chainVersion: 1)).IsTrue();
            }
        }

        /// <summary>
        /// A two-ring leaf without a reverse offset reads as unstaged, and with one as staged.
        /// </summary>
        [Test]
        public async Task ATwoRingLeafHoldsTwoFitTableEntries()
        {
            var ungrouped = TwoVersionTree(rootRingFirst: true, thinTipGrouped: true, wideLeafGrouped: false).Reconstruct();
            var grouped = TwoVersionTree(rootRingFirst: true, thinTipGrouped: true, wideLeafGrouped: true).Reconstruct();

            static BoneChainJoint[] WideJoints(ClothReconstruction cloth) =>
            [
                .. cloth.BuildBoneChains().SelectMany(static chain => chain.Joints)
                    .Where(static joint => joint.Name is "b0" or "b1"),
            ];

            using (Assert.Multiple())
            {
                await Assert.That(ungrouped.ThinJointStagingOf(WideJoints(ungrouped))).IsEqualTo(ClothReconstruction.ThinJointStaging.Unstaged);
                await Assert.That(grouped.ThinJointStagingOf(WideJoints(grouped))).IsEqualTo(ClothReconstruction.ThinJointStaging.Staged);
            }
        }

        /// <summary>
        /// Below version 2 a parent fit holding a fit-owning static joint and its direct children reads
        /// <c>lock_translation</c>; one holding only the joint does not, except at version 2.
        /// </summary>
        [Test]
        public async Task AParentFitHoldingAFitJointsOneWideEntryIsLockTranslation()
        {
            var model = OneWideEntryTree();

            using (Assert.Multiple())
            {
                await Assert.That(model.LocksTranslation(Array.IndexOf(model.Fe.CtrlName, "a0"), chainVersion: 1)).IsTrue();
                await Assert.That(model.LocksTranslation(Array.IndexOf(model.Fe.CtrlName, "b0"), chainVersion: 1)).IsFalse();
                await Assert.That(model.LocksTranslation(Array.IndexOf(model.Fe.CtrlName, "b0"), chainVersion: 2)).IsTrue();
            }
        }

        /// <summary>
        /// A static root over static arms a0 and b0, each with two free joints below, held to the root by a parent lock;
        /// the root's fit holds a0, a1 and b0, a0's its own arm and b0's its own.
        /// </summary>
        private static ClothReconstruction OneWideEntryTree()
        {
            List<(string Name, string? Parent, Vector3 Position)> nodes =
            [
                ("root", null, Vector3.Zero), ("a0", "root", new Vector3(10f, 0f, 0f)), ("b0", "root", new Vector3(-10f, 0f, 0f)),
                ("a1", "a0", new Vector3(10f, 0f, -10f)), ("b1", "b0", new Vector3(-10f, 0f, -10f)),
                ("a2", "a1", new Vector3(10f, 0f, -20f)), ("b2", "b1", new Vector3(-10f, 0f, -20f)),
            ];
            var names = nodes.ConvertAll(static node => node.Name);
            int At(string name) => names.IndexOf(name);

            var fits = new List<FeFitMatrix>();
            var weights = new List<FeFitWeight>();
            foreach (var group in "root:a0 a1 b0|a0:a0 a1 a2|b0:b0 b1 b2".Split('|'))
            {
                var (owner, members) = (group.Split(':')[0], group.Split(':')[1]);
                weights.AddRange(members.Split(' ').Select(member => FitWeight(At(member))));
                fits.Add(FitMatrix(At(owner), weights.Count, weights.Count));
            }

            return new FeModelBuilder
            {
                Names = [.. names],
                StaticNodes = 3,
                RotLockStaticNodes = 0,
                Parents = [.. nodes.Select(node => node.Parent is null ? -1 : At(node.Parent))],
                Positions = [.. nodes.Select(static node => node.Position)],
                LockToGoal = [0],
                LockToParent = [Offset(0, At("a0"), 10f, 0f, 0f), Offset(0, At("b0"), -10f, 0f, 0f)],
                FitMatrices = [.. fits],
                FitWeights = [.. weights],
            }.Reconstruct();
        }

        /// <summary>
        /// A one-wide joint's base naming the parent ring reads as bulk graded even when the scan does not predict it
        /// and with no skeleton parents; an entry inside the preset's candidates reads neither.
        /// </summary>
        [Test]
        public async Task AJointBasisNamingTheParentRingIsBulkGradedWithoutItsScan()
        {
            var parentRingEntry = OneWideRope(NodeBase(3, 2, 1, 5, 6));
            var unpredicted = parentRingEntry.Reconstruct();
            var unparented = (parentRingEntry with { Parents = null }).Reconstruct();
            var inside = OneWideRope(NodeBase(3, 3, 6, 4, 5)).Reconstruct();

            var unparentedChain = new BoneChain { RootBone = "j1", ExtrudeSides = 1 };
            unparentedChain.Joints.Add(new BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1, InvMass = 1f, ExtrudeSides = 1, RingNodes = [2] });
            unparentedChain.Joints.Add(new BoneChainJoint { Node = 3, Name = "j2", ParentNode = 1, InvMass = 1f, ExtrudeSides = 1, RingNodes = [4] });
            unparentedChain.Joints.Add(new BoneChainJoint { Node = 5, Name = "j3", ParentNode = 3, InvMass = 1f, ExtrudeSides = 1, RingNodes = [6] });

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

            static BoneChain Chain(ClothReconstruction cloth, bool withKid, bool tipSimulated = false)
            {
                var chain = new BoneChain { RootBone = "tip" };
                var tip = Array.IndexOf(cloth.Fe.CtrlName, "tip");
                chain.Joints.Add(new BoneChainJoint { Node = tip, Name = "tip", ParentNode = -1, InvMass = tipSimulated ? 1f : 0f });
                if (withKid)
                {
                    chain.Joints.Add(new BoneChainJoint
                    {
                        Node = Array.IndexOf(cloth.Fe.CtrlName, "kid"),
                        Name = "kid",
                        ParentNode = tip,
                        ParentName = "tip",
                        InvMass = 1f,
                    });
                }

                return chain;
            }

            var ringedTip = Array.IndexOf(ringed.Fe.CtrlName, "tip");
            var ringlessTip = Array.IndexOf(ringless.Fe.CtrlName, "tip");

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

        private static ClothReconstruction ParentLockedTip(bool rings)
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

            return new FeModelBuilder
            {
                Names = [.. names],
                StaticNodes = At("kid"),
                RotLockStaticNodes = 0,
                Parents = [.. nodes.Select(node => node.Parent is null ? -1 : At(node.Parent))],
                Positions = [.. nodes.Select(static node => node.Position)],
                LockToGoal = [0],
                LockToParent = [Offset(0, At("tip"), 10f, 0f, 0f)],
                NodeBases = [NodeBase(At("tip"), At("tip"), At("kid"), 0, At("kid"))],
            }.Reconstruct();
        }

        /// <summary>
        /// A reverse offset's version reading without <c>m_SkelParents</c> uses the chain's own rings and matches the
        /// parented rope's reading.
        /// </summary>
        [Test]
        public async Task AReverseOffsetIsReadOverTheChainsOwnRingsWithoutSkeletonParents()
        {
            var parentedRope = OneWideRope(NodeBase(3, 2, 1, 5, 6)) with { ReverseOffsets = [ReverseOffset(3, 4, 0f, 0f, 0f)] };
            var parented = parentedRope.Reconstruct();
            var unparented = (parentedRope with { Parents = null }).Reconstruct();

            var unparentedChain = new BoneChain { RootBone = "j1", ExtrudeSides = 1 };
            unparentedChain.Joints.Add(new BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1, InvMass = 1f, ExtrudeSides = 1, RingNodes = [2] });
            unparentedChain.Joints.Add(new BoneChainJoint { Node = 3, Name = "j2", ParentNode = 1, InvMass = 1f, ExtrudeSides = 1, RingNodes = [4] });
            unparentedChain.Joints.Add(new BoneChainJoint { Node = 5, Name = "j3", ParentNode = 3, InvMass = 1f, ExtrudeSides = 1, RingNodes = [6] });

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
            static BoneChainJoint Joint(ClothReconstruction model, string name)
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
        private static ClothReconstruction OldEraTube(bool compiledParents, bool kFace)
        {
            var model = new FeModelBuilder
            {
                Names = ["j1", "$ccj1_0", "j2", "$ccj2_0", "j3", "$ccj3_0", "k", "$cck_0"],
                Positions = [new(0f, 0f, 0f), new(3f, 0f, 0f), new(0f, 0f, -10f), new(3f, 0f, -10f), new(0f, 0f, -20f),
                    new(3f, 0f, -20f), new(0f, 0f, -30f), new(3f, 0f, -30f)],
                Parents = compiledParents ? [-1, 0, 0, 2, 2, 4, 2, 6] : null,
                CtrlOffsets = [Offset(0, 1, 3f, 0f, 0f), Offset(2, 3, 3f, 0f, 0f), Offset(4, 5, 3f, 0f, 0f), Offset(6, 7, 3f, 0f, 0f)],
                Rods = [RigidRod(1, 3, 10f), RigidRod(3, 5, 10f), RigidRod(3, 7, 20f)],
                SourceElems = kFace ? [0, 0, 0, 3, 0, 1, 3, 2, 2, 3, 5, 4, 4, 5, 7, 6] : [0, 0, 0, 2, 0, 1, 3, 2, 2, 3, 5, 4],
            }.Reconstruct();
            return WithSkeleton(model,
                new Dictionary<string, string?> { ["j1"] = null, ["j2"] = "j1", ["j3"] = "j2", ["k"] = "j2" });
        }

        /// <summary>
        /// A rope with no rods between its joints reads as a chain with <c>stretch_spring</c> 0; without the rope the
        /// joints stay unlinked.
        /// </summary>
        [Test]
        public async Task ARopeWithNoRodsBetweenItsJointsIsAChainWithNoStretchSpring()
        {
            var roped = (RodlessTail with { RopeCount = 1, Ropes = [4, 0, 1, 2] }).Reconstruct();
            var unroped = RodlessTail.Reconstruct();

            var chain = roped.BuildBoneChains().Find(static chain => chain.Joints.Count == 3);

            using (Assert.Multiple())
            {
                await Assert.That(chain).IsNotNull();
                await Assert.That(chain!.Joints[1].StretchStiffness).IsEqualTo(0f);
                await Assert.That(chain.Joints[2].StretchStiffness).IsEqualTo(0f);
                await Assert.That(unroped.BuildBoneChains().Exists(static chain => chain.Joints.Count > 1)).IsFalse();
            }
        }

        /// <summary>A static tail bone with two free bones after it and no rod between them.</summary>
        private static FeModelBuilder RodlessTail => new()
        {
            Names = ["tail_0", "tail_1", "tail_2"],
            StaticNodes = 1,
            Parents = [-1, 0, 1],
            Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f)],
        };

        /// <summary>
        /// Raw-integrator flags with no goal bit, a follow link or a legacy stretch force mark an imported cloth;
        /// goal-integrator nodes, raw static nodes or a proxy sheet vertex do not.
        /// </summary>
        [Test]
        public async Task AnFxTableWithNoPairedColumnIsAnImportedCloth()
        {
            using (Assert.Multiple())
            {
                await Assert.That(FxTable(0xF00, 0x2F10).Reconstruct().IsImportedCloth).IsTrue();
                await Assert.That((FxTable(0x880, 0x2880) with { FollowNodes = [new FeFollowNode(0, 1, 0.1f)] }).Reconstruct().IsImportedCloth).IsTrue();
                await Assert.That((FxTable(0x880, 0x2880) with { LegacyStretchForce = [0f, 1f, 1f] }).Reconstruct().IsImportedCloth).IsTrue();
                await Assert.That(FxTable(0x880, 0x2880).Reconstruct().IsImportedCloth).IsFalse();
                await Assert.That(FxTable(0x200, 0x2080).Reconstruct().IsImportedCloth).IsFalse();
                await Assert.That(FxTable(0xF00, 0x2F10, "$cloth_m0p2").Reconstruct().IsImportedCloth).IsFalse();
            }
        }

        /// <summary>
        /// A static tail bone over two free nodes, the last named <paramref name="tip"/>, linked by banded rods, under the
        /// given static and dynamic mode words.
        /// </summary>
        private static FeModelBuilder FxTable(uint staticFlags, uint dynamicFlags, string tip = "tail_2") => new()
        {
            Names = ["tail_0", "tail_1", tip],
            StaticNodes = 1,
            StaticNodeFlags = staticFlags,
            DynamicNodeFlags = dynamicFlags,
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -8.5f), new(0f, 0f, -17f)],
            Rods = [Rod(0, 1, 0.425f, 8.5f), Rod(1, 2, 0.425f, 8.5f)],
        };

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

        private static ClothReconstruction StripBesideChain(bool ringed) => new FeModelBuilder
        {
            Names = ["strip_r0c0", "strip_r0c1", "strip_r1c0", "strip_r1c1", "hat", "hat_end", .. ringed ? ["$cchat_end_0"] : Array.Empty<string>()],
            StaticNodes = 3,
            StaticNodeFlags = 3840,
            DynamicNodeFlags = 12048,
            Parents = [-1, 0, 0, 2, -1, 4, .. ringed ? [5] : Array.Empty<int>()],
            Positions = [new(0f, 0f, 0f), new(0f, -20f, 0f), new(0f, 0f, -8f), new(0f, -20f, -8f), new(40f, 0f, 0f), new(40f, 0f, -8f),
                .. ringed ? [new Vector3(40f, 2f, -8f)] : Array.Empty<Vector3>()],
            CtrlOsOffsets = [new FeCtrlOsOffset(0, 1), new FeCtrlOsOffset(2, 3)],
            CtrlOffsets = ringed ? [Offset(5, 6, 0f, 2f, 0f)] : [],
            Rods = [Rod(0, 2, 0.4f, 8f), Rod(1, 3, 0.4f, 8f), Rod(2, 3, 1f, 20f), RigidRod(4, 5, 8f)],
        }.Reconstruct();

        /// <summary>
        /// A hinge ring perpendicular to its child ring tilts the recovered hinge vector along the quad's diagonals,
        /// the other way for the swapped corner order; an untied child ring leaves it as compiled.
        /// </summary>
        [Test]
        public async Task ATiedHingeFanQuadTiltsTheHingeVectorAlongItsDiagonals()
        {
            var forward = (HatHinge(20f) with { Quads = [Quad(1, 0, 3, 4)] }).Reconstruct();
            var swapped = (HatHinge(20f) with { Quads = [Quad(1, 0, 4, 3)] }).Reconstruct();
            var untied = (HatHinge(22f) with { Quads = [Quad(1, 0, 3, 4)] }).Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(forward.RigidHingeJoints[2].Z).IsGreaterThan(5e-5f);
                await Assert.That(swapped.RigidHingeJoints[2].Z).IsLessThan(-5e-5f);
                await Assert.That(untied.RigidHingeJoints[2]).IsEqualTo(new Vector3(0f, 10f, -1e-6f));
            }
        }

        /// <summary>
        /// A static "hat" with a two-node ring over a free "hat_end" whose two-node ring reaches
        /// <paramref name="upperChildOffset"/> up, its ring held by one rod and fanned over by the hat's ring.
        /// </summary>
        private static FeModelBuilder HatHinge(float upperChildOffset) => new()
        {
            Names = ["$cchat_0", "$cchat_1", "hat", "$cchat_end_0", "$cchat_end_1", "hat_end"],
            StaticNodes = 3,
            Parents = [2, 2, -1, 5, 5, 2],
            Positions = [new(0f, -10f, 0f), new(0f, 10f, 0f), new(0f, 0f, 0f), new(8f, 0f, -20f), new(8f, 0f, upperChildOffset), new(8f, 0f, 0f)],
            CtrlOffsets =
            [
                Offset(2, 0, 0f, -10f, 0.000001f),
                Offset(2, 1, 0f, 10f, -0.000001f),
                Offset(5, 3, 0f, 0f, -20f),
                Offset(5, 4, 0f, 0f, upperChildOffset),
            ],
            Quads = [Quad(1, 0, 3, 4)],
            Rods = [RigidRod(3, 4, 40f)],
        };

        /// <summary>
        /// A position-driven joint in a one-sided ring chain is not restated by its parent rod; one inside a two-sided
        /// ring is.
        /// </summary>
        [Test]
        public async Task AParentRodBesideAOneSidedRingIsNotARestatement()
        {
            var oneSided = OneSidedRingChain.Reconstruct();

            var twoSided = TwoSidedRingChain.Reconstruct();

            static string[] Restated(ClothReconstruction cloth)
                => [.. cloth.BuildBoneChains().SelectMany(static chain => chain.Joints).Where(static joint => joint.Restated).Select(static joint => joint.Name)];

            using (Assert.Multiple())
            {
                await Assert.That(Restated(oneSided)).IsEmpty();
                await Assert.That(Restated(twoSided)).IsNotEmpty();
            }
        }

        /// <summary>
        /// <see cref="FeModelTestModels.Coattail"/> with every joint after the static first position-driven, its tip ring
        /// staged by a reverse offset.
        /// </summary>
        private static FeModelBuilder OneSidedRingChain => Coattail with
        {
            FirstPositionDrivenNode = 2,
            InvMasses = [0f, 0f, 0.003428f, 0.003444f, 0.003428f, 0.003427f, 0.0065f, 0.0065f],
            Rods =
            [
                RigidRod(0, 2, 8.499948f, 1f, 0f),
                RigidRod(0, 3, 8.646747f, 1f, 0f),
                RigidRod(1, 2, 8.732066f, 1f, 0f),
                RigidRod(1, 3, 8.412711f, 1f, 0f),
                RigidRod(2, 3, 2f),
                RigidRod(2, 4, 8.499931f),
                RigidRod(2, 5, 8.735466f),
                RigidRod(3, 4, 8.732044f),
                RigidRod(3, 5, 8.503419f),
                RigidRod(4, 5, 2.000001f),
                RigidRod(4, 6, 8.500037f),
                RigidRod(4, 7, 8.732154f),
                RigidRod(5, 6, 8.732168f),
                RigidRod(5, 7, 8.500037f),
                RigidRod(6, 7, 2.000001f),
            ],
            ReverseOffsets = [ReverseOffset(6, 7, -0.000001f, 2.000001f, -0.000001f)],
        };

        /// <summary>
        /// A coattail of four joints, each with a two-node ring, the first joint and its ring static and the joints
        /// position-driven after their rings.
        /// </summary>
        private static FeModelBuilder TwoSidedRingChain => new()
        {
            Names = ["coattail_0_L", "$cccoattail_0_L_0", "$cccoattail_1_L_0", "$cccoattail_1_L_1", "$cccoattail_2_L_0", "$cccoattail_2_L_1",
                "$cccoattail_end_L_0", "$cccoattail_end_L_1", "coattail_1_L", "coattail_2_L", "coattail_end_L"],
            StaticNodes = 2,
            FirstPositionDrivenNode = 8,
            InvMasses = [0f, 0f, 0.003209f, 0.003111f, 0.003141f, 0.003142f, 0.005709f, 0.005709f, 1f, 1f, 1f],
            Poses =
            [
                Pose(-8.915481f, 4.000124f, 65.447983f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-10.723646f, 4.561181f, 66.092773f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-13.473376f, 4.824905f, 58.146507f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-9.917552f, 3.709337f, 56.693367f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-16.587204f, 5.195801f, 50.242416f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-13.028829f, 4.079931f, 48.795761f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-19.686529f, 5.562407f, 42.336063f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-16.128153f, 4.446536f, 40.889408f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-11.695464f, 4.267121f, 57.419937f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-14.808016f, 4.637866f, 49.519089f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-17.907341f, 5.004471f, 41.612736f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
            ],
            Parents = [-1, 0, 8, 8, 9, 9, 10, 10, 0, 8, 9],
            SourceElems = [0, 0, 0, 6, 5, 4, 6, 7, 4, 5, 7, 6, 3, 2, 4, 5, 2, 3, 5, 4, 1, 0, 2, 3, 0, 1, 3, 2],
            Rods =
            [
                RigidRod(0, 2, 8.646747f, 1f, 0f),
                RigidRod(0, 3, 8.816575f, 1f, 0f),
                RigidRod(1, 2, 8.412711f, 1f, 0f),
                RigidRod(1, 3, 9.472289f, 1f, 0f),
                RigidRod(2, 3, 4f),
                RigidRod(2, 4, 8.503419f),
                RigidRod(2, 5, 9.390903f),
                RigidRod(3, 4, 9.397265f),
                RigidRod(3, 5, 8.496444f),
                RigidRod(4, 5, 4.000001f),
                RigidRod(4, 6, 8.500037f),
                RigidRod(4, 7, 9.394195f),
                RigidRod(5, 6, 9.394169f),
                RigidRod(5, 7, 8.500037f),
                RigidRod(6, 7, 4.000002f),
                RigidRod(0, 8, 8.499948f, 1f, 0f),
                RigidRod(8, 9, 8.499931f),
                RigidRod(9, 10, 8.500037f),
            ],
            ReverseOffsets =
            [
                ReverseOffset(8, 5, 8.496444f, -1.999937f, 0.000001f),
                ReverseOffset(9, 5, 0.000001f, -2f, 0f),
                ReverseOffset(10, 6, -0.000001f, 2.000001f, -0.000001f),
            ],
        };

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
        private static ClothReconstruction SiblingHubs(bool locked, bool rings)
        {
            var model = new FeModelBuilder
            {
                Names = ["hub", "r1", "r2", "c1", "c2", .. rings ? ["$ccr1_0", "$ccr2_0"] : Array.Empty<string>()],
                StaticNodes = 3,
                Parents = [-1, -1, -1, 1, 2, .. rings ? [1, 2] : Array.Empty<int>()],
                LockToGoal = locked ? [1, 2] : [],
                Positions = [new(0f, 0f, 0f), new(-5f, 0f, -10f), new(5f, 0f, -10f), new(-5f, 0f, -20f), new(5f, 0f, -20f),
                    .. rings ? [new Vector3(-2f, 0f, -10f), new Vector3(8f, 0f, -10f)] : Array.Empty<Vector3>()],
                CtrlOffsets = rings ? [Offset(1, 5, 3f, 0f, 0f), Offset(2, 6, 3f, 0f, 0f)] : null,
                Rods = [RigidRod(1, 3, 10f), RigidRod(2, 4, 10f)],
            }.Reconstruct();
            return WithSkeleton(model, new Dictionary<string, string?>
            {
                ["hub"] = null,
                ["r1"] = "hub",
                ["r2"] = "hub",
                ["c1"] = "r1",
                ["c2"] = "r2",
                ["$ccr1_0"] = "r1",
                ["$ccr2_0"] = "r2",
            });
        }

        /// <summary>
        /// An ungraded twist-written hint reads as version 0 only on a joint with a chain child, not on a leaf.
        /// </summary>
        [Test]
        public async Task ALeafsUngradedHintStatesNoVersion()
        {
            var model = TwistedRope(new FeNodeWindBase(2, 1, 0, 0), new FeNodeWindBase(3, 2, 0, 0));

            var interior = new BoneChain { RootBone = "j1" };
            interior.Joints.Add(new BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1 });
            interior.Joints.Add(new BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f });
            interior.Joints.Add(new BoneChainJoint { Node = 3, Name = "j3", ParentNode = 2, InvMass = 1f });

            var leaf = new BoneChain { RootBone = "j1" };
            leaf.Joints.Add(new BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1 });
            leaf.Joints.Add(new BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f });

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
        private static ClothReconstruction HingeFanGate(bool limits, bool anchor)
        {
            var hat = HatHinge(20f);
            return (hat with
            {
                Names = [.. hat.Names, "$cloth_m0p0", .. anchor ? ["$ha_hat"] : Array.Empty<string>()],
                Parents = [.. hat.Parents!, -1, .. anchor ? [-1] : Array.Empty<int>()],
                InvMasses = [0f, 0f, 0f, 1f, 1f, 1f, 1f, .. anchor ? [0f] : Array.Empty<float>()],
                Positions = [.. hat.Positions!, new(16f, 0f, 0f), .. anchor ? [Vector3.Zero] : Array.Empty<Vector3>()],
                HingeLimits = limits ? [new FeHingeLimit([0, 1, 2, 5, 2, 5], 0, 0f, 0f, 0f, 0.785398f)] : null,
            }).Reconstruct();
        }

        /// <summary>
        /// A hinge over a ringless child fanning over a triangle is a rigid hinge link, as a quad is; a triangle over
        /// bones only, or no surface element, is not.
        /// </summary>
        [Test]
        public async Task AHingeOverARinglessChildFansOutOverATriangleAndIsStillARigidHingeLink()
        {
            var fan = (HingeTriGate with { Tris = [Tri(2, 1, 4)] }).Reconstruct();
            var authoredOverBones = (HingeTriGate with { Tris = [Tri(3, 4, 5)] }).Reconstruct();
            var noSurface = HingeTriGate.Reconstruct();
            var quad = (HingeTriGate with { Quads = [Quad(2, 1, 4, 5)] }).Reconstruct();

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
        private static FeModelBuilder HingeTriGate => new()
        {
            Names = ["$ha_hat", "$cchat_0", "$cchat_1", "hat", "hat_end", "hat_tip"],
            StaticNodes = 4,
            Parents = [3, 3, 3, -1, 3, 4],
            Positions = [new(0f, 0f, 0f), new(0f, -10f, 0f), new(0f, 10f, 0f), new(0f, 0f, 0f), new(8f, 0f, 0f), new(16f, 0f, 0f)],
            CtrlOffsets = [Offset(3, 1, 0f, -10f, 0.000001f), Offset(3, 2, 0f, 10f, -0.000001f)],
            Rods = [RigidRod(4, 5, 8f)],
            HingeLimits = [new FeHingeLimit([1, 2, 3, 4, 3, 4], 0, 0f, 0f, 0f, 0.785398f)],
        };

        /// <summary>
        /// A twist between two bones links them into a chain with or without <c>m_SkelParents</c>; without twists the
        /// run stays loose.
        /// </summary>
        [Test]
        public async Task ATwistBetweenTwoBonesIsAChainLink()
        {
            static bool Chained(ClothReconstruction model) => model.BuildBoneChains().Exists(chain => chain.Joints.Count == 3);

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
        private static ClothReconstruction TwistedRun(bool skelParents, bool twisted) => new FeModelBuilder
        {
            Names = ["w0", "w1", "w2"],
            StaticNodes = 1,
            Positions = [new(0f, 0f, 0f), new(-8.5f, 0f, 0f), new(-17f, 0f, 0f)],
            Parents = skelParents ? [-1, 0, 1] : null,
            Twists = twisted ? [Twist(0, 1, 0f, 1f), Twist(1, 0, 0.618f, 0f), Twist(1, 2, 0.382f, 0.5f), Twist(2, 1, 0.618f, 1f)] : [],
        }.Reconstruct();

        /// <summary>
        /// <c>TwistRecords</c> and <c>NodeBaseRecords</c> keep every record in array order with its swing relaxation,
        /// while <c>NodeBases</c> keeps each node's last record and <c>TwistNodes</c> every named node.
        /// </summary>
        [Test]
        public async Task EveryTwistAndNodeBaseRecordIsKept()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["root", "j1", "j2"],
                StaticNodes = 1,
                Parents = [-1, 0, 1],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                Twists = [Twist(1, 0, 0.618f, 0f), Twist(1, 2, 0.382f, 1f), Twist(1, 0, 0.2163f, 0.5f)],
                NodeBases = [NodeBase(1, 1, 2, 0, 2), NodeBase(1, 1, 0, 2, 0), NodeBase(2, 2, 1, 0, 1)],
            }.Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(cloth.Index.NodeBases.Count).IsEqualTo(2);
                await Assert.That(cloth.Index.NodeBases[1]).IsEqualTo(new FeModelIndex.NodeBasis(1, 0, 2, 0));
                await Assert.That(cloth.Index.TwistNodes.Order()).IsEquivalentTo([0, 1, 2], CollectionOrdering.Matching);

                await Assert.That(cloth.Fe.Twists).IsEquivalentTo(
                    [
                        new FeModel.FeTwistConstraint(1, 0, 0.618f, 0f),
                        new FeModel.FeTwistConstraint(1, 2, 0.382f, 1f),
                        new FeModel.FeTwistConstraint(1, 0, 0.2163f, 0.5f),
                    ], CollectionOrdering.Matching);
                await Assert.That(cloth.Index.NodeBaseRecords).IsEquivalentTo(
                    [
                        (1, new FeModelIndex.NodeBasis(1, 2, 0, 2)),
                        (1, new FeModelIndex.NodeBasis(1, 0, 2, 0)),
                        (2, new FeModelIndex.NodeBasis(2, 1, 0, 1)),
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
            static bool Joined(bool centre) => new FeModelBuilder
            {
                Names = ["w0", "w1", "w2", "k1", centre ? "$ccw2_Ctr" : "k2"],
                StaticNodes = 1,
                FirstPositionDrivenNode = 5,
                Parents = [-1, 0, 1, 2, centre ? 2 : -1],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(0f, 5f, -20f)],
                Rods = [RigidRod(0, 1, 10f), RigidRod(1, 2, 10f)],
                RopeCount = 1,
                Ropes = [3, 2, 3],
            }.Reconstruct().BuildBoneChains().Exists(chain => chain.Joints.Exists(static j => j.Name == "w2")
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
            static BoneChain Chain(ClothReconstruction cloth, bool withKid, int sides)
            {
                var chain = new BoneChain { RootBone = "tip" };
                var tip = Array.IndexOf(cloth.Fe.CtrlName, "tip");
                chain.Joints.Add(new BoneChainJoint { Node = tip, Name = "tip", ParentNode = -1, InvMass = 0f, ExtrudeSides = sides });
                if (withKid)
                {
                    chain.Joints.Add(new BoneChainJoint
                    {
                        Node = Array.IndexOf(cloth.Fe.CtrlName, "kid"),
                        Name = "kid",
                        ParentNode = tip,
                        ParentName = "tip",
                        InvMass = 1f,
                        ExtrudeSides = sides,
                    });
                }

                return chain;
            }

            static BoneChain TwoKids(ClothReconstruction cloth)
            {
                var chain = Chain(cloth, withKid: true, sides: 0);
                var tip = Array.IndexOf(cloth.Fe.CtrlName, "tip");
                chain.Joints.Add(new BoneChainJoint
                {
                    Node = Array.IndexOf(cloth.Fe.CtrlName, "root"),
                    Name = "root",
                    ParentNode = tip,
                    ParentName = "tip",
                    InvMass = 1f,
                });
                return chain;
            }

            var ringed = ParentLockedTip(rings: true);
            var ringless = ParentLockedTip(rings: false);
            var ringedTip = Array.IndexOf(ringed.Fe.CtrlName, "tip");
            var ringlessTip = Array.IndexOf(ringless.Fe.CtrlName, "tip");

            using (Assert.Multiple())
            {
                await Assert.That(ringed.LocksTranslation(ringedTip, chainVersion: 2, chain: Chain(ringed, withKid: true, sides: 1))).IsTrue();
                await Assert.That(ringless.LocksTranslation(ringlessTip, chainVersion: 2)).IsTrue();
                await Assert.That(ringless.LocksTranslation(ringlessTip, chainVersion: 2, chain: TwoKids(ringless))).IsTrue();

                await Assert.That(ringless.LocksTranslation(ringlessTip, chainVersion: 2, chain: Chain(ringless, withKid: false, sides: 0))).IsFalse();
                await Assert.That(ringless.LocksTranslation(ringlessTip, chainVersion: 2, chain: Chain(ringless, withKid: true, sides: 0))).IsFalse();
            }
        }

        /// <summary>A chain ten thousand joints deep is walked in pre-order without overflowing the stack.</summary>
        [Test]
        public async Task AVeryDeepChainIsWalkedInPreOrder()
        {
            const int Depth = 10_000;
            var cloth = new FeModelBuilder
            {
                Names = [.. Enumerable.Range(0, Depth).Select(static node => $"bone{node}")],
                StaticNodes = 1,
                Parents = [.. Enumerable.Range(-1, Depth)],
                Positions = [.. Enumerable.Range(0, Depth).Select(static node => new Vector3(0f, 0f, -node))],
                Rods = [.. Enumerable.Range(1, Depth - 1).Select(static node => RigidRod(node - 1, node, 1f))],
            }.Reconstruct();

            var chains = cloth.BuildBoneChains();

            using (Assert.Multiple())
            {
                await Assert.That(chains.Count).IsEqualTo(1);
                await Assert.That(chains[0].Joints.Select(static joint => joint.Node)).IsEquivalentTo(Enumerable.Range(0, Depth),
                    CollectionOrdering.Matching);
            }
        }
    }
}
