using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace Tests
{
    /// <summary>Reading a chain joint's springs, iterations, suspenders, twists and clusters off the compiled rods.</summary>
    public class ClothFeModelRodTest : ClothTestFixtures
    {
        /// <summary>
        /// A chain rod's relaxation is the slider scaled by <c>exp(-default_stretch)</c>, which the reading divides
        /// back out: 0.5 * exp(-0.985) = 0.18672.
        /// </summary>
        [Test]
        public async Task ChainRodRelaxationDividesOutTheDefaultStretch()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                body: $$"""
                    m_flDefaultSurfaceStretch = 0.985
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 0.18672f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 0.18672f)}}
                    ]
                    """);

            var chains = feModel.BuildBoneChains();
            await Assert.That(chains.Count).IsEqualTo(1);

            using (Assert.Multiple())
            {
                foreach (var joint in chains[0].Joints)
                {
                    await Assert.That(joint.StretchStiffness).IsEqualTo(0.5f).Within(1e-3f);
                }
            }
        }

        /// <summary>
        /// Without <c>default_stretch</c> the slider is the compiled relaxation verbatim.
        /// </summary>
        [Test]
        public async Task ChainRodRelaxationIsVerbatimWithoutDefaultStretch()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1"], staticNodes: 1, parents: [-1, 0], poses: [new(0f, 0f, 0f), new(0f, 0f, -10f)], body: $$"""
                    m_Rods = [ {{SyntheticCloth.RigidRod(0, 1, 10f, 0.8f)}} ]
                    """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            await Assert.That(joint).IsNotNull();
            await Assert.That(joint!.StretchStiffness).IsEqualTo(0.8f).Within(1e-4f);
        }

        /// <summary>
        /// On a pair holding the chain's rod and a banded constraint, the chain keeps its matching rod and the banded
        /// copy comes back as surplus in either order.
        /// </summary>
        [Test]
        public async Task GetUngeneratedRodsKeepsTheChainRodAndReturnsTheBandedCopy()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1"], staticNodes: 1, parents: [-1, 0], poses: [new(0f, 0f, 0f), new(0f, 0f, -3f)], body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.BandedRod(0, 1, 1f, 5f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 3f, 0.6f)}}
                    ]
                    """);

            var chains = feModel.BuildBoneChains();
            var surplus = feModel.GetUngeneratedRods(chains);

            using (Assert.Multiple())
            {
                await Assert.That(chains[0].Joints.Find(j => j.Name == "j1")!.StretchStiffness)
                    .IsEqualTo(0.6f).Within(1e-4f);
                await Assert.That(surplus.Count).IsEqualTo(1);
                await Assert.That(surplus[0].MinDist).IsEqualTo(1f);
                await Assert.That(surplus[0].MaxDist).IsEqualTo(5f);
            }
        }

        /// <summary>
        /// A twist entry pointing at the joint's own extrude ring carries the authored value scaled by
        /// the child branch factor: 0.5 * 0.382 = 0.191.
        /// </summary>
        [Test]
        public async Task TwistRelaxDividesByTheChildBranchFactor()
        {
            var feModel = TwistModel(1, 2, 0.191f);

            await Assert.That(feModel.GetAuthoredTwistRelax(1, 0, 2)).IsEqualTo(0.5f).Within(1e-4f);
        }

        /// <summary>
        /// A joint read through its parent-ward entry instead carries the other branch factor:
        /// 0.5 * 0.618 = 0.309.
        /// </summary>
        [Test]
        public async Task TwistRelaxDividesByTheParentBranchFactorWithoutARing()
        {
            var feModel = TwistModel(1, 0, 0.309f);

            await Assert.That(feModel.GetAuthoredTwistRelax(1, 0, -1)).IsEqualTo(0.5f).Within(1e-4f);
        }

        private static FeModel TwistModel(int orient, int end, float relax) => SyntheticCloth.Model(
            ["root", "j1", "$ccj1_0"], staticNodes: 1, parents: [-1, 0, 1], body: $$"""
                m_Twists =
                [
                    { nNodeOrient = {{orient}} nNodeEnd = {{end}} flTwistRelax = {{SyntheticCloth.Num(relax)}} },
                ]
                """);

        /// <summary>
        /// Three rigid copies of a joint's parent span read two extra iterations.
        /// </summary>
        [Test]
        public async Task ExtraIterationsCountsTheRigidCopiesOfASpan()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1"], staticNodes: 1, parents: [-1, 0], poses: [new(0f, 0f, 0f), new(0f, 0f, -3f)], body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 3f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 3f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 3f, 1f)}}
                    ]
                    """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.ExtraIterations).IsEqualTo(2);
                await Assert.That(joint.Suspender).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// Three identical slack copies of a parent span read two extra iterations, as rigid copies do.
        /// </summary>
        [Test]
        public async Task ExtraIterationsCountsIdenticalSlackCopiesOfASpan()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1"], staticNodes: 1, parents: [-1, 0], poses: [new(0f, 0f, 0f), new(0f, 0f, -3f)], body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.BandedRod(0, 1, 0f, 3f, 1f)}}
                        {{SyntheticCloth.BandedRod(0, 1, 0f, 3f, 1f)}}
                        {{SyntheticCloth.BandedRod(0, 1, 0f, 3f, 1f)}}
                    ]
                    """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.ExtraIterations).IsEqualTo(2);
                await Assert.That(joint.Suspender).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A span rod holding a quarter of its rest span reads <c>antishrink</c> 0.25.
        /// </summary>
        [Test]
        public async Task ChainJointAntishrinkIsTheSlackItsOwnSpanKeeps()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1"], staticNodes: 1, parents: [-1, 0], poses: [new(0f, 0f, 0f), new(0f, 0f, -3f)], body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.BandedRod(0, 1, 0.75f, 3f, 1f)}}
                    ]
                    """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            await Assert.That(joint!.Antishrink).IsEqualTo(0.25f);
        }

        /// <summary>
        /// Slack rods on one pair that are not the same record state neither an extra iteration nor an antishrink.
        /// </summary>
        [Test]
        public async Task SlackRodsThatDisagreeAreNotExtraIterations()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1"], staticNodes: 1, parents: [-1, 0], poses: [new(0f, 0f, 0f), new(0f, 0f, -3f)], body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.BandedRod(0, 1, 0f, 3f, 1f)}}
                        {{SyntheticCloth.BandedRod(0, 1, 1f, 3f, 1f)}}
                    ]
                    """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.ExtraIterations).IsEqualTo(0);
                await Assert.That(joint.Antishrink).IsEqualTo(1f);
            }
        }

        /// <summary>
        /// The rods between every pair of a joint's children read as its <c>child_sibling_spring</c>.
        /// </summary>
        [Test]
        public async Task ChildSiblingSpringIsTheRodBetweenTwoChildrenOfOneJoint()
        {
            var joint = SiblingChain($"""
                {SyntheticCloth.RigidRod(0, 1, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 2, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 3, 3f, 1f)}
                {SyntheticCloth.RigidRod(1, 2, 3f, 0.5f)}
                {SyntheticCloth.RigidRod(1, 3, 3f, 0.5f)}
                {SyntheticCloth.RigidRod(2, 3, 3f, 0.5f)}
                """);

            await Assert.That(joint!.ChildSiblingSpring).IsEqualTo(0.5f);
        }

        /// <summary>
        /// A sibling set missing one of its pairs states no <c>child_sibling_spring</c>.
        /// </summary>
        [Test]
        public async Task AnIncompleteSiblingSetIsNotAChildSiblingSpring()
        {
            var joint = SiblingChain($"""
                {SyntheticCloth.RigidRod(0, 1, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 2, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 3, 3f, 1f)}
                {SyntheticCloth.RigidRod(1, 2, 3f, 0.5f)}
                """);

            await Assert.That(joint!.ChildSiblingSpring).IsEqualTo(0f);
        }

        private static FeModel.BoneChainJoint? SiblingChain(string rods) => SyntheticCloth.Model(
            ["root", "j1", "j2", "j3"], staticNodes: 1, parents: [-1, 0, 0, 0],
            poses: [new(0f, 0f, 0f), new(3f, 0f, 0f), new(0f, 3f, 0f), new(0f, 0f, 3f)],
            body: $$"""
                m_Rods =
                [
                    {{rods}}
                ]
                """).BuildBoneChains()[0].Joints.Find(j => j.Name == "root");

        /// <summary>
        /// Rods joining a node to itself or missing an endpoint are dropped at parse.
        /// </summary>
        [Test]
        public async Task SelfRodsAndDegenerateRodsAreDropped()
        {
            var feModel = SyntheticCloth.Model(["a", "b", "c", "d"], staticNodes: 0, body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(3, 3, 1f, 1f)}}
                        { nNode = [ 2 ] flMinDist = 1.0 flMaxDist = 1.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                        {{SyntheticCloth.RigidRod(0, 1, 2f, 1f)}}
                    ]
                    """);

            await Assert.That(feModel.Rods.Length).IsEqualTo(1);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.Rods[0].NodeA).IsEqualTo(0);
                await Assert.That(feModel.Rods[0].NodeB).IsEqualTo(1);
                await Assert.That(feModel.Rods[0].MaxDist).IsEqualTo(2f);
            }
        }

        /// <summary>
        /// A chain root reads its iterations off its only child's span, three copies reading two; a root with two
        /// children keeps zero.
        /// </summary>
        [Test]
        public async Task AChainRootCountsItsIterationsOnItsOnlyChildsSpan()
        {
            var oneChild = SyntheticCloth.Model(
                ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    ]
                    """);

            var twoChildren = SyntheticCloth.Model(
                ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 0],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, 0f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                    ]
                    """);

            using (Assert.Multiple())
            {
                await Assert.That(RootJoint(oneChild).ExtraIterations).IsEqualTo(2);
                await Assert.That(RootJoint(twoChildren).ExtraIterations).IsEqualTo(0);
            }
        }

        private static FeModel.BoneChainJoint RootJoint(FeModel feModel)
            => feModel.BuildBoneChains()[0].Joints.Find(static joint => joint.IsRoot)!;

        /// <summary>
        /// A joint whose upward span reaches the root reads the odd rod on that span as its suspender companion.
        /// </summary>
        [Test]
        public async Task ASuspenderCompanionIsTheSurplusRodOnTheRootSpan()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 20f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 20f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 20f, 0.35f)}}
                    ]
                    """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(static j => j.Name == "j2");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.Suspender).IsEqualTo(0.35f).Within(1e-4f);
                await Assert.That(joint.ExtraIterations).IsEqualTo(1);
            }
        }

        /// <summary>
        /// Past the torsion span every rigid rod on the pair is a suspender copy, so two agreeing copies read 0.42.
        /// </summary>
        [Test]
        public async Task ASuspenderPastTheTorsionSpanIsReadFromItsRepeatedCopies()
        {
            var joint = LongChainWithSuspender().BuildBoneChains()[0].Joints
                .Find(static j => j.Name == "j4");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.Suspender).IsEqualTo(0.42f).Within(1e-4f);
                await Assert.That(joint.ExtraIterations).IsEqualTo(1);
            }
        }

        /// <summary>
        /// At two iterations both suspender companion copies are regenerated and none is surplus.
        /// </summary>
        [Test]
        public async Task ASuspenderCompanionIsRegeneratedOncePerIteration()
        {
            var feModel = LongChainWithSuspender();

            await Assert.That(feModel.GetUngeneratedRods(feModel.BuildBoneChains())).IsEmpty();
        }

        private static FeModel LongChainWithSuspender() => SyntheticCloth.Model(
            ["root", "j1", "j2", "j3", "j4"], staticNodes: 1, parents: [-1, 0, 1, 2, 3],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(0f, 0f, -40f)],
            body: $$"""
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(2, 3, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(3, 4, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(3, 4, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(0, 4, 40f, 0.42f)}}
                    {{SyntheticCloth.RigidRod(0, 4, 40f, 0.42f)}}
                ]
                """);

        /// <summary>
        /// A two-corner source element between two chain joints is re-declared as a spring with the rod's fields, and
        /// the joint's stretch slider is zeroed.
        /// </summary>
        [Test]
        public async Task ASourceElementBetweenTwoChainJointsIsAnAuthoredSpring()
        {
            var feModel = SpringedChain(string.Empty);
            var chains = feModel.BuildBoneChains();
            var springs = feModel.GetAuthoredSourceSprings(chains);

            using (Assert.Multiple())
            {
                await Assert.That(chains[0].Joints.Find(static j => j.Name == "j2")!.StretchStiffness)
                    .IsEqualTo(0f);
                await Assert.That(springs.Count).IsEqualTo(1);
                await Assert.That(springs[0]).IsEqualTo((1, 2, 1));
            }
        }

        /// <summary>
        /// On a compile with no node bases a roped node keeps its chain span, and its two-corner element is not a
        /// spring.
        /// </summary>
        [Test]
        public async Task ARopedChainKeepsItsOwnSpanWhereTheCompileWroteNoNodeBase()
        {
            var feModel = SpringedChain("""
                m_nRopeCount = 1
                m_Ropes = [ 4, 0, 1, 2 ]
                """);
            var chains = feModel.BuildBoneChains();

            using (Assert.Multiple())
            {
                await Assert.That(chains[0].Joints.Find(static j => j.Name == "j2")!.StretchStiffness)
                    .IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(feModel.GetAuthoredSourceSprings(chains)).IsEmpty();
            }
        }

        private static FeModel SpringedChain(string ropes) => SyntheticCloth.Model(
            ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
            body: $$"""
                m_SourceElems = [ 0, 1, 0, 0, 1, 2 ]
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(1, 2, 10f, 0.5f)}}
                ]
                {{ropes}}
                """);

        /// <summary>
        /// A chain root whose ring pair carries a rigid rod at 0.6 beside a banded surface rod at 1.0 reads its
        /// <c>stretch_spring</c> off the rigid rod alone.
        /// </summary>
        [Test]
        public async Task AChainRootReadsItsStretchSpringOffItsOwnRigidRingRod()
        {
            var joint = RingWithASurfaceRod(1.0f).BuildBoneChains()[0].Joints[0];

            await Assert.That(joint.StretchStiffness).IsEqualTo(0.6f).Within(1e-4f);
        }

        /// <summary>
        /// Where every rod inside the root's extrusion agrees, that reading stands over the rigid rods.
        /// </summary>
        [Test]
        public async Task AChainRootWhoseExtrusionAgreesKeepsTheWholeReading()
        {
            var joint = RingWithASurfaceRod(0.6f).BuildBoneChains()[0].Joints[0];

            await Assert.That(joint.StretchStiffness).IsEqualTo(0.6f).Within(1e-4f);
        }

        /// <summary>
        /// A chain root with a two-node ring whose pair carries a rigid rod at 0.6 beside a banded rod at <paramref
        /// name="surfaceRelaxation"/>.
        /// </summary>
        private static FeModel RingWithASurfaceRod(float surfaceRelaxation) => SyntheticCloth.Model(
            ["root", "$ccroot_0", "$ccroot_1"], staticNodes: 1, parents: [-1, 0, 0],
            poses: [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, -2f, 0f)],
            body: $$"""
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(1, 2, 4f, 0.6f)}}
                    {{SyntheticCloth.BandedRod(1, 2, 1f, 8f, surfaceRelaxation)}}
                ]
                """);

        [Test]
        public async Task MotionBiasIsReadOffTheJointsOwnSpanRod()
        {
            var joint = new FeModel.BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f };
            using (Assert.Multiple())
            {
                await Assert.That(BiasedRope("0.5").GetMotionBias(joint)).IsNull();
                await Assert.That(BiasedRope("0.333333").GetMotionBias(joint)!.Value).IsEqualTo(0.5f).Within(1e-3f);
                await Assert.That(BiasedRope("0.666667").GetMotionBias(joint)!.Value).IsEqualTo(-0.5f).Within(1e-3f);
                await Assert.That(BiasedRope("0.0").GetMotionBias(joint)!.Value).IsEqualTo(1f).Within(1e-3f);
            }
        }

        private static FeModel BiasedRope(string weight) => SyntheticCloth.Model(
            ["root", "j1", "j2", "j3"], staticNodes: 1, parents: [-1, 0, 1, 2],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f)],
            body: $$"""
                m_Rods =
                [
                    { nNode = [ 1, 2 ] flMaxDist = 10.0 flMinDist = 10.0 flWeight0 = {{weight}} flRelaxationFactor = 1.0 },
                ]
                """);

        /// <summary>
        /// A span with one pair carrying an extra rod reads <c>extra_iterations</c> off the count every pair reaches.
        /// </summary>
        [Test]
        public async Task ExtraIterationsIsTheFloorOfASpanWhoseOnePairCarriesASurplusRod()
        {
            var even = RingSpanChain(surplus: 0).BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");
            var lopsided = RingSpanChain(surplus: 1).BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(even!.ExtraIterations).IsEqualTo(2);
                await Assert.That(lopsided!.ExtraIterations).IsEqualTo(2);
            }
        }

        /// <summary>
        /// A ringed root and a ringed joint, so the joint's own span is the four pairs of the two rings.
        /// Every pair carries three copies; <paramref name="surplus"/> more go on one of them.
        /// </summary>
        private static FeModel RingSpanChain(int surplus)
        {
            var rods = new System.Text.StringBuilder();
            foreach (var (a, b) in new[] { (1, 4), (1, 5), (2, 4), (2, 5) })
            {
                var copies = 3 + (a == 1 && b == 4 ? surplus : 0);
                for (var i = 0; i < copies; i++)
                {
                    rods.Append(SyntheticCloth.RigidRod(a, b, 4f, 1f));
                }
            }

            return SyntheticCloth.Model(
                ["root", "$ccroot_0", "$ccroot_1", "j1", "$ccj1_0", "$ccj1_1"], staticNodes: 1, parents: [-1, 0, 0, 0, 3, 3],
                poses: [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, -2f, 0f), new(0f, 0f, -4f), new(0f, 2f, -4f), new(0f, -2f, -4f)],
                body: $$"""
                    m_Rods =
                    [
                        {{rods}}
                    ]
                    """);
        }

        /// <summary>
        /// A complete clique of banded rods reads as a <c>ClothSelfCollisionCluster</c>; a triangle stays plain rods.
        /// </summary>
        [Test]
        public async Task ACompleteBandedRodCliqueIsReadBackAsASelfCollisionCluster()
        {
            var clique = ClusterCloth(4);
            var triangle = ClusterCloth(3);

            using (Assert.Multiple())
            {
                await Assert.That(clique.SelfCollisionClusters.Count).IsEqualTo(1);
                await Assert.That(string.Join(",", clique.SelfCollisionClusters[0].Nodes)).IsEqualTo("1,2,3,4");
                await Assert.That(clique.SelfCollisionClusters[0].MinDist).IsEqualTo(2f);
                await Assert.That(clique.SelfCollisionClusters[0].MaxDist).IsEqualTo(10f);
                await Assert.That(clique.SelfCollisionClusterRods.Count).IsEqualTo(6);
                await Assert.That(triangle.SelfCollisionClusters.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A chain whose every span a cluster owns reads <c>stretch_spring</c> 0; without a cluster it keeps the
        /// default.
        /// </summary>
        [Test]
        public async Task AChainWhoseSpansACollisionClusterOwnsRecoversAZeroStretchSlider()
        {
            var clustered = ClusteredChain().BuildBoneChains()[0].Joints;
            var plain = StretchlessChain(SyntheticCloth.RigidRod(0, 2, 20f, 1f)
                + SyntheticCloth.RigidRod(1, 3, 20f, 1f)).BuildBoneChains()[0].Joints;

            using (Assert.Multiple())
            {
                await Assert.That(clustered.Count).IsEqualTo(5);
                await Assert.That(clustered.TrueForAll(joint => joint.IsRoot || joint.StretchStiffness == 0f))
                    .IsTrue();
                await Assert.That(plain.TrueForAll(joint => joint.IsRoot || joint.StretchStiffness == 1f))
                    .IsTrue();
            }
        }

        private static FeModel ClusteredChain()
        {
            var rods = new StringBuilder();
            for (var a = 1; a <= 4; a++)
            {
                for (var b = a + 1; b <= 4; b++)
                {
                    rods.Append(SyntheticCloth.BandedRod(a, b, 2f, 10f, 1f));
                }
            }

            rods.Append(SyntheticCloth.RigidRod(0, 2, 20f, 1f));
            rods.Append(SyntheticCloth.RigidRod(1, 3, 20f, 1f));
            rods.Append(SyntheticCloth.RigidRod(2, 4, 20f, 1f));
            return SyntheticCloth.Model(
                ["j0", "j1", "j2", "j3", "j4"], staticNodes: 0, parents: [-1, 0, 1, 2, 3],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(0f, 0f, -40f)],
                body: $$"""
                    m_nFirstPositionDrivenNode = 5
                    m_Rods =
                    [
                        {{rods}}
                    ]
                    """);
        }

        private static FeModel ClusterCloth(int members)
        {
            var names = string.Join(", ", Enumerable.Range(0, members + 1).Select(i => $"\"j{i}\""));
            var parents = string.Join(", ", Enumerable.Range(-1, members + 1));
            var poses = string.Concat(Enumerable.Range(0, members + 1)
                .Select(i => SyntheticCloth.Pose(0f, 0f, -10f * i) + "\n                    "));
            var masses = string.Join(", ", Enumerable.Range(0, members + 1).Select(static _ => "1.0"));
            var rods = new StringBuilder();
            for (var a = 1; a <= members; a++)
            {
                for (var b = a + 1; b <= members; b++)
                {
                    rods.Append(SyntheticCloth.BandedRod(a, b, 2f, 10f, 1f));
                }
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{names}} ]
                    m_SkelParents = [ {{parents}} ]
                    m_nNodeCount = {{members + 1}}
                    m_nStaticNodes = 0
                    m_nFirstPositionDrivenNode = {{members + 1}}
                    m_NodeInvMasses = [ {{masses}} ]
                    m_InitPose =
                    [
                        {{poses}}
                    ]
                    m_Rods =
                    [
                        {{rods}}
                    ]
                }
                """);
        }

        /// <summary>
        /// A joint whose parent is the root reads a doubled parent span as a suspender, not an extra iteration.
        /// </summary>
        [Test]
        public async Task AJointOnTheChainRootReadsItsDoubledRootPairAsASuspender()
        {
            var joint = SuspendedRope().BuildBoneChains()[0].Joints[1];

            using (Assert.Multiple())
            {
                await Assert.That(joint.Suspender).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(joint.ExtraIterations).IsEqualTo(0);
            }
        }

        private static FeModel SuspendedRope() => SyntheticCloth.Model(
            ["root", "j1", "j2", "j3"], staticNodes: 1, parents: [-1, 0, 1, 2],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f)],
            body: $$"""
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 0.5f)}}
                    {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(2, 3, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(0, 2, 20f, 0.5f)}}
                    {{SyntheticCloth.RigidRod(0, 3, 30f, 0.5f)}}
                ]
                """);

        /// <summary>
        /// A three-member cluster is read off a shared 12 to 48 band no pair rests at; the same triangle banded at a
        /// pair's rest distance is a surface, not a cluster.
        /// </summary>
        [Test]
        public async Task AThreeMemberClusterIsReadOffABandNoPairRestsAt()
        {
            var cluster = SyntheticCloth.Parse(ThreeMemberClusterText).SelfCollisionClusters;
            var surface = SyntheticCloth.Parse(ThreeMemberClusterText
                .Replace("flMinDist = 12.0 flMaxDist = 48.0", "flMinDist = 2.0 flMaxDist = 8.5", StringComparison.Ordinal))
                .SelfCollisionClusters;

            using (Assert.Multiple())
            {
                await Assert.That(cluster.Count).IsEqualTo(1);
                await Assert.That(cluster[0].Nodes.SequenceEqual(new[] { 2, 4, 6 })).IsTrue();
                await Assert.That(cluster[0].MinDist).IsEqualTo(12f);
                await Assert.That(cluster[0].MaxDist).IsEqualTo(48f);
                await Assert.That(surface.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A cluster member's stiffness is read off the products its pair rods carry: 0.25 on every pair is three
        /// members at 0.5, and 0.25 / 0.5 / 0.5 is 0.5, 0.5 and 1.0.
        /// </summary>
        [Test]
        public async Task AClusterMembersStiffnessIsReadOffTheProductsItsPairRodsCarry()
        {
            const string Band = "flMinDist = 12.0 flMaxDist = 48.0 flWeight0 = 0.5 flRelaxationFactor = 1.0";
            var uniform = SyntheticCloth.Parse(ThreeMemberClusterText
                .Replace(Band, "flMinDist = 12.0 flMaxDist = 48.0 flWeight0 = 0.5 flRelaxationFactor = 0.25", StringComparison.Ordinal))
                .SelfCollisionClusters;
            var mixed = SyntheticCloth.Parse(ThreeMemberClusterText
                .Replace("{ nNode = [ 2, 4 ] " + Band, "{ nNode = [ 2, 4 ] flMinDist = 12.0 flMaxDist = 48.0 flWeight0 = 0.5 flRelaxationFactor = 0.25", StringComparison.Ordinal)
                .Replace(Band, "flMinDist = 12.0 flMaxDist = 48.0 flWeight0 = 0.5 flRelaxationFactor = 0.5", StringComparison.Ordinal))
                .SelfCollisionClusters;

            using (Assert.Multiple())
            {
                await Assert.That(uniform.Count).IsEqualTo(1);
                await Assert.That(uniform[0].Stiffness!.All(static s => MathF.Abs(s - 0.5f) < 1e-4f)).IsTrue();
                await Assert.That(mixed.Count).IsEqualTo(1);
                await Assert.That(mixed[0].Stiffness![0]).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(mixed[0].Stiffness![1]).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(mixed[0].Stiffness![2]).IsEqualTo(1f).Within(1e-4f);
            }
        }

        private static string ThreeMemberClusterText => SyntheticCloth.Fixture("cloth_chain_three_member_cluster.kv3");

        /// <summary>
        /// A joint's <c>motion_bias</c> is read off its span weights whatever its ends weigh: 1/3 reads 0.5 and 2/3
        /// reads -0.5.
        /// </summary>
        [Test]
        public async Task AJointsMotionBiasIsReadOffItsSpanWhateverItsEndsWeigh()
        {
            var positive = SyntheticCloth.Parse(BiasedChainText).BuildBoneChains()[0].Joints.First(static joint => joint.Node == 6);
            var negativeModel = SyntheticCloth.Parse(BiasedChainText
                .Replace("flWeight0 = 0.333333", "flWeight0 = 0.666667", StringComparison.Ordinal));
            var negative = negativeModel.BuildBoneChains()[0].Joints.First(static joint => joint.Node == 6);

            using (Assert.Multiple())
            {
                await Assert.That(SyntheticCloth.Parse(BiasedChainText).GetMotionBias(positive)!.Value).IsEqualTo(0.5f).Within(1e-3f);
                await Assert.That(negativeModel.GetMotionBias(negative)!.Value).IsEqualTo(-0.5f).Within(1e-3f);
            }
        }

        private static string BiasedChainText => SyntheticCloth.Fixture("cloth_chain_motion_bias.kv3");

        /// <summary>
        /// A joint with no rod on its span and none on its ring reads <c>stretch_spring</c> 0; putting its ring rod
        /// back reads 1.
        /// </summary>
        [Test]
        public async Task AJointsZeroStretchSpringIsReadOffItsRodlessSpanAndRing()
        {
            var joints = SyntheticCloth.Parse(AlternatingStretchText).BuildBoneChains()[0].Joints;
            var ringed = SyntheticCloth.Parse(AlternatingStretchText.Replace(
                "{ nNode = [ 5, 6 ]",
                "{ nNode = [ 3, 4 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },\n{ nNode = [ 5, 6 ]",
                StringComparison.Ordinal)).BuildBoneChains()[0].Joints;

            using (Assert.Multiple())
            {
                await Assert.That(joints.First(static joint => joint.Name == "coattail_1_L").StretchStiffness).IsEqualTo(0f);
                await Assert.That(joints.First(static joint => joint.Name == "coattail_2_L").StretchStiffness).IsEqualTo(1f);
                await Assert.That(joints.First(static joint => joint.Name == "coattail_end_L").StretchStiffness).IsEqualTo(0f);
                await Assert.That(ringed.First(static joint => joint.Name == "coattail_1_L").StretchStiffness).IsEqualTo(1f);
            }
        }

        private static string AlternatingStretchText => SyntheticCloth.Fixture("cloth_chain_alternating_stretch.kv3");

        /// <summary>
        /// A joint's <c>animated_length</c> is read off the rods it and its children move into <c>m_SimdRodsAnim</c>;
        /// on a childless tip only a node base tells it from a zero <c>stretch_spring</c>.
        /// </summary>
        [Test]
        public async Task AJointsAnimatedLengthIsReadOffTheRodsItAndItsChildrenLose()
        {
            var joints = SyntheticCloth.Parse(AnimatedJointTwoText).BuildBoneChains()[0].Joints;
            var tipBased = SyntheticCloth.Parse(AnimatedEveryJointText.Replace(
                "m_Rods =",
                "m_NodeBases = [ { nNode = 7 nNodeX0 = 7 nNodeX1 = 3 nNodeY0 = 4 nNodeY1 = 6 } ]\nm_Rods =",
                StringComparison.Ordinal)).BuildBoneChains()[0].Joints.First(static joint => joint.Name == "coattail_end_L");
            var tipFree = SyntheticCloth.Parse(AnimatedEveryJointText).BuildBoneChains()[0].Joints
                .First(static joint => joint.Name == "coattail_end_L");

            using (Assert.Multiple())
            {
                await Assert.That(joints.First(static joint => joint.Name == "coattail_2_L").AnimatedLength).IsTrue();
                await Assert.That(joints.First(static joint => joint.Name == "coattail_2_L").StretchStiffness).IsEqualTo(1f);
                await Assert.That(joints.First(static joint => joint.Name == "coattail_1_L").AnimatedLength).IsFalse();
                await Assert.That(joints.First(static joint => joint.Name == "coattail_end_L").AnimatedLength).IsFalse();
                await Assert.That(tipBased.AnimatedLength).IsTrue();
                await Assert.That(tipFree.AnimatedLength).IsFalse();
                await Assert.That(tipFree.StretchStiffness).IsEqualTo(0f);
            }
        }

        private static string AnimatedJointTwoText => SyntheticCloth.Fixture("cloth_chain_animated_joint.kv3");

        private static string AnimatedEveryJointText => SyntheticCloth.Fixture("cloth_chain_animated_every_joint.kv3");

        /// <summary>
        /// A suspender on a joint next to the root is read off the root span's copies even where every span is
        /// repeated: suspender 0.5 at two extra iterations.
        /// </summary>
        [Test]
        public async Task ARootAdjacentSuspenderIsReadWhereEverySpanIsRepeated()
        {
            var coattail = SyntheticCloth.Parse(RepeatedSuspenderChainText).BuildBoneChains()[0].Joints
                .First(static joint => joint.Name == "coattail_1_L");

            using (Assert.Multiple())
            {
                await Assert.That(coattail.Suspender).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(coattail.ExtraIterations).IsEqualTo(2);
            }
        }

        private static string RepeatedSuspenderChainText => SyntheticCloth.Fixture("cloth_chain_repeated_suspender.kv3");

        /// <summary>
        /// Under version 1 a joint cut off from its children reads as animated where it keeps a node base, and as a
        /// zero <c>stretch_spring</c> where it does not.
        /// </summary>
        [Test]
        public async Task AnAnimatedLengthJointIsReadOffTheNodeBaseItKeeps()
        {
            const string InnerBases = "{ nNode = 5 nNodeX0 = 3 nNodeX1 = 0 nNodeY0 = 1 nNodeY1 = 6 }, "
                + "{ nNode = 6 nNodeX0 = 5 nNodeX1 = 4 nNodeY0 = 7 nNodeY1 = 2 }";
            var based = SyntheticCloth.Parse(AnimatedEveryJointText.Replace("m_Rods =",
                "m_NodeBases = [ " + InnerBases + ", { nNode = 7 nNodeX0 = 7 nNodeX1 = 3 nNodeY0 = 4 nNodeY1 = 6 } ]\nm_Rods =",
                StringComparison.Ordinal)).BuildBoneChains()[0].Joints;
            var tipFree = SyntheticCloth.Parse(AnimatedEveryJointText.Replace("m_Rods =",
                "m_NodeBases = [ " + InnerBases + " ]\nm_Rods =", StringComparison.Ordinal)).BuildBoneChains()[0].Joints;

            using (Assert.Multiple())
            {
                await Assert.That(based.Where(static joint => !joint.IsRoot).All(static joint => joint.AnimatedLength)).IsTrue();
                await Assert.That(tipFree.First(static joint => joint.Name == "coattail_1_L").AnimatedLength).IsTrue();
                await Assert.That(tipFree.First(static joint => joint.Name == "coattail_end_L").AnimatedLength).IsFalse();
                await Assert.That(tipFree.First(static joint => joint.Name == "coattail_end_L").StretchStiffness).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A suspender of 1.0 next to the root is read off the ring rod carrying as many copies as the base span, not
        /// as a repeat.
        /// </summary>
        [Test]
        public async Task ASuspenderAtTheChainsOwnRelaxationIsReadOffItsRingCopies()
        {
            var coattail = SyntheticCloth.Parse(SuspenderAtNaturalRelaxationText).BuildBoneChains()[0].Joints
                .First(static joint => joint.Name == "coattail_1_L");

            using (Assert.Multiple())
            {
                await Assert.That(coattail.Suspender).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(coattail.ExtraIterations).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A twist link a static root authored carries no relaxation either way, while the simulated child's link
        /// carries 0.5 * 0.618 = 0.309.
        /// </summary>
        [Test]
        public async Task ARelaxlessTwistLinkIsTheStaticEndsOwnTwist()
        {
            var rootAuthored = TwistPair("0.0", "0.0");
            var childAuthored = TwistPair("0.0", "0.309");

            using (Assert.Multiple())
            {
                await Assert.That(rootAuthored.HasRelaxlessTwistLink(0)).IsTrue();
                await Assert.That(childAuthored.HasRelaxlessTwistLink(0)).IsFalse();
                await Assert.That(childAuthored.GetAuthoredTwistRelax(1, 0, -1)).IsEqualTo(0.5f).Within(1e-4f);
            }
        }

        /// <summary>
        /// A joint whose extrusion has no <c>m_SimdRodsAnim</c> entry reads a zero <c>stretch_spring</c>, not an
        /// animated length.
        /// </summary>
        [Test]
        public async Task AJointWithNoAnimatedRodDeclaresNoStretchInstead()
        {
            const string Bases = "m_NodeBases = [ { nNode = 5 nNodeX0 = 3 nNodeX1 = 0 nNodeY0 = 1 nNodeY1 = 6 }, "
                + "{ nNode = 6 nNodeX0 = 5 nNodeX1 = 4 nNodeY0 = 7 nNodeY1 = 2 } ]\nm_Rods =";
            var control = SyntheticCloth.Parse(AnimatedEveryJointText
                .Replace("m_Rods =", Bases, StringComparison.Ordinal)).BuildBoneChains()[0].Joints;
            var rodless = SyntheticCloth.Parse(WithoutAnimatedRods(AnimatedEveryJointText)
                .Replace("m_Rods =", Bases, StringComparison.Ordinal)).BuildBoneChains()[0].Joints;

            using (Assert.Multiple())
            {
                await Assert.That(control.First(static joint => joint.Name == "coattail_1_L").AnimatedLength).IsTrue();
                await Assert.That(rodless.First(static joint => joint.Name == "coattail_1_L").AnimatedLength).IsFalse();
                await Assert.That(rodless.First(static joint => joint.Name == "coattail_1_L").StretchStiffness)
                    .IsEqualTo(0f);
            }
        }

        private static string WithoutAnimatedRods(string text)
            => text[..text.IndexOf("m_SimdRodsAnim", StringComparison.Ordinal)]
                + text[text.IndexOf("m_Rods =", StringComparison.Ordinal)..];

        /// <summary>
        /// A source spring's rod copies are its <c>extra_iterations</c> and leave no surplus rod, and the spring keeps
        /// its source element's corner order.
        /// </summary>
        [Test]
        public async Task ASourceSpringsRodCopiesAreItsExtraIterations()
        {
            var repeated = SpringCopies(3);
            var single = SpringCopies(1);

            using (Assert.Multiple())
            {
                await Assert.That(repeated.GetAuthoredSourceSprings(repeated.BuildBoneChains())[0])
                    .IsEqualTo((2, 1, 3));
                await Assert.That(repeated.GetUngeneratedRods(repeated.BuildBoneChains())).IsEmpty();
                await Assert.That(single.GetAuthoredSourceSprings(single.BuildBoneChains())[0])
                    .IsEqualTo((2, 1, 1));
            }
        }

        private static FeModel SpringCopies(int copies) => SyntheticCloth.Model(
            ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
            body: $$"""
                m_SourceElems = [ 0, 1, 0, 0, 2, 1 ]
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                    {{string.Concat(Enumerable.Repeat(SyntheticCloth.RigidRod(1, 2, 10f, 0.5f), copies))}}
                ]
                """);

        /// <summary>
        /// A <c>motion_bias</c> under an animated parent is read off the <c>m_SimdRodsAnim</c> weights of its span: 2/3
        /// reads 0.5.
        /// </summary>
        [Test]
        public async Task AMotionBiasIsReadOffTheAnimatedRodsOfItsSpan()
        {
            var biased = SyntheticCloth.Parse(AnimatedJointTwoText
                .Replace("[ 7, 6, 6, 6 ] ] f4Weight0 = [ 0.5,", "[ 7, 6, 6, 6 ] ] f4Weight0 = [ 0.666667,", StringComparison.Ordinal)
                .Replace("[ 7, 5, 5, 5 ] ] f4Weight0 = [ 0.5,", "[ 7, 5, 5, 5 ] ] f4Weight0 = [ 0.666667,", StringComparison.Ordinal));
            var unbiased = SyntheticCloth.Parse(AnimatedJointTwoText);

            using (Assert.Multiple())
            {
                await Assert.That(biased.GetMotionBias(TipOf(biased)) ?? float.NaN).IsEqualTo(0.5f).Within(1e-3f);
                await Assert.That(unbiased.GetMotionBias(TipOf(unbiased)).HasValue).IsFalse();
            }
        }

        private static FeModel.BoneChainJoint TipOf(FeModel feModel)
            => feModel.BuildBoneChains()[0].Joints.First(static joint => joint.Name == "coattail_end_L");

        /// <summary>
        /// On a root pair holding one bend rod and one suspender companion, the suspender is the rod away from the bend
        /// reading in either order.
        /// </summary>
        [Test]
        public async Task ASuspenderBesideASingleBendRodIsTheRodAwayFromTheBendReading()
        {
            static FeModel.BoneChainJoint Tip(bool companionFirst)
            {
                var bend = SyntheticCloth.RigidRod(0, 2, 20f, 1f);
                var companion = SyntheticCloth.RigidRod(0, 2, 20f, 0.21f);
                var feModel = SyntheticCloth.Model(
                    ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
                    poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                    body: $$"""
                        m_Rods =
                        [
                            {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                            {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                            {{(companionFirst ? companion : bend)}}
                            {{(companionFirst ? bend : companion)}}
                        ]
                        """);
                return feModel.BuildBoneChains()[0].Joints.Find(static joint => joint.Name == "j2")!;
            }

            FeModel.BoneChainJoint bendFirst = Tip(companionFirst: false);
            FeModel.BoneChainJoint suspenderFirst = Tip(companionFirst: true);

            using (Assert.Multiple())
            {
                await Assert.That(bendFirst.BendSpring).IsTrue();
                await Assert.That(bendFirst.BendStiffness).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(bendFirst.Suspender).IsEqualTo(0.21f).Within(1e-4f);
                await Assert.That(bendFirst.ExtraIterations).IsEqualTo(0);
                await Assert.That(suspenderFirst.Suspender).IsEqualTo(0.21f).Within(1e-4f);
                await Assert.That(suspenderFirst.ExtraIterations).IsEqualTo(0);
            }
        }

        /// <summary>
        /// Where both root-pair rods differ from the chain's own relaxation, the higher is read as the bend and the
        /// lower as the suspender, in either record order.
        /// </summary>
        [Test]
        public async Task ASuspenderAndABendRodOffTheChainRateAreSplitByValue()
        {
            static FeModel.BoneChainJoint Tip(bool companionFirst)
            {
                var bend = SyntheticCloth.RigidRod(0, 2, 20f, 1f);
                var companion = SyntheticCloth.RigidRod(0, 2, 20f, 0.2f);
                var feModel = SyntheticCloth.Model(
                    ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
                    poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                    body: $$"""
                        m_Rods =
                        [
                            {{SyntheticCloth.RigidRod(0, 1, 10f, 0.9f)}}
                            {{SyntheticCloth.RigidRod(1, 2, 10f, 0.9f)}}
                            {{(companionFirst ? companion : bend)}}
                            {{(companionFirst ? bend : companion)}}
                        ]
                        """);
                return feModel.BuildBoneChains()[0].Joints.Find(static joint => joint.Name == "j2")!;
            }

            FeModel.BoneChainJoint bendFirst = Tip(companionFirst: false);
            FeModel.BoneChainJoint suspenderFirst = Tip(companionFirst: true);

            using (Assert.Multiple())
            {
                await Assert.That(bendFirst.BendSpring).IsTrue();
                await Assert.That(bendFirst.BendStiffness).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(bendFirst.Suspender).IsEqualTo(0.2f).Within(1e-4f);
                await Assert.That(suspenderFirst.BendStiffness).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(suspenderFirst.Suspender).IsEqualTo(0.2f).Within(1e-4f);
            }
        }

        /// <summary>
        /// The sibling spring is the relaxation every child pair shares, so an extra rod on one pair does not refute
        /// it; pairs sharing no value or two values state none.
        /// </summary>
        [Test]
        public async Task AForeignRodOnOnePairDoesNotRefuteTheSiblingSpring()
        {
            var shared = SiblingChain($"""
                {SyntheticCloth.RigidRod(0, 1, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 2, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 3, 3f, 1f)}
                {SyntheticCloth.RigidRod(1, 2, 3f, 0.5f)}
                {SyntheticCloth.RigidRod(1, 2, 3f, 0.9f)}
                {SyntheticCloth.RigidRod(1, 3, 3f, 0.5f)}
                {SyntheticCloth.RigidRod(2, 3, 3f, 0.5f)}
                """);

            var disjoint = SiblingChain($"""
                {SyntheticCloth.RigidRod(0, 1, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 2, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 3, 3f, 1f)}
                {SyntheticCloth.RigidRod(1, 2, 3f, 0.9f)}
                {SyntheticCloth.RigidRod(1, 3, 3f, 0.5f)}
                {SyntheticCloth.RigidRod(2, 3, 3f, 0.5f)}
                """);

            var ambiguous = SiblingChain($"""
                {SyntheticCloth.RigidRod(0, 1, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 2, 3f, 1f)}
                {SyntheticCloth.RigidRod(0, 3, 3f, 1f)}
                {SyntheticCloth.RigidRod(1, 2, 3f, 0.5f)}
                {SyntheticCloth.RigidRod(1, 2, 3f, 0.9f)}
                {SyntheticCloth.RigidRod(1, 3, 3f, 0.5f)}
                {SyntheticCloth.RigidRod(1, 3, 3f, 0.9f)}
                {SyntheticCloth.RigidRod(2, 3, 3f, 0.5f)}
                {SyntheticCloth.RigidRod(2, 3, 3f, 0.9f)}
                """);

            using (Assert.Multiple())
            {
                await Assert.That(shared!.ChildSiblingSpring).IsEqualTo(0.5f);
                await Assert.That(disjoint!.ChildSiblingSpring).IsEqualTo(0f);
                await Assert.That(ambiguous!.ChildSiblingSpring).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A chain root reads <c>extra_iterations</c> from its children's sibling rod copies; single copies read 0, and
        /// one extra copy on one pair does not lift the reading.
        /// </summary>
        [Test]
        public async Task ARootReadsItsIterationsFromItsChildrensSiblingRods()
        {
            static FeModel Fan(int copies, int extraOnOnePair) => SyntheticCloth.Model(
                ["root", "c1", "c2", "c3"], staticNodes: 1, parents: [-1, 0, 0, 0],
                poses: [new(0f, 0f, 0f), new(-10f, 0f, -10f), new(0f, 0f, -10f), new(10f, 0f, -10f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 14.142136f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 3, 14.142136f, 1f)}}
                        {{string.Concat(Enumerable.Repeat(SyntheticCloth.RigidRod(1, 2, 10f, 0.5f), copies))}}
                        {{string.Concat(Enumerable.Repeat(SyntheticCloth.RigidRod(1, 3, 20f, 0.5f), copies + extraOnOnePair))}}
                        {{string.Concat(Enumerable.Repeat(SyntheticCloth.RigidRod(2, 3, 10f, 0.5f), copies))}}
                    ]
                    """);

            static int Iterations(FeModel feModel)
                => feModel.BuildBoneChains()[0].Joints.Find(static joint => joint.Name == "root")!.ExtraIterations;

            using (Assert.Multiple())
            {
                await Assert.That(Iterations(Fan(3, 0))).IsEqualTo(2);
                await Assert.That(Iterations(Fan(1, 0))).IsEqualTo(0);
                await Assert.That(Iterations(Fan(3, 1))).IsEqualTo(2);
            }
        }

        /// <summary>
        /// A joint whose upward span carries no rod reads <c>extra_iterations</c> off its children's sibling rods; a
        /// rod on the span, disagreeing span counts, or a single child do not read the siblings.
        /// </summary>
        [Test]
        public async Task AJointWhoseSpanCarriesNoRodStillReadsItsSiblingRods()
        {
            static FeModel Rodless(int siblings, bool twoChildren = true) => SyntheticCloth.Model(
                ["root", "p", "j", "c1", "c2", "sibling_of_j"], staticNodes: 3, parents: [-1, 0, 1, 2, 2, 1],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(-10f, 0f, -30f), new(10f, 0f, -30f),
                    new(30f, 0f, -20f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(1, 5, 30f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 3, 22.36068f, 1f)}}
                        {{SyntheticCloth.RigidRod(2, 3, 14.142136f, 1f)}}
                        {{(twoChildren ? SyntheticCloth.RigidRod(2, 4, 14.142136f, 1f) : string.Empty)}}
                        {{(twoChildren
                            ? string.Concat(Enumerable.Repeat(SyntheticCloth.RigidRod(3, 4, 20f, 0.5f), siblings))
                            : string.Empty)}}
                    ]
                    """);

            static FeModel Spanned(int spanRods, int bendRods, int siblings) => SyntheticCloth.Model(
                ["root", "p1", "p2", "j", "c1", "c2"], staticNodes: 1, parents: [-1, 0, 1, 2, 3, 3],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(-10f, 0f, -40f),
                    new(10f, 0f, -40f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                        {{string.Concat(Enumerable.Repeat(SyntheticCloth.RigidRod(2, 3, 10f, 1f), spanRods))}}
                        {{string.Concat(Enumerable.Repeat(SyntheticCloth.RigidRod(1, 3, 20f, 1f), bendRods))}}
                        {{SyntheticCloth.RigidRod(3, 4, 14.142136f, 1f)}}
                        {{SyntheticCloth.RigidRod(3, 5, 14.142136f, 1f)}}
                        {{string.Concat(Enumerable.Repeat(SyntheticCloth.RigidRod(4, 5, 20f, 0.5f), siblings))}}
                    ]
                    """);

            static int Iterations(FeModel feModel)
            {
                foreach (var chain in feModel.BuildBoneChains())
                {
                    if (chain.Joints.Find(static joint => joint.Name == "j") is { } joint)
                    {
                        return joint.ExtraIterations;
                    }
                }

                return -1;
            }

            using (Assert.Multiple())
            {
                await Assert.That(Iterations(Rodless(2))).IsEqualTo(1);
                await Assert.That(Iterations(Rodless(4))).IsEqualTo(3);
                await Assert.That(Iterations(Rodless(7))).IsEqualTo(6);

                await Assert.That(Iterations(Spanned(1, 0, 4))).IsEqualTo(0);

                await Assert.That(Iterations(Spanned(2, 3, 5))).IsEqualTo(1);

                await Assert.That(Iterations(Rodless(4, twoChildren: false))).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A spring over a pair the chain also spans keeps the joint's stretch and declares only the surplus copy; a
        /// spring on a pair with its own rod alone zeroes the span.
        /// </summary>
        [Test]
        public async Task ASpringOverAChainSpanAddsARodRatherThanReplacingIt()
        {
            var doubled = SpringOverASpan(secondRodOnThePair: true);
            var single = SpringOverASpan(secondRodOnThePair: false);

            var doubledChain = doubled.BuildBoneChains()[0];
            var singleChain = single.BuildBoneChains()[0];

            using (Assert.Multiple())
            {
                await Assert.That(string.Join(",", doubledChain.Joints.Select(static joint => joint.Name)))
                    .IsEqualTo("j0,j1,j2,j3");
                await Assert.That(doubled.SourceSprings.Length).IsEqualTo(1);

                await Assert.That(doubledChain.Joints[2].StretchStiffness).IsNotEqualTo(0f);
                await Assert.That(doubled.GetAuthoredSourceSprings([doubledChain])
                    .Select(static spring => spring.Copies).Sum()).IsEqualTo(1);

                await Assert.That(singleChain.Joints[2].StretchStiffness).IsEqualTo(0f);
                await Assert.That(single.GetAuthoredSourceSprings([singleChain])
                    .Select(static spring => spring.Copies).Sum()).IsEqualTo(1);
            }
        }

        /// <summary>
        /// Four chain joints with one-node rings, rods between consecutive extrusions and a two-corner source element on
        /// (j1, j2); <paramref name="secondRodOnThePair"/> doubles that pair's rod.
        /// </summary>
        private static FeModel SpringOverASpan(bool secondRodOnThePair) => SyntheticCloth.Model(
            ["j0", "$ccj0_0", "j1", "$ccj1_0", "j2", "$ccj2_0", "j3", "$ccj3_0"], staticNodes: 2,
                parents: [-1, 0, 0, 2, 2, 4, 4, 6],
            poses: [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f), new(0f, 0f, -16f), new(0f, 2f, -16f),
                new(0f, 0f, -24f), new(0f, 2f, -24f)],
            body: $$"""
                m_CtrlOffsets =
                [
                    { vOffset = [ 0.0, 2.0, 0.0 ] nCtrlParent = 0 nCtrlChild = 1 },
                    { vOffset = [ 0.0, 2.0, 0.0 ] nCtrlParent = 2 nCtrlChild = 3 },
                    { vOffset = [ 0.0, 2.0, 0.0 ] nCtrlParent = 4 nCtrlChild = 5 },
                    { vOffset = [ 0.0, 2.0, 0.0 ] nCtrlParent = 6 nCtrlChild = 7 },
                ]
                m_SourceElems = [ 0, 1, 0, 0, 2, 4 ]
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 2, 8f, 1f)}}
                    {{SyntheticCloth.RigidRod(0, 3, 8.246211f, 1f)}}
                    {{SyntheticCloth.RigidRod(1, 2, 8.246211f, 1f)}}
                    {{SyntheticCloth.RigidRod(1, 3, 8f, 1f)}}
                    {{SyntheticCloth.RigidRod(2, 3, 2f, 1f)}}
                    {{SyntheticCloth.RigidRod(2, 4, 8f, 1f)}}
                    {{SyntheticCloth.RigidRod(2, 5, 8.246211f, 1f)}}
                    {{SyntheticCloth.RigidRod(3, 4, 8.246211f, 1f)}}
                    {{SyntheticCloth.RigidRod(3, 5, 8f, 1f)}}
                    {{SyntheticCloth.RigidRod(4, 5, 2f, 1f)}}
                    {{SyntheticCloth.RigidRod(4, 6, 8f, 1f)}}
                    {{SyntheticCloth.RigidRod(4, 7, 8.246211f, 1f)}}
                    {{SyntheticCloth.RigidRod(5, 6, 8.246211f, 1f)}}
                    {{SyntheticCloth.RigidRod(5, 7, 8f, 1f)}}
                    {{SyntheticCloth.RigidRod(6, 7, 2f, 1f)}}
                    {{(secondRodOnThePair ? SyntheticCloth.RigidRod(2, 4, 8f, 1f) : string.Empty)}}
                ]
                """);

        /// <summary>
        /// A fold-weighted rod between chain joints reads as <c>add_stiffness_rods</c> and is no surplus; the
        /// even-split rod, or a fan-weighted rod on an unfolded pair, is not the switch's.
        /// </summary>
        [Test]
        public async Task ASurfaceFoldBetweenChainJointsIsTheSwitchsAndNoSurplus()
        {
            var folded = FoldedChainModel(0.666667f, fanPair: true);
            var declared = FoldedChainModel(0.5f, fanPair: true);
            var elsewhere = FoldedChainModel(0.666667f, fanPair: false);

            static bool Holds(List<FeModel.Rod> rods, int a, int b)
                => rods.Exists(rod => (rod.NodeA == a && rod.NodeB == b) || (rod.NodeA == b && rod.NodeB == a));

            using (Assert.Multiple())
            {
                await Assert.That(declared.HasChainStiffnessRods(declared.BuildBoneChains())).IsFalse();
                await Assert.That(Holds(declared.GetUngeneratedRods(declared.BuildBoneChains(), true), 1, 3)).IsTrue();
                await Assert.That(elsewhere.HasChainStiffnessRods(elsewhere.BuildBoneChains())).IsFalse();

                await Assert.That(folded.HasChainStiffnessRods(folded.BuildBoneChains())).IsTrue();
                await Assert.That(Holds(folded.GetUngeneratedRods(folded.BuildBoneChains(), true), 1, 3)).IsFalse();
            }
        }

        /// <summary>
        /// A static root over joints a, b and c, two triangles folding across root-b, and a banded rod a-c at <paramref
        /// name="weight"/>; without <paramref name="fanPair"/> nothing folds onto a-c.
        /// </summary>
        private static FeModel FoldedChainModel(float weight, bool fanPair) => SyntheticCloth.Model(
            ["root", "a", "b", "c"], staticNodes: 1, parents: [-1, 0, 0, 2], invMasses: "0.0, 1.0, 1.0, 0.5",
            poses: [new(0f, 0f, 0f), new(-1f, 0f, -2f), new(0f, 0f, -2f), new(1f, 0f, -4f)],
            body: $$"""
                m_SourceElems = {{(fanPair ? "[ 0, 0, 2, 0, 0, 1, 2, 0, 2, 3 ]" : "[ 0, 0, 2, 0, 0, 1, 2, 0, 3, 1 ]")}}
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 2.236068f, 1f)}}
                    {{SyntheticCloth.RigidRod(0, 2, 2f, 1f)}}
                    {{SyntheticCloth.RigidRod(2, 3, 2.236068f, 1f)}}
                    { nNode = [ 1, 3 ] flMinDist = 1.5 flMaxDist = 2.828427 flWeight0 = {{SyntheticCloth.Num(weight)}} flRelaxationFactor = 1.0 },
                ]
                """);

        /// <summary>
        /// A centre-only end effector's bend and torsion spans run from the joint's own node, reading 0.6 and 0.8; its
        /// stretch reads 0.6.
        /// </summary>
        [Test]
        public async Task ACentreOnlyEndEffectorsSpansRunFromTheJointsOwnNode()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "j1", "j2", "tip", "$cctip_Ctr"], staticNodes: 1, parents: [-1, 0, 1, 2, 3],
                poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f), new(40f, 0f, 0f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 0.6f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 0.6f)}}
                        {{SyntheticCloth.RigidRod(2, 3, 10f, 0.6f)}}
                        {{SyntheticCloth.RigidRod(1, 3, 20f, 0.6f)}}
                        {{SyntheticCloth.RigidRod(0, 3, 30f, 0.8f)}}
                        {{SyntheticCloth.RigidRod(3, 4, 10f, 0.6f)}}
                        {{SyntheticCloth.RigidRod(2, 4, 20f, 0.6f)}}
                        {{SyntheticCloth.RigidRod(1, 4, 30f, 0.8f)}}
                    ]
                    """);

            var tip = feModel.BuildBoneChains().SelectMany(chain => chain.Joints).FirstOrDefault(joint => joint.Name == "tip");

            using (Assert.Multiple())
            {
                await Assert.That(tip?.BendStiffness ?? -1f).IsEqualTo(0.6f).Within(1e-4f);
                await Assert.That(tip?.TorsionStiffness ?? -1f).IsEqualTo(0.8f).Within(1e-4f);

                await Assert.That(tip?.StretchStiffness ?? -1f).IsEqualTo(0.6f).Within(1e-4f);
            }
        }

        /// <summary>
        /// A joint whose grandparent span is only a fold-weighted rod declares no bend spring; the even-split rod is
        /// its bend spring.
        /// </summary>
        [Test]
        public async Task ABendSpanThatIsOnlyASurfaceFoldIsNoBendSpring()
        {
            static bool Bends(float weight) => SyntheticCloth.Model(
                ["L0", "R0", "L1", "R1", "L2", "R2", "L3", "R3"], staticNodes: 2, parents: [-1, -1, 0, 1, 2, 3, 4, 5],
                    invMasses: "0.0, 0.0, 0.02, 0.02, 0.015, 0.015, 0.01, 0.01",
                poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, -10f), new(0f, 0f, -20f),
                    new(10f, 0f, -20f), new(0f, 0f, -30f), new(10f, 0f, -30f)],
                body: $$"""
                    m_Quads = [ { nNode = [ 4, 2, 3, 5 ] }, { nNode = [ 4, 6, 7, 5 ] } ]
                    m_Rods =
                    [
                        { nNode = [ 0, 2 ] flMinDist = 10.0 flMaxDist = 10.0 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                        { nNode = [ 1, 3 ] flMinDist = 10.0 flMaxDist = 10.0 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                        {{SyntheticCloth.RigidRod(2, 4, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(3, 5, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(4, 6, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(5, 7, 10f, 1f)}}
                        { nNode = [ 2, 6 ] flMinDist = 12.0 flMaxDist = 20.0 flWeight0 = {{SyntheticCloth.Num(weight)}} flRelaxationFactor = 1.0 },
                        { nNode = [ 3, 7 ] flMinDist = 12.0 flMaxDist = 20.0 flWeight0 = {{SyntheticCloth.Num(weight)}} flRelaxationFactor = 1.0 },
                    ]
                    """).BuildBoneChains().SelectMany(static chain => chain.Joints).Where(static joint => joint.Name == "L3")
                .Select(static joint => joint.BendSpring).DefaultIfEmpty(false).First();

            using (Assert.Multiple())
            {
                await Assert.That(Bends(0.5f)).IsTrue();

                await Assert.That(Bends(0.666667f)).IsFalse();
            }
        }
    }
}
