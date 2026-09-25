using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class ClothLawTest
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
        /// A ring bend rod carries <c>flMinDist = flMaxDist * sin(add_curvature * pi / 2)</c>: minimum 2.3344536 at
        /// maximum 10 reads 0.15.
        /// </summary>
        [Test]
        public async Task ChainRingCurvatureInvertsTheHalfSineLaw()
        {
            var feModel = RingCurvatureModel(SyntheticCloth.BandedRod(1, 2, 2.3344536f, 10f, 1f));

            await Assert.That(feModel.ChainRingCurvature).IsEqualTo(0.15f).Within(1e-4f);
        }

        /// <summary>
        /// Two ring rods reading different curvatures (0.4 and 0.15) are refused.
        /// </summary>
        [Test]
        public async Task ChainRingCurvatureRefusesADisagreeingRing()
        {
            var feModel = RingCurvatureModel(
                SyntheticCloth.BandedRod(1, 2, 2.3344536f, 10f, 1f)
                + SyntheticCloth.BandedRod(2, 3, 5.8778525f, 10f, 1f));

            await Assert.That(feModel.ChainRingCurvature).IsEqualTo(0f);
        }

        private static FeModel RingCurvatureModel(string rods) => SyntheticCloth.Model(
            ["j", "$ccj_0", "$ccj_1", "$ccj_2"], staticNodes: 0, parents: [-1, 0, 0, 0],
            poses: [new(0f, 0f, 0f), new(0f, 1f, 0f), new(0f, 0f, 1f), new(0f, -1f, 0f)],
            body: $$"""
                m_Rods = [ {{rods}} ]
                """);

        /// <summary>
        /// Every element credits both ends of each corner pair with 4 per unit of rest length, times the squared
        /// <c>mass</c> multiplier: a 3x4 rectangle's corner weighs 4 * 12 * 1.5^2 = 108.
        /// </summary>
        [Test]
        public async Task ElementMassCreditsFourPerUnitOfEachCornerPair()
        {
            var feModel = SyntheticCloth.Model(
                ["v0", "v1", "v2", "v3"], staticNodes: 0, invMasses: "0.009259259, 0.009259259, 0.009259259, 0.009259259",
                poses: [new(0f, 0f, 0f), new(3f, 0f, 0f), new(3f, 4f, 0f), new(0f, 4f, 0f)],
                body: """
                    m_Quads = [ { nNode = [ 0, 1, 2, 3 ] } ]
                    """);

            using (Assert.Multiple())
            {
                for (var node = 0; node < 4; node++)
                {
                    var multiplier = feModel.RecoverMassMultiplier(node);
                    await Assert.That(multiplier).IsNotNull();
                    await Assert.That(multiplier!.Value).IsEqualTo(1.5f).Within(1e-3f);
                }
            }
        }

        /// <summary>
        /// Without a proxy sheet a rod credits both ends 8 per unit of rest length: a rod of 3 gives 24, and 54 at
        /// multiplier 1.5.
        /// </summary>
        [Test]
        public async Task RodMassCreditsEightPerUnitOfLength()
        {
            var feModel = RodMassModel(SyntheticCloth.RigidRod(0, 1, 3f, 1f));

            using (Assert.Multiple())
            {
                await Assert.That(feModel.RecoverMassMultiplier(0)).IsNotNull();
                await Assert.That(feModel.RecoverMassMultiplier(0)!.Value).IsEqualTo(1.5f).Within(1e-3f);
                await Assert.That(feModel.RecoverMassMultiplier(1)!.Value).IsEqualTo(1.5f).Within(1e-3f);
            }
        }

        /// <summary>
        /// A rod with the unbounded maximum weighs nothing, so the same mass reads no multiplier.
        /// </summary>
        [Test]
        public async Task AnUnboundedRodDoesNotWeigh()
        {
            var feModel = RodMassModel(SyntheticCloth.BandedRod(0, 1, 3f, FeModel.UnboundedRodDistance, 1f));

            await Assert.That(feModel.RecoverMassMultiplier(0)).IsNull();
        }

        private static FeModel RodMassModel(string rod) => SyntheticCloth.Model(
            ["a", "b"], staticNodes: 0, parents: [-1, -1], invMasses: "0.018518519, 0.018518519",
            poses: [new(0f, 0f, 0f), new(3f, 0f, 0f)],
            body: $$"""
                m_Rods = [ {{rod}} ]
                """);

        /// <summary>
        /// A volume-solved selection credits each covered node 12 per unit of its members' summed extent: 72 for extent
        /// 6, and 162 at multiplier 1.5.
        /// </summary>
        [Test]
        public async Task VolumetricSelectionCreditsTwelvePerUnitOfExtent()
        {
            var feModel = SyntheticCloth.Model(
                ["a", "b"], staticNodes: 0, invMasses: "0.006172839, 0.006172839",
                poses: [new(0f, 0f, 0f), new(1f, 2f, 3f)],
                body: """
                    m_VertexMapValues = [ 255, 255 ]
                    m_VertexMaps =
                    [
                        {
                            sName = "body"
                            nNameHash = 1
                            nVertexBase = 0
                            nVertexCount = 2
                            nMapOffset = 0
                            nScaleSourceNode = -1
                            flVolumetricSolveStrength = 1.0
                            vCenterOfMass = [ 0.0, 0.0, 0.0 ]
                        },
                    ]
                    """);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.VertexMaps.Count).IsEqualTo(1);
                await Assert.That(feModel.RecoverMassMultiplier(0)!.Value).IsEqualTo(1.5f).Within(1e-3f);
                await Assert.That(feModel.RecoverMassMultiplier(1)!.Value).IsEqualTo(1.5f).Within(1e-3f);
            }
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
        /// Six planarized planes around a sphere recover its centre (1, 2, 3) and radius 4.
        /// </summary>
        [Test]
        public async Task PlanarizedSphereRecoversItsCentreAndRadius()
        {
            var feModel = SyntheticCloth.Model(
                ["bone", "n0", "n1", "n2", "n3", "n4", "n5"], staticNodes: 0, parents: [-1, 0, 0, 0, 0, 0, 0],
                poses: [new(0f, 0f, 0f), new(7f, 2f, 3f), new(-5f, 2f, 3f), new(1f, 8f, 3f), new(1f, -4f, 3f), new(1f, 2f, 9f),
                    new(1f, 2f, -3f)],
                body: $$"""
                    m_CollisionPlanes =
                    [
                        {{Plane(1, "1.0, 0.0, 0.0", 5f)}}
                        {{Plane(2, "-1.0, 0.0, 0.0", 3f)}}
                        {{Plane(3, "0.0, 1.0, 0.0", 6f)}}
                        {{Plane(4, "0.0, -1.0, 0.0", 2f)}}
                        {{Plane(5, "0.0, 0.0, 1.0", 7f)}}
                        {{Plane(6, "0.0, 0.0, -1.0", 1f)}}
                    ]
                    m_VertexMapValues = [ 255, 255, 255, 255, 255, 255 ]
                    m_VertexMaps =
                    [
                        {
                            sName = "belt"
                            nNameHash = 1
                            nVertexBase = 1
                            nVertexCount = 6
                            nMapOffset = 0
                            nScaleSourceNode = -1
                            flVolumetricSolveStrength = 0.0
                            vCenterOfMass = [ 0.0, 0.0, 0.0 ]
                        },
                    ]
                    """);

            var shapes = feModel.BuildPlanarizeCapsules();

            await Assert.That(shapes.Count).IsEqualTo(1);

            using (Assert.Multiple())
            {
                await Assert.That(shapes[0].Planarize).IsTrue();
                await Assert.That(shapes[0].ParentBone).IsEqualTo("bone");
                await Assert.That(shapes[0].VertexMap).IsEqualTo("belt");
                await Assert.That(shapes[0].Radius0).IsEqualTo(4f).Within(1e-3f);
                await Assert.That(shapes[0].Radius1).IsEqualTo(4f).Within(1e-3f);
                await Assert.That(shapes[0].Point0.X).IsEqualTo(1f).Within(1e-3f);
                await Assert.That(shapes[0].Point0.Y).IsEqualTo(2f).Within(1e-3f);
                await Assert.That(shapes[0].Point0.Z).IsEqualTo(3f).Within(1e-3f);
                await Assert.That(Vector3.Distance(shapes[0].Point0, shapes[0].Point1)).IsLessThan(1e-3f);
            }
        }

        private static string Plane(int node, string normal, float offset)
            => $"{{ nCtrlParent = 0 nChildNode = {node} flStickiness = 0.0 flStrength = 0.0 "
                + $"m_Plane = {{ m_vNormal = [ {normal} ] m_flOffset = {SyntheticCloth.Num(offset)} }} }},";

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
        /// Goal strength is the cube root of <c>flAnimationForceAttraction</c>: 0.343 reads 0.7 and 0.008 reads 0.2.
        /// </summary>
        [Test]
        public async Task GoalStrengthIsTheCubeRootOfTheForceAttraction()
        {
            using (Assert.Multiple())
            {
                await Assert.That(FeModel.GoalStrengthFromAttraction(0.343f)).IsEqualTo(0.7f).Within(1e-5f);
                await Assert.That(FeModel.GoalStrengthFromAttraction(0.008f)).IsEqualTo(0.2f).Within(1e-5f);
                await Assert.That(FeModel.GoalStrengthFromAttraction(1f)).IsEqualTo(1f).Within(1e-6f);
            }
        }

        /// <summary>
        /// The damping comes back out of the force and vertex attraction pair: 0.343 with a vertex attraction of
        /// 0.370103 was authored at 0.01.
        /// </summary>
        [Test]
        public async Task GoalDampingInvertsTheAttractionSolve()
        {
            await Assert.That(FeModel.GoalDampingFromAttraction(0.343f, 0.370103f))
                .IsEqualTo(0.01f).Within(1e-4f);
        }

        /// <summary>
        /// Outside the solve range goal damping reads back unchanged.
        /// </summary>
        [Test]
        public async Task GoalDampingPassesThroughOutsideTheSolveRange()
        {
            using (Assert.Multiple())
            {
                await Assert.That(FeModel.GoalDampingFromAttraction(0.99995f, 0.42f)).IsEqualTo(0.42f);
                await Assert.That(FeModel.GoalDampingFromAttraction(0f, 0.42f)).IsEqualTo(0.42f);
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
        /// A stiff hinge's bend weights are <c>stiffness * [-1, 0.5, 0.5]</c> at equal inverse masses, and its height
        /// inverts to the angle: sqrt(4 - 4 cos 120) / 3 = 0.8164966.
        /// </summary>
        [Test]
        public async Task StiffHingeInvertsTheKelagerWeightSpread()
        {
            var hinge = KelagerModel("-1.0, 0.5, 0.5", 0.8164966f).GetStiffHinge(1);

            await Assert.That(hinge).IsNotNull();

            using (Assert.Multiple())
            {
                await Assert.That(hinge!.Value.Stiffness).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(hinge.Value.Angle).IsEqualTo(120f).Within(0.01f);
                await Assert.That(hinge.Value.MotionBias).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A bend with a zero mid weight reads a full motion bias, an end weight of 1.5 being a stiffness of 0.5.
        /// </summary>
        [Test]
        public async Task StiffHingeReadsAFullMotionBiasOffAZeroedMidWeight()
        {
            var hinge = KelagerModel("0.0, 1.5, 0.0", 0.8164966f).GetStiffHinge(1);

            await Assert.That(hinge).IsNotNull();

            using (Assert.Multiple())
            {
                await Assert.That(hinge!.Value.Stiffness).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(hinge.Value.MotionBias).IsEqualTo(1f);
            }
        }

        /// <summary>
        /// A height the rest pose already exceeds leaves no trace, and the angle recovers as zero.
        /// </summary>
        [Test]
        public async Task StiffHingeRecoversNoAngleBelowTheRestHeight()
        {
            var hinge = KelagerModel("-1.0, 0.5, 0.5", 0.5f).GetStiffHinge(1);

            await Assert.That(hinge!.Value.Angle).IsEqualTo(0f);
        }

        private static FeModel KelagerModel(string weights, float height) => SyntheticCloth.Model(
            ["mid", "end0", "end1"], staticNodes: 0, parents: [-1, 0, 0],
            poses: [new(0f, 1f, 0f), new(-1f, 0f, 0f), new(1f, 0f, 0f)],
            body: $$"""
                m_KelagerBends =
                [
                    { nNode = [ 0, 1, 2 ] flWeight = [ {{weights}} ] flHeight0 = {{SyntheticCloth.Num(height)}} },
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
        /// A solve-element quad too bent to keep whole gets a split rod across its longer diagonal.
        /// </summary>
        [Test]
        public async Task ABentQuadLosesItsLongerDiagonalToASplitRod()
        {
            Vector3[] corners =
            [
                new(0f, 0f, 0f),
                new(1f, 0f, 0f),
                new(1f, 1f, 1f),
                new(0f, 1f, 0f),
            ];

            var rods = FeModel.BentQuadRodsFromFaces([[0, 1, 2, 3]], corners, static _ => false, 0.05f);

            using (Assert.Multiple())
            {
                await Assert.That(rods.Count).IsEqualTo(1);
                await Assert.That(rods.Contains((0, 2))).IsTrue();
            }
        }

        /// <summary>
        /// A flat quad and a quad with a static corner yield no bent-quad split rod.
        /// </summary>
        [Test]
        public async Task AFlatOrPartlyStaticQuadKeepsBothDiagonals()
        {
            Vector3[] flat =
            [
                new(0f, 0f, 0f),
                new(1f, 0f, 0f),
                new(1f, 1f, 0f),
                new(0f, 1f, 0f),
            ];
            Vector3[] bent =
            [
                new(0f, 0f, 0f),
                new(1f, 0f, 0f),
                new(1f, 1f, 1f),
                new(0f, 1f, 0f),
            ];

            using (Assert.Multiple())
            {
                await Assert.That(FeModel.BentQuadRodsFromFaces([[0, 1, 2, 3]], flat, static _ => false, 0.05f))
                    .IsEmpty();
                await Assert.That(FeModel.BentQuadRodsFromFaces([[0, 1, 2, 3]], bent, static node => node == 3, 0.05f))
                    .IsEmpty();
            }
        }

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
        /// A vertex lists every selection covering it, with the weight where membership is below 1.
        /// </summary>
        [Test]
        public async Task VertexMapNamesCarryAPartialMembershipWeight()
        {
            var feModel = SyntheticCloth.Model(["a", "b"], staticNodes: 0, body: """
                    m_VertexMapValues = [ 255, 128 ]
                    m_VertexMaps =
                    [
                        {
                            sName = "skirt"
                            nNameHash = 1
                            nVertexBase = 0
                            nVertexCount = 2
                            nMapOffset = 0
                            nScaleSourceNode = -1
                            flVolumetricSolveStrength = 0.0
                            vCenterOfMass = [ 0.0, 0.0, 0.0 ]
                        },
                    ]
                    """);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.GetVertexMapNames(0)).IsEqualTo("skirt");
                await Assert.That(feModel.GetVertexMapNames(1)).Contains("skirt=");
                await Assert.That(feModel.VertexMapWeight("skirt", 1)).IsEqualTo(128f / 255f).Within(1e-6f);
                await Assert.That(FeModel.VertexMapName("skirt=0.5")).IsEqualTo("skirt");
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
        /// A planarized shape whose end caps coincide gets a 0.01 axis pointing away from the nodes it owns: here along
        /// -Z from the recovered centre (1, 2, 3).
        /// </summary>
        [Test]
        public async Task APlanarizedEndCapIsGivenAShortAxisAwayFromItsNodes()
        {
            var feModel = SyntheticCloth.Model(
                ["bone", "n0", "n1", "n2", "n3", "n4"], staticNodes: 0, parents: [-1, 0, 0, 0, 0, 0],
                poses: [new(0f, 0f, 0f), new(7f, 2f, 3f), new(-5f, 2f, 3f), new(1f, 8f, 3f), new(1f, -4f, 3f), new(1f, 2f, 9f)],
                body: $$"""
                    m_CollisionPlanes =
                    [
                        {{Plane(1, "1.0, 0.0, 0.0", 5f)}}
                        {{Plane(2, "-1.0, 0.0, 0.0", 3f)}}
                        {{Plane(3, "0.0, 1.0, 0.0", 6f)}}
                        {{Plane(4, "0.0, -1.0, 0.0", 2f)}}
                        {{Plane(5, "0.0, 0.0, 1.0", 7f)}}
                    ]
                    m_VertexMapValues = [ 255, 255, 255, 255, 255 ]
                    m_VertexMaps =
                    [
                        {
                            sName = "belt"
                            nNameHash = 1
                            nVertexBase = 1
                            nVertexCount = 5
                            nMapOffset = 0
                            nScaleSourceNode = -1
                            flVolumetricSolveStrength = 0.0
                            vCenterOfMass = [ 0.0, 0.0, 0.0 ]
                        },
                    ]
                    """);

            var shapes = feModel.BuildPlanarizeCapsules();

            await Assert.That(shapes.Count).IsEqualTo(1);

            using (Assert.Multiple())
            {
                await Assert.That(shapes[0].Radius0).IsEqualTo(4f).Within(1e-3f);
                await Assert.That(Vector3.Distance(shapes[0].Point0, new Vector3(1f, 2f, 3f)))
                    .IsLessThan(1e-3f);
                await Assert.That(Vector3.Distance(shapes[0].Point0, shapes[0].Point1))
                    .IsEqualTo(0.01f).Within(1e-4f);
                await Assert.That((shapes[0].Point1 - shapes[0].Point0).Z).IsLessThan(0f);
            }
        }

        /// <summary>
        /// A face over declared cloth nodes including a free <c>$cloth_node_</c> is no sheet face; a face over a sheet
        /// vertex is.
        /// </summary>
        [Test]
        public async Task ASurfaceFaceOverAFreeClothNodeIsNotASheetFace()
        {
            using (Assert.Multiple())
            {
                await Assert.That(FaceOverNode("$cloth_node_tie").BuildProxyMesh()).IsNull();
                await Assert.That(FaceOverNode("$cloth_m0p0").BuildProxyMesh()).IsNotNull();
            }
        }

        /// <summary>
        /// A deferred sheet vertex on a compile without <c>m_SkelParents</c> keeps the bones its offset network names:
        /// $cloth_m0p3 at 0.7 on bone_a and 0.3 on bone_c.
        /// </summary>
        [Test]
        public async Task AnUnanchoredSheetVertexKeepsItsOffsetNetworkPaint()
        {
            var feModel = DeferredOffsetSheet(skelParents: null);
            var proxies = feModel.BuildProxyMeshes();

            await Assert.That(proxies.Count).IsEqualTo(1);
            var influences = proxies[0].SkinInfluences[3];

            using (Assert.Multiple())
            {
                await Assert.That(feModel.DeferredOffsetSkinWeights.ContainsKey(7)).IsTrue();
                await Assert.That(influences.Length).IsEqualTo(2);
                await Assert.That(influences[0].Bone).IsEqualTo("bone_a");
                await Assert.That(influences[0].Weight).IsEqualTo(0.7f).Within(1e-4f);
                await Assert.That(influences[1].Bone).IsEqualTo("bone_c");
                await Assert.That(influences[1].Weight).IsEqualTo(0.3f).Within(1e-4f);
            }
        }

        /// <summary>
        /// With <c>m_SkelParents</c> the sheet vertex keeps the synthesised chain paint.
        /// </summary>
        [Test]
        public async Task ASheetVertexWithASkeletonAnchorKeepsTheSynthesisedPaint()
        {
            var feModel = DeferredOffsetSheet(skelParents: "[ -1, 0, 0, 0, 1, 1, 1, 1 ]");
            var influences = feModel.BuildProxyMeshes()[0].SkinInfluences[3];

            using (Assert.Multiple())
            {
                await Assert.That(feModel.DeferredOffsetSkinWeights.ContainsKey(7)).IsTrue();
                await Assert.That(influences.Length).IsNotEqualTo(0);
                await Assert.That(Array.Exists(influences, i => i.Bone == "bone_c")).IsFalse();
            }
        }

        /// <summary>
        /// A sheet whose fitless $cloth_m0p3 carries a soft offset onto a bone no other vertex anchors.
        /// </summary>
        private static FeModel DeferredOffsetSheet(string? skelParents) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "bone_a", "bone_b", "bone_c",
                               "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3" ]
                {{(skelParents is null ? "" : "m_SkelParents = " + skelParents)}}
                m_nNodeCount = 8
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                    {{SyntheticCloth.Pose(0f, 4f, -10f)}}
                    {{SyntheticCloth.Pose(4f, 4f, -10f)}}
                    {{SyntheticCloth.Pose(4f, 4f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 4f, -20f)}}
                ]
                m_Tris = [ { nNode = [ 4, 5, 6 ] }, { nNode = [ 4, 6, 7 ] } ]
                m_CtrlOffsets =
                [
                    { vOffset = [ 0.0, 4.0, 0.0 ] nCtrlParent = 2 nCtrlChild = 4 },
                    { vOffset = [ 4.0, 4.0, 0.0 ] nCtrlParent = 2 nCtrlChild = 5 },
                    { vOffset = [ 4.0, 4.0, 0.0 ] nCtrlParent = 2 nCtrlChild = 6 },
                    { vOffset = [ 0.0, 4.0, 0.0 ] nCtrlParent = 1 nCtrlChild = 7 },
                ]
                m_CtrlSoftOffsets =
                [
                    { nCtrlParent = 3 nCtrlChild = 7 vOffset = [ 0.0, 4.0, 0.0 ] flAlpha = 0.7 },
                ]
                m_FitMatrices =
                [
                    { bone = [ 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 ] vCenter = [ 0.0, 0.0, 0.0 ]
                      nEnd = 3 nNode = 2 nBeginDynamic = 0 },
                ]
                m_FitWeights =
                [
                    { flWeight = 0.5 nNode = 4 nDummy = 0 },
                    { flWeight = 0.5 nNode = 5 nDummy = 0 },
                    { flWeight = 0.5 nNode = 6 nDummy = 0 },
                ]
            }
            """);

        private static FeModel FaceOverNode(string third) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "bone_a", "bone_b", "{{third}}" ]
                m_SkelParents = [ -1, -1, -1 ]
                m_nNodeCount = 3
                m_nStaticNodes = 0
                m_NodeInvMasses = [ 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(4f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 3f, 0f)}}
                ]
                m_Tris = [ { nNode = [ 0, 1, 2 ] } ]
            }
            """);

        /// <summary>
        /// Anti-tunnel probes are declared in the order of their target slices in <c>m_AntiTunnelTargetNodes</c>, here
        /// the reverse of <c>m_AntiTunnelProbes</c>.
        /// </summary>
        [Test]
        public async Task AntiTunnelProbesAreDeclaredInTheOrderTheirTargetsAreConcatenated()
        {
            var children = KVObject.Array();
            ClothExtract.AddClothAntiTunnelProbes(children, SwappedAntiTunnelProbes(), null);

            using (Assert.Multiple())
            {
                await Assert.That(AntiTunnelSources(children))
                    .IsEquivalentTo(SwappedProbeSources, CollectionOrdering.Matching);
                await Assert.That(AntiTunnelTargets(children, 0))
                    .IsEquivalentTo(SwappedProbeFirstTargets, CollectionOrdering.Matching);
                await Assert.That(AntiTunnelTargets(children, 1))
                    .IsEquivalentTo(SwappedProbeSecondTargets, CollectionOrdering.Matching);
            }
        }

        /// <summary>
        /// A probe's targets keep their compiled slice order.
        /// </summary>
        [Test]
        public async Task AnAntiTunnelProbeKeepsTheSliceOrderOfItsTargets()
        {
            var children = KVObject.Array();
            ClothExtract.AddClothAntiTunnelProbes(children, ShuffledAntiTunnelTargets(), null);

            await Assert.That(AntiTunnelTargets(children, 0))
                .IsEquivalentTo(ShuffledProbeTargets, CollectionOrdering.Matching);
        }

        private static readonly string[] SwappedProbeSources = ["body", "tip"];
        private static readonly string[] SwappedProbeFirstTargets = ["a", "b", "c"];
        private static readonly string[] SwappedProbeSecondTargets = ["body"];
        private static readonly string[] ShuffledProbeTargets = ["c", "a", "b"];

        private static string[] AntiTunnelSources(KVObject children)
            => children.Select(static c => c.Value.GetStringProperty("source_node")).ToArray();

        private static string[] AntiTunnelTargets(KVObject children, int index)
            => children.ElementAt(index).Value.GetSubCollection("data").GetSubCollection("nodes")
                .Select(static n => n.Key).ToArray();

        /// <summary>Two probes whose target slices are laid out in the reverse of the probe order.</summary>
        private static FeModel SwappedAntiTunnelProbes() => SyntheticCloth.Model(
            ["root", "a", "b", "c", "body", "tip"], staticNodes: 1, parents: [-1, 0, 0, 0, 0, 0],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(0f, 4f, -30f), new(0f, 8f, -30f)],
            body: """
                m_AntiTunnelTargetNodes = [ 1, 2, 3, 4 ]
                m_AntiTunnelProbes =
                [
                    { flWeight = 1.0 nFlags = 1 nProbeNode = 5 nCount = 1 nBegin = 3
                      flActivationDistance = 1.0 flCurvatureRadius = 0.0 flBias = 0.0 },
                    { flWeight = 1.0 nFlags = 0 nProbeNode = 4 nCount = 3 nBegin = 0
                      flActivationDistance = 1.0 flCurvatureRadius = 0.0 flBias = 0.0 },
                ]
                """);

        /// <summary>One probe whose target slice is not in ascending node order.</summary>
        private static FeModel ShuffledAntiTunnelTargets() => SyntheticCloth.Model(
            ["root", "a", "b", "c", "body"], staticNodes: 1, parents: [-1, 0, 0, 0, 0],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(0f, 4f, -30f)],
            body: """
                m_AntiTunnelTargetNodes = [ 3, 1, 2 ]
                m_AntiTunnelProbes =
                [
                    { flWeight = 1.0 nFlags = 0 nProbeNode = 4 nCount = 3 nBegin = 0
                      flActivationDistance = 1.0 flCurvatureRadius = 0.0 flBias = 0.0 },
                ]
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

        private static string OneWideRopeDocument(string nodeBase) => SyntheticCloth.Document(
            ["root", "j1", "$ccj1_0", "j2", "$ccj2_0", "j3", "$ccj3_0"], staticNodes: 1, parents: [-1, 0, 1, 1, 3, 3, 5],
            poses: [new(0f, 0f, 10f), new(0f, 0f, 0f), new(3f, 0f, 0f), new(0f, 0f, -10f), new(3f, 0f, -10f), new(0f, 0f, -20f),
                new(3f, 0f, -20f)],
            body: $$"""
                m_SourceElems = [ 0, 0, 0, 2, 1, 2, 4, 3, 3, 4, 6, 5 ]
                m_NodeBases = [ { {{nodeBase}} } ]
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

        /// <summary>
        /// A pinned sheet vertex whose soft offset ties its anchor with another bone keeps both influences, the anchor
        /// first.
        /// </summary>
        [Test]
        public async Task ATiedPinnedSheetVertexKeepsBothItsBones()
        {
            var influences = TiedPinSheet().BuildProxyMeshes()[0].SkinInfluences[0];

            using (Assert.Multiple())
            {
                await Assert.That(influences.Length).IsEqualTo(2);
                await Assert.That(influences[0].Bone).IsEqualTo("bone_a");
                await Assert.That(influences[1].Bone).IsEqualTo("bone_c");
                await Assert.That(influences[0].Weight).IsGreaterThanOrEqualTo(influences[1].Weight);
            }
        }

        /// <summary>
        /// A sheet whose pinned $cloth_m0p0 has no fit weight and one soft offset at 0.5, tying bone_a and bone_c.
        /// </summary>
        private static FeModel TiedPinSheet() => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "$cloth_m0p0", "root", "bone_a", "bone_b", "bone_c",
                               "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3" ]
                m_SkelParents = [ 2, -1, 1, 1, 1, 3, 3, 3 ]
                m_nNodeCount = 8
                m_nStaticNodes = 2
                m_NodeInvMasses = [ 0.0, 0.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 4f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                    {{SyntheticCloth.Pose(4f, 4f, -10f)}}
                    {{SyntheticCloth.Pose(4f, 4f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 4f, -20f)}}
                ]
                m_Tris = [ { nNode = [ 0, 5, 6 ] }, { nNode = [ 0, 6, 7 ] } ]
                m_CtrlOffsets =
                [
                    { vOffset = [ 0.0, 4.0, 0.0 ] nCtrlParent = 2 nCtrlChild = 0 },
                    { vOffset = [ 4.0, 4.0, 0.0 ] nCtrlParent = 3 nCtrlChild = 5 },
                    { vOffset = [ 4.0, 4.0, 0.0 ] nCtrlParent = 3 nCtrlChild = 6 },
                    { vOffset = [ 0.0, 4.0, 0.0 ] nCtrlParent = 3 nCtrlChild = 7 },
                ]
                m_CtrlSoftOffsets =
                [
                    { nCtrlParent = 4 nCtrlChild = 0 vOffset = [ 0.0, 4.0, 0.0 ] flAlpha = 0.5 },
                ]
                m_FitMatrices =
                [
                    { bone = [ 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 ] vCenter = [ 0.0, 0.0, 0.0 ]
                      nEnd = 3 nNode = 3 nBeginDynamic = 0 },
                ]
                m_FitWeights =
                [
                    { flWeight = 0.5 nNode = 5 nDummy = 0 },
                    { flWeight = 0.5 nNode = 6 nDummy = 0 },
                    { flWeight = 0.5 nNode = 7 nDummy = 0 },
                ]
            }
            """);

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

        private static FeModel.BoneChain TwistedRopeChain()
        {
            var chain = new FeModel.BoneChain { RootBone = "j1" };
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1 });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 3, Name = "j3", ParentNode = 2, InvMass = 1f });
            return chain;
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
        /// With <c>rigid_edge_hinges</c> the curvature is read off the ring bends' heights: none means zero, a folded
        /// hub inverts to its angle, and a saturated or disagreeing set reads 1.
        /// </summary>
        [Test]
        public async Task RigidHingeSheetReadsItsCurvatureFromTheRingBends()
        {
            using (Assert.Multiple())
            {
                await Assert.That(RigidSheet(bends: "").RigidHingeCurvature).IsEqualTo(0f);
                await Assert.That(RigidSheet(bends: Bend(4.714045f)).RigidHingeCurvature)
                    .IsEqualTo(0.5f).Within(0.001f);
                await Assert.That(RigidSheet(bends: Bend(6.666667f)).RigidHingeCurvature)
                    .IsEqualTo(0f).Within(0.001f);
                await Assert.That(RigidSheet(bends: Bend(0.1f)).RigidHingeCurvature).IsEqualTo(1f);
                await Assert.That(RigidSheet(bends: Bend(0f)).RigidHingeCurvature).IsEqualTo(1f);
                await Assert.That(RigidSheet(bends: Bend(4.714045f) + Bend(6.666667f)).RigidHingeCurvature)
                    .IsEqualTo(1f);
            }
        }

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
        /// <c>goal_strength_bias</c> is the constant cube-root gap between the goal-damped nodes' force and vertex
        /// attractions; raw-integrator nodes state none, and the paint takes the bias back out below saturation.
        /// </summary>
        [Test]
        public async Task GoalStrengthBiasIsTheCubeRootGapTheGoalDampedNodesShare()
        {
            using (Assert.Multiple())
            {
                await Assert.That(BiasedGoals(bias: 0.02f, flags: "128").GoalStrengthBias)
                    .IsEqualTo(0.02f).Within(0.0001f);
                await Assert.That(BiasedGoals(bias: 0f, flags: "128").GoalStrengthBias).IsEqualTo(0f);
                await Assert.That(BiasedGoals(bias: 0.02f, flags: "1024").GoalStrengthBias).IsEqualTo(0f);

                var biased = BiasedGoals(bias: 0.02f, flags: "128");
                await Assert.That(biased.GoalStrengthPaint(0.140608f)).IsEqualTo(0.5f).Within(0.0005f);
                await Assert.That(biased.GoalStrengthPaint(1f)).IsEqualTo(1f);
                await Assert.That(biased.GoalDampingPaint(1f, 0f))
                    .IsEqualTo(FeModel.GoalDampingFromAttraction(1f, 0f));
            }
        }

        /// <summary>
        /// Ten goal nodes whose force attraction is <c>(g + bias)^3</c> against a vertex attraction of
        /// <c>g^3</c>, with <paramref name="flags"/> as the dynamic band's own mode word (128 = goal
        /// damped, 1024 = raw).
        /// </summary>
        private static FeModel BiasedGoals(float bias, string flags)
        {
            var poses = new System.Text.StringBuilder();
            var integrators = new System.Text.StringBuilder();
            poses.Append(SyntheticCloth.Pose(0f, 0f, 0f));
            integrators.Append("{ flPointDamping = 0.0 flAnimationForceAttraction = 0.0 "
                + "flAnimationVertexAttraction = 0.0 flGravity = 360.0 },");
            var strengths = new[] { 0.5f, 0.4f, 0.3f, 0.35f, 0.5f, 0.4f, 0.3f, 0.35f, 0.45f, 0.25f };
            for (var i = 0; i < strengths.Length; i++)
            {
                var g = strengths[i];
                var force = (g + bias) * (g + bias) * (g + bias);
                poses.Append(SyntheticCloth.Pose(0f, 0f, -1f * (i + 1)));
                integrators.Append(CultureInfo.InvariantCulture, $"{{ flPointDamping = 0.0 flAnimationForceAttraction = {SyntheticCloth.Num(force)} "
                    + $"flAnimationVertexAttraction = {SyntheticCloth.Num(g * g * g)} flGravity = 360.0 }},");
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_nNodeCount = 11
                    m_nStaticNodes = 1
                    m_nDynamicNodeFlags = {{flags}}
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                    m_InitPose = [ {{poses}} ]
                    m_NodeIntegrator = [ {{integrators}} ]
                }
                """);
        }

        private static string Bend(float height)
            => $"{{ nNode = [ 1, 2, 3 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = {SyntheticCloth.Num(height)} }},";

        /// <summary>
        /// A sheet marked rigid-hinged by its axial edges, whose one hub (node 1) sits between two ring
        /// members (nodes 2 and 3) ten units away on opposite sides, so the hub's own rest distance from
        /// their centroid is zero and every fold it can record still tracks the angle.
        /// </summary>
        private static FeModel RigidSheet(string bends) => SyntheticCloth.Model(
            ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"], staticNodes: 1,
            poses: [new(0f, 0f, 0f), new(0f, 0f, 0f), new(10f, 0f, 0f), new(-10f, 0f, 0f)],
            body: $$"""
                m_AxialEdges = [ { nNode = [ 1, 2, 3, 3, 2, 1 ] }, ]
                m_KelagerBends = [ {{bends}} ]
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

        private static FeModel StretchlessChain(string rods) => SyntheticCloth.Model(
            ["j0", "j1", "j2", "j3"], staticNodes: 0, parents: [-1, 0, 1, 2],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f)],
            body: $$"""
                m_nFirstPositionDrivenNode = 4
                m_Rods =
                [
                    {{rods}}
                ]
                """);

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
        /// A selection covering the union of a sheet's exported islands is still the sheet's container.
        /// </summary>
        [Test]
        public async Task ASelectionOverSeveralExportedProxiesIsStillTheSheetsContainer()
        {
            var model = SplitSheet();
            var islands = new[] { Island([0, 1]), Island([2, 3]) };

            using (Assert.Multiple())
            {
                await Assert.That(model.GetProxyVertexMapName(islands[0], islands)).IsEqualTo("sheet");
                await Assert.That(model.GetProxyVertexMapName(islands[1], islands)).IsEqualTo("sheet");
                await Assert.That(model.GetProxyVertexMapName(islands[0])).IsNull();
            }
        }

        private static FeModel.ProxyMesh Island(int[] nodes) => SyntheticCloth.Proxy(nodes, [.. nodes.Select(static _ => 1f)], []);

        private static FeModel SplitSheet() => SyntheticCloth.Parse("""
            {
                m_CtrlName = [ "v0", "v1", "v2", "v3" ]
                m_nNodeCount = 4
                m_nStaticNodes = 0
                m_VertexMaps =
                [
                    { sName = "sheet" nNameHash = 1 nColor = 0 nFlags = 0 nVertexBase = 0
                      nVertexCount = 4 nMapOffset = 0 nNodeListOffset = 0
                      vCenterOfMass = [ 0.0, 0.0, 0.0 ] flVolumetricSolveStrength = 0.0
                      nScaleSourceNode = -1 },
                ]
                m_VertexMapValues = [ 255, 255, 255, 255 ]
                m_VertexSetNames = [  ]
            }
            """);

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
        /// On a three-wide ring whose sides each carry one rod, the chain's geometric masses match the shipped ones and
        /// no joint states a mass multiplier.
        /// </summary>
        [Test]
        public async Task AThreeWideRingWeighsItsOwnSidesWhenEachCarriesOneRod()
        {
            var feModel = RingThreeWideChain();

            using (Assert.Multiple())
            {
                foreach (var joint in (int[])[13, 14, 15])
                {
                    await Assert.That(feModel.RecoverJointMassMultiplier(joint)!.Value).IsEqualTo(1f).Within(1e-3f);
                    await Assert.That(feModel.RecoverJointMass(joint, 1f)).IsNull();
                }
            }
        }

        private static FeModel RingThreeWideChain() => SyntheticCloth.Load("cloth_chain_three_wide_ring.kv3");

        /// <summary>
        /// A selection whose covered nodes share one partial value carries it as the container weight; full coverage or
        /// mixed values carry none.
        /// </summary>
        [Test]
        public async Task AContainerWeightIsTheOnePartialWeightItsSelectionShares()
        {
            using (Assert.Multiple())
            {
                await Assert.That(WeightedSheet("128, 128, 128, 128").UniformVertexMapWeight("sheet")!.Value)
                    .IsEqualTo(128f / 255f).Within(1e-4f);
                await Assert.That(WeightedSheet("255, 255, 255, 255").UniformVertexMapWeight("sheet")).IsNull();
                await Assert.That(WeightedSheet("128, 255, 128, 255").UniformVertexMapWeight("sheet")).IsNull();
            }
        }

        private static FeModel WeightedSheet(string values) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "v0", "v1", "v2", "v3" ]
                m_nNodeCount = 4
                m_nStaticNodes = 0
                m_VertexMaps =
                [
                    { sName = "sheet" nNameHash = 1 nColor = 0 nFlags = 0 nVertexBase = 0
                      nVertexCount = 4 nMapOffset = 0 nNodeListOffset = 0
                      vCenterOfMass = [ 0.0, 0.0, 0.0 ] flVolumetricSolveStrength = 0.0
                      nScaleSourceNode = -1 },
                ]
                m_VertexMapValues = [ {{values}} ]
                m_VertexSetNames = [  ]
            }
            """);

        /// <summary>
        /// A quad over declared cloth nodes in a model with no sheet node is an authored <c>ClothQuad</c>; beside a
        /// sheet node it stays with the sheet.
        /// </summary>
        [Test]
        public async Task AQuadOverDeclaredNodesInASheetlessModelIsAnAuthoredElement()
        {
            using (Assert.Multiple())
            {
                await Assert.That(QuadOverBones("").GetAuthoredElementFaces().Count).IsEqualTo(1);
                await Assert.That(QuadOverBones(", \"$cloth_m0p0\"").GetAuthoredElementFaces().Count).IsEqualTo(0);
            }
        }

        private static FeModel QuadOverBones(string extraName) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "a", "b", "c", "d"{{extraName}} ]
                m_nNodeCount = 4
                m_nStaticNodes = 0
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(3f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(3f, 4f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 4f, 0f)}}
                ]
                m_Quads = [ { nNode = [ 0, 1, 2, 3 ] } ]
            }
            """);

        /// <summary>
        /// An <c>explicit_masses</c> chain is read off its mass-proportional rod weights: the mass 0.5 joint reads 0.5
        /// and its neighbour 1, while flat 0.5 weights read as a geometric chain.
        /// </summary>
        [Test]
        public async Task AnExplicitMassChainIsReadOffItsMassProportionalRodWeights()
        {
            var explicitChain = SyntheticCloth.Parse(ExplicitMassChainText);
            var flatChain = SyntheticCloth.Parse(ExplicitMassChainText
                .Replace("flWeight0 = 0.333333", "flWeight0 = 0.5", StringComparison.Ordinal)
                .Replace("flWeight0 = 0.666667", "flWeight0 = 0.5", StringComparison.Ordinal));

            using (Assert.Multiple())
            {
                await Assert.That(explicitChain.HasExplicitMasses).IsTrue();
                await Assert.That(explicitChain.RecoverJointMassMultiplier(4)!.Value).IsEqualTo(0.5f).Within(1e-4f);

                await Assert.That(explicitChain.RecoverJointMassMultiplier(2)!.Value).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(explicitChain.RecoverJointMass(2, 1f)).IsNull();
                await Assert.That(flatChain.HasExplicitMasses).IsFalse();
            }
        }

        private static string ExplicitMassChainText => SyntheticCloth.Fixture("cloth_chain_explicit_mass.kv3");

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
        /// <c>explicit_masses</c> is read past a span a <c>motion_bias</c> flattens, as long as another span stays
        /// proportional.
        /// </summary>
        [Test]
        public async Task ExplicitMassesAreReadPastASpanAMotionBiasMoves()
        {
            var biased = SyntheticCloth.Parse(ExplicitBiasedChainText);
            var flat = SyntheticCloth.Parse(ExplicitBiasedChainText.Replace("flWeight0 = 0.333333", "flWeight0 = 0.5", StringComparison.Ordinal));

            using (Assert.Multiple())
            {
                await Assert.That(biased.HasExplicitMasses).IsTrue();
                await Assert.That(biased.RecoverJointMassMultiplier(4)!.Value).IsEqualTo(2f).Within(1e-3f);
                await Assert.That(flat.HasExplicitMasses).IsFalse();
            }
        }

        private static string ExplicitBiasedChainText => SyntheticCloth.Fixture("cloth_chain_explicit_mass_bias.kv3");

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
        /// Under damping, <c>goal_strength_bias</c> is the largest cube-root gap three nodes share, and a
        /// zero-attraction node paints at minus its cube root.
        /// </summary>
        [Test]
        public async Task GoalStrengthBiasIsTheLargestGapThreeNodesShareUnderDamping()
        {
            var sheet = DampedBiasedGoals(bias: 0.25f, undamped: 3);

            using (Assert.Multiple())
            {
                await Assert.That(sheet.GoalStrengthBias).IsEqualTo(0.25f).Within(0.0002f);
                await Assert.That(DampedBiasedGoals(bias: 0.25f, undamped: 2).GoalStrengthBias).IsEqualTo(0f);
                await Assert.That(sheet.GoalStrengthPaint(0f)).IsEqualTo(-MathF.Cbrt(sheet.GoalStrengthBias));
                await Assert.That(float.IsFinite(sheet.GoalDampingPaint(0f, 0f))).IsTrue();
            }
        }

        private static FeModel DampedBiasedGoals(float bias, int undamped)
        {
            var poses = new System.Text.StringBuilder();
            var integrators = new System.Text.StringBuilder();
            poses.Append(SyntheticCloth.Pose(0f, 0f, 0f));
            integrators.Append("{ flPointDamping = 0.0 flAnimationForceAttraction = 0.0 "
                + "flAnimationVertexAttraction = 0.0 flGravity = 360.0 },");
            var strengths = new[] { 0.3f, 0.4f, 0.5f, 0.35f, 0.45f, 0.55f, 0.6f, 0.38f, 0.48f, 0.58f };
            var dampedGaps = new[] { 0.05f, 0.08f, 0.11f, 0.14f, 0.17f, 0.2f, 0.23f, 0.02f, 0.035f, 0.065f };
            for (var i = 0; i < strengths.Length; i++)
            {
                var g = strengths[i];
                var force = (g + bias) * (g + bias) * (g + bias);
                var vertexRoot = i < undamped ? g : g + bias - dampedGaps[i];
                poses.Append(SyntheticCloth.Pose(0f, 0f, -1f * (i + 1)));
                integrators.Append(CultureInfo.InvariantCulture, $"{{ flPointDamping = 0.0 flAnimationForceAttraction = {SyntheticCloth.Num(force)} "
                    + $"flAnimationVertexAttraction = {SyntheticCloth.Num(vertexRoot * vertexRoot * vertexRoot)} flGravity = 360.0 }},");
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_nNodeCount = 11
                    m_nStaticNodes = 1
                    m_nDynamicNodeFlags = 128
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                    m_InitPose = [ {{poses}} ]
                    m_NodeIntegrator = [ {{integrators}} ]
                }
                """);
        }

        /// <summary>
        /// An effect declares <c>cloth_effect_version</c> from its <c>Version</c> parameter, and none without it.
        /// </summary>
        [Test]
        public async Task AClothEffectVersionIsItsVersionParameter()
        {
            var versioned = ClothWindEffect("Version = 2");
            var unversioned = ClothWindEffect(string.Empty);
            var maps = new HashSet<string>();
            var node = ClothExtract.MakeClothEffect(versioned, versioned.Effects.First(), maps);
            var plain = ClothExtract.MakeClothEffect(unversioned, unversioned.Effects.First(), maps);

            using (Assert.Multiple())
            {
                await Assert.That(node!.GetInt32Property("cloth_effect_version")).IsEqualTo(2);
                await Assert.That(plain!.ContainsKey("cloth_effect_version")).IsFalse();
            }
        }

        private static FeModel ClothWindEffect(string version) => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 1
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0 ]
                m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} ]
                m_Effects =
                [
                    {
                        sName = "wind0"
                        nNameHash = 1456486
                        nType = 1
                        m_Params =
                        {
                            Strength = [ 70.400002, 0.0, 0.0 ]
                            AirToCloth = 0.249439
                            LocalSpace = 0.0
                            Choppiness = 1.0
                            Vortices = [ { MaxSpeed = 123.200005 MaxCell = 32.0 } ]
                            {{version}}
                        }
                    },
                ]
            }
            """);

        /// <summary>
        /// A <c>leader_type</c> 1 follower names the known bone whose string token is its parent hash; an unmatched
        /// hash declares nothing.
        /// </summary>
        [Test]
        public async Task ABoneMergeFollowerNamesTheBoneWhoseTokenIsItsParentHash()
        {
            var feModel = SyntheticCloth.Model(
                ["coattail_0_L", "coattail_1_L", "coattail_2_L"], staticNodes: 1, parents: [-1, 0, 1],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                body: $$"""
                    m_BoneMergeLinks =
                    [
                        { m_nParentHash = {{ValveResourceFormat.Utils.StringToken.Get("spine_2")}} m_nChildNode = 2 },
                        { m_nParentHash = 12345 m_nChildNode = 1 },
                    ]
                    """);
            feModel.SkeletonBoneNames = new HashSet<string>(["pelvis", "spine_2", "coattail_0_L", "coattail_1_L", "coattail_2_L"],
                StringComparer.OrdinalIgnoreCase);
            var children = KVObject.Array();
            ClothExtract.AddClothFollowBones(children, feModel,
                new HashSet<string>(["coattail_1_L", "coattail_2_L"], StringComparer.OrdinalIgnoreCase));

            await Assert.That(children.Count).IsEqualTo(1);
            var follow = children.ElementAt(0).Value;

            using (Assert.Multiple())
            {
                await Assert.That(follow.GetInt32Property("leader_type")).IsEqualTo(1);
                await Assert.That(follow.GetStringProperty("leader_bone")).IsEqualTo("spine_2");
                await Assert.That(follow.GetStringProperty("follower_bone")).IsEqualTo("coattail_2_L");
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

        private static string SuspenderAtNaturalRelaxationText => SyntheticCloth.Fixture("cloth_chain_natural_suspender.kv3");

        /// <summary>
        /// Selections over the same nodes at the same weights are aliases of one container; other weights and
        /// registered vertex sets are not.
        /// </summary>
        [Test]
        public async Task SelectionsOverTheSameNodesAndWeightsAreAliasesOfOneContainer()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_nNodeCount = 3
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    ]
                    m_VertexMaps =
                    [
                        {{VertexMapEntry("alias0", 1548087834, 0, 1, 2)}}
                        {{VertexMapEntry("alias1", 2540411548, 0, 1, 2)}}
                        {{VertexMapEntry("half", 7, 2, 1, 2)}}
                        {{VertexMapEntry("painted", 99, 0, 1, 2)}}
                    ]
                    m_VertexMapValues = [ 255, 255, 255, 128 ]
                    m_VertexSetNames = [ 99 ]
                }
                """);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.VertexMapAliases("alias1")).IsEquivalentTo(AliasPair, CollectionOrdering.Matching);
                await Assert.That(feModel.VertexMapAliases("half")).IsEquivalentTo(HalfOnly, CollectionOrdering.Matching);
                await Assert.That(feModel.VertexMapAliases("missing").Count).IsEqualTo(0);
            }
        }

        private static readonly string[] AliasPair = ["alias0", "alias1"];
        private static readonly string[] HalfOnly = ["half"];

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
        /// A stiffen effect declares its <c>BoneOverlay</c> parameter beside <c>Stiffness</c>, and none without it.
        /// </summary>
        [Test]
        public async Task AStiffenEffectsBoneOverlayIsItsOwnParameter()
        {
            var overlaid = ClothStiffenEffect("BoneOverlay = 0.5");
            var plain = ClothStiffenEffect(string.Empty);
            var maps = new HashSet<string>();
            var node = ClothExtract.MakeClothEffect(overlaid, overlaid.Effects.First(), maps);
            var bare = ClothExtract.MakeClothEffect(plain, plain.Effects.First(), maps);

            using (Assert.Multiple())
            {
                await Assert.That(node!.GetFloatProperty("BoneOverlay")).IsEqualTo(0.5f);
                await Assert.That(node!.GetFloatProperty("Stiffness")).IsEqualTo(2f);
                await Assert.That(bare!.ContainsKey("BoneOverlay")).IsFalse();
            }
        }

        private static FeModel ClothStiffenEffect(string overlay) => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 1
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0 ]
                m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} ]
                m_Effects =
                [
                    {
                        sName = "stiffen0"
                        nNameHash = 1
                        nType = 3
                        m_Params =
                        {
                            Stiffness = 2.0
                            {{overlay}}
                        }
                    },
                ]
            }
            """);

        /// <summary>
        /// <c>stiffness_on_ragdoll</c> and <c>cloth_sleep_enabled</c> come back from the model's key values, and
        /// neither is declared without them.
        /// </summary>
        [Test]
        public async Task SoftbodyKeysComeBackFromTheModelKeyValues()
        {
            var keyValues = KVObject.Collection();
            keyValues.Add("cloth_stiffness_on_ragdoll", 0.5f);
            keyValues.Add("cloth_sleep_enabled", true);
            var softbody = KVObject.Collection();
            ClothExtract.AddSoftbodyModelKeyValues(softbody, keyValues);
            var bare = KVObject.Collection();
            ClothExtract.AddSoftbodyModelKeyValues(bare, KVObject.Collection());

            using (Assert.Multiple())
            {
                await Assert.That(softbody.GetFloatProperty("stiffness_on_ragdoll")).IsEqualTo(0.5f);
                await Assert.That(softbody.GetBooleanProperty("cloth_sleep_enabled")).IsTrue();
                await Assert.That(bare.Count).IsEqualTo(0);
            }
        }

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
        /// Capsules sorted into priority groups are declared in their parent bones' node order; without groups the
        /// array order is kept.
        /// </summary>
        [Test]
        public async Task ShapesSortedIntoPriorityGroupsAreDeclaredInTheirParentBonesOrder()
        {
            const string Groups = "m_RigidColliderPriorities = [ "
                + "{ m_nTaperedCapsuleRigidIndex = 0 m_nSphereRigidIndex = 0 m_nBoxRigidIndex = 0 m_nSDFRigidIndex = 0 m_nCollisionPlaneIndex = 0 }, "
                + "{ m_nTaperedCapsuleRigidIndex = 1 m_nSphereRigidIndex = 0 m_nBoxRigidIndex = 0 m_nSDFRigidIndex = 0 m_nCollisionPlaneIndex = 0 }, "
                + "{ m_nTaperedCapsuleRigidIndex = 2 m_nSphereRigidIndex = 0 m_nBoxRigidIndex = 0 m_nSDFRigidIndex = 0 m_nCollisionPlaneIndex = 0 } ]";
            var grouped = ClothExtract.AddClothCollisionShapes(KVObject.Array(), PriorityCapsules(Groups));
            var ungrouped = ClothExtract.AddClothCollisionShapes(KVObject.Array(), PriorityCapsules("m_RigidColliderPriorities = [ ]"));

            using (Assert.Multiple())
            {
                await Assert.That(grouped).IsEquivalentTo(DeclaredCapsules, CollectionOrdering.Matching);
                await Assert.That(ungrouped).IsEquivalentTo(ArrayOrderCapsules, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] DeclaredCapsules = ["spine_2_clothCapsule", "pelvis_clothCapsule"];
        private static readonly string[] ArrayOrderCapsules = ["pelvis_clothCapsule", "spine_2_clothCapsule"];

        private static FeModel PriorityCapsules(string groups) => SyntheticCloth.Model(
            ["spine_2", "pelvis", "coattail_0_L"], staticNodes: 3, parents: [-1, -1, -1],
            poses: [new(0f, 0f, 40f), new(0f, 0f, 30f), new(-8f, 4f, 65f)],
            body: $$"""
                m_TaperedCapsuleRigids =
                [
                    { vSphere = [ [ 0.0, 0.0, -4.0, 4.0 ], [ 0.0, 0.0, 4.0, 4.0 ] ] nNode = 1 nCollisionMask = 15 nVertexMapIndex = 65535 nFlags = 0 },
                    { vSphere = [ [ 0.0, 0.0, -4.0, 4.0 ], [ 0.0, 0.0, 4.0, 4.0 ] ] nNode = 0 nCollisionMask = 15 nVertexMapIndex = 65535 nFlags = 0 },
                ]
                {{groups}}
                """);

        private static string VertexMapEntry(string name, uint hash, int offset, int vertexBase, int count, float volumetric = 0f)
            => $"{{ sName = \"{name}\" nNameHash = {hash} nVertexBase = {vertexBase} nVertexCount = {count} nMapOffset = {offset} "
                + $"vCenterOfMass = [ 0.0, 0.0, 0.0 ] flVolumetricSolveStrength = {SyntheticCloth.Num(volumetric)} nScaleSourceNode = -1 }},";

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

        private static FeModel TwistPair(string toChild, string toRoot) => SyntheticCloth.Model(
            ["coattail_0_L", "coattail_1_L"], staticNodes: 1, parents: [-1, 0], body: $$"""
                m_Twists =
                [
                    { nNodeOrient = 0 nNodeEnd = 1 flTwistRelax = {{toChild}} flSwingRelax = 1.0 },
                    { nNodeOrient = 1 nNodeEnd = 0 flTwistRelax = {{toRoot}} flSwingRelax = 0.0 },
                ]
                """);

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
        /// The exported skeleton puts every cloth control bone on its <c>m_InitPose</c> position, where the compiled
        /// bone table alone does not.
        /// </summary>
        [Test]
        public async Task AClothControlBoneIsEmittedOnItsRecordedRestPosition()
        {
            var (corrected, compiled) = RestPoseDistances("sw_donkey_10th_anniversary_kv3_v3_zstd");

            using (Assert.Multiple())
            {
                await Assert.That(corrected).IsLessThan(1e-4f);
                await Assert.That(compiled).IsGreaterThan(1e-3f);
            }
        }

        /// <summary>
        /// A model with no cloth registers no rest-position correction.
        /// </summary>
        [Test]
        public async Task AModelWithoutClothEmitsItsCompiledSkeleton()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "townsfolk_03.vmdl_c"));
            var extract = new ModelExtract(resource, new NullFileLoader());

            using (Assert.Multiple())
            {
                await Assert.That(((Model)resource.DataBlock!).Skeleton.Bones.Length).IsEqualTo(56);
                await Assert.That(extract.Cloth.RestBonePositions.Count).IsEqualTo(0);
                await Assert.That(extract.Cloth.ProxyRestBonePositions.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// The worst distance between a real cloth control bone's <c>m_InitPose</c> and the world position
        /// its emitted joint chain accumulates to, with the rest-pose correction and with the compiled
        /// bone transforms.
        /// </summary>
        private static (float Corrected, float Compiled) RestPoseDistances(string modelName)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", modelName + ".vmdl_c"));
            var model = (Model)resource.DataBlock!;
            var extract = new ModelExtract(resource, new NullFileLoader());
            var feModel = model.GetEmbeddedPhys()!.FeModel!;

            var targets = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            for (var node = 0; node < feModel.CtrlNames.Length && node < feModel.InitPosePositions.Length; node++)
            {
                var name = feModel.CtrlNames[node];
                if (!string.IsNullOrEmpty(name) && !feModel.IsGeneratedNodeName(name))
                {
                    targets.TryAdd(name, feModel.InitPosePositions[node]);
                }
            }

            var corrected = 0f;
            var compiled = 0f;
            void Walk(Bone bone, Vector3 correctedParent, Vector3 compiledParent, Quaternion parentRotation)
            {
                var here = correctedParent
                    + Vector3.Transform(ModelExtract.BonePosition(bone, extract.Cloth.RestBonePositions), parentRotation);
                var asCompiled = compiledParent + Vector3.Transform(bone.Position, parentRotation);
                var rotation = parentRotation * bone.Angle;

                if (targets.TryGetValue(bone.Name, out var target))
                {
                    corrected = Math.Max(corrected, Vector3.Distance(here, target));
                    compiled = Math.Max(compiled, Vector3.Distance(asCompiled, target));
                }

                foreach (var child in bone.Children)
                {
                    Walk(child, here, asCompiled, rotation);
                }
            }

            foreach (var root in model.Skeleton.Roots)
            {
                Walk(root, Vector3.Zero, Vector3.Zero, Quaternion.Identity);
            }

            return (corrected, compiled);
        }

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
        /// A banded rod beside a chain's rigid span is re-declared as a two-member cluster at half its band per member;
        /// two banded copies stay springs.
        /// </summary>
        [Test]
        public async Task AClusterTieBesideAChainSpanIsItsTwoMemberCluster()
        {
            var tiedModel = ClusterTieChain(1);
            var tied = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(tied, tiedModel, tiedModel.BuildBoneChains());
            var doubledModel = ClusterTieChain(2);
            var doubled = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(doubled, doubledModel, doubledModel.BuildBoneChains());

            await Assert.That(tied.Count).IsEqualTo(1);
            var cluster = tied.ElementAt(0).Value;
            var joints = cluster.GetSubCollection("chain").GetArray("joints")!.ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(cluster.GetStringProperty("_class")).IsEqualTo("ClothSelfCollisionCluster");
                await Assert.That(joints.Select(static joint => joint.GetStringProperty("joint_name")).ToArray())
                    .IsEquivalentTo(ClusterTieMembers, CollectionOrdering.Matching);
                await Assert.That(joints[0].GetFloatProperty("collision_radius")).IsEqualTo(6f);
                await Assert.That(joints[0].GetFloatProperty("stray_radius")).IsEqualTo(24f);
                await Assert.That(doubled.Select(static child => child.Value.GetStringProperty("_class")).ToArray())
                    .IsEquivalentTo(ClusterTieControlClasses, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] ClusterTieMembers = ["j1", "j2"];
        private static readonly string[] ClusterTieControlClasses = ["ClothSpring", "ClothSpring"];

        private static FeModel ClusterTieChain(int ties) => SyntheticCloth.Model(
            ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
            body: $$"""
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    {{string.Concat(Enumerable.Repeat(SyntheticCloth.BandedRod(1, 2, 12f, 48f, 1f), ties))}}
                ]
                """);

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
        /// Planarized planes standing off a box's faces, edges and corner recover that box, not a capsule; planes at a
        /// sphere's six axis points recover a capsule instead.
        /// </summary>
        [Test]
        public async Task APlanarizedBoxIsRecoveredFromItsContactPoints()
        {
            var half = new Vector3(2f, 3f, 4f);
            Vector3[] nodes =
            [
                new(6f, 0f, 0f), new(6f, 1f, 1f), new(6f, -1f, -2f), new(0f, 7f, 0f), new(0f, -7f, 0f),
                new(0f, 0f, 9f), new(0f, 0f, -9f), new(6f, 7f, 0f), new(6f, 7f, 9f),
            ];
            var box = PlanarizedGroup([.. nodes.Select(node =>
            {
                var contact = Vector3.Clamp(node, -half, half);
                var normal = Vector3.Normalize(node - contact);
                return (node, normal, Vector3.Dot(normal, contact));
            })]);
            var sphere = PlanarizedGroup(
            [
                (new Vector3(7f, 2f, 3f), Vector3.UnitX, 5f), (new Vector3(-5f, 2f, 3f), -Vector3.UnitX, 3f),
                (new Vector3(1f, 8f, 3f), Vector3.UnitY, 6f), (new Vector3(1f, -4f, 3f), -Vector3.UnitY, 2f),
                (new Vector3(1f, 2f, 9f), Vector3.UnitZ, 7f), (new Vector3(1f, 2f, -3f), -Vector3.UnitZ, 1f),
            ]);

            var boxes = box.BuildPlanarizeBoxes();

            using (Assert.Multiple())
            {
                await Assert.That(box.BuildPlanarizeCapsules()).IsEmpty();
                await Assert.That(boxes.Count).IsEqualTo(1);
                await Assert.That(sphere.BuildPlanarizeBoxes()).IsEmpty();
                await Assert.That(sphere.BuildPlanarizeCapsules().Count).IsEqualTo(1);
            }

            using (Assert.Multiple())
            {
                await Assert.That(boxes[0].Planarize).IsTrue();
                await Assert.That(boxes[0].VertexMap).IsEqualTo("belt");
                await Assert.That(boxes[0].Origin.Length()).IsLessThan(1e-3f);
                await Assert.That(Vector3.Distance(boxes[0].Size, half)).IsLessThan(1e-3f);
            }
        }

        private static FeModel PlanarizedGroup(IReadOnlyList<(Vector3 Node, Vector3 Normal, float Offset)> planes)
        {
            var count = planes.Count;
            var names = string.Join(", ", Enumerable.Range(0, count).Select(static i => $"\"n{i}\""));
            var poses = string.Concat(planes.Select(static p => SyntheticCloth.Pose(p.Node.X, p.Node.Y, p.Node.Z)));
            var records = string.Concat(planes.Select(static (p, i) => Plane(i + 1,
                $"{SyntheticCloth.Num(p.Normal.X)}, {SyntheticCloth.Num(p.Normal.Y)}, {SyntheticCloth.Num(p.Normal.Z)}",
                p.Offset)));

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "bone", {{names}} ]
                    m_SkelParents = [ -1, {{string.Join(", ", Enumerable.Repeat("0", count))}} ]
                    m_nNodeCount = {{count + 1}}
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ {{string.Join(", ", Enumerable.Repeat("1.0", count + 1))}} ]
                    m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} {{poses}} ]
                    m_CollisionPlanes = [ {{records}} ]
                    m_VertexMapValues = [ {{string.Join(", ", Enumerable.Repeat("255", count))}} ]
                    m_VertexMaps =
                    [
                        {
                            sName = "belt"
                            nNameHash = 1
                            nVertexBase = 1
                            nVertexCount = {{count}}
                            nMapOffset = 0
                            nScaleSourceNode = -1
                            flVolumetricSolveStrength = 0.0
                            vCenterOfMass = [ 0.0, 0.0, 0.0 ]
                        },
                    ]
                }
                """);
        }

        /// <summary>
        /// A rotation-locked static ClothNode declares its node-base preset: <c>transform_alignment</c> 3 when X0 and a
        /// Y reference are the node itself, otherwise 4 with the stored references; a free static node keeps 0.
        /// </summary>
        [Test]
        public async Task AStaticClothNodeDeclaresThePresetItsNodeBaseCompiledFrom()
        {
            var feModel = SyntheticCloth.Model(
                ["spine", "hip", "pin", "a", "b", "c", "d"], staticNodes: 3,
                poses: [new(0f, 0f, 60f), new(0f, 0f, 40f), new(0f, 5f, 40f), new(-4f, 4f, 55f), new(-4f, -4f, 55f),
                    new(-6f, 4f, 45f), new(-6f, -4f, 45f)],
                body: $$"""
                    m_nRotLockStaticNodes = 2
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(2, 3, 15.56f, 1f)}}
                        {{SyntheticCloth.RigidRod(2, 4, 17.94f, 1f)}}
                    ]
                    m_NodeBases =
                    [
                        { nNode = 0 nNodeX0 = 0 nNodeX1 = 3 nNodeY0 = 4 nNodeY1 = 0 },
                        { nNode = 1 nNodeX0 = 3 nNodeX1 = 4 nNodeY0 = 6 nNodeY1 = 5 },
                        { nNode = 2 nNodeX0 = 2 nNodeX1 = 3 nNodeY0 = 2 nNodeY1 = 4 },
                        { nNode = 3 nNodeX0 = 4 nNodeX1 = 3 nNodeY0 = 3 nNodeY1 = 5 },
                    ]
                    """);

            var spine = ClothExtract.MakeClothNode(feModel, "spine", 0, isStaticNode: true);
            var hip = ClothExtract.MakeClothNode(feModel, "hip", 1, isStaticNode: true);
            var pin = ClothExtract.MakeClothNode(feModel, "pin", 2, isStaticNode: true);
            var crossed = feModel.ClothNodeBasisPreset(3);

            using (Assert.Multiple())
            {
                await Assert.That(spine.GetInt32Property("transform_alignment")).IsEqualTo(3);
                await Assert.That(spine.GetStringProperty("node_base_x0")).IsEqualTo(string.Empty);
                await Assert.That(spine.GetStringProperty("node_base_x1")).IsEqualTo("a");
                await Assert.That(spine.GetStringProperty("node_base_y0")).IsEqualTo(string.Empty);
                await Assert.That(spine.GetStringProperty("node_base_y1")).IsEqualTo("b");
                await Assert.That(hip.GetInt32Property("transform_alignment")).IsEqualTo(4);
                await Assert.That(hip.GetStringProperty("node_base_x0")).IsEqualTo("a");
                await Assert.That(hip.GetStringProperty("node_base_x1")).IsEqualTo("b");
                await Assert.That(hip.GetStringProperty("node_base_y0")).IsEqualTo("d");
                await Assert.That(hip.GetStringProperty("node_base_y1")).IsEqualTo("c");
                await Assert.That(pin.GetInt32Property("transform_alignment")).IsEqualTo(0);
                await Assert.That(crossed?.TransformAlignment).IsEqualTo(4);
                await Assert.That(crossed?.References).IsEqualTo(new FeModel.NodeBasis(4, 3, 3, 5));
                await Assert.That(feModel.ClothNodeBasisPreset(5) is null).IsTrue();
            }
        }

        /// <summary>
        /// An effect recording a <c>Node</c> is declared under the static ClothNode rooted on that bone, with its
        /// <c>strength</c> and <c>angles</c>; an effect with no <c>Node</c> stays at the top level.
        /// </summary>
        [Test]
        public async Task AnEffectRecordingANodeIsDeclaredUnderThatStaticClothNode()
        {
            var feModel = SyntheticCloth.Model(
                ["spine_2", "coattail_0_L"], staticNodes: 2, parents: [-1, 0],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f)],
                body: """
                    m_Effects =
                    [
                        { sName = "gravity0" nNameHash = 1 nType = 4 m_Params = { Node = 0 Strength = [ 0.353553, 0.353553, -0.0 ] } },
                        { sName = "gravity1" nNameHash = 2 nType = 4 m_Params = { Strength = [ 0.0, 0.0, -2.0 ] } },
                    ]
                    """);
            var (folder, folderChildren) = KVHelpers.MakeListNode("Folder");
            folderChildren.Add(EffectParentNode("spine_2", isStatic: true));
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(folder);

            ClothExtract.AddClothEffects(softbodyChildren, feModel, new HashSet<string>());

            var nested = folderChildren.ElementAt(0).Value.GetArray("children");
            var top = softbodyChildren.Select(static child => child.Value).Skip(1).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(nested.Count).IsEqualTo(1);
                await Assert.That(nested[0].GetStringProperty("_class")).IsEqualTo("ClothEffectAddGravity");
                await Assert.That(nested[0].GetStringProperty("name")).IsEqualTo("gravity0");
                await Assert.That(nested[0].GetFloatProperty("strength")).IsEqualTo(0.5f).Within(1e-5f);
                await Assert.That(Vector3.Distance(nested[0].GetSubCollection("angles").ToVector3(), new Vector3(0f, 45f, 0f)))
                    .IsLessThan(1e-3f);
                await Assert.That(top.Select(static child => child.GetStringProperty("name")).ToArray())
                    .IsEquivalentTo(["gravity1"], CollectionOrdering.Matching);
                await Assert.That(top[0].GetFloatProperty("strength")).IsEqualTo(2f).Within(1e-5f);
                await Assert.That(Vector3.Distance(top[0].GetSubCollection("angles").ToVector3(), new Vector3(90f, 0f, 0f)))
                    .IsLessThan(1e-3f);
            }
        }

        private static KVObject EffectParentNode(string bone, bool isStatic) => KVHelpers.MakeNode("ClothNode",
            ("name", bone), ("cloth_node_root_bone", bone), ("is_static_node", isStatic));

        /// <summary>
        /// A planarized box turned in its parent's frame is recovered in its own axes, also from one edge and a corner
        /// alone; the unturned box keeps the parent's axes.
        /// </summary>
        [Test]
        public async Task ATurnedPlanarizedBoxIsRecoveredInItsOwnFrame()
        {
            var half = new Vector3(2f, 3f, 4f);
            var turn = Quaternion.CreateFromYawPitchRoll(0.3f, 0.5f, 0.2f);
            Vector3[] faces =
            [
                new(6f, 0f, 0f), new(6f, 1f, 1f), new(6f, -1f, -2f), new(0f, 7f, 0f), new(0f, -7f, 0f),
                new(0f, 0f, 9f), new(0f, 0f, -9f), new(6f, 7f, 0f), new(6f, 7f, 9f),
            ];
            Vector3[] edge = [new(-5f, -6f, -3f), new(-6f, -5f, -1f), new(-4f, -7f, 1f), new(-7f, -6f, 2f), new(-5f, -5f, -6f)];

            var turned = PlanarizedGroup([.. faces.Select(local => TurnedBoxPlane(local, half, turn))]);
            var edgeOnly = PlanarizedGroup([.. edge.Select(local => TurnedBoxPlane(local, half, turn))]);
            var control = PlanarizedGroup([.. faces.Select(local => TurnedBoxPlane(local, half, Quaternion.Identity))]);

            var turnedBoxes = turned.BuildPlanarizeBoxes();
            var edgeBoxes = edgeOnly.BuildPlanarizeBoxes();
            var controlBoxes = control.BuildPlanarizeBoxes();

            using (Assert.Multiple())
            {
                await Assert.That(turned.BuildPlanarizeCapsules()).IsEmpty();
                await Assert.That(edgeOnly.BuildPlanarizeCapsules()).IsEmpty();
                await Assert.That(turnedBoxes.Count).IsEqualTo(1);
                await Assert.That(edgeBoxes.Count).IsEqualTo(1);
                await Assert.That(controlBoxes.Count).IsEqualTo(1);
            }

            using (Assert.Multiple())
            {
                await Assert.That(controlBoxes[0].Rotation).IsEqualTo(Quaternion.Identity);
                await Assert.That(Math.Abs(Quaternion.Dot(turnedBoxes[0].Rotation, Quaternion.Identity))).IsLessThan(0.999f);
                await Assert.That(WorstTurnedBoxPlaneMiss(turnedBoxes[0], faces, half, turn)).IsLessThan(1e-3f);
                await Assert.That(WorstTurnedBoxPlaneMiss(edgeBoxes[0], edge, half, turn)).IsLessThan(1e-3f);
            }
        }

        private static (Vector3 Node, Vector3 Normal, float Offset) TurnedBoxPlane(Vector3 local, Vector3 half, Quaternion turn)
        {
            var contact = Vector3.Clamp(local, -half, half);
            var normal = Vector3.Transform(Vector3.Normalize(local - contact), turn);
            return (Vector3.Transform(local, turn), normal, Vector3.Dot(normal, Vector3.Transform(contact, turn)));
        }

        private static float WorstTurnedBoxPlaneMiss(FeModel.CollisionBox box, Vector3[] locals, Vector3 half, Quaternion turn)
        {
            var toBox = Quaternion.Conjugate(box.Rotation);
            var worst = 0f;
            foreach (var local in locals)
            {
                var (node, normal, offset) = TurnedBoxPlane(local, half, turn);
                var inBox = Vector3.Transform(node - box.Origin, toBox);
                var contact = Vector3.Clamp(inBox, -box.Size, box.Size);
                var fitNormal = Vector3.Transform(Vector3.Normalize(inBox - contact), box.Rotation);
                var fitOffset = Vector3.Dot(fitNormal, Vector3.Transform(contact, box.Rotation) + box.Origin);
                worst = Math.Max(worst, Math.Max((fitNormal - normal).Length(), Math.Abs(fitOffset - offset)));
            }

            return worst;
        }

        /// <summary>
        /// An effect whose bone has no emitted static ClothNode is declared under a bare static one per bone; effects
        /// on an emitted static node join it, and effects with no or a generated <c>Node</c> stay at the top level.
        /// </summary>
        [Test]
        public async Task AnEffectWhoseNodeHasNoStaticClothNodeGetsABareStaticOne()
        {
            var feModel = SyntheticCloth.Model(
                ["spine_2", "coattail_0_L", "coattail_1_L", "$cccoattail_1_L_0"], staticNodes: 2, parents: [-1, 0, 1, 2],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 2f, -20f)],
                body: """
                    m_Effects =
                    [
                        { sName = "gravity_root" nNameHash = 1 nType = 4 m_Params = { Node = 1 Strength = [ 1.0, 0.0, 0.0 ] } },
                        { sName = "gravity_root2" nNameHash = 2 nType = 4 m_Params = { Node = 1 Strength = [ 0.0, 1.0, 0.0 ] } },
                        { sName = "gravity_joint" nNameHash = 3 nType = 4 m_Params = { Node = 2 Strength = [ 0.0, 0.0, 1.0 ] } },
                        { sName = "gravity_static" nNameHash = 4 nType = 4 m_Params = { Node = 0 Strength = [ 1.0, 0.0, 0.0 ] } },
                        { sName = "gravity_top" nNameHash = 5 nType = 4 m_Params = { Strength = [ 1.0, 0.0, 0.0 ] } },
                        { sName = "gravity_generated" nNameHash = 6 nType = 4 m_Params = { Node = 3 Strength = [ 1.0, 0.0, 0.0 ] } },
                    ]
                    """);
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(EffectParentNode("spine_2", isStatic: true));
            softbodyChildren.Add(EffectParentNode("coattail_1_L", isStatic: false));

            ClothExtract.AddClothEffects(softbodyChildren, feModel, new HashSet<string>());

            var top = softbodyChildren.Select(static child => child.Value).ToArray();
            static string[] Names(IEnumerable<KVObject> nodes) => [.. nodes.Select(static node => node.GetStringProperty("name"))];

            await Assert.That(Names(top)).IsEquivalentTo(
                ["spine_2", "coattail_1_L", "coattail_0_L_effects", "coattail_1_L_effects", "gravity_top", "gravity_generated"],
                CollectionOrdering.Matching);

            using (Assert.Multiple())
            {
                foreach (var (bare, bone) in new[] { (top[2], "coattail_0_L"), (top[3], "coattail_1_L") })
                {
                    await Assert.That(bare.GetStringProperty("_class")).IsEqualTo("ClothNode");
                    await Assert.That(bare.GetStringProperty("cloth_node_root_bone")).IsEqualTo(bone);
                    await Assert.That(bare.GetBooleanProperty("is_static_node")).IsTrue();
                }

                await Assert.That(Names(top[2].GetArray("children"))).IsEquivalentTo(["gravity_root", "gravity_root2"],
                    CollectionOrdering.Matching);
                await Assert.That(Names(top[3].GetArray("children"))).IsEquivalentTo(["gravity_joint"]);
                await Assert.That(Names(top[0].GetArray("children"))).IsEquivalentTo(["gravity_static"]);
                await Assert.That(top[1].ContainsKey("children")).IsFalse();
            }
        }

        /// <summary>
        /// A rotated <c>$cloth_node_</c> element gets its <c>angles</c> back relative to its bone with a zero origin,
        /// and its effects are declared in its frame; an unrotated element keeps angles 0 and an offset origin.
        /// </summary>
        [Test]
        public async Task ARotatedClothNodeGetsItsAnglesBackAndItsEffectsInItsFrame()
        {
            var bone = Quaternion.Normalize(new Quaternion(-0.435361f, -0.55719f, -0.55719f, 0.435361f));
            var turned = bone * EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(0f, 90f, 0f));
            static string RotatedPose(Quaternion q) => $"[ 0.0, 0.0, 0.0, 1.0, {SyntheticCloth.Num(q.X)}, "
                + $"{SyntheticCloth.Num(q.Y)}, {SyntheticCloth.Num(q.Z)}, {SyntheticCloth.Num(q.W)} ],";
            var feModel = SyntheticCloth.Model(
                ["spine_2", "$cloth_node_node_spine", "$cloth_node_node_flat", "pelvis"], staticNodes: 4, parents: [-1, 0, 0, -1],
                body: $$"""
                    m_InitPose = [ {{RotatedPose(bone)}} {{RotatedPose(turned)}} {{RotatedPose(bone)}} {{SyntheticCloth.Pose(0f, 0f, -5f)}} ]
                    m_CtrlOffsets =
                    [
                        { vOffset = [ 0.0, 0.0, 0.0 ] nCtrlParent = 0 nCtrlChild = 1 },
                        { vOffset = [ 0.0, 0.0, 0.0 ] nCtrlParent = 0 nCtrlChild = 2 },
                    ]
                    m_Effects =
                    [
                        { sName = "gravity0" nNameHash = 1 nType = 4 m_Params = { Node = 0 Strength = [ -0.353553, 0.353553, 0.0 ] } },
                        { sName = "gravity1" nNameHash = 2 nType = 4 m_Params = { Node = 3 Strength = [ -0.353553, 0.353553, 0.0 ] } },
                    ]
                    """);
            var anchors = feModel.CtrlOffsets.ToDictionary(static offset => offset.CtrlChild);
            var rotatedFound = ClothExtract.TryResolveClothNodeAnchor(feModel, anchors, 1, out var rotatedRoot, out var rotatedOrigin,
                out var rotatedAngles);
            var flatFound = ClothExtract.TryResolveClothNodeAnchor(feModel, anchors, 2, out _, out var flatOrigin, out var flatAngles);
            var element = ClothExtract.MakeClothNode(feModel, rotatedRoot!, 1, isStaticNode: true, elementName: "node_spine",
                origin: rotatedOrigin, angles: rotatedAngles);

            using (Assert.Multiple())
            {
                await Assert.That(rotatedFound).IsTrue();
                await Assert.That(rotatedRoot).IsEqualTo("spine_2");
                await Assert.That(Vector3.Distance(rotatedAngles, new Vector3(0f, 90f, 0f))).IsLessThan(1e-3f);
                await Assert.That(rotatedOrigin).IsEqualTo(Vector3.Zero);
                await Assert.That(Vector3.Distance(element.GetSubCollection("angles").ToVector3(), new Vector3(0f, 90f, 0f)))
                    .IsLessThan(1e-3f);
                await Assert.That(flatFound).IsTrue();
                await Assert.That(flatAngles).IsEqualTo(Vector3.Zero);
                await Assert.That(flatOrigin.Length()).IsGreaterThan(1e-3f);
            }

            static KVObject StaticNode(string name, string root, float yaw) => KVHelpers.MakeNode("ClothNode",
                ("name", name), ("cloth_node_root_bone", root), ("is_static_node", true),
                ("angles", KVHelpers.ToKVArray(new Vector3(0f, yaw, 0f))));
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(StaticNode("spine_2", "spine_2", 0f));
            softbodyChildren.Add(StaticNode("node_spine", "spine_2", 90f));
            softbodyChildren.Add(StaticNode("pelvis", "pelvis", 0f));

            ClothExtract.AddClothEffects(softbodyChildren, feModel, new HashSet<string>());

            var nodes = softbodyChildren.Select(static child => child.Value).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(nodes.Length).IsEqualTo(3);
                await Assert.That(nodes[0].ContainsKey("children")).IsFalse();
                await Assert.That(nodes[1].GetArray("children")[0].GetStringProperty("name")).IsEqualTo("gravity0");
                await Assert.That(Vector3.Distance(nodes[1].GetArray("children")[0].GetSubCollection("angles").ToVector3(),
                    new Vector3(0f, 45f, 0f))).IsLessThan(1e-3f);
                await Assert.That(nodes[2].GetArray("children")[0].GetStringProperty("name")).IsEqualTo("gravity1");
                await Assert.That(Vector3.Distance(nodes[2].GetArray("children")[0].GetSubCollection("angles").ToVector3(),
                    new Vector3(0f, 135f, 0f))).IsLessThan(1e-3f);
            }
        }

        /// <summary>
        /// Mass readings scattered within the float32 step collapse to their median; a gradient or a single reading
        /// does not.
        /// </summary>
        [Test]
        public async Task AUniformMassPaintIsEmittedAsOneValue()
        {
            const float Step = 6.6e-6f;
            (float, float)[] uniform = [(2.0000017f, Step), (1.9999996f, Step), (1.9999976f, Step), (2.0000057f, Step), (2.0000017f, Step)];
            (float, float)[] gradient = [(1.9f, Step), (2.0f, Step), (2.1f, Step)];

            using (Assert.Multiple())
            {
                await Assert.That(FeModel.UniformMassPaint(uniform)).IsEqualTo(2.0000017f);
                await Assert.That(FeModel.UniformMassPaint(gradient)).IsNull();
                await Assert.That(FeModel.UniformMassPaint([(2.0f, Step)])).IsNull();
            }
        }

        /// <summary>
        /// A colliding jiggle bone declares false for each <c>cloth_collision_layer</c> its mask leaves out; a full
        /// mask or a non-colliding bone declares no layers.
        /// </summary>
        [Test]
        public async Task AJiggleBoneDeclaresTheCollisionLayersItsMaskLeavesOut()
        {
            static KVObject? Declare(uint flags, int mask) => ClothExtract.ProcessJiggleBone(
                new FeModel.IndexedJiggleBone(0, -1, default(FeModel.JiggleBone) with { Flags = flags, Length = 5f, CollisionMask = mask }),
                ["tophat"]);

            var leftOut = Declare(802, 13)!;
            var allLayers = Declare(802, 15)!;
            var noCollision = Declare(0x22, 0)!;

            using (Assert.Multiple())
            {
                await Assert.That(leftOut.GetBooleanProperty("has_collision")).IsTrue();
                await Assert.That(leftOut.GetBooleanProperty("cloth_collision_layer0")).IsTrue();
                await Assert.That(leftOut.GetBooleanProperty("cloth_collision_layer1")).IsFalse();
                await Assert.That(leftOut.GetBooleanProperty("cloth_collision_layer2")).IsTrue();
                await Assert.That(leftOut.GetBooleanProperty("cloth_collision_layer3")).IsTrue();
                await Assert.That(allLayers.ContainsKey("cloth_collision_layer1")).IsFalse();
                await Assert.That(noCollision.GetBooleanProperty("has_collision")).IsFalse();
                await Assert.That(noCollision.ContainsKey("cloth_collision_layer0")).IsFalse();
            }
        }

        /// <summary>
        /// A rebuilt vertex set whose hash is the model's file name is dropped; another file name keeps it, and a
        /// selection read from <c>m_VertexMaps</c> is kept.
        /// </summary>
        [Test]
        public async Task TheVertexSetNamedAfterTheModelIsNotRedeclared()
        {
            const string ModelFileName = "chain_extrude_sides_1";
            var modelHash = ValveResourceFormat.Utils.StringToken.Get(ModelFileName);
            string Body(string maps) => SyntheticCloth.Document(
                ["root", "a", "b", "jiggle"], staticNodes: 1, parents: [-1, 0, 1, 0],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -5f), new(0f, 0f, -10f), new(5f, 0f, 0f)],
                body: $$"""
                    m_VertexSetNames = [ 0, {{modelHash}} ]
                    m_DynNodeVertexSet = [ 1, 1, 0 ]
                    {{maps}}
                    """);
            static string[] Names(FeModel feModel) => [.. feModel.VertexMaps.Select(static map => map.Name)];

            var defaultSet = SyntheticCloth.Parse(Body(string.Empty));
            var rebuilt = Names(defaultSet);
            defaultSet.DropModelNameVertexSet(ModelFileName);
            var otherModel = SyntheticCloth.Parse(Body(string.Empty));
            otherModel.DropModelNameVertexSet("another_model");
            var shipped = SyntheticCloth.Parse(Body($$"""m_VertexMapValues = [ 255, 255 ] m_VertexMaps = [ { sName = "chain" nNameHash = {{modelHash}} nVertexBase = 1 nVertexCount = 2 nMapOffset = 0 vCenterOfMass = [ 0.0, 0.0, 0.0 ] flVolumetricSolveStrength = 0.0 nScaleSourceNode = -1 }, ]"""));
            shipped.DropModelNameVertexSet(ModelFileName);

            using (Assert.Multiple())
            {
                await Assert.That(rebuilt).IsEquivalentTo(["vertex_set_0", "vertex_set_1"], CollectionOrdering.Matching);
                await Assert.That(Names(defaultSet)).IsEquivalentTo(["vertex_set_0"]);
                await Assert.That(Names(otherModel)).IsEquivalentTo(["vertex_set_0", "vertex_set_1"], CollectionOrdering.Matching);
                await Assert.That(Names(shipped)).IsEquivalentTo(["chain"]);
            }
        }

        /// <summary>
        /// Anti-tunnel bytecode on a sheetless model declares a <c>ClothAntiTunnelColliderGroup</c> naming the capsule
        /// and each chain once; no bytecode declares no group.
        /// </summary>
        [Test]
        public async Task AChainModelWithAntiTunnelBytecodeDeclaresItsColliderGroup()
        {
            static FeModel Model(string bytecode) => SyntheticCloth.Model(
                ["spine_2", "coattail_0_L", "coattail_1_L"], staticNodes: 2, parents: [-1, -1, 1],
                poses: [new(0f, 0f, 0f), new(0f, 5f, 0f), new(0f, 5f, -8f)],
                body: $$"""
                    m_AntiTunnelBytecode = [ {{bytecode}} ]
                    """);

            var withBytecode = KVObject.Array();
            ClothExtract.AddClothAntiTunnelGroup(withBytecode, Model("131072, 805306368, 2, 131073, 196609"), ["spine_2_clothCapsule"],
                ["coattail_0_L", "coattail_0_L"]);
            var withoutBytecode = KVObject.Array();
            ClothExtract.AddClothAntiTunnelGroup(withoutBytecode, Model(string.Empty), ["spine_2_clothCapsule"], ["coattail_0_L"]);

            var groups = withBytecode.Select(static child => child.Value).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(groups.Length).IsEqualTo(1);
                await Assert.That(groups[0].GetStringProperty("_class")).IsEqualTo("ClothAntiTunnelColliderGroup");
                await Assert.That(groups[0].GetSubCollection("data").GetSubCollection("nodes").Select(static member => member.Key))
                    .IsEquivalentTo(["spine_2_clothCapsule", "coattail_0_L"], CollectionOrdering.Matching);
                await Assert.That(withoutBytecode.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A Kelager bend over three free cloth nodes comes back as a <c>ClothStiffHinge</c> with its <c>max_angle</c>;
        /// a bend over chain joints does not.
        /// </summary>
        [Test]
        public async Task ABendOverFreeClothNodesComesBackAsAStiffHinge()
        {
            var feModel = SyntheticCloth.Model(
                ["spine_2", "$cloth_node_hinge_n0", "$cloth_node_hinge_n1", "$cloth_node_hinge_n2", "coattail_0_L", "coattail_1_L", "coattail_2_L"],
                    staticNodes: 3, parents: [-1, 0, 0, 0, -1, 4, 5], invMasses: "0.0, 0.0, 0.0, 1.0, 0.0, 1.0, 1.0",
                poses: [new(0f, 0f, 0f), new(0f, 0f, -4f), new(0f, 0f, -8f), new(0f, 0f, -12f), new(0f, 5f, 0f), new(0f, 5f, -8f),
                    new(0f, 5f, -16f)],
                body: """
                    m_KelagerBends =
                    [
                        { flWeight = [ -0.0, 1.0, 2.0 ] flHeight0 = 1.652419 nNode = [ 1, 2, 3 ] nReserved = 0 },
                        { flWeight = [ -0.0, 1.0, 2.0 ] flHeight0 = 2.981424 nNode = [ 1, 2, 3 ] nReserved = 0 },
                        { flWeight = [ -2.0, 1.0, 1.0 ] flHeight0 = 0.5 nNode = [ 5, 4, 6 ] nReserved = 0 },
                    ]
                    """);

            var softbodyChildren = KVObject.Array();
            ClothExtract.AddClothStiffHinges(softbodyChildren, feModel);
            var hinges = softbodyChildren.Select(static child => child.Value).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(hinges.Length).IsEqualTo(2);
                foreach (var (hinge, angle) in hinges.Zip([30f, 90f]))
                {
                    await Assert.That(hinge.GetStringProperty("_class")).IsEqualTo("ClothStiffHinge");
                    await Assert.That(hinge.GetStringProperty("cloth_node_0")).IsEqualTo("hinge_n0");
                    await Assert.That(hinge.GetStringProperty("cloth_node_1")).IsEqualTo("hinge_n1");
                    await Assert.That(hinge.GetStringProperty("cloth_node_2")).IsEqualTo("hinge_n2");
                    await Assert.That(MathF.Abs(hinge.GetFloatProperty("max_angle") - angle)).IsLessThan(1e-2f);
                }
            }
        }

        /// <summary>
        /// A jiggle-bone model with nonzero extra iterations keeps its ClothParams; zero counts or no jiggle bone do
        /// not.
        /// </summary>
        [Test]
        public async Task AJiggleBoneModelKeepsTheClothParamsItsIterationCountsRecord()
        {
            static FeModel Model(int extraIterations, string jiggleBones) => SyntheticCloth.Model(
                ["tophat"], staticNodes: 0, parents: [-1], poses: [new(0f, 0f, 60f)], body: $$"""
                    m_nExtraIterations = {{extraIterations}}
                    m_nExtraGoalIterations = {{extraIterations}}
                    m_JiggleBones = [ {{jiggleBones}} ]
                    """);
            const string Jiggle = "{ m_nNode = 0 m_nJiggleParent = 0 m_jiggleBone = { m_nFlags = 38 m_flLength = 5.0 } },";

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.HasJiggleBoneClothParams(Model(1, Jiggle))).IsTrue();
                await Assert.That(ClothExtract.HasJiggleBoneClothParams(Model(0, Jiggle))).IsFalse();
                await Assert.That(ClothExtract.HasJiggleBoneClothParams(Model(1, string.Empty))).IsFalse();
            }
        }

        /// <summary>
        /// The rebuilt vertex set with hash 0 is dropped; a selection read from <c>m_VertexMaps</c> under hash 0 is
        /// kept.
        /// </summary>
        [Test]
        public async Task TheUnnamedJiggleBoneVertexSetIsNotRedeclared()
        {
            string Body(string maps) => SyntheticCloth.Document(
                ["root", "a", "b", "jiggle"], staticNodes: 1, parents: [-1, 0, 1, 0],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -5f), new(0f, 0f, -10f), new(5f, 0f, 0f)],
                body: $$"""
                    m_VertexSetNames = [ 0, 91207372 ]
                    m_DynNodeVertexSet = [ 1, 1, 0 ]
                    {{maps}}
                    """);
            static string[] Names(FeModel feModel) => [.. feModel.VertexMaps.Select(static map => map.Name)];

            var rebuilt = SyntheticCloth.Parse(Body(string.Empty));
            rebuilt.DropUnnamedVertexSet();
            var shipped = SyntheticCloth.Parse(Body("""m_VertexMapValues = [ 255 ] m_VertexMaps = [ { sName = "jiggles" nNameHash = 0 nVertexBase = 3 nVertexCount = 1 nMapOffset = 0 vCenterOfMass = [ 0.0, 0.0, 0.0 ] flVolumetricSolveStrength = 0.0 nScaleSourceNode = -1 }, ]"""));
            shipped.DropUnnamedVertexSet();

            using (Assert.Multiple())
            {
                await Assert.That(Names(rebuilt)).IsEquivalentTo(["vertex_set_1"]);
                await Assert.That(Names(shipped)).IsEquivalentTo(["jiggles"]);
            }
        }

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

                await Assert.That(ClothExtract.ClothChainVersion(jointCount: 4, hasOtherChains: false, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: true, rigidCloudClusterLock: false, locksJoints: false, basesBulkGraded: true,
                    hintsTwistWritten: false, hasUnstagedThinJoint: false)).IsEqualTo(1);
                await Assert.That(ClothExtract.ClothChainVersion(jointCount: 5, hasOtherChains: false, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: true, rigidCloudClusterLock: true, locksJoints: false, basesBulkGraded: false,
                    hintsTwistWritten: false, hasUnstagedThinJoint: false)).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(jointCount: 5, hasOtherChains: false, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: true, rigidCloudClusterLock: false, locksJoints: false, basesBulkGraded: false,
                    hintsTwistWritten: false, hasUnstagedThinJoint: false)).IsEqualTo(1);
            }
        }

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

        private static string TwoVersionTree(bool rootRingFirst, bool thinTipGrouped, bool wideLeafGrouped,
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

            static Func<FeModel.BoneChain, bool, int> VersionOf(FeModel feModel)
                => (chain, hasOtherChains) => ClothExtract.ClothChainVersion(feModel, chain, hasOtherChains);

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
                await Assert.That(ClothExtract.ClothChainVersion(split, Owning(splitChains, "a1"), hasOtherChains: true)).IsEqualTo(0);
                await Assert.That(ClothExtract.ClothChainVersion(split, Owning(splitChains, "b1"), hasOtherChains: true)).IsEqualTo(1);
                await Assert.That(movedChains.Count).IsEqualTo(2);
                await Assert.That(Owning(movedChains, "a1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(Owning(movedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(1);
                await Assert.That(lockedChains.Count).IsEqualTo(2);
                await Assert.That(Owning(lockedChains, "a1").Joints[0].RingNodes.Count).IsEqualTo(1);
                await Assert.That(Owning(lockedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(flippedChains.Count).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(flipped, Owning(flippedChains, "a1"), hasOtherChains: true)).IsEqualTo(1);
                await Assert.That(ClothExtract.ClothChainVersion(flipped, Owning(flippedChains, "b1"), hasOtherChains: true)).IsEqualTo(0);
                await Assert.That(Owning(flippedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(mergedChains.Count).IsEqualTo(1);
                await Assert.That(mergedChains[0].Joints.Count).IsEqualTo(5);
                await Assert.That(ClothExtract.ClothChainVersion(merged, mergedChains[0], hasOtherChains: false)).IsEqualTo(0);
            }
        }

        private static readonly string[] ThinSubChain = ["root", "a0", "a1"];
        private static readonly string[] WideSubChain = ["root", "b0", "b1"];

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
        /// <c>flex_cloth_borders</c> holds only where the pins a face frees carry node bases; a face with one simulated
        /// corner frees no pin.
        /// </summary>
        [Test]
        public async Task FlexClothBordersNeedsItsFreedPinsToCarryNodeBases()
        {
            static FeModel Model(string bases) => SyntheticCloth.Model(
                ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"], staticNodes: 2, parents: [-1, -1, 0, 1],
                poses: [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f)],
                body: $$"""
                    m_nRotLockStaticNodes = 0
                    m_NodeBases = [ {{bases}} ]
                    """);
            static string Base(int node)
                => $"{{ nNode = {node} nDummy = [ 0, 0, 0 ] nNodeX0 = 2 nNodeX1 = 3 nNodeY0 = 0 nNodeY1 = 1 qAdjust = [ 0.0, 0.0, 0.0, 1.0 ] }},";
            static FeModel.ProxyMesh Sheet(List<int[]> faces) => SyntheticCloth.Proxy([0, 1, 2, 3], [0f, 0f, 1f, 1f], faces,
                [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f)]);

            var quad = Sheet([[0, 1, 3, 2]]);
            var painted = Model(string.Empty);
            var flexed = Model(Base(0) + Base(1) + Base(2) + Base(3));

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.FlexedPinsCarryNodeBases(painted, quad)).IsFalse();
                await Assert.That(ClothExtract.FlexedPinsCarryNodeBases(flexed, quad)).IsTrue();
                await Assert.That(ClothExtract.FlexedPinsCarryNodeBases(painted, Sheet([[0, 1, 2]]))).IsTrue();
            }
        }

        /// <summary>
        /// A nearly planar quad whose diagonal ships as a rigid rod reads <c>quad_bend_tolerance</c> 0; without the
        /// rod, or bent past the default, it reads 0.05.
        /// </summary>
        [Test]
        public async Task TheQuadBendToleranceIsReadOffTheSplit()
        {
            static FeModel Model(float bend, string rods) => SyntheticCloth.Model(
                ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"], staticNodes: 1, parents: [-1, 0, 0, 0, 0],
                poses: [new(0f, 0f, 10f), new(0f, 0f, 0f), new(2f, 0f, bend), new(2f, 3f, 0f), new(0f, 3f, 0f)],
                body: $$"""
                    m_Tris = [ { nNode = [ 1, 2, 3 ] }, { nNode = [ 1, 3, 4 ] } ]
                    m_Rods = [ {{rods}} ]
                    """);

            var rod = SyntheticCloth.RigidRod(2, 4, 3.6056f, 1f);
            var nearlyPlanar = Model(0.01f, rod);
            List<int[]> faces = [[1, 2, 3, 4]];
            (int, int)[] splitRod = [(2, 4)];

            using (Assert.Multiple())
            {
                await Assert.That(nearlyPlanar.QuadBendTolerance).IsEqualTo(0f);
                await Assert.That(Model(0.01f, string.Empty).QuadBendTolerance).IsEqualTo(0.05f);
                await Assert.That(Model(1f, rod).QuadBendTolerance).IsEqualTo(0.05f);
                await Assert.That(FeModel.BentQuadRodsFromFaces(faces, nearlyPlanar.InitPosePositions, static node => node == 0, 0f))
                    .IsEquivalentTo(splitRod);
                await Assert.That(FeModel.BentQuadRodsFromFaces(faces, nearlyPlanar.InitPosePositions, static node => node == 0, 0.05f))
                    .IsEmpty();
            }
        }

        /// <summary>
        /// Faces are reordered so the importer's creation order reproduces the shipped node order; a sorted order is
        /// stable and an unfixable one keeps the lane order.
        /// </summary>
        [Test]
        public async Task FacesAreDeclaredInTheShippedNodeOrder()
        {
            int[] gridNodes = [0, 14, 19, 24, 1, 13, 18, 23, 2, 15, 20, 25, 3, 16, 21, 26, 4, 17, 22, 27];
            static string Order(List<int[]> faces) => string.Join(" | ", faces.Select(static face => string.Join(",", face)));
            static List<int[]> Choose(List<int[]> faces, IReadOnlyList<int> nodes, FeModel pins)
                => pins.ChooseFaceDeclarationOrder(faces, faces.Count, nodes);
            static FeModel Pinned(int nodes, int pinned) => SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{string.Join(", ", Enumerable.Range(0, nodes).Select(static node => $"\"n{node}\""))}} ]
                    m_nNodeCount = {{nodes}}
                    m_nStaticNodes = {{pinned}}
                    m_nRotLockStaticNodes = 13
                    m_NodeInvMasses = [ {{string.Join(", ", Enumerable.Range(0, nodes).Select(node => node < pinned ? "0.0" : "1.0"))}} ]
                }
                """);
            var gridPins = Pinned(28, 13);

            List<int[]> lanes =
            [
                [0, 4, 5, 1], [4, 8, 9, 5], [8, 12, 13, 9], [12, 16, 17, 13], [2, 6, 7, 3], [13, 14, 10, 9],
                [18, 19, 15, 14], [6, 10, 11, 7], [17, 18, 14, 13], [14, 15, 11, 10], [5, 9, 10, 6], [1, 5, 6, 2],
            ];
            const string NodeSorted = "0,4,5,1 | 4,8,9,5 | 8,12,13,9 | 12,16,17,13 | 1,5,6,2 | 5,9,10,6 | 13,14,10,9 | "
                + "17,18,14,13 | 2,6,7,3 | 6,10,11,7 | 14,15,11,10 | 18,19,15,14";
            var sorted = Choose(lanes, gridNodes, gridPins);
            var again = Choose(sorted, gridNodes, gridPins);

            int[] smallNodes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
            var smallPins = Pinned(10, 5);
            List<int[]> unfixable = [[0, 4, 9, 5], [2, 3, 8, 7], [1, 2, 7, 6]];

            using (Assert.Multiple())
            {
                await Assert.That(Order(sorted)).IsEqualTo(NodeSorted);
                await Assert.That(Order(again)).IsEqualTo(NodeSorted);
                await Assert.That(Order(Choose(unfixable, smallNodes, smallPins))).IsEqualTo(Order(unfixable));
            }
        }

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
                await Assert.That(ClothExtract.ClothChainVersion(preset, presetChain, hasOtherChains: false)).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(fitted, fittedChain, hasOtherChains: false)).IsEqualTo(1);
                await Assert.That(ClothExtract.ClothChainVersion(bare, bareChain, hasOtherChains: false)).IsEqualTo(2);
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

        /// <summary>
        /// Rigid-hinge hubs folding by different angles read a <c>cloth_bend_stiffness</c> paint per hub and a
        /// saturated curvature; hubs that agree keep one model-wide value and no paint.
        /// </summary>
        [Test]
        public async Task ARigidHingeSheetWhoseHubsFoldApartReadsItsBendPaintPerHub()
        {
            var apart = ThreeHubRigidSheet(4.714045f, 6.159197f);
            var agreeing = ThreeHubRigidSheet(4.714045f, 4.714045f);
            var paint = apart.RigidHingeBendPaint;

            using (Assert.Multiple())
            {
                await Assert.That(paint).IsNotNull();
                await Assert.That(paint![1]).IsEqualTo(0.5f).Within(0.001f);
                await Assert.That(paint[4]).IsEqualTo(0.25f).Within(0.001f);
                await Assert.That(paint[7]).IsEqualTo(1f);
                await Assert.That(apart.RigidHingeCurvature).IsEqualTo(FeModel.SaturatedCurvature);
                await Assert.That(agreeing.RigidHingeBendPaint).IsNull();
                await Assert.That(agreeing.RigidHingeCurvature).IsEqualTo(0.5f).Within(0.001f);
            }
        }

        private static FeModel ThreeHubRigidSheet(float firstHeight, float secondHeight) => SyntheticCloth.Model(
            ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7", "$cloth_m0p8"],
                staticNodes: 1,
            poses: [new(0f, 0f, 0f), new(0f, 0f, 0f), new(10f, 0f, 0f), new(-10f, 0f, 0f), new(0f, 0f, -20f), new(10f, 0f, -20f),
                new(-10f, 0f, -20f), new(0f, 0f, -40f), new(10f, 0f, -40f), new(-10f, 0f, -40f)],
            body: $$"""
                m_AxialEdges = [ { nNode = [ 1, 2, 3, 3, 2, 1 ] }, ]
                m_KelagerBends =
                [
                    { nNode = [ 1, 2, 3 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = {{SyntheticCloth.Num(firstHeight)}} },
                    { nNode = [ 4, 5, 6 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = {{SyntheticCloth.Num(secondHeight)}} },
                    { nNode = [ 7, 8, 9 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = 0.0 },
                ]
                """);

        /// <summary>
        /// A sheet whose edges and diagonals both relax to 0.125 reads a <c>cloth_stretch</c> paint of 0.5 and no shear
        /// stretch; rigid edges with relaxed diagonals read <c>additional_shear_stretch</c> ln 8 and no paint.
        /// </summary>
        [Test]
        public async Task AStretchPaintedSheetRelaxesItsEdgesAndDiagonalsAlike()
        {
            var painted = StretchQuad(edge: 0.125f, diagonal: 0.125f);
            var sheared = StretchQuad(edge: 1f, diagonal: 0.125f);
            var paint = painted.StretchPaint;

            using (Assert.Multiple())
            {
                await Assert.That(paint).IsNotNull();
                await Assert.That(paint!.Count).IsEqualTo(4);
                await Assert.That(paint.Values.All(static value => MathF.Abs(value - 0.5f) <= 1e-3f)).IsTrue();
                await Assert.That(painted.AdditionalShearStretch).IsEqualTo(0f).Within(1e-3f);
                await Assert.That(sheared.StretchPaint).IsNull();
                await Assert.That(sheared.AdditionalShearStretch).IsEqualTo(MathF.Log(8f)).Within(1e-3f);
            }
        }

        private static FeModel StretchQuad(float edge, float diagonal) => SyntheticCloth.Model(
            ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"], staticNodes: 0,
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 0f, -10f), new(0f, 0f, -10f)],
            body: $$"""
                m_flDefaultSurfaceStretch = 0.0
                m_SourceElems = [ 0, 0, 0, 1, 0, 1, 2, 3 ]
                m_Rods =
                [
                    { nNode = [ 0, 1 ] flMaxDist = 10.0 flMinDist = 7.5 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(edge)}} },
                    { nNode = [ 1, 2 ] flMaxDist = 10.0 flMinDist = 7.5 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(edge)}} },
                    { nNode = [ 2, 3 ] flMaxDist = 10.0 flMinDist = 7.5 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(edge)}} },
                    { nNode = [ 0, 3 ] flMaxDist = 10.0 flMinDist = 7.5 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(edge)}} },
                    { nNode = [ 0, 2 ] flMaxDist = 14.142136 flMinDist = 10.606602 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(diagonal)}} },
                    { nNode = [ 1, 3 ] flMaxDist = 14.142136 flMinDist = 10.606602 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(diagonal)}} },
                ]
                """);

        /// <summary>
        /// Hinges that fold apart put every fold into the <c>cloth_bend_stiffness</c> paint with <c>add_curvature</c>
        /// 0; hinges folded alike, or a sheet that keeps its curvature, write no paint.
        /// </summary>
        [Test]
        public async Task ABendNetworkWhoseHingesFoldApartCarriesEveryFoldInItsPaint()
        {
            List<int[]> faces = [[0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6]];
            HashSet<(int, int)> network = [(0, 2), (4, 6), (1, 3), (5, 7)];
            var (apart, apartCurvature) = ClothExtract.ClothBendStiffnessOverFold(FoldStrip(0f, 14.142136f), faces,
                network, 0.5f, keepsCurvature: false);
            var (alike, alikeCurvature) = ClothExtract.ClothBendStiffnessOverFold(FoldStrip(14.142136f, 14.142136f),
                faces, network, 0.5f, keepsCurvature: false);
            var (kept, keptCurvature) = ClothExtract.ClothBendStiffnessOverFold(FoldStrip(0f, 14.142136f), faces,
                network, 0.5f, keepsCurvature: true);

            using (Assert.Multiple())
            {
                await Assert.That(apart).IsNotNull();
                await Assert.That(apartCurvature).IsEqualTo(0f);
                await Assert.That(apart!.GetValueOrDefault(1) + apart.GetValueOrDefault(5)).IsEqualTo(0f).Within(0.02f);
                await Assert.That(apart.GetValueOrDefault(2) + apart.GetValueOrDefault(6)).IsEqualTo(1f).Within(0.02f);
                await Assert.That(alike).IsNull();
                await Assert.That(alikeCurvature).IsEqualTo(0.5f);
                await Assert.That(kept).IsNull();
                await Assert.That(keptCurvature).IsEqualTo(0.5f);
            }
        }

        private static FeModel FoldStrip(float firstHingeMinDist, float secondHingeMinDist) => SyntheticCloth.Model(
            ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7"],
                staticNodes: 0,
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, -10f),
                new(20f, 0f, -10f), new(30f, 0f, -10f)],
            body: $$"""
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMaxDist = 20.0 flMinDist = {{SyntheticCloth.Num(firstHingeMinDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 6 ] flMaxDist = 20.0 flMinDist = {{SyntheticCloth.Num(firstHingeMinDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMaxDist = 20.0 flMinDist = {{SyntheticCloth.Num(secondHingeMinDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 7 ] flMaxDist = 20.0 flMinDist = {{SyntheticCloth.Num(secondHingeMinDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
                """);

        /// <summary>
        /// A bend rod several hinges generate states the most folded one, so each hinge taken at its largest reading
        /// recovers every hinge's pair sum with <c>add_curvature</c> 0.
        /// </summary>
        [Test]
        public async Task ARodSeveralHingesGenerateStatesTheMostFoldedOne()
        {
            var faces = HingeGridFaces;
            var network = HingeGridNetwork;
            var (paint, curvature) = ClothExtract.ClothBendStiffnessOverFold(LeastFoldedHingeGrid, faces, network, 0.375f,
                keepsCurvature: false);

            using (Assert.Multiple())
            {
                await Assert.That(paint).IsNotNull();
                await Assert.That(curvature).IsEqualTo(0f);
                await Assert.That(paint!.GetValueOrDefault(1) + paint.GetValueOrDefault(5)).IsEqualTo(0.25f).Within(0.02f);
                await Assert.That(paint.GetValueOrDefault(5) + paint.GetValueOrDefault(9)).IsEqualTo(0.75f).Within(0.02f);
                await Assert.That(paint.GetValueOrDefault(4) + paint.GetValueOrDefault(5)).IsEqualTo(0.5f).Within(0.02f);
            }
        }

        private static List<int[]> HingeGridFaces => [[0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [4, 5, 9, 8], [5, 6, 10, 9], [6, 7, 11, 10]];

        private static HashSet<(int, int)> HingeGridNetwork => [(0, 2), (1, 3), (4, 6), (5, 7), (8, 10), (9, 11), (0, 8), (1, 9), (2, 10), (3, 11)];

        private static FeModel LeastFoldedHingeGrid => SyntheticCloth.Model(
            ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7", "$cloth_m0p8", "$cloth_m0p9", "$cloth_m0p10", "$cloth_m0p11"],
                staticNodes: 0,
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, -10f),
                new(20f, 0f, -10f), new(30f, 0f, -10f), new(0f, 0f, -20f), new(10f, 0f, -20f), new(20f, 0f, -20f),
                new(30f, 0f, -20f)],
            body: """
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMaxDist = 20.0 flMinDist = 3.901806 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMaxDist = 20.0 flMinDist = 3.901806 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 6 ] flMaxDist = 20.0 flMinDist = 3.901806 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 7 ] flMaxDist = 20.0 flMinDist = 3.901806 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 8, 10 ] flMaxDist = 20.0 flMinDist = 11.111405 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 9, 11 ] flMaxDist = 20.0 flMinDist = 11.111405 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 8 ] flMaxDist = 20.0 flMinDist = 7.653669 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 9 ] flMaxDist = 20.0 flMinDist = 7.653669 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 10 ] flMaxDist = 20.0 flMinDist = 7.653669 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 11 ] flMaxDist = 20.0 flMinDist = 7.653669 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
                """);

        /// <summary>
        /// A drifted mass gradient takes its differences from the face rod weights, anchored on the readings' mean;
        /// with a tolerance below the drift the readings are kept.
        /// </summary>
        [Test]
        public async Task AMassPaintGradientTakesItsShapeFromTheFaceRodWeights()
        {
            float[] authored = [1.0f, 1.4f, 2.0f];
            float[] readings = [1.000003f, 1.399998f, 2.000001f];
            (int, int, float)[] rods = [(0, 1, Weight(authored[0], authored[1])), (1, 2, Weight(authored[1], authored[2]))];
            var refined = FeModel.RefineMassPaint(readings, [(0, 1e-5f), (1, 1e-5f), (2, 1e-5f)], rods);
            var kept = FeModel.RefineMassPaint(readings, [(0, 1e-9f), (1, 1e-9f), (2, 1e-9f)], rods);

            using (Assert.Multiple())
            {
                await Assert.That(refined[1] - refined[0]).IsEqualTo(0.4f).Within(2e-6f);
                await Assert.That(refined[2] - refined[1]).IsEqualTo(0.6f).Within(2e-6f);
                await Assert.That((refined[0] + refined[1] + refined[2]) / 3f)
                    .IsEqualTo((readings[0] + readings[1] + readings[2]) / 3f).Within(2e-6f);
                await Assert.That(kept[0]).IsEqualTo(readings[0]);
                await Assert.That(kept[1]).IsEqualTo(readings[1]);
                await Assert.That(kept[2]).IsEqualTo(readings[2]);
            }

            static float Weight(float a, float b) => MathF.Exp(b) / (MathF.Exp(a) + MathF.Exp(b));
        }

        /// <summary>
        /// Fully dynamic quads are rotated to the corner whose mass sums reproduce the shipped inverse masses; masses
        /// already matched, or unreachable by any rotation, keep the declared faces.
        /// </summary>
        [Test]
        public async Task FullyDynamicQuadsAreDeclaredFromTheCornerTheShippedMassesWitness()
        {
            (int Node, uint X, uint Y, uint Z)[] pose =
            [
                (0, 0xc10ea620, 0xc07fffd9, 0x4282e57b), (1, 0xc10ea622, 0xbfffff92, 0x4282e57a),
                (2, 0xc10ea624, 0x378d0000, 0x4282e579), (3, 0xc10ea626, 0x40000056, 0x4282e578),
                (4, 0xc10ea628, 0x40800033, 0x4282e577), (13, 0xc13b2105, 0xc0088b12, 0x4265ae3c),
                (14, 0xc13b20ff, 0xc0888b30, 0x4265ae3d), (15, 0xc13b210c, 0x37740000, 0x4265ae3c),
                (16, 0xc13b2112, 0x40088b8c, 0x4265ae3b), (17, 0xc13b2118, 0x40888b6d, 0x4265ae3a),
                (18, 0xc16cee2d, 0xc0146850, 0x424613c2), (19, 0xc16cee25, 0xc0946869, 0x424613c2),
                (20, 0xc16cee34, 0x37480000, 0x424613c3), (21, 0xc16cee3c, 0x401468b4, 0x424613c4),
                (22, 0xc16cee44, 0x4094689b, 0x424613c4), (23, 0xc18f427e, 0xc020239a, 0x422673be),
                (24, 0xc18f427a, 0xc0a023af, 0x422673bd), (25, 0xc18f4282, 0x37240000, 0x422673c0),
                (26, 0xc18f4286, 0x402023ec, 0x422673c1), (27, 0xc18f428b, 0x40a023d8, 0x422673c3),
            ];
            var positions = new Vector3[28];
            foreach (var (node, x, y, z) in pose)
            {
                positions[node] = new Vector3(BitConverter.UInt32BitsToSingle(x), BitConverter.UInt32BitsToSingle(y),
                    BitConverter.UInt32BitsToSingle(z));
            }

            uint[] shipped =
            [
                0x3b532e7d, 0x3bd348ca, 0x3b5336ad, 0x3b532e89, 0x3bd348db, 0x3b50c6eb, 0x3bd0b9f9, 0x3b50d147,
                0x3b50c6f5, 0x3bd0ba08, 0x3bcedbe2, 0x3c4dbd42, 0x3bcee5f1, 0x3bcedbe8, 0x3c4dbd51,
            ];
            uint[] readBack = [.. shipped[..12], 0x3bcee5f3, 0x3bcedbe7, 0x3c4dbd50];
            uint[] unreachable = [.. shipped[..14], 0x3c4c0000];
            static float[] InvMasses(uint[] simulated)
                => [.. Enumerable.Repeat(0f, 13), .. simulated.Select(BitConverter.UInt32BitsToSingle)];

            int[] nodes = [.. Enumerable.Range(0, 28)];
            List<int[]> declared =
            [
                [0, 1, 13, 14], [1, 2, 15, 13], [2, 3, 16, 15], [3, 4, 17, 16], [14, 13, 18, 19], [13, 15, 20, 18],
                [16, 21, 20, 15], [17, 22, 21, 16], [19, 18, 23, 24], [18, 20, 25, 23], [21, 26, 25, 20], [22, 27, 26, 21],
            ];
            static string Order(List<int[]> faces) => string.Join(" | ", faces.Select(static face => string.Join(",", face)));
            string Rotated(uint[] simulated)
                => Order(FeModel.RotateQuadsToShippedMasses(declared, declared.Count, nodes, positions, InvMasses(simulated)));

            const string Witnessed = "0,1,13,14 | 1,2,15,13 | 2,3,16,15 | 3,4,17,16 | 14,13,18,19 | 13,15,20,18 | "
                + "16,21,20,15 | 17,22,21,16 | 19,18,23,24 | 18,20,25,23 | 20,21,26,25 | 21,22,27,26";

            using (Assert.Multiple())
            {
                await Assert.That(Rotated(shipped)).IsEqualTo(Witnessed);
                await Assert.That(Rotated(readBack)).IsEqualTo(Order(declared));
                await Assert.That(Rotated(unreachable)).IsEqualTo(Order(declared));
            }
        }

        private static readonly (string Name, Vector3 Origin, Vector3 Angles)[] AuthoredCoattailChain =
        [
            ("root_motion", new(0f, 0f, 0f), new(0.000007f, 89.999992f, 89.999992f)),
            ("pelvis", new(0.000019f, 55.962578f, -3.491205f), new(0.00002f, 89.999939f, 89.999954f)),
            ("spine_0", new(1.21064f, 0.305322f, 0.000013f), new(-0.000025f, 7.112689f, 0.000002f)),
            ("spine_1", new(5.321613f, 0.00001f, 0.000016f), new(-0.000007f, -9.975122f, 0f)),
            ("spine_2", new(5.752628f, 0.000014f, 0.000017f), new(0.000077f, -11.132942f, 0.000001f)),
            ("coattail_0_L", new(-1.194107f, -6.585538f, 4.000018f), new(-1.799955f, -146.904633f, 16.299978f)),
            ("coattail_1_L", new(8.499947f, 0.000031f, 0.000009f), new(0.000029f, 2.499934f, 0.000038f)),
            ("coattail_2_L", new(8.499931f, 0.000061f, 0.000001f), new(-0.000065f, -0.099939f, -0.000009f)),
            ("coattail_end_L", new(8.500038f, -0.000027f, 0.000008f), new(-0.000018f, 0.000002f, 0.000011f)),
        ];

        private static readonly (string Name, Vector3 Origin, Vector3 Angles)[] RebuiltCoattailChain =
        [
            ("root_motion", new(-0f, -0f, -0f), new(0.000014f, 89.999992f, 89.999992f)),
            ("pelvis", new(0.000031f, 55.96254f, -3.491195f), new(0.000027f, 89.999924f, 89.999939f)),
            ("spine_0", new(1.21062f, 0.30533f, 0.000021f), new(-0.000025f, 7.112689f, 0.000001f)),
            ("spine_1", new(5.321589f, 0.00002f, 0.000026f), new(-0.000006f, -9.975122f, 0.000001f)),
            ("spine_2", new(5.752597f, 0.000023f, 0.000028f), new(0.000078f, -11.132943f, 0.000002f)),
            ("coattail_0_L", new(-1.194f, -6.585543f, 3.99998f), new(-1.799955f, -146.904633f, 16.299982f)),
            ("coattail_1_L", new(8.499946f, 0.000034f, 0.000007f), new(0.000029f, 2.499935f, 0.000037f)),
            ("coattail_2_L", new(8.499929f, 0.000062f, -0f), new(-0.000065f, -0.099939f, -0.00001f)),
            ("coattail_end_L", new(8.500035f, -0.000024f, 0.000006f), new(-0.000018f, 0.000002f, 0.000011f)),
        ];

        private static readonly Dictionary<string, Vector3> AuthoredCoattailRestPositions = CoattailPositions(
            (0xc10ea5cf, 0x40800104, 0x4282e55e), (0xc13b209f, 0x40888c41, 0x4265ae04),
            (0xc16ceda2, 0x40946966, 0x4246138c), (0xc18f423c, 0x40a024a1, 0x42267371));

        private static readonly Dictionary<string, Vector3> RebuiltCoattailRestPositions = CoattailPositions(
            (0xc10ea5ca, 0x4080011e, 0x4282e55d), (0xc13b209b, 0x40888c55, 0x4265ae02),
            (0xc16ced9e, 0x40946975, 0x4246138a), (0xc18f423a, 0x40a024ab, 0x4226736f));

        private static readonly Dictionary<string, Quaternion> AuthoredCoattailRestRotations = CoattailRotations(
            (0x3eacd3b6, 0xbf257574, 0xbefdd65f, 0xbef186be), (0xbea5912e, 0x3f274df7, 0x3f0185d4, 0x3eebee78),
            (0xbea5dbd7, 0x3f273b73, 0x3f016b7f, 0x3eec2854), (0xbea5dbd3, 0x3f273b73, 0x3f016b7f, 0x3eec2858));

        private static readonly Dictionary<string, Quaternion> RebuiltCoattailRestRotations = CoattailRotations(
            (0x3eacd3b6, 0xbf257576, 0xbefdd655, 0xbef186c1), (0xbea5912e, 0x3f274dfa, 0x3f0185d0, 0x3eebee7d),
            (0xbea5dbd6, 0x3f273b75, 0x3f016b7b, 0x3eec2858), (0xbea5dbd2, 0x3f273b75, 0x3f016b7b, 0x3eec285c));

        private static Dictionary<string, Quaternion> CoattailRotations(params (uint X, uint Y, uint Z, uint W)[] joints)
        {
            string[] names = ["coattail_0_L", "coattail_1_L", "coattail_2_L", "coattail_end_L"];
            return names.Select((name, i) => (name, rotation: new Quaternion(BitConverter.UInt32BitsToSingle(joints[i].X),
                    BitConverter.UInt32BitsToSingle(joints[i].Y), BitConverter.UInt32BitsToSingle(joints[i].Z),
                    BitConverter.UInt32BitsToSingle(joints[i].W))))
                .ToDictionary(static joint => joint.name, static joint => joint.rotation);
        }

        private static bool SameRotationBits(Quaternion a, Quaternion b)
            => BitConverter.SingleToUInt32Bits(a.X) == BitConverter.SingleToUInt32Bits(b.X)
                && BitConverter.SingleToUInt32Bits(a.Y) == BitConverter.SingleToUInt32Bits(b.Y)
                && BitConverter.SingleToUInt32Bits(a.Z) == BitConverter.SingleToUInt32Bits(b.Z)
                && BitConverter.SingleToUInt32Bits(a.W) == BitConverter.SingleToUInt32Bits(b.W);

        private static Dictionary<string, Vector3> CoattailPositions(params (uint X, uint Y, uint Z)[] joints)
        {
            string[] names = ["coattail_0_L", "coattail_1_L", "coattail_2_L", "coattail_end_L"];
            return names.Select((name, i) => (name, position: new Vector3(BitConverter.UInt32BitsToSingle(joints[i].X),
                    BitConverter.UInt32BitsToSingle(joints[i].Y), BitConverter.UInt32BitsToSingle(joints[i].Z))))
                .ToDictionary(static joint => joint.name, static joint => joint.position);
        }

        private static (Dictionary<string, Vector3> Positions, Dictionary<string, Quaternion> Rotations,
            Dictionary<string, Vector3> Landed, Dictionary<string, Vector3> LandedAngles) ComposeCoattailChain(
            (string Name, Vector3 Origin, Vector3 Angles)[] chain, Dictionary<string, Vector3>? positionTargets,
            Dictionary<string, Quaternion>? rotationTargets)
        {
            var positions = new Dictionary<string, Vector3>();
            var rotations = new Dictionary<string, Quaternion>();
            var landed = new Dictionary<string, Vector3>();
            var landedAngles = new Dictionary<string, Vector3>();
            ClothExtract.CompilerTransform? parent = null;
            foreach (var (name, origin, angles) in chain)
            {
                Vector3? position = positionTargets is not null && positionTargets.TryGetValue(name, out var p) ? p : null;
                Quaternion? rotation = rotationTargets is not null && rotationTargets.TryGetValue(name, out var r) ? r : null;
                var world = ClothExtract.ComposeChainBone(parent, origin, angles, position, rotation, out var landedOrigin,
                    out var turned);
                positions[name] = world.Position;
                rotations[name] = world.Rotation;
                if (landedOrigin is { } moved)
                {
                    landed[name] = moved;
                }

                if (turned is { } solved)
                {
                    landedAngles[name] = solved;
                }

                parent = world;
            }

            return (positions, rotations, landed, landedAngles);
        }

        private static bool SameBits(Vector3 a, Vector3 b)
            => BitConverter.SingleToUInt32Bits(a.X) == BitConverter.SingleToUInt32Bits(b.X)
                && BitConverter.SingleToUInt32Bits(a.Y) == BitConverter.SingleToUInt32Bits(b.Y)
                && BitConverter.SingleToUInt32Bits(a.Z) == BitConverter.SingleToUInt32Bits(b.Z);

        /// <summary>
        /// The compiler's transform chain over the printed Bone text reproduces each coattail joint's rest position and
        /// rotation bit for bit, for the authored and rebuilt documents; accumulating with System.Numerics does not.
        /// </summary>
        [Test]
        public async Task AClothChainJointRestPoseIsTheCompilersTransformChainOverTheDocumentText()
        {
            var authoredChain = ComposeCoattailChain(AuthoredCoattailChain, null, null);
            var rebuiltChain = ComposeCoattailChain(RebuiltCoattailChain, null, null);
            var authored = authoredChain.Positions;
            var rebuilt = rebuiltChain.Positions;

            var accumulated = new Dictionary<string, Vector3>();
            var position = Vector3.Zero;
            var rotation = Quaternion.Identity;
            foreach (var (name, origin, angles) in AuthoredCoattailChain)
            {
                position += Vector3.Transform(origin, rotation);
                rotation *= EntityTransformHelper.EulerAnglesToQuaternion(angles);
                accumulated[name] = position;
            }

            using (Assert.Multiple())
            {
                foreach (var (name, expected) in AuthoredCoattailRestPositions)
                {
                    await Assert.That(SameBits(authored[name], expected)).IsTrue();
                    await Assert.That(SameBits(rebuilt[name], RebuiltCoattailRestPositions[name])).IsTrue();
                    await Assert.That(SameRotationBits(authoredChain.Rotations[name], AuthoredCoattailRestRotations[name])).IsTrue();
                    await Assert.That(SameRotationBits(rebuiltChain.Rotations[name], RebuiltCoattailRestRotations[name])).IsTrue();
                }

                await Assert.That(AuthoredCoattailRestPositions.All(joint => SameBits(accumulated[joint.Key], joint.Value)))
                    .IsFalse();
            }
        }

        /// <summary>
        /// Six-decimal <c>angles</c> and <c>origin</c> land each rebuilt joint on the recorded rest pose exactly; an
        /// unreachable rotation or position keeps the printed value, and a rebuild already there moves nothing.
        /// </summary>
        [Test]
        public async Task AClothChainJointOriginIsLandedOnItsRecordedPositionOnTheSixDecimalGrid()
        {
            var landing = ComposeCoattailChain(RebuiltCoattailChain, AuthoredCoattailRestPositions, AuthoredCoattailRestRotations);
            var control = ComposeCoattailChain(RebuiltCoattailChain, RebuiltCoattailRestPositions, RebuiltCoattailRestRotations);

            var unreachable = new Dictionary<string, Vector3>(AuthoredCoattailRestPositions);
            var nudged = unreachable["coattail_1_L"];
            nudged.Z = BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(nudged.Z) + 1);
            unreachable["coattail_1_L"] = nudged;
            var kept = ComposeCoattailChain(RebuiltCoattailChain, unreachable, AuthoredCoattailRestRotations);

            string[] turned = ["coattail_1_L", "coattail_2_L"];
            using (Assert.Multiple())
            {
                await Assert.That(landing.Landed.Count).IsEqualTo(4);
                await Assert.That(string.Join(",", landing.LandedAngles.Keys.Order())).IsEqualTo(string.Join(",", turned.Order()));
                await Assert.That(SameRotationBits(landing.Rotations["coattail_end_L"], AuthoredCoattailRestRotations["coattail_end_L"]))
                    .IsTrue();
                foreach (var (name, expected) in AuthoredCoattailRestPositions)
                {
                    var printed = RebuiltCoattailChain.First(bone => bone.Name == name);
                    await Assert.That(SameBits(landing.Positions[name], expected)).IsTrue();
                    await Assert.That(SameBits(ClothExtract.CompilerTextFloat(landing.Landed[name]), landing.Landed[name])).IsTrue();
                    await Assert.That(Vector3.Distance(landing.Landed[name], printed.Origin)).IsLessThan(1e-4f);
                }

                foreach (var name in AuthoredCoattailRestPositions.Keys)
                {
                    await Assert.That(SameBits(control.Positions[name], RebuiltCoattailRestPositions[name])).IsTrue();
                    await Assert.That(SameRotationBits(control.Rotations[name], RebuiltCoattailRestRotations[name])).IsTrue();
                }

                foreach (var name in turned)
                {
                    await Assert.That(SameRotationBits(landing.Rotations[name], AuthoredCoattailRestRotations[name])).IsTrue();
                    await Assert.That(SameBits(ClothExtract.CompilerTextFloat(landing.LandedAngles[name]),
                        landing.LandedAngles[name])).IsTrue();
                }

                await Assert.That(SameRotationBits(landing.Rotations["coattail_0_L"], AuthoredCoattailRestRotations["coattail_0_L"]))
                    .IsFalse();
                await Assert.That(control.Landed.Count + control.LandedAngles.Count).IsEqualTo(0);
                await Assert.That(kept.Landed.ContainsKey("coattail_1_L")).IsFalse();
                await Assert.That(SameBits(kept.Positions["coattail_1_L"], nudged)).IsFalse();
            }
        }

        /// <summary>
        /// A joint re-solved onto the rest pose writes <c>extrude_twist</c> without its tie roll; an unresolved joint
        /// keeps it.
        /// </summary>
        [Test]
        public async Task AChainJointReSolvedOntoTheRestPoseIsWrittenWithoutItsTieRoll()
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

            var relanded = ClothExtract.MakeClothJoint(feModel, RolledJoint(), chainExtrudes: true, rollTies: false);
            var control = ClothExtract.MakeClothJoint(feModel, RolledJoint(), chainExtrudes: true);

            using (Assert.Multiple())
            {
                await Assert.That(relanded.GetFloatProperty("extrude_twist")).IsEqualTo(0f);
                await Assert.That(control.GetFloatProperty("extrude_twist")).IsEqualTo(-0.012008f).Within(1e-6f);
            }
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

                await Assert.That(ClothExtract.ClothChainVersion(jointCount: 5, hasOtherChains: true, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: false, rigidCloudClusterLock: false, locksJoints: false, basesBulkGraded: null,
                    hintsTwistWritten: true, hasUnstagedThinJoint: false)).IsEqualTo(0);
                await Assert.That(ClothExtract.ClothChainVersion(jointCount: 5, hasOtherChains: true, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: false, rigidCloudClusterLock: false, locksJoints: true, basesBulkGraded: null,
                    hintsTwistWritten: true, hasUnstagedThinJoint: false)).IsEqualTo(2);
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

                await Assert.That(ClothExtract.ClothChainVersion(jointCount: 3, hasOtherChains: false, rootAllowsRotation: true,
                    rootHasBase: false, lockedJoint: false, rigidCloudClusterLock: false, locksJoints: false, basesBulkGraded: null,
                    hintsTwistWritten: false, hasUnstagedThinJoint: false, hasUnbasedLeaf: true)).IsEqualTo(0);
                await Assert.That(ClothExtract.ClothChainVersion(jointCount: 3, hasOtherChains: false, rootAllowsRotation: true,
                    rootHasBase: false, lockedJoint: false, rigidCloudClusterLock: false, locksJoints: true, basesBulkGraded: null,
                    hintsTwistWritten: false, hasUnstagedThinJoint: false, hasUnbasedLeaf: true)).IsEqualTo(2);
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
                await Assert.That(ClothExtract.ClothChainVersion(tied, tiedChain, hasOtherChains: false)).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(unbased, unbasedChain, hasOtherChains: false)).IsEqualTo(1);
                await Assert.That(ClothExtract.ClothChainVersion(decided, decidedChain, hasOtherChains: false)).IsEqualTo(1);
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
        /// Solving each bend rod over every hinge that generates it keeps the model-wide <c>add_curvature</c> 0.25
        /// under the paint and recovers each hinge's sum; the sheet folded by curvature alone writes no paint.
        /// </summary>
        [Test]
        public async Task ARodSetByANarrowerHingeKeepsTheModelWideCurvatureUnderItsPaint()
        {
            List<int[]> faces = [[0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [4, 8, 9, 5], [5, 9, 10, 6], [6, 10, 11, 7], [8, 12, 13, 9],
                [9, 13, 14, 10], [10, 14, 15, 11], [12, 16, 17, 13], [13, 17, 18, 14], [14, 18, 19, 15]];
            HashSet<(int, int)> network = [(0, 2), (1, 3), (1, 9), (2, 10), (3, 11), (4, 6), (5, 7), (5, 13), (6, 14), (7, 15), (8, 10),
                (9, 11), (9, 17), (10, 18), (11, 19), (12, 14), (13, 15), (16, 18), (17, 19)];
            var (paint, curvature) = ClothExtract.ClothBendStiffnessOverFold(SyntheticCloth.Load("cloth_sheet_bent_painted.kv3"), faces, network, 0.25f,
                keepsCurvature: false);
            var (plain, plainCurvature) = ClothExtract.ClothBendStiffnessOverFold(SyntheticCloth.Load("cloth_sheet_bent_plain.kv3"), faces, network, 0.25f,
                keepsCurvature: false);

            using (Assert.Multiple())
            {
                await Assert.That(paint).IsNotNull();
                await Assert.That(curvature).IsEqualTo(0.25f);
                await Assert.That(paint!.GetValueOrDefault(1) + paint.GetValueOrDefault(5)).IsEqualTo(1f).Within(0.02f);
                await Assert.That(paint.GetValueOrDefault(5) + paint.GetValueOrDefault(6)).IsEqualTo(0.5f).Within(0.02f);
                await Assert.That(paint.GetValueOrDefault(2) + paint.GetValueOrDefault(6)).IsEqualTo(0f).Within(0.02f);
                await Assert.That(plain).IsNull();
                await Assert.That(plainCurvature).IsEqualTo(0.25f);
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
        /// A wind effect declares <c>local_space</c> from its <c>LocalSpace</c> parameter; zero declares no key.
        /// </summary>
        [Test]
        public async Task AWindEffectsLocalSpaceIsItsLocalSpaceParameter()
        {
            var local = WindInLocalSpace("0.469");
            var world = WindInLocalSpace("0.0");
            var maps = new HashSet<string>();
            var node = ClothExtract.MakeClothEffect(local, local.Effects.First(), maps);
            var plain = ClothExtract.MakeClothEffect(world, world.Effects.First(), maps);

            using (Assert.Multiple())
            {
                await Assert.That(node!.GetFloatProperty("local_space")).IsEqualTo(0.469f).Within(1e-6f);
                await Assert.That(plain!.ContainsKey("local_space")).IsFalse();
            }
        }

        private static FeModel WindInLocalSpace(string localSpace) => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 1
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0 ]
                m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} ]
                m_Effects =
                [
                    {
                        sName = "ClothEffectWind"
                        nNameHash = 2353092209
                        nType = 1
                        m_Params =
                        {
                            Strength = [ -281.600006, 0.0, 0.0 ]
                            AirToCloth = 0.5
                            LocalSpace = {{localSpace}}
                            Choppiness = 8.0
                            Vortices = [ { MaxSpeed = 211.199997 MaxCell = 1658.880005 } ]
                        }
                    },
                ]
            }
            """);

        /// <summary>
        /// An effect names a selection the document declares in a <c>ClothVertexMap</c> or a joint's <c>vertex_map</c>;
        /// one declared nowhere is not named.
        /// </summary>
        [Test]
        public async Task AnEffectNamesASelectionTheDocumentDeclares()
        {
            var feModel = StiffenOnSelection();

            var map = KVObject.Collection();
            map.Add("_class", "ClothVertexMap");
            map.Add("name", "sail_vm");
            var folderChildren = KVObject.Array();
            folderChildren.Add(map);
            var folder = KVObject.Collection();
            folder.Add("_class", "Folder");
            folder.Add("children", folderChildren);
            var container = KVObject.Array();
            container.Add(folder);

            var joint = KVObject.Collection();
            joint.Add("joint_name", "sail_top");
            joint.Add("vertex_map", "sail_vm=0.5");
            var joints = KVObject.Array();
            joints.Add(joint);
            var table = KVObject.Collection();
            table.Add("joints", joints);
            var chain = KVObject.Collection();
            chain.Add("_class", "ClothChain");
            chain.Add("chain", table);
            var jointDocument = KVObject.Array();
            jointDocument.Add(chain);

            var bare = KVObject.Array();

            foreach (var document in new[] { container, jointDocument, bare })
            {
                ClothExtract.AddClothEffects(document, feModel, new HashSet<string>());
            }

            static KVObject Effect(KVObject document)
                => document.Select(static child => child.Value).First(static child => child.GetStringProperty("_class") == "ClothEffectStiffen");

            using (Assert.Multiple())
            {
                await Assert.That(Effect(container).GetStringProperty("vertex_map")).IsEqualTo("sail_vm");
                await Assert.That(Effect(jointDocument).GetStringProperty("vertex_map")).IsEqualTo("sail_vm");
                await Assert.That(Effect(bare).ContainsKey("vertex_map")).IsFalse();
            }
        }

        private static FeModel StiffenOnSelection() => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 2
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                ]
                m_VertexMaps = [ {{VertexMapEntry("sail_vm", 7, 0, 1, 1)}} ]
                m_VertexMapValues = [ 255 ]
                m_Effects =
                [
                    {
                        sName = "stiffen0"
                        nNameHash = 1
                        nType = 3
                        m_Params =
                        {
                            Stiffness = 1.0
                            VertexMap = 7
                        }
                    },
                ]
            }
            """);

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
        /// A simulated bone in the unnamed vertex set with a jiggle bone is left to the jiggle bone; without one it
        /// stays a lone cloth node.
        /// </summary>
        [Test]
        public async Task ABoneOnlyItsJiggleBoneDeclaresIsNotALoneClothNode()
        {
            var feModel = SyntheticCloth.Model(
                ["tophat", "tail"], staticNodes: 0, parents: [-1, -1], poses: [new(0f, 0f, 60f), new(10f, 0f, 40f)], body: """
                    m_VertexSetNames = [ 0, 2103756403 ]
                    m_DynNodeVertexSet = [ 0, 1 ]
                    m_JiggleBones = [ { m_nNode = 0 m_nJiggleParent = 0 m_jiggleBone = { m_nFlags = 38 m_flLength = 5.0 } }, ]
                    """);

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.IsDeclaredByItsJiggleBone(feModel, 0)).IsTrue();
                await Assert.That(ClothExtract.IsDeclaredByItsJiggleBone(feModel, 1)).IsFalse();
            }
        }

        /// <summary>
        /// A rod from a free node to a chain joint is re-declared as a <c>ClothSpring</c> where a two-corner source
        /// element records it and as a two-member cluster where none does; without chain joints nothing is declared.
        /// </summary>
        [Test]
        public async Task ASpringFromAFreeNodeToAChainJointIsDeclared()
        {
            static FeModel Model(string sourceElems) => SyntheticCloth.Model(
                ["coattail_0_L", "coattail_0_R", "coattail_1_R"], staticNodes: 2, parents: [-1, -1, 1],
                    invMasses: "0.0, 0.0, 0.006141",
                poses: [new(4f, 0f, 60f), new(-4f, 0f, 60f), new(-4f, 0f, 51.5f)],
                body: $$"""
                    m_SourceElems = [ {{sourceElems}} ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 2, 11.854138f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 8.5f, 1f)}}
                    ]
                    """);

            static string[] Ties(FeModel feModel, HashSet<int>? chainJoints)
            {
                KVObject clothChildren = KVObject.Array();
                KVObject softbodyChildren = KVObject.Array();
                ClothExtract.AddFreeClothNodesAndSprings(clothChildren, softbodyChildren, feModel, [1, 2], static _ => true,
                    [], chainJoints: chainJoints);
                return [.. softbodyChildren.Select(static child => child.Value).Select(static child =>
                    child.GetStringProperty("_class") == "ClothSpring"
                        ? "spring:" + child.GetStringProperty("cloth_node_0") + "|" + child.GetStringProperty("cloth_node_1")
                        : "cluster:" + string.Join("|", child.GetSubCollection("chain").GetArray("joints")
                            .Select(static joint => joint.GetStringProperty("joint_name"))))];
            }

            FeModel recorded = Model("0, 1, 0, 0, 0, 2");
            FeModel unrecorded = Model("0, 0, 0, 0");

            using (Assert.Multiple())
            {
                await Assert.That(Ties(recorded, [1, 2])).IsEquivalentTo(["spring:coattail_0_L|coattail_1_R"]);
                await Assert.That(Ties(unrecorded, [1, 2])).IsEquivalentTo(["cluster:coattail_0_L|coattail_1_R"]);
                await Assert.That(Ties(unrecorded, null)).IsEmpty();
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
        /// A planarized capsule whose nearest column is clamped through its nodes is still recovered at its radius on
        /// its own axis; an unclamped capsule is recovered as well.
        /// </summary>
        [Test]
        public async Task APlanarizedCapsuleOverAClampedColumnIsFitOnItsOwnAxis()
        {
            var clamped = PlanarizedColumns(14f).BuildPlanarizeCapsules();
            var open = PlanarizedColumns(8f).BuildPlanarizeCapsules();

            using (Assert.Multiple())
            {
                await Assert.That(clamped.Count).IsEqualTo(1);
                await Assert.That(clamped[0].Radius0).IsEqualTo(14f).Within(1e-2f);
                await Assert.That(open.Count).IsEqualTo(1);
                await Assert.That(open[0].Radius0).IsEqualTo(8f).Within(1e-2f);
            }
        }

        private static FeModel PlanarizedColumns(float radius)
        {
            (float X, float Y, float NodeRadius, float[] Heights)[] columns =
            [
                (-8.311f, -11.225f, 4f, [-4.267f, -2.134f, 0f, 2.133f, 4.267f]),
                (-15.225f, -16.156f, 6f, [-4.638f, -2.319f, 0f, 2.319f, 4.638f]),
                (-22.147f, -21.075f, 8f, [-5.004f, -2.502f, 0f, 2.502f, 5.004f]),
            ];

            var names = new StringBuilder("\"spine_2\"");
            var poses = new StringBuilder(SyntheticCloth.Pose(0f, 0f, 0f));
            var radii = new List<string>();
            var planes = new StringBuilder();
            var node = 0;
            foreach (var (x, y, nodeRadius, heights) in columns)
            {
                foreach (var z in heights)
                {
                    node++;
                    var point = new Vector3(x, y, z);
                    var centre = new Vector3(0f, 0f, Math.Clamp(z, -4f, 4f));
                    var normal = Vector3.Normalize(point - centre);
                    var offset = Vector3.Dot(normal, centre) + radius;
                    if (Vector3.Dot(normal, point) - offset < nodeRadius)
                    {
                        offset = Vector3.Dot(normal, point);
                    }

                    names.Append(CultureInfo.InvariantCulture, $", \"v{node}\"");
                    poses.Append(SyntheticCloth.Pose(x, y, z));
                    radii.Add(SyntheticCloth.Num(nodeRadius));
                    planes.Append(CultureInfo.InvariantCulture,
                        $"{{ nCtrlParent = 0 nChildNode = {node} m_Plane = {{ m_vNormal = [ {SyntheticCloth.Num(normal.X)}, {SyntheticCloth.Num(normal.Y)}, {SyntheticCloth.Num(normal.Z)} ] m_flOffset = {SyntheticCloth.Num(offset)} }} flStrength = 1.0 }},");
                }
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{names}} ]
                    m_nNodeCount = {{node + 1}}
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0{{string.Concat(Enumerable.Repeat(", 1.0", node))}} ]
                    m_NodeCollisionRadii = [ {{string.Join(", ", radii)}} ]
                    m_InitPose = [ {{poses}} ]
                    m_CollisionPlanes = [ {{planes}} ]
                    m_VertexMaps = [ {{VertexMapEntry("vmap0", 4164734239, 0, 1, node)}} ]
                    m_VertexMapValues = [ {{string.Join(", ", Enumerable.Repeat("255", node))}} ]
                }
                """);
        }

        /// <summary>
        /// A graded <c>cloth_stretch</c> across a quad sheet is recovered whole, the diagonals deciding the edge
        /// offset; a uniform paint reads its value.
        /// </summary>
        [Test]
        public async Task AStretchGradientTakesTheOffsetItsDiagonalsState()
        {
            var graded = StretchedSheet([0.6f, 0.35f, 0.1f]).StretchPaint;
            var uniform = StretchedSheet([0.5f, 0.5f, 0.5f]).StretchPaint;

            using (Assert.Multiple())
            {
                await Assert.That(graded).IsNotNull();
                await Assert.That(graded![0]).IsEqualTo(0.6f).Within(1e-3f);
                await Assert.That(graded[4]).IsEqualTo(0.35f).Within(1e-3f);
                await Assert.That(graded[7]).IsEqualTo(0.1f).Within(1e-3f);
                await Assert.That(uniform).IsNotNull();
                await Assert.That(uniform![4]).IsEqualTo(0.5f).Within(1e-3f);
            }
        }

        private static FeModel StretchedSheet(float[] rowPaint)
        {
            const float ShearFactor = 0.5f;
            var rods = new StringBuilder();
            void Rod(int a, int b)
            {
                var edge = a / 3 == b / 3 || a % 3 == b % 3;
                var rest = edge ? 10f : MathF.Sqrt(200f);
                var open = 1f - (0.5f * (rowPaint[a / 3] + rowPaint[b / 3]));
                rods.Append(SyntheticCloth.BandedRod(a, b, rest * 0.5f, rest, (edge ? 1f : ShearFactor) * open * open * open));
            }

            foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(0, 1), (1, 2), (3, 4), (4, 5), (6, 7), (7, 8), (0, 3), (1, 4), (2, 5), (3, 6), (4, 7), (5, 8),
                (0, 4), (1, 3), (1, 5), (2, 4), (3, 7), (4, 6), (4, 8), (5, 7)])
            {
                Rod(a, b);
            }

            var poses = new StringBuilder();
            for (var node = 0; node < 9; node++)
            {
                poses.Append(SyntheticCloth.Pose((node % 3) * 10f, 0f, -(node / 3) * 10f));
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{string.Join(", ", Enumerable.Range(0, 9).Select(static node => $"\"$cloth_m0p{node}\""))}} ]
                    m_nNodeCount = 9
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ {{string.Join(", ", Enumerable.Repeat("1.0", 9))}} ]
                    m_InitPose = [ {{poses}} ]
                    m_SourceElems = [ 0, 0, 0, 4, 0, 1, 4, 3, 1, 2, 5, 4, 3, 4, 7, 6, 4, 5, 8, 7 ]
                    m_Rods = [ {{rods}} ]
                }
                """);
        }

        /// <summary>
        /// A Kelager bend whose hub lies between its ends is a chain ring bend, not a stiff hinge; one bending its
        /// first end's parent is a stiff hinge.
        /// </summary>
        [Test]
        public async Task ARingBendOverAJointIsNotAStiffHingeOnItsParent()
        {
            static FeModel Model(string bendNodes) => SyntheticCloth.Model(
                ["root", "j1", "j2"], staticNodes: 1, parents: [-1, 0, 1],
                poses: [new(0f, 0f, 0f), new(0f, 0f, -8.5f), new(0f, 0f, -17f)],
                body: $$"""
                    m_KelagerBends =
                    [
                        { nNode = [ {{bendNodes}} ] flWeight = [ -1.0, 0.0, 0.9 ] flHeight0 = 4.0 nReserved = 0 },
                    ]
                    """);

            FeModel ring = Model("1, 0, 2");
            FeModel hinge = Model("1, 2, 0");

            using (Assert.Multiple())
            {
                await Assert.That(ring.IsChainRingBend(ring.KelagerBends[0])).IsTrue();
                await Assert.That(ring.HasChainRingBends).IsTrue();
                await Assert.That(ring.GetStiffHinge(0)).IsNull();
                await Assert.That(hinge.IsChainRingBend(hinge.KelagerBends[0])).IsFalse();
                await Assert.That(hinge.HasChainRingBends).IsFalse();
                await Assert.That(hinge.GetStiffHinge(2)).IsNotNull();
            }
        }

        /// <summary>
        /// A lone simulated root whose stray radius is relaxed to zero is a <c>ClothNode</c>; a partly relaxed record,
        /// no record, or a control-node ancestor keeps a <c>ClothChain</c>.
        /// </summary>
        [Test]
        public async Task AStrayRecordRelaxedToZeroIsALoneClothNode()
        {
            static string[] Classes(FeModel feModel, Func<string, bool> hasControlAncestor)
            {
                KVObject clothChildren = KVObject.Array();
                ClothExtract.AddFreeClothNodesAndSprings(clothChildren, KVObject.Array(), feModel, [], static _ => true, [],
                    bareStaticReparented: hasControlAncestor);
                return [.. clothChildren.Select(static child => child.Value.GetStringProperty("_class"))];
            }

            var feModel = SyntheticCloth.Model(
                ["spine_2", "tail", "ear"], staticNodes: 0, parents: [-1, -1, -1],
                poses: [new(0f, 0f, 60f), new(10f, 0f, 40f), new(-10f, 0f, 40f)],
                body: """
                    m_AnimStrayRadii =
                    [
                        { nNode = [ 0, 0 ] flMaxDist = 7.0 flRelaxationFactor = 0.0 },
                        { nNode = [ 1, 1 ] flMaxDist = 7.0 flRelaxationFactor = 0.25 },
                    ]
                    """);

            using (Assert.Multiple())
            {
                await Assert.That(Classes(feModel, static _ => false)).IsEquivalentTo(["ClothNode", "ClothChain", "ClothChain"]);
                await Assert.That(Classes(feModel, static _ => true)).IsEquivalentTo(["ClothChain", "ClothChain", "ClothChain"]);
            }
        }

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
        /// A planarized box is recovered with planes drawn through nodes that reach or sit inside it; the clear planes
        /// alone recover it too.
        /// </summary>
        [Test]
        public async Task APlanarizedBoxKeepsThePlanesItsNodesReachThrough()
        {
            Vector3 half = new(2f, 3f, 4f);
            (Vector3 Node, float Radius)[] clear =
            [
                (new(6f, 0f, 0f), 1f), (new(-6f, 1f, 0f), 1f), (new(0f, 7f, 0f), 1f), (new(1f, -7f, 0f), 1f),
                (new(0f, 0f, 9f), 1f), (new(0f, 1f, -9f), 1f), (new(6f, 7f, 0f), 1f),
            ];
            (Vector3 Node, float Radius)[] reaching = [(new(2.5f, 1f, 1f), 1f), (new(0f, 3.4f, 0f), 2f), (new(1.9f, 0f, 0f), 1f)];

            FeModel reached = PlanarizedBoxGroup([.. clear, .. reaching], half);
            var boxes = reached.BuildPlanarizeBoxes();
            var control = PlanarizedBoxGroup(clear, half).BuildPlanarizeBoxes();

            using (Assert.Multiple())
            {
                await Assert.That(reached.BuildPlanarizeCapsules()).IsEmpty();
                await Assert.That(boxes.Count).IsEqualTo(1);
                await Assert.That(control.Count).IsEqualTo(1);
            }

            using (Assert.Multiple())
            {
                await Assert.That(boxes[0].Origin.Length()).IsLessThan(1e-3f);
                await Assert.That(Vector3.Distance(boxes[0].Size, half)).IsLessThan(1e-3f);
            }
        }

        private static FeModel PlanarizedBoxGroup((Vector3 Node, float Radius)[] nodes, Vector3 half)
        {
            StringBuilder names = new("\"bone\"");
            StringBuilder poses = new(SyntheticCloth.Pose(0f, 0f, 0f));
            List<string> radii = [];
            StringBuilder planes = new();
            for (int i = 0; i < nodes.Length; i++)
            {
                (Vector3 node, float radius) = nodes[i];
                Vector3 contact = Vector3.Clamp(node, -half, half);
                float reach = Vector3.Distance(node, contact);
                Vector3 normal;
                float offset;
                if (reach <= 0f)
                {
                    Vector3 toFace = half - Vector3.Abs(node);
                    normal = toFace.X <= toFace.Y && toFace.X <= toFace.Z ? new Vector3(MathF.Sign(node.X), 0f, 0f)
                        : toFace.Y <= toFace.Z ? new Vector3(0f, MathF.Sign(node.Y), 0f) : new Vector3(0f, 0f, MathF.Sign(node.Z));
                    offset = Vector3.Dot(normal, node);
                }
                else
                {
                    normal = (node - contact) / reach;
                    offset = reach < radius ? Vector3.Dot(normal, node) : Vector3.Dot(normal, contact) + radius;
                }

                names.Append(CultureInfo.InvariantCulture, $", \"v{i + 1}\"");
                poses.Append(SyntheticCloth.Pose(node.X, node.Y, node.Z));
                radii.Add(SyntheticCloth.Num(radius));
                planes.Append(CultureInfo.InvariantCulture,
                    $"{{ nCtrlParent = 0 nChildNode = {i + 1} m_Plane = {{ m_vNormal = [ {SyntheticCloth.Num(normal.X)}, {SyntheticCloth.Num(normal.Y)}, {SyntheticCloth.Num(normal.Z)} ] m_flOffset = {SyntheticCloth.Num(offset)} }} flStrength = 1.0 }},");
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{names}} ]
                    m_nNodeCount = {{nodes.Length + 1}}
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0{{string.Concat(Enumerable.Repeat(", 1.0", nodes.Length))}} ]
                    m_NodeCollisionRadii = [ {{string.Join(", ", radii)}} ]
                    m_InitPose = [ {{poses}} ]
                    m_CollisionPlanes = [ {{planes}} ]
                    m_VertexMaps = [ {{VertexMapEntry("vmap0", 4164734239, 0, 1, nodes.Length)}} ]
                    m_VertexMapValues = [ {{string.Join(", ", Enumerable.Repeat("255", nodes.Length))}} ]
                }
                """);
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
        /// A static root in <c>m_LockToGoal</c> is declared as a one-joint chain; an unlocked root or a free node stays
        /// a ClothNode.
        /// </summary>
        [Test]
        public async Task AStaticRootTheOriginalLocksToItsGoalIsASingleJointChain()
        {
            static FeModel Model(string locks) => SyntheticCloth.Model(
                ["tophat", "$cloth_node_sb_tw_end"], staticNodes: 1, parents: [-1, 0], invMasses: "0.0, 312.5",
                poses: [new(0f, 0f, 0f), new(0f, 0f, -4f)],
                body: $$"""
                    m_nRotLockStaticNodes = 0
                    m_LockToGoal = [ {{locks}} ]
                    """);

            FeModel locked = Model("0");
            FeModel free = Model("");

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.LoneNodeIsJointChain(locked, 0, bareStatic: false, bareStaticReparented: false)).IsTrue();
                await Assert.That(ClothExtract.LoneNodeIsJointChain(free, 0, bareStatic: false, bareStaticReparented: false)).IsFalse();
                await Assert.That(ClothExtract.LoneNodeIsJointChain(free, 1, bareStatic: false, bareStaticReparented: false)).IsFalse();
            }
        }

        /// <summary>
        /// A quad with edge rods but no diagonal paints its corners at zero <c>cloth_shear_resistance</c>; a sheet with
        /// no such quad states no paint.
        /// </summary>
        [Test]
        public async Task AQuadWithEdgesButNoDiagonalPaintsItsCornersAtZeroShear()
        {
            var holed = ShearedSheet(leftEdges: true).ShearResistance;
            var ungated = ShearedSheet(leftEdges: false).ShearResistance;

            using (Assert.Multiple())
            {
                await Assert.That(holed).IsNotNull();
                foreach (var node in new[] { 0, 1, 3, 4, 6, 7 })
                {
                    await Assert.That(holed!.Value.Paint[node]).IsEqualTo(0f).Within(1e-3f);
                }

                await Assert.That(holed!.Value.Paint[5]).IsGreaterThan(0.5f);
                await Assert.That(ungated).IsNull();
            }
        }

        private static FeModel ShearedSheet(bool leftEdges)
        {
            var rods = new StringBuilder();
            foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(1, 2), (4, 5), (7, 8), (1, 4), (2, 5), (4, 7), (5, 8)])
            {
                rods.Append(SyntheticCloth.RigidRod(a, b, 10f, 1f));
            }

            if (leftEdges)
            {
                foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(0, 1), (3, 4), (6, 7), (0, 3), (3, 6)])
                {
                    rods.Append(SyntheticCloth.RigidRod(a, b, 10f, 1f));
                }
            }

            foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(1, 5), (2, 4), (4, 8), (5, 7)])
            {
                rods.Append(SyntheticCloth.BandedRod(a, b, MathF.Sqrt(200f) * 0.75f, MathF.Sqrt(200f), 0.125f));
            }

            var poses = new StringBuilder();
            for (var node = 0; node < 9; node++)
            {
                poses.Append(SyntheticCloth.Pose((node % 3) * 10f, 0f, -(node / 3) * 10f));
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{string.Join(", ", Enumerable.Range(0, 9).Select(static node => $"\"$cloth_m0p{node}\""))}} ]
                    m_nNodeCount = 9
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ {{string.Join(", ", Enumerable.Repeat("1.0", 9))}} ]
                    m_InitPose = [ {{poses}} ]
                    m_SourceElems = [ 0, 0, 0, 4, 0, 1, 4, 3, 1, 2, 5, 4, 3, 4, 7, 6, 4, 5, 8, 7 ]
                    m_Rods = [ {{rods}} ]
                }
                """);
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
        /// A free node's spring to a chain joint names the joint once; without chain joints the rod is not declared.
        /// </summary>
        [Test]
        public async Task AFreeNodesSpringToAChainJointNamesTheJoint()
        {
            var feModel = FreeNodeSprungToJoint;
            var joints = feModel.BuildBoneChains().SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();

            var withJoints = KVObject.Array();
            ClothExtract.AddFreeClothNodesAndSprings(KVObject.Array(), withJoints, feModel, joints, static _ => true, [], chainJoints: joints);
            var withoutJoints = KVObject.Array();
            ClothExtract.AddFreeClothNodesAndSprings(KVObject.Array(), withoutJoints, feModel, joints, static _ => true, []);

            static (string, string)[] Springs(KVObject children) => children
                .Select(static child => child.Value)
                .Where(static node => node.GetStringProperty("_class") == "ClothSpring")
                .Select(static node => (node.GetStringProperty("cloth_node_0"), node.GetStringProperty("cloth_node_1")))
                .ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(Springs(withJoints).Count(static s => s == ("node_b", "coattail_1_L"))).IsEqualTo(1);
                await Assert.That(Springs(withJoints).Count(static s => s.Item1 == "coattail_1_L" || s.Item2 == "coattail_1_L")).IsEqualTo(1);
                await Assert.That(Springs(withoutJoints).Any(static s => s.Item1 == "coattail_1_L" || s.Item2 == "coattail_1_L")).IsFalse();
            }
        }

        private static FeModel FreeNodeSprungToJoint => SyntheticCloth.Load("cloth_chain_free_node_spring.kv3");

        /// <summary>
        /// A proxy sheet no fit range names, beside a fit-covered one, is not back-solved and keeps its skin paint
        /// verbatim; a fit-named sheet defers it, and with no fits the paint is recovered anyway.
        /// </summary>
        [Test]
        public async Task ASheetTheOriginalDidNotBackSolveKeepsItsAuthoredProxyPaint()
        {
            var oneBackSolving = TwoProxySheets("""
                m_FitMatrices =
                [
                    { nEnd = 2 nNode = 6 nBeginDynamic = 0 },
                    { nEnd = 4 nNode = 7 nBeginDynamic = 0 },
                ]
                m_FitWeights =
                [
                    { flWeight = 0.75 nNode = 2 nDummy = 0 },
                    { flWeight = 0.5 nNode = 3 nDummy = 0 },
                    { flWeight = 0.25 nNode = 2 nDummy = 0 },
                    { flWeight = 0.5 nNode = 3 nDummy = 0 },
                ]
                """);
            var bothBackSolving = TwoProxySheets("""
                m_FitMatrices =
                [
                    { nEnd = 3 nNode = 6 nBeginDynamic = 0 },
                    { nEnd = 5 nNode = 7 nBeginDynamic = 0 },
                ]
                m_FitWeights =
                [
                    { flWeight = 0.75 nNode = 2 nDummy = 0 },
                    { flWeight = 0.5 nNode = 3 nDummy = 0 },
                    { flWeight = 0.6 nNode = 5 nDummy = 0 },
                    { flWeight = 0.25 nNode = 2 nDummy = 0 },
                    { flWeight = 0.5 nNode = 3 nDummy = 0 },
                ]
                """);
            var noFits = TwoProxySheets("");

            const int Mesh1Vertex = 4;
            var recovered = oneBackSolving.RecoveredSkinWeights.GetValueOrDefault(Mesh1Vertex, []);

            using (Assert.Multiple())
            {
                await Assert.That(oneBackSolving.UnbackSolvedProxyMeshes.Contains(1)).IsTrue();
                await Assert.That(oneBackSolving.UnbackSolvedProxyMeshes.Contains(0)).IsFalse();
                await Assert.That(recovered.Length).IsEqualTo(2);
                await Assert.That(recovered[0].Bone).IsEqualTo("bone_1");
                await Assert.That(recovered[0].Weight).IsEqualTo(0.75f).Within(1e-4f);
                await Assert.That(recovered[1].Bone).IsEqualTo("bone_2");
                await Assert.That(recovered[1].Weight).IsEqualTo(0.25f).Within(1e-4f);

                await Assert.That(bothBackSolving.UnbackSolvedProxyMeshes.Count).IsEqualTo(0);
                await Assert.That(bothBackSolving.RecoveredSkinWeights.ContainsKey(Mesh1Vertex)).IsFalse();
                await Assert.That(bothBackSolving.DeferredOffsetSkinWeights.ContainsKey(Mesh1Vertex)).IsTrue();

                await Assert.That(noFits.UnbackSolvedProxyMeshes.Count).IsEqualTo(0);
                await Assert.That(noFits.RecoveredSkinWeights.ContainsKey(Mesh1Vertex)).IsTrue();
            }
        }

        /// <summary>
        /// Vertex-set streams are ordered so each node's recorded winner precedes its rivals; no constraints or a
        /// cyclic set keep the input order.
        /// </summary>
        [Test]
        public async Task AVertexSetStreamOrderPutsEveryNodesWinnerBeforeItsRivals()
        {
            string[] names = ["charlie", "bravo", "alpha"];
            var ordered = FeModel.OrderByFirstWriterWins(names,
                [("alpha", "bravo"), ("alpha", "charlie"), ("bravo", "charlie")]);
            var unconstrained = FeModel.OrderByFirstWriterWins(names, []);
            var cyclic = FeModel.OrderByFirstWriterWins(names, [("alpha", "bravo"), ("bravo", "alpha")]);
            var partial = FeModel.OrderByFirstWriterWins(names, [("alpha", "charlie")]);

            string[] winnersFirst = ["alpha", "bravo", "charlie"];
            string[] charlieLast = ["bravo", "alpha", "charlie"];

            using (Assert.Multiple())
            {
                await Assert.That(ordered).IsEquivalentTo(winnersFirst, CollectionOrdering.Matching);
                await Assert.That(unconstrained).IsEquivalentTo(names, CollectionOrdering.Matching);
                await Assert.That(cyclic).IsEquivalentTo(names, CollectionOrdering.Matching);
                await Assert.That(partial).IsEquivalentTo(charlieLast, CollectionOrdering.Matching);
                await Assert.That(string.Join(",", ordered)).IsEqualTo("alpha,bravo,charlie");
            }
        }

        /// <summary>
        /// A named <c>m_VertexMaps</c> record with no vertices is listed for an all-zero paint; a record with vertices
        /// or without a name is not.
        /// </summary>
        [Test]
        public async Task ASelectionRegisteredOverNoVertexIsNamedByAnAllZeroPaint()
        {
            var feModel = SelectionsOverNoVertex;

            using (Assert.Multiple())
            {
                await Assert.That(feModel.ZeroVertexSelectionNames.Count).IsEqualTo(1);
                await Assert.That(feModel.ZeroVertexSelectionNames[0]).IsEqualTo("ghost");
                await Assert.That(feModel.VertexMaps.Count).IsEqualTo(3);
                await Assert.That(feModel.ZeroVertexSelectionNames.Contains("real")).IsFalse();
            }
        }

        /// <summary>
        /// Three <c>m_VertexMaps</c> records: "ghost" named over no vertex, "real" over two, and an unnamed one over
        /// none.
        /// </summary>
        private static FeModel SelectionsOverNoVertex => SyntheticCloth.Model(
            ["bone_0", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"], staticNodes: 2, parents: [-1, 0, 0, 0],
            poses: [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 0f, -8f), new(1f, 0f, -16f)],
            body: """
                m_VertexMapValues = [ 255, 255 ]
                m_VertexMaps =
                [
                    { sName = "ghost" nNameHash = 2018973841 nVertexBase = 0 nVertexCount = 0 nMapOffset = 0 nNodeListOffset = 0 nNodeListCount = 0 flVolumetricSolveStrength = 0.0 nScaleSourceNode = -1 },
                    { sName = "real" nNameHash = 2081852616 nVertexBase = 2 nVertexCount = 2 nMapOffset = 0 nNodeListOffset = 0 nNodeListCount = 2 flVolumetricSolveStrength = 0.0 nScaleSourceNode = -1 },
                    { sName = "" nNameHash = 0 nVertexBase = 0 nVertexCount = 0 nMapOffset = 0 nNodeListOffset = 0 nNodeListCount = 0 flVolumetricSolveStrength = 0.0 nScaleSourceNode = -1 },
                ]
                """);

        /// <summary>
        /// Two proxy sheets over a two-bone chain: mesh 0's vertices are fit targets, and mesh 1's vertex 4 has a
        /// two-bone paint and no fit entry. <paramref name="fits"/> holds the fit arrays.
        /// </summary>
        private static FeModel TwoProxySheets(string fits) => SyntheticCloth.Model(
            ["bone_0", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m1p1", "$cloth_m1p2", "bone_1", "bone_2"],
                staticNodes: 2, parents: [-1, 0, 6, 7, 6, 6, 0, 6],
            poses: [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 0f, -8f), new(1f, 0f, -16f), new(-1f, 0f, -8f), new(-1f, 0f, -16f),
                new(0f, 0f, -8f), new(0f, 0f, -16f)],
            body: $$"""
                m_nFirstPositionDrivenNode = 6
                m_CtrlOffsets =
                [
                    { vOffset = [ 1.0, 0.0, 0.0 ] nCtrlParent = 6 nCtrlChild = 2 },
                    { vOffset = [ 1.0, 0.0, 0.0 ] nCtrlParent = 7 nCtrlChild = 3 },
                    { vOffset = [ -1.0, 0.0, 0.0 ] nCtrlParent = 6 nCtrlChild = 4 },
                    { vOffset = [ -1.0, 0.0, -8.0 ] nCtrlParent = 6 nCtrlChild = 5 },
                ]
                m_CtrlSoftOffsets =
                [
                    { nCtrlParent = 7 nCtrlChild = 4 vOffset = [ -1.0, 0.0, 8.0 ] flAlpha = 0.75 },
                ]
                {{fits}}
                """);

        /// <summary>
        /// A recorded set member its selection weighs 0 is painted at <see cref="FeModel.SubQuantumMembershipWeight"/>,
        /// which still rounds to byte 0; non-members, static nodes, positive weights and models without a set array are
        /// unchanged.
        /// </summary>
        [Test]
        public async Task ASetMemberItsSelectionWeighsZeroIsPaintedBelowAQuantum()
        {
            var recorded = SubQuantumMembers("m_DynNodeVertexSet = [ 0, 0, 0, 1 ]").BuildProxyMeshes()[0];
            var unrecorded = SubQuantumMembers("").BuildProxyMeshes()[0];

            static float Weight(FeModel.ProxyMesh proxy, string map, int node)
                => Array.Find(proxy.VertexMaps, m => m.Name == map).Weights[Array.IndexOf(proxy.NodeIndices, node)];

            var inRange = Weight(recorded, "qb", 5);

            using (Assert.Multiple())
            {
                await Assert.That(Weight(recorded, "qb", 3)).IsEqualTo(FeModel.SubQuantumMembershipWeight);
                await Assert.That(inRange).IsEqualTo(FeModel.SubQuantumMembershipWeight);
                await Assert.That(inRange).IsGreaterThan(0f);
                await Assert.That(MathF.Round(inRange * 255f)).IsEqualTo(0f);
                await Assert.That(Weight(recorded, "qb", 4)).IsEqualTo(1f);
                await Assert.That(Weight(recorded, "qz", 5)).IsEqualTo(0f);
                await Assert.That(Weight(recorded, "qz", 3)).IsEqualTo(0f);
                await Assert.That(Weight(recorded, "qb", 1)).IsEqualTo(0f);

                await Assert.That(Weight(unrecorded, "qb", 3)).IsEqualTo(0f);
                await Assert.That(Weight(unrecorded, "qb", 5)).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A two-row sheet with selections "qb" (255 / 0 / 128 over nodes 4-6) and "qz" (0 / 255 over nodes 5-6);
        /// <paramref name="sets"/> holds <c>m_DynNodeVertexSet</c>.
        /// </summary>
        private static FeModel SubQuantumMembers(string sets) => SyntheticCloth.Model(
            ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5"], staticNodes: 3,
                parents: [-1, 0, 0, 0, 0, 0, 0],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(4f, 0f, -10f), new(0f, 0f, -20f), new(4f, 0f, -20f),
                new(0f, 0f, -30f), new(4f, 0f, -30f)],
            body: $$"""
                m_Tris =
                [
                    { nNode = [ 1, 2, 4 ] }, { nNode = [ 1, 4, 3 ] },
                    { nNode = [ 3, 4, 6 ] }, { nNode = [ 3, 6, 5 ] },
                ]
                m_VertexSetNames = [ 3919779763, 51193340 ]
                {{sets}}
                m_VertexMapValues = [ 255, 0, 128, 0, 255 ]
                m_VertexMaps =
                [
                    { sName = "qb" nNameHash = 3919779763 nVertexBase = 4 nVertexCount = 3 nMapOffset = 0 nNodeListOffset = 0 nNodeListCount = 2 flVolumetricSolveStrength = 0.0 nScaleSourceNode = -1 },
                    { sName = "qz" nNameHash = 51193340 nVertexBase = 5 nVertexCount = 2 nMapOffset = 3 nNodeListOffset = 2 nNodeListCount = 1 flVolumetricSolveStrength = 0.0 nScaleSourceNode = -1 },
                ]
                """);

        /// <summary>
        /// A capsule parent bone at the ClothNode goal and gravity defaults is declared as a bare static
        /// <c>ClothNode</c>; other goal values, bones already declared or chain joints, and bones no shape names are
        /// not.
        /// </summary>
        [Test]
        public async Task AShapeParentAtTheClothNodeDefaultsIsDeclaredABareStaticClothNode()
        {
            var feModel = ShapeParentIntegrators;
            var (folder, folderChildren) = KVHelpers.MakeListNode("Folder");
            folderChildren.Add(EffectParentNode("head", isStatic: true));
            var joint = KVObject.Collection();
            joint.Add("joint_name", "neck_0");
            var joints = KVObject.Array();
            joints.Add(joint);
            var chain = KVObject.Collection();
            chain.Add("joints", joints);
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(folder);
            softbodyChildren.Add(KVHelpers.MakeNode("ClothChain", ("name", "neck_0"), ("root_bone", "neck_0"), ("chain", chain)));

            ClothExtract.AddShapeParentDefaultClothNodes(softbodyChildren, feModel);

            var added = softbodyChildren.Select(static child => child.Value).Skip(2).ToArray();
            var bones = added.Select(static node => node.GetStringProperty("cloth_node_root_bone")).ToArray();
            string[] declaredBones = ["pelvis"];
            string[] undeclaredBones = ["spine_2", "clavicle_L", "head", "neck_0", "hand_R"];

            using (Assert.Multiple())
            {
                await Assert.That(bones).IsEquivalentTo(declaredBones, CollectionOrdering.Matching);
                await Assert.That(added.Select(static node => node.GetStringProperty("name")).ToArray())
                    .IsEquivalentTo(declaredBones, CollectionOrdering.Matching);
                await Assert.That(added.Count(static node => node.GetStringProperty("_class") == "ClothNode"
                    && node.GetBooleanProperty("is_static_node") && !node.ContainsKey("goal_strength"))).IsEqualTo(1);

                await Assert.That(bones.Intersect(undeclaredBones).Count()).IsEqualTo(0);
                await Assert.That(added.Count(static node => node.ContainsKey("goal_strength"))).IsEqualTo(0);
                await Assert.That(folderChildren.Count).IsEqualTo(1);
            }
        }

        /// <summary>
        /// Six static bones, five parenting a capsule: pelvis, head, neck_0 and hand_R at the ClothNode goal defaults,
        /// spine_2 at no goal and clavicle_L at other goal values.
        /// </summary>
        private static FeModel ShapeParentIntegrators => SyntheticCloth.Model(
            ["pelvis", "spine_2", "clavicle_L", "head", "neck_0", "hand_R"], staticNodes: 6, parents: [-1, 0, 1, 1, 1, 2],
            poses: [new(0f, 0f, 30f), new(0f, 0f, 40f), new(4f, 0f, 50f), new(0f, 0f, 60f), new(0f, 0f, 55f), new(8f, 0f, 45f)],
            body: """
                m_NodeIntegrator =
                [
                    { flPointDamping = 0.0 flAnimationForceAttraction = 0.216 flAnimationVertexAttraction = 0.797273 flGravity = 360.0 },
                    { flPointDamping = 0.0 flAnimationForceAttraction = 0.0 flAnimationVertexAttraction = 0.0 flGravity = 360.0 },
                    { flPointDamping = 0.0 flAnimationForceAttraction = 0.125 flAnimationVertexAttraction = 0.173846 flGravity = 360.0 },
                    { flPointDamping = 0.0 flAnimationForceAttraction = 0.216 flAnimationVertexAttraction = 0.797273 flGravity = 360.0 },
                    { flPointDamping = 0.0 flAnimationForceAttraction = 0.216 flAnimationVertexAttraction = 0.797273 flGravity = 360.0 },
                    { flPointDamping = 0.0 flAnimationForceAttraction = 0.216 flAnimationVertexAttraction = 0.797273 flGravity = 360.0 },
                ]
                m_TaperedCapsuleRigids =
                [
                    { vSphere = [ [ 0.0, 0.0, -4.0, 4.0 ], [ 0.0, 0.0, 4.0, 4.0 ] ] nNode = 0 nCollisionMask = 15 nVertexMapIndex = 65535 nFlags = 0 },
                    { vSphere = [ [ 0.0, 0.0, -4.0, 4.0 ], [ 0.0, 0.0, 4.0, 4.0 ] ] nNode = 1 nCollisionMask = 15 nVertexMapIndex = 65535 nFlags = 0 },
                    { vSphere = [ [ 0.0, 0.0, -4.0, 4.0 ], [ 0.0, 0.0, 4.0, 4.0 ] ] nNode = 2 nCollisionMask = 15 nVertexMapIndex = 65535 nFlags = 0 },
                    { vSphere = [ [ 0.0, 0.0, -4.0, 4.0 ], [ 0.0, 0.0, 4.0, 4.0 ] ] nNode = 3 nCollisionMask = 15 nVertexMapIndex = 65535 nFlags = 0 },
                    { vSphere = [ [ 0.0, 0.0, -4.0, 4.0 ], [ 0.0, 0.0, 4.0, 4.0 ] ] nNode = 4 nCollisionMask = 15 nVertexMapIndex = 65535 nFlags = 0 },
                ]
                m_RigidColliderPriorities = [ ]
                """);

        /// <summary>
        /// A face diagonal with no rod adds no geometric mass, so a sheet missing some diagonals still reads its
        /// uniform <c>cloth_mass</c> of 1, as the fully built sheet does.
        /// </summary>
        [Test]
        public async Task AnUnbuiltFaceDiagonalAddsNoGeometricMassToItsCorners()
        {
            var holed = MassedSheet(leftDiagonals: false);
            var built = MassedSheet(leftDiagonals: true);
            var holedPaint = holed.RecoverMassPaint(holed.BuildProxyMeshes()[0]);
            var builtPaint = built.RecoverMassPaint(built.BuildProxyMeshes()[0]);

            using (Assert.Multiple())
            {
                await Assert.That(holedPaint).IsNotNull();
                await Assert.That(holedPaint?.Count(static value => MathF.Abs(value - 1f) <= 1e-3f) ?? 0).IsEqualTo(9);
                await Assert.That(builtPaint).IsNotNull();
                await Assert.That(builtPaint?.Count(static value => MathF.Abs(value - 1f) <= 1e-3f) ?? 0).IsEqualTo(9);
            }
        }

        /// <summary>
        /// A 3x3 sheet whose left-hand diagonals are built only with <paramref name="leftDiagonals"/>, each inverse mass
        /// 1 / (8 per unit of rod length + e).
        /// </summary>
        private static FeModel MassedSheet(bool leftDiagonals)
        {
            var built = new List<(int A, int B, bool Diagonal)>();
            foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(0, 1), (1, 2), (3, 4), (4, 5), (6, 7), (7, 8), (0, 3), (1, 4), (2, 5), (3, 6),
                (4, 7), (5, 8)])
            {
                built.Add((a, b, false));
            }

            foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(1, 5), (2, 4), (4, 8), (5, 7)])
            {
                built.Add((a, b, true));
            }

            if (leftDiagonals)
            {
                foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(0, 4), (1, 3), (3, 7), (4, 6)])
                {
                    built.Add((a, b, true));
                }
            }

            var rods = new StringBuilder();
            var geometric = new float[9];
            foreach (var (a, b, diagonal) in built)
            {
                var length = diagonal ? MathF.Sqrt(200f) : 10f;
                rods.Append(diagonal
                    ? SyntheticCloth.BandedRod(a, b, length * 0.75f, length, 0.125f)
                    : SyntheticCloth.RigidRod(a, b, length, 1f));
                geometric[a] += 8f * length;
                geometric[b] += 8f * length;
            }

            var poses = new StringBuilder();
            for (var node = 0; node < 9; node++)
            {
                poses.Append(SyntheticCloth.Pose((node % 3) * 10f, 0f, -(node / 3) * 10f));
            }

            var invMasses = geometric.Select(static mass => SyntheticCloth.Num(1f / (mass + MathF.E)));

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{string.Join(", ", Enumerable.Range(0, 9).Select(static node => $"\"$cloth_m0p{node}\""))}} ]
                    m_nNodeCount = 9
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ {{string.Join(", ", invMasses)}} ]
                    m_InitPose = [ {{poses}} ]
                    m_SourceElems = [ 0, 0, 0, 4, 0, 1, 4, 3, 1, 2, 5, 4, 3, 4, 7, 6, 4, 5, 8, 7 ]
                    m_Rods = [ {{rods}} ]
                }
                """);
        }

        /// <summary>
        /// A bend rod held at its rest span bounds every hinge that generates it from below, and the solved paint meets
        /// each bound on top of the model-wide curvature.
        /// </summary>
        [Test]
        public async Task ACappedBendRodBoundsEveryHingeThatGeneratesIt()
        {
            List<int[]> faces = [[0, 1, 5, 6], [6, 5, 10, 11], [11, 10, 15, 16], [1, 2, 7, 5], [5, 7, 12, 10], [10, 12, 17, 15],
                [2, 3, 8, 7], [7, 8, 13, 12], [12, 13, 18, 17], [3, 4, 9, 8], [8, 9, 14, 13], [13, 14, 19, 18]];
            HashSet<(int, int)> network = [(0, 11), (1, 10), (2, 12), (3, 13), (4, 14), (5, 8), (5, 15), (6, 7), (6, 16), (7, 9),
                (7, 17), (8, 18), (9, 19), (10, 13), (11, 12), (12, 14), (15, 18), (16, 17), (17, 19)];
            var (paint, curvature) = ClothExtract.ClothBendStiffnessOverFold(CorrugatedSheet, faces, network, 0.49999997f, keepsCurvature: false);

            using (Assert.Multiple())
            {
                await Assert.That(paint).IsNotNull();
                await Assert.That(curvature).IsEqualTo(0.49999997f);
                await Assert.That((paint?.GetValueOrDefault(10) ?? 0f) + (paint?.GetValueOrDefault(11) ?? 0f)).IsGreaterThanOrEqualTo(0.5605f);
                await Assert.That((paint?.GetValueOrDefault(10) ?? 0f) + (paint?.GetValueOrDefault(12) ?? 0f)).IsGreaterThanOrEqualTo(0.5663f);
            }
        }

        private static FeModel CorrugatedSheet => SyntheticCloth.Load("cloth_sheet_corrugated.kv3");

        /// <summary>
        /// A banded rod between two joints' ring nodes is declared as a two-member <c>ClothSelfCollisionCluster</c>
        /// whose name has no <c>$</c>; the band closed onto the rest length or between one joint's rings declares
        /// nothing.
        /// </summary>
        [Test]
        public async Task ARingRingClusterTieIsItsTwoMemberClusterUnderANameWithoutADollar()
        {
            var tiedModel = SyntheticCloth.Parse(RingClusterTieText);
            var tied = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(tied, tiedModel, tiedModel.BuildBoneChains());

            var restBandModel = SyntheticCloth.Parse(RingClusterTieText.Replace(
                "{ nNode = [ 2, 4 ] flMaxDist = 48.0 flMinDist = 12.0",
                "{ nNode = [ 2, 4 ] flMaxDist = 8.503419 flMinDist = 8.4",
                StringComparison.Ordinal));
            var restBand = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(restBand, restBandModel, restBandModel.BuildBoneChains());

            var sameJointModel = SyntheticCloth.Parse(RingClusterTieText.Replace(
                "{ nNode = [ 2, 4 ] flMaxDist = 48.0 flMinDist = 12.0",
                "{ nNode = [ 2, 3 ] flMaxDist = 48.0 flMinDist = 12.0",
                StringComparison.Ordinal));
            var sameJoint = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(sameJoint, sameJointModel, sameJointModel.BuildBoneChains());

            var emitted = tied.Select(static child => child.Value).ToArray();

            static IEnumerable<KVObject> Members(IEnumerable<KVObject> nodes)
                => nodes.SelectMany(static node => node.GetSubCollection("chain").GetArray("joints")!);

            using (Assert.Multiple())
            {
                await Assert.That(emitted.Select(static node => node.GetStringProperty("_class")).ToArray())
                    .IsEquivalentTo(RingClusterTieClasses, CollectionOrdering.Matching);
                await Assert.That(emitted.Select(static node => node.GetStringProperty("name")).ToArray())
                    .IsEquivalentTo(RingClusterTieNames, CollectionOrdering.Matching);
                await Assert.That(Members(emitted).Select(static joint => joint.GetStringProperty("joint_name")).ToArray())
                    .IsEquivalentTo(RingClusterTieMembers, CollectionOrdering.Matching);
                await Assert.That(Members(emitted).Select(static joint => joint.GetFloatProperty("collision_radius")).ToArray())
                    .IsEquivalentTo(RingClusterTieRadii, CollectionOrdering.Matching);
                await Assert.That(Members(emitted).Select(static joint => joint.GetFloatProperty("stray_radius")).ToArray())
                    .IsEquivalentTo(RingClusterTieStrays, CollectionOrdering.Matching);

                await Assert.That(restBand.Count).IsEqualTo(0);
                await Assert.That(sameJoint.Count).IsEqualTo(0);
            }
        }

        private static readonly string[] RingClusterTieClasses = ["ClothSelfCollisionCluster"];
        private static readonly string[] RingClusterTieNames = ["cluster_cccoattail_1_L_0_cccoattail_2_L_0"];
        private static readonly string[] RingClusterTieMembers = ["$cccoattail_1_L_0", "$cccoattail_2_L_0"];
        private static readonly float[] RingClusterTieRadii = [6f, 6f];
        private static readonly float[] RingClusterTieStrays = [24f, 24f];

        private static string RingClusterTieText => SyntheticCloth.Fixture("cloth_chain_ring_cluster_tie.kv3");

        /// <summary>
        /// A bend rod is read only through the hinge its element pairing builds it from: at its own fold it writes no
        /// paint, and held further open it paints the generating hinge's vertices only.
        /// </summary>
        [Test]
        public async Task ABendRodIsReadOnlyThroughTheHingesItsElementPairingBuildsItFrom()
        {
            List<int[]> faces = [[4, 0, 3], [0, 1, 2, 3], [5, 4, 3, 2]];
            HashSet<(int, int)> network = [(0, 5)];
            var (paint, curvature) = ClothExtract.ClothBendStiffnessOverFold(PairedHingeSheet(1.0513982f), faces,
                network, 0.64999986f, keepsCurvature: false);

            var (folded, foldedCurvature) = ClothExtract.ClothBendStiffnessOverFold(PairedHingeSheet(1.06f),
                faces, network, 0.64999986f, keepsCurvature: false);

            using (Assert.Multiple())
            {
                await Assert.That(paint).IsNull();
                await Assert.That(curvature).IsEqualTo(0.64999986f);
                await Assert.That(folded).IsNotNull();
                await Assert.That(folded!.GetValueOrDefault(3) + folded.GetValueOrDefault(4)).IsGreaterThan(0f);
                await Assert.That(folded.GetValueOrDefault(2)).IsEqualTo(0f);
                await Assert.That(foldedCurvature).IsEqualTo(0.64999986f);
            }
        }

        private static FeModel PairedHingeSheet(float minDist) => SyntheticCloth.Model(
            ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5"], staticNodes: 0,
            poses: [new(-1.7257074f, 20.404041f, 46.822933f), new(-1.7570662f, 20.385893f, 46.739212f),
                new(-2.2579334f, 20.29848f, 47.05001f), new(-2.1855373f, 20.323782f, 47.10828f),
                new(-2.3192735f, 20.206625f, 47.622124f), new(-2.404188f, 20.17036f, 47.61194f)],
            body: $$"""
                m_Rods =
                [
                    { nNode = [ 0, 5 ] flMaxDist = 1.0688722 flMinDist = {{SyntheticCloth.Num(minDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
                """);

        /// <summary>
        /// A sheet paints <c>cloth_collision_layer</c> streams only for the layers some vertex's tree mask clears, 0 on
        /// that vertex; all-set masks paint none.
        /// </summary>
        [Test]
        public async Task ASheetStatesTheCollisionLayersItsCompiledMasksClear()
        {
            var cleared = ClothExtract.ClothCollisionLayerPaints(SyntheticCloth.Parse(LayerMaskText("65533")),
                [1, 2, 3], 3).ToList();
            var whole = ClothExtract.ClothCollisionLayerPaints(SyntheticCloth.Parse(LayerMaskText("65535")),
                [1, 2, 3], 3).ToList();
            var lowFour = ClothExtract.ClothCollisionLayerPaints(SyntheticCloth.Parse(LayerMaskText("65520")),
                [1, 2, 3], 3).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(cleared.Count).IsEqualTo(1);
                await Assert.That(cleared[0].Layer).IsEqualTo(1);
                await Assert.That(cleared[0].Painted).IsEquivalentTo(ClearedLayerPaint, CollectionOrdering.Matching);
                await Assert.That(whole.Count).IsEqualTo(0);
                await Assert.That(lowFour.Select(static paint => paint.Layer).ToArray())
                    .IsEquivalentTo(LowFourLayers, CollectionOrdering.Matching);
            }
        }

        private static readonly float[] ClearedLayerPaint = [0f, 1f, 1f];
        private static readonly int[] LowFourLayers = [0, 1, 2, 3];

        private static string LayerMaskText(string firstMask) => SyntheticCloth.Document(
            ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"], staticNodes: 1,
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f)],
            body: $$"""
                m_TreeCollisionMasks = [ {{firstMask}}, 65535, 65535, 65535, 65535 ]
                """);

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
        /// <c>RegistersVertexSet</c> is true only for hashes in <c>m_VertexSetNames</c>, not for a selection's own
        /// unregistered hash.
        /// </summary>
        [Test]
        public async Task ASelectionTheModelRegistersNoSetForIsNotPainted()
        {
            var feModel = SyntheticCloth.Parse(UnregisteredSelectionText);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.RegistersVertexSet(3027761651)).IsTrue();
                await Assert.That(feModel.RegistersVertexSet(4042757229)).IsFalse();
                await Assert.That(feModel.VertexMaps.Count).IsEqualTo(1);
                await Assert.That(feModel.RegistersVertexSet(feModel.VertexMaps[0].NameHash)).IsFalse();
            }
        }

        private static string UnregisteredSelectionText => SyntheticCloth.Document(
            ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"], staticNodes: 1, body: """
                m_VertexSetNames = [ 3027761651 ]
                m_VertexMaps =
                [
                    { m_Name = "coat_clothVertMap" m_nNameHash = 4042757229 m_nVertexBase = 1 m_nVertexCount = 3 m_Weights = [ 255, 255, 0 ] },
                ]
                """);

        /// <summary>
        /// Contesting planarized shapes are declared smallest first: the five-plane sphere precedes the six-plane one.
        /// </summary>
        [Test]
        public async Task ThePlanarizedShapeOwningFewestPlanesIsDeclaredFirst()
        {
            var order = ClothExtract.PlanarizedShapesInClaimOrder(TwoPlanarizedSpheres());

            using (Assert.Multiple())
            {
                await Assert.That(order.Count).IsEqualTo(2);
                await Assert.That(order[0].GetStringProperty("parent_bone")).IsEqualTo("boneB");
                await Assert.That(order[1].GetStringProperty("parent_bone")).IsEqualTo("boneA");
            }
        }

        /// <summary>A six-plane sphere on <c>boneA</c> and a five-plane one on <c>boneB</c>.</summary>
        private static FeModel TwoPlanarizedSpheres()
        {
            Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY,
                Vector3.UnitZ, -Vector3.UnitZ];
            var bigB = new Vector3(100f, 0f, 0f);
            var poses = new List<string> { SyntheticCloth.Pose(0f, 0f, 0f), SyntheticCloth.Pose(100f, 0f, 0f) };
            var planes = new List<string>();
            var node = 2;
            foreach (var axis in axes)
            {
                var at = axis * 7f;
                poses.Add(SyntheticCloth.Pose(at.X, at.Y, at.Z));
                planes.Add(SpherePlane(0, node++, axis));
            }

            foreach (var axis in axes.Take(5))
            {
                var at = bigB + (axis * 7f);
                poses.Add(SyntheticCloth.Pose(at.X, at.Y, at.Z));
                planes.Add(SpherePlane(1, node++, axis));
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "boneA", "boneB", {{string.Join(", ", Enumerable.Range(2, 11).Select(static i => $"\"n{i}\""))}} ]
                    m_SkelParents = [ -1, -1, {{string.Join(", ", Enumerable.Repeat("0", 6).Concat(Enumerable.Repeat("1", 5)))}} ]
                    m_nNodeCount = 13
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ {{string.Join(", ", Enumerable.Repeat("1.0", 13))}} ]
                    m_InitPose = [ {{string.Concat(poses)}} ]
                    m_CollisionPlanes = [ {{string.Concat(planes)}} ]
                    m_VertexMapValues = [ {{string.Join(", ", Enumerable.Repeat("255", 11))}} ]
                    m_VertexMaps =
                    [
                        { sName = "setA" nNameHash = 1 nVertexBase = 2 nVertexCount = 6 nMapOffset = 0 nScaleSourceNode = -1 flVolumetricSolveStrength = 0.0 vCenterOfMass = [ 0.0, 0.0, 0.0 ] },
                        { sName = "setB" nNameHash = 2 nVertexBase = 8 nVertexCount = 5 nMapOffset = 6 nScaleSourceNode = -1 flVolumetricSolveStrength = 0.0 vCenterOfMass = [ 0.0, 0.0, 0.0 ] },
                    ]
                }
                """);
        }

        private static string SpherePlane(int parent, int node, Vector3 normal)
            => $"{{ nCtrlParent = {parent} nChildNode = {node} flStickiness = 0.0 flStrength = 0.0 "
                + $"m_Plane = {{ m_vNormal = [ {SyntheticCloth.Num(normal.X)}, {SyntheticCloth.Num(normal.Y)}, "
                + $"{SyntheticCloth.Num(normal.Z)} ] m_flOffset = {SyntheticCloth.Num(5f)} }} }},";

        /// <summary>
        /// A capsule whose planes are side contacts square to its axis is still recovered, at radius 4.
        /// </summary>
        [Test]
        public async Task ACapsuleIsRecoveredFromSideContactsWhoseNormalsSpanNoCone()
        {
            var sideContacts = PlanarizedGroup(
            [
                (new Vector3(-8.311506f, -11.224525f, 4.267012f),
                    new Vector3(-0.595091f, -0.803658f, 0f), 4f),
                (new Vector3(-15.225058f, -16.15545f, 4.63776f),
                    new Vector3(-0.685841f, -0.727751f, 0f), 4f),
                (new Vector3(-22.147151f, -21.074872f, 5.004368f),
                    new Vector3(-0.72441f, -0.689337f, 0.006685f), 4.032088f),
            ]);

            var shapes = sideContacts.BuildPlanarizeCapsules();

            using (Assert.Multiple())
            {
                await Assert.That(shapes.Count).IsEqualTo(1);
                await Assert.That(shapes[0].Planarize).IsTrue();
                await Assert.That(shapes[0].Radius0).IsEqualTo(4f).Within(1e-3f);
                await Assert.That(shapes[0].Radius1).IsEqualTo(4f).Within(1e-3f);
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
        /// A one-joint chain reads the same version as a longer chain, with or without other chains.
        /// </summary>
        [Test]
        public async Task AOneJointChainIsNotForcedToVersionZero()
        {
            static int Version(int joints, bool otherChains) => ClothExtract.ClothChainVersion(
                joints, otherChains, rootAllowsRotation: true, rootHasBase: false, lockedJoint: false,
                rigidCloudClusterLock: false, locksJoints: false, basesBulkGraded: null,
                hintsTwistWritten: false, hasUnstagedThinJoint: false);

            using (Assert.Multiple())
            {
                await Assert.That(Version(1, true)).IsEqualTo(Version(4, true));
                await Assert.That(Version(1, true)).IsEqualTo(2);
                await Assert.That(Version(1, false)).IsEqualTo(2);
            }
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
                await Assert.That(ClothExtract.ClothChainVersion(ringless, chain, hasOtherChains: true))
                    .IsEqualTo(2);
            }
        }

        /// <summary>
        /// A proxy sheet fit over a bone outside the position-driven suffix reads as
        /// <c>back_solve_joints_drive_meshes</c> alone; a driven bone, an unfit sheet, or no compiled boundary do not.
        /// </summary>
        [Test]
        public async Task ASheetFittingAnUndrivenBoneStatesDriveMeshesAlone()
        {
            var undriven = SheetFitOverTipBone("m_nFirstPositionDrivenNode = 8");
            var driven = SheetFitOverTipBone("m_nFirstPositionDrivenNode = 7");
            var unbounded = SheetFitOverTipBone("");

            static FeModel.ProxyMesh Sheet(FeModel feModel, int mesh)
                => feModel.BuildProxyMeshes().First(proxy => Array.Exists(proxy.NodeIndices,
                    node => feModel.CtrlNames[node].StartsWith($"$cloth_m{mesh}p", StringComparison.Ordinal)));

            using (Assert.Multiple())
            {
                await Assert.That(undriven.FitMatrixTargets.Count).IsEqualTo(1);
                await Assert.That(string.Join(",", undriven.FitMatrixTargets[7])).IsEqualTo("4,5,6");
                await Assert.That(undriven.IsPositionDriven(7)).IsFalse();

                await Assert.That(undriven.ProxyFitsUndrivenBone(Sheet(undriven, 1))).IsTrue();
                await Assert.That(undriven.ProxyFitsUndrivenBone(Sheet(undriven, 0))).IsFalse();

                await Assert.That(driven.IsPositionDriven(7)).IsTrue();
                await Assert.That(driven.ProxyFitsUndrivenBone(Sheet(driven, 1))).IsFalse();

                await Assert.That(unbounded.HasCompiledFirstPositionDrivenNode).IsFalse();
                await Assert.That(unbounded.ProxyFitsUndrivenBone(Sheet(unbounded, 1))).IsFalse();
            }
        }

        /// <summary>
        /// Two proxy sheets and a tip bone fit over mesh 1's vertices; <paramref name="firstPositionDriven"/> holds
        /// <c>m_nFirstPositionDrivenNode</c>.
        /// </summary>
        private static FeModel SheetFitOverTipBone(string firstPositionDriven) => SyntheticCloth.Model(
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

        /// <summary>
        /// A sheet simulating a rotation-locked static node paints <c>cloth_anchor_free_rotate</c> with or without
        /// <c>flex_cloth_borders</c>; a sheet pinning it or holding no static node paints nothing.
        /// </summary>
        [Test]
        public async Task ASheetPaintsTheRotationLockNoFlagCanState()
        {
            var feModel = SheetFitOverTipBone("m_nFirstPositionDrivenNode = 8");
            var sheet = feModel.BuildProxyMeshes().First(proxy => Array.Exists(proxy.NodeIndices,
                node => feModel.CtrlNames[node].StartsWith("$cloth_m1p", StringComparison.Ordinal)));

            var lockedSimulated = RotationSheet(sheet, [0, 4, 5, 6], [1f, 1f, 1f, 1f]);
            var pinnedInstead = RotationSheet(sheet, [0, 4, 5, 6], [0f, 1f, 1f, 1f]);
            var allSimulated = RotationSheet(sheet, [4, 5, 6, 4], [1f, 1f, 1f, 1f]);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.AllowsRotation(0)).IsFalse();
                await Assert.That(feModel.StaticNodeCount).IsEqualTo(1);

                var flexed = ClothExtract.ClothAnchorFreeRotatePaint(feModel, lockedSimulated, sheetFlexes: true);
                var unflexed = ClothExtract.ClothAnchorFreeRotatePaint(feModel, lockedSimulated, sheetFlexes: false);
                await Assert.That(string.Join(",", flexed ?? [])).IsEqualTo("0,1,1,1");
                await Assert.That(string.Join(",", unflexed ?? [])).IsEqualTo("0,1,1,1");

                await Assert.That(ClothExtract.ClothAnchorFreeRotatePaint(feModel, pinnedInstead, sheetFlexes: false)).IsNull();
                await Assert.That(ClothExtract.ClothAnchorFreeRotatePaint(feModel, pinnedInstead, sheetFlexes: true)).IsNull();

                await Assert.That(ClothExtract.ClothAnchorFreeRotatePaint(feModel, allSimulated, sheetFlexes: true)).IsNull();
                await Assert.That(ClothExtract.ClothAnchorFreeRotatePaint(feModel, allSimulated, sheetFlexes: false)).IsNull();
            }
        }

        /// <summary><paramref name="sheet"/>'s faces over the given nodes and <c>cloth_enable</c> pattern.</summary>
        private static FeModel.ProxyMesh RotationSheet(FeModel.ProxyMesh sheet, int[] nodes, float[] enable)
            => SyntheticCloth.Proxy(nodes, enable, sheet.Faces);
        /// <summary>
        /// A <c>ClothSelfCollisionCluster</c>'s member table states its members, version 0 and its own <c>attrs</c>
        /// defaults.
        /// </summary>
        [Test]
        public async Task AClusterMemberTableStatesItsOwnDefaults()
        {
            var cluster = ClothExtract.MakeClothSelfCollisionCluster(
                "cluster_0", ["j1", "j2"], radius: 6f, strayRadius: 24f);
            var chain = cluster.GetSubCollection("chain");

            static string[] Keys(KVObject table) => table is null ? [] : [.. table.Select(a => a.Key)];
            static float[] Numbers(KVObject table) => table is null
                ? []
                : [table.GetSubCollection("stiffness").GetFloatProperty("default"),
                   table.GetSubCollection("stray_radius").GetFloatProperty("default"),
                   table.GetSubCollection("collision_radius").GetFloatProperty("default")];

            string[] expectedMembers = ["j1", "j2"];
            string[] expectedKeys = ["joint_name", "stiffness", "stray_radius", "collision_radius"];
            float[] expectedNumbers = [1f, 2f, 2f];

            using (Assert.Multiple())
            {
                await Assert.That(chain.GetSubCollection("joints")
                    .Select(j => ((KVObject)j.Value!).GetStringProperty("joint_name")))
                    .IsEquivalentTo(expectedMembers);
                await Assert.That(chain.GetInt32Property("version")).IsEqualTo(0);
                await Assert.That(Keys(chain.GetSubCollection("attrs"))).IsEquivalentTo(expectedKeys);
                await Assert.That(Numbers(chain.GetSubCollection("attrs"))).IsEquivalentTo(expectedNumbers);
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
        /// A sheet with fewer face corners than vertex slots gets all-pinned filler triangles until every slot has a
        /// corner; enough corners, no faces, or fewer than three pins leave the faces unchanged.
        /// </summary>
        [Test]
        public async Task ASheetShortOfCornersIsGivenAllPinnedFillerFaces()
        {
            var shortOfCorners = CornerSheet([0, 0, 0, 0, 4, 5], [[0, 1, 2, 3]]);
            var evenCorners = CornerSheet([0, 0, 0, 0, 4, 5], [[0, 1, 2, 3], [0, 1, 2, 3]]);
            var faceless = CornerSheet([0, 0, 0, 0, 4, 5], []);
            var twoPins = CornerSheet([0, 0, 4, 5, 4, 5], [[0, 1, 2, 3]]);

            static List<int[]> Padded(FeModel.ProxyMesh sheet)
                => ClothExtract.PadSheetCornersToSlotCount(sheet, sheet.Positions.Length);

            static string Shape(List<int[]> faces)
                => string.Join(" ", faces.Select(f => string.Concat(f)));

            using (Assert.Multiple())
            {
                var padded = Padded(shortOfCorners);
                await Assert.That(padded.Count).IsEqualTo(2);
                await Assert.That(padded.Sum(f => f.Length)).IsGreaterThanOrEqualTo(shortOfCorners.Positions.Length);
                await Assert.That(padded.Skip(1).SelectMany(f => f)
                    .All(c => shortOfCorners.ClothEnable[c] == 0f)).IsTrue();
                await Assert.That(Shape(padded).StartsWith("0123 ", StringComparison.Ordinal)).IsTrue();

                await Assert.That(Shape(Padded(evenCorners))).IsEqualTo("0123 0123");
                await Assert.That(Shape(Padded(faceless))).IsEqualTo(string.Empty);
                await Assert.That(Shape(Padded(twoPins))).IsEqualTo("0123");
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
        /// A chain's <c>mass</c> default is the multiplier most of its simulating joints carry, sheet or no sheet, and
        /// only the others state their own; an even split states 1 and every joint's own value.
        /// </summary>
        [Test]
        public async Task AChainStatesTheMassMostOfItsJointsCarry()
        {
            static FeModel Sheeted(params float[] multipliers) => Chain(true, multipliers);

            static FeModel Chain(bool sheet, params float[] multipliers) => SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1", "j2", "j3", "j4", {{(sheet ? "\"$cloth_m0p0\"" : "\"spare\"")}} ]
                    m_SkelParents = [ -1, 0, 1, 2, 3, -1 ]
                    m_nNodeCount = 6
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, {{string.Join(", ", multipliers.Select((m, i) =>
                        (1f / ((i == multipliers.Length - 1 ? 24f : 48f) * m * m))
                            .ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}}, 0.5 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(3f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(6f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(9f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(12f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 20f, 0f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 3f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 3f, 1f)}}
                        {{SyntheticCloth.RigidRod(2, 3, 3f, 1f)}}
                        {{SyntheticCloth.RigidRod(3, 4, 3f, 1f)}}
                    ]
                }
                """);

            static float Default(FeModel feModel)
                => feModel.RecoverChainMassDefault(feModel.BuildBoneChains()[0]);

            static string Stated(FeModel feModel)
            {
                var chain = feModel.BuildBoneChains()[0];
                var chainDefault = feModel.RecoverChainMassDefault(chain);
                return string.Join(" ", chain.Joints
                    .Where(joint => feModel.RecoverJointMass(joint.Node, chainDefault) is not null)
                    .Select(joint => joint.Name));
            }

            using (Assert.Multiple())
            {
                await Assert.That(Default(Sheeted(0.5f, 0.5f, 0.5f, 0.5f))).IsEqualTo(0.5f).Within(1e-3f);
                await Assert.That(Default(Sheeted(1f, 1f, 1f, 1f))).IsEqualTo(1f).Within(1e-3f);
                await Assert.That(Default(Sheeted(2f, 2f, 2f, 2f))).IsEqualTo(2f).Within(1e-3f);
                await Assert.That(Stated(Sheeted(0.5f, 0.5f, 0.5f, 0.5f))).IsEqualTo(string.Empty);
                await Assert.That(Stated(Sheeted(2f, 2f, 2f, 2f))).IsEqualTo(string.Empty);

                await Assert.That(Default(Sheeted(0.5f, 0.5f, 0.5f, 2f))).IsEqualTo(0.5f).Within(1e-3f);
                await Assert.That(Stated(Sheeted(0.5f, 0.5f, 0.5f, 2f))).IsEqualTo("j4");

                await Assert.That(Default(Sheeted(0.5f, 0.5f, 2f, 2f))).IsEqualTo(1f).Within(1e-3f);
                await Assert.That(Stated(Sheeted(0.5f, 0.5f, 2f, 2f))).IsEqualTo("j1 j2 j3 j4");

                await Assert.That(Default(Chain(false, 0.5f, 0.5f, 0.5f, 0.5f))).IsEqualTo(0.5f).Within(1e-3f);

                await Assert.That(Sheeted(1f, 1f, 1f, 1f).RecoverJointMassMultiplier(0)).IsNull();
            }
        }

        /// <summary>
        /// Under <c>explicit_masses</c> an inverse mass of 1 reads as a mass of 1; on a geometric chain it stays
        /// unread.
        /// </summary>
        [Test]
        public async Task AnExplicitMassOfOneIsAValueAndNotTheWeighedNothingSentinel()
        {
            var explicitChain = SyntheticCloth.Parse(ExplicitMassChainText);
            var geometric = SyntheticCloth.Parse(ExplicitMassChainText
                .Replace("flWeight0 = 0.333333", "flWeight0 = 0.5", StringComparison.Ordinal)
                .Replace("flWeight0 = 0.666667", "flWeight0 = 0.5", StringComparison.Ordinal));

            using (Assert.Multiple())
            {
                await Assert.That(explicitChain.RecoverJointMassMultiplier(2)!.Value).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(explicitChain.RecoverJointMassMultiplier(6)!.Value).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(explicitChain.RecoverChainMassDefault(explicitChain.BuildBoneChains()[0]))
                    .IsEqualTo(1f).Within(1e-4f);
                await Assert.That(explicitChain.RecoverJointMass(4, 1f)!.Value).IsEqualTo(0.5f).Within(1e-4f);
                await Assert.That(explicitChain.RecoverJointMass(2, 1f)).IsNull();

                await Assert.That(geometric.HasExplicitMasses).IsFalse();
                await Assert.That(geometric.RecoverJointMassMultiplier(2)).IsNull();
            }
        }

        /// <summary>A sheet with only a <c>cloth_enable</c> pattern and faces.</summary>
        private static FeModel.ProxyMesh CornerSheet(float[] enable, List<int[]> faces)
            => SyntheticCloth.Proxy([.. Enumerable.Range(0, enable.Length)], enable, faces);

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

        private static readonly string[] SecondDeclarationRunMembers =
            ["coattail_1_L", "coattail_2_L", "coattail_end_L"];
        private static readonly string[] SecondDeclarationRunSimulating = ["true", "true", "true"];
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
        /// On the proxy sheet route a run declared twice gets its second declaration and simulates, its root staying
        /// static; a single declaration emits none.
        /// </summary>
        [Test]
        public async Task AMarkedRunIsReDeclaredOnTheSheetRouteToo()
        {
            static string Extract(string fixture)
            {
                using var resource = new Resource();
                resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", fixture));
                return new ModelExtract(resource, new NullFileLoader()).ToValveModel();
            }

            static string LastSimulate(string document, string bone)
            {
                var simulate = "<no declaration>";
                for (var at = document.IndexOf("joint_name = \"" + bone + "\"", StringComparison.Ordinal);
                    at >= 0;
                    at = document.IndexOf("joint_name = \"" + bone + "\"", at + 1, StringComparison.Ordinal))
                {
                    var key = document.IndexOf("simulate = ", at, StringComparison.Ordinal);
                    if (key < 0)
                    {
                        break;
                    }

                    var end = document.IndexOfAny(['\n', '\r'], key);
                    simulate = document[(key + "simulate = ".Length)..end].Trim();
                }

                return simulate;
            }

            var redeclared = Extract("cloth_sheet_redeclared_run.vmdl_c");
            var single = Extract("cloth_sheet_single_run.vmdl_c");
            var members = SecondDeclarationRunMembers;
            var simulating = SecondDeclarationRunSimulating;

            using (Assert.Multiple())
            {
                await Assert.That(redeclared).Contains("coattail_0_L_second");
                await Assert.That(members.Select(bone => LastSimulate(redeclared, bone)))
                    .IsEquivalentTo(simulating, CollectionOrdering.Matching);

                await Assert.That(LastSimulate(redeclared, "coattail_0_L")).IsEqualTo("false");

                await Assert.That(single).DoesNotContain("_second");
                await Assert.That(members.Select(bone => LastSimulate(single, bone)))
                    .IsEquivalentTo(simulating, CollectionOrdering.Matching);
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
        /// A back-solving sheet whose freed pins carry node bases states <c>flex_cloth_borders</c>; pins without bases,
        /// or a face reaching no pin, do not.
        /// </summary>
        [Test]
        public async Task ABackSolvedSheetTakesFlexClothBordersFromItsPinsOwnNodeBases()
        {
            static FeModel Model(string bases)
            {
                var model = SyntheticCloth.Model(
                    ["anchor", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"], staticNodes: 3,
                        parents: [-1, 0, 0, 0, 0],
                    poses: [new(0f, 0f, 0f), new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f)],
                    body: $$"""
                        m_nRotLockStaticNodes = 1
                        m_NodeBases = [ {{bases}} ]
                        """);
                model.SkeletonBoneParents = new Dictionary<string, string?> { ["anchor"] = "spine" };
                return model;
            }
            static string Base(int node)
                => $"{{ nNode = {node} nDummy = [ 0, 0, 0 ] nNodeX0 = 3 nNodeX1 = 4 nNodeY0 = 1 nNodeY1 = 2 qAdjust = [ 0.0, 0.0, 0.0, 1.0 ] }},";
            static FeModel.ProxyMesh Sheet(List<int[]> faces) => SyntheticCloth.Proxy([1, 2, 3, 4], [0f, 0f, 1f, 1f], faces,
                [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f)]);

            var quad = Sheet([[0, 1, 3, 2]]);
            var flexed = Model(Base(1) + Base(2));
            var painted = Model(string.Empty);
            var unreached = Sheet([[0, 1, 2]]);

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.ProxyFlexesClothBorders(flexed, quad, true, true)).IsTrue();
                await Assert.That(ClothExtract.ProxyFlexesClothBorders(painted, quad, true, true)).IsFalse();
                await Assert.That(ClothExtract.ProxyFlexesClothBorders(flexed, unreached, true, true)).IsFalse();
                await Assert.That(ClothExtract.ProxyFlexesClothBorders(flexed, quad, false, true)).IsTrue();
                await Assert.That(ClothExtract.FlexedPinsStateClothBorders(flexed, quad)).IsTrue();
                await Assert.That(ClothExtract.FlexedPinsStateClothBorders(painted, quad)).IsFalse();
                await Assert.That(ClothExtract.FlexedPinsStateClothBorders(flexed, unreached)).IsFalse();
            }
        }

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
        /// A stiff hinge's stiffness is <c>(End0Weight + End1Weight - 2 * MidWeight) / 3</c>, biased or not, with the
        /// angle and motion bias unchanged.
        /// </summary>
        [Test]
        public async Task AStiffHingeStiffnessIsTheBendWeightsLinearCombination()
        {
            var biased = BiasedKelagerModel("-0.5, 0.25, 0.25").GetStiffHinge(1);
            var unbiased = BiasedKelagerModel("-0.4615385, 0.1153846, 0.4615385").GetStiffHinge(1);

            using (Assert.Multiple())
            {
                await Assert.That(unbiased?.Stiffness ?? float.NaN).IsEqualTo(0.5f).Within(1e-4f);

                await Assert.That(biased?.Angle ?? float.NaN).IsEqualTo(120f).Within(0.01f);
                await Assert.That(unbiased?.Angle ?? float.NaN).IsEqualTo(120f).Within(0.01f);
                await Assert.That(biased?.MotionBias ?? float.NaN).IsEqualTo(0f);
                await Assert.That(unbiased?.MotionBias ?? float.NaN).IsEqualTo(0f);

                await Assert.That(biased?.Stiffness ?? float.NaN).IsEqualTo(0.5f).Within(1e-4f);
            }
        }

        /// <summary>One bend over three nodes of unequal inverse mass.</summary>
        private static FeModel BiasedKelagerModel(string weights) => SyntheticCloth.Model(
            ["mid", "end0", "end1"], staticNodes: 0, parents: [-1, 0, 0], invMasses: "2.0, 1.0, 4.0",
            poses: [new(0f, 1f, 0f), new(-1f, 0f, 0f), new(1f, 0f, 0f)],
            body: $$"""
                m_KelagerBends =
                [
                    { nNode = [ 0, 1, 2 ] flWeight = [ {{weights}} ] flHeight0 = 0.8164966 },
                ]
                """);

        /// <summary>
        /// A bend height on the 0.001 floor recovers a zero angle; a height above it recovers its angle, at the same
        /// stiffness.
        /// </summary>
        [Test]
        public async Task ABendHeightOnTheCompilerFloorStatesNoAngle()
        {
            var onTheFloor = FlatKelagerModel(0.001f).GetStiffHinge(1);
            var aboveTheFloor = FlatKelagerModel(0.01f).GetStiffHinge(1);

            using (Assert.Multiple())
            {
                await Assert.That(aboveTheFloor?.Angle ?? float.NaN).IsGreaterThan(0f);
                await Assert.That(onTheFloor?.Stiffness ?? float.NaN).IsEqualTo(1f).Within(1e-4f);
                await Assert.That(aboveTheFloor?.Stiffness ?? float.NaN).IsEqualTo(1f).Within(1e-4f);

                await Assert.That(onTheFloor?.Angle ?? float.NaN).IsEqualTo(0f);
            }
        }

        /// <summary>One bend over three collinear nodes.</summary>
        private static FeModel FlatKelagerModel(float height) => SyntheticCloth.Model(
            ["mid", "end0", "end1"], staticNodes: 0, parents: [-1, 0, 0],
            poses: [new(0f, 0f, 0f), new(-1f, 0f, 0f), new(1f, 0f, 0f)],
            body: $$"""
                m_KelagerBends =
                [
                    { nNode = [ 0, 1, 2 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = {{SyntheticCloth.Num(height)}} },
                ]
                """);

        /// <summary>
        /// A sheet whose capped rods no paint can satisfy states the fold they allow as its model-wide value; a loose
        /// rod, kept curvature, or a recoverable paint keep it at zero.
        /// </summary>
        [Test]
        public async Task ASheetWithNoPaintStatesAFoldItsCappedRodsAllow()
        {
            var faces = HingeGridFaces;
            var network = HingeGridNetwork;

            var (bounded, boundedCurvature) = ClothExtract.ClothBendStiffnessOverFold(
                CappedAgainstFlatHingeGrid(20f), faces, network, 0f, keepsCurvature: false);
            var (loose, looseCurvature) = ClothExtract.ClothBendStiffnessOverFold(
                CappedAgainstFlatHingeGrid(0f), faces, network, 0f, keepsCurvature: false);
            var (suspended, suspendedCurvature) = ClothExtract.ClothBendStiffnessOverFold(
                CappedAgainstFlatHingeGrid(20f), faces, network, 0f, keepsCurvature: true);
            var (painted, paintedCurvature) = ClothExtract.ClothBendStiffnessOverFold(
                LeastFoldedHingeGrid, faces, network, 0.375f, keepsCurvature: false);

            using (Assert.Multiple())
            {
                await Assert.That(bounded).IsNull();
                await Assert.That(boundedCurvature).IsEqualTo(1f).Within(0.01f);
                await Assert.That(loose).IsNull();
                await Assert.That(looseCurvature).IsEqualTo(0f);
                await Assert.That(suspended).IsNull();
                await Assert.That(suspendedCurvature).IsEqualTo(0f);
                await Assert.That(painted).IsNotNull();
                await Assert.That(paintedCurvature).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// The 4x3 grid of <see cref="LeastFoldedHingeGrid"/> with every hinge read flat except the lower vertical one,
        /// whose rod (8, 10) sits at <paramref name="lowerHingeMinDist"/>. At its rest span of 20 that rod is capped and
        /// bounds its hinge from below; short of it the hinge states a fold of its own instead.
        /// </summary>
        private static FeModel CappedAgainstFlatHingeGrid(float lowerHingeMinDist) => SyntheticCloth.Model(
            ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7", "$cloth_m0p8", "$cloth_m0p9", "$cloth_m0p10", "$cloth_m0p11"],
                staticNodes: 0,
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, -10f),
                new(20f, 0f, -10f), new(30f, 0f, -10f), new(0f, 0f, -20f), new(10f, 0f, -20f), new(20f, 0f, -20f),
                new(30f, 0f, -20f)],
            body: $$"""
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMaxDist = 20.0 flMinDist = 0.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMaxDist = 20.0 flMinDist = 0.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 6 ] flMaxDist = 20.0 flMinDist = 0.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 7 ] flMaxDist = 20.0 flMinDist = 0.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 8, 10 ] flMaxDist = 20.0 flMinDist = {{SyntheticCloth.Num(lowerHingeMinDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 9, 11 ] flMaxDist = 20.0 flMinDist = 0.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 8 ] flMaxDist = 20.0 flMinDist = 0.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 9 ] flMaxDist = 20.0 flMinDist = 0.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 10 ] flMaxDist = 20.0 flMinDist = 0.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 11 ] flMaxDist = 20.0 flMinDist = 0.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
                """);

        /// <summary>
        /// A joint bent twice states each declaration's own angle by rank; a joint bent once has no rank 1.
        /// </summary>
        [Test]
        public async Task EachDeclarationStatesTheBendOfItsOwnRank()
        {
            var doubled = TwiceBentKelagerModel(0.2044406f, 0.5887841f);
            var once = TwiceBentKelagerModel(0.2044406f, null);

            using (Assert.Multiple())
            {
                await Assert.That(doubled.GetStiffHinge(1)?.Angle ?? float.NaN).IsEqualTo(35f).Within(0.05f);
                await Assert.That(once.GetStiffHinge(1)?.Angle ?? float.NaN).IsEqualTo(35f).Within(0.05f);

                await Assert.That(once.GetStiffHinge(1, 1)).IsNull();

                await Assert.That(doubled.GetStiffHinge(1, 1)?.Angle ?? float.NaN).IsEqualTo(120f).Within(0.05f);
            }
        }

        /// <summary>One joint bent once, or twice at a wider second angle.</summary>
        private static FeModel TwiceBentKelagerModel(float first, float? second) => SyntheticCloth.Model(
            ["mid", "joint", "end1"], staticNodes: 0, parents: [-1, 0, 0],
            poses: [new(0f, 0.2f, 0f), new(-1f, 0f, 0f), new(1f, 0f, 0f)],
            body: $$"""
                m_KelagerBends =
                [
                    { nNode = [ 0, 1, 2 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = {{SyntheticCloth.Num(first)}} },
                    {{(second is { } h ? $"{{ nNode = [ 0, 1, 2 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = {SyntheticCloth.Num(h)} }}," : string.Empty)}}
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
                await Assert.That(ClothExtract.ClothChainVersion(permuted, permutedChain, hasOtherChains: false)).IsEqualTo(2);
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
        /// A cross-chain surplus rod off its rest distance with no two-corner source element is a two-member cluster,
        /// members reversed; a recorded pair or a rod at rest distance stays a spring.
        /// </summary>
        [Test]
        public async Task AnUnrecordedOffRestSurplusRodIsAClusterNotASpring()
        {
            var law = CrossChainTieModel(12f, 48f, string.Empty);
            var recorded = CrossChainTieModel(12f, 48f, "0, 1, 0, 0, 2, 3");
            var rest = CrossChainTieModel(20f, 20f, string.Empty);

            using (Assert.Multiple())
            {
                await Assert.That(SurplusClasses(recorded)).DoesNotContain("ClothSelfCollisionCluster");

                await Assert.That(SurplusClasses(rest)).IsEquivalentTo(SpringOnly, CollectionOrdering.Matching);

                await Assert.That(SurplusClasses(law)).IsEquivalentTo(ClusterOnly, CollectionOrdering.Matching);
                await Assert.That(SurplusClusterMembers(law)).IsEquivalentTo(ReversedTie, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] SpringOnly = ["ClothSpring"];
        private static readonly string[] ClusterOnly = ["ClothSelfCollisionCluster"];
        private static readonly string[] ReversedTie = ["b1", "a1"];

        private static string[] SurplusClusterMembers(FeModel feModel)
        {
            var children = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(children, feModel, feModel.BuildBoneChains());
            return children.Where(static c => c.Value.GetStringProperty("_class") == "ClothSelfCollisionCluster")
                .SelectMany(static c => c.Value.GetSubCollection("chain").GetArray("joints"))
                .Select(static j => j.GetStringProperty("joint_name")).ToArray();
        }

        private static string[] SurplusClasses(FeModel feModel)
        {
            var children = KVObject.Array();
            ClothExtract.AddClothChainSurplusRods(children, feModel, feModel.BuildBoneChains());
            return children.Select(static c => c.Value.GetStringProperty("_class")).ToArray();
        }

        /// <summary>
        /// Two one-joint chains 20 apart with a banded rod between their joints and the given source elements.
        /// </summary>
        private static FeModel CrossChainTieModel(float min, float max, string sourceElems) => SyntheticCloth.Model(
            ["rootA", "rootB", "a1", "b1"], staticNodes: 2, parents: [-1, -1, 0, 1],
            poses: [new(0f, 0f, 0f), new(20f, 0f, 0f), new(0f, 0f, -10f), new(20f, 0f, -10f)],
            body: $$"""
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(1, 3, 10f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 3, min, max, 1f)}}
                ]
                m_SourceElems = [ {{sourceElems}} ]
                """);

        /// <summary>
        /// A rod on a folded pair, declared or folded, reads no mass multiplier on its endpoint.
        /// </summary>
        [Test]
        public async Task ADeclaredRodOnAFoldedPairIsInTheMassPass()
        {
            var declared = FoldedPairMassModel(folded: false);
            var folded = FoldedPairMassModel(folded: true);

            using (Assert.Multiple())
            {
                await Assert.That(folded.RecoverMassMultiplier(1)).IsNull();
                await Assert.That(declared.RecoverMassMultiplier(1)).IsNull();
            }
        }

        /// <summary>
        /// <see cref="FoldedChainModel"/> with unequal fold-pair masses: declared, a-c weighs at 0.5; folded, it carries
        /// the mass ratio.
        /// </summary>
        private static FeModel FoldedPairMassModel(bool folded) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "a", "b", "c" ]
                m_SkelParents = [ -1, 0, 0, 2 ]
                m_nNodeCount = 4
                m_nStaticNodes = 1
                m_NodeInvMasses = {{(folded ? "[ 0.0, 0.0559017, 0.02421412, 0.03952847 ]" : "[ 0.0, 0.02139819, 0.02421412, 0.01846971 ]")}}
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(-1f, 0f, -2f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -2f)}}
                    {{SyntheticCloth.Pose(1f, 0f, -5f)}}
                ]
                m_SourceElems = [ 0, 0, 2, 0, 0, 1, 2, 0, 2, 3 ]
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 2.236068f, 1f)}}
                    {{SyntheticCloth.RigidRod(0, 2, 2f, 1f)}}
                    {{SyntheticCloth.RigidRod(2, 3, 3.1622777f, 1f)}}
                    { nNode = [ 1, 3 ] flMinDist = 2.0 flMaxDist = 3.6055512 flWeight0 = {{(folded ? "0.585786" : "0.5")}} flRelaxationFactor = 1.0 },
                ]
            }
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
                await Assert.That(ClothExtract.ClothChainVersion(bare, bareChain, hasOtherChains: false)).IsEqualTo(2);
                await Assert.That(ClothExtract.ClothChainVersion(leaf, leafChain, hasOtherChains: false)).IsEqualTo(2);

                await Assert.That(ClothExtract.ClothChainVersion(fitted, fittedChain, hasOtherChains: false)).IsLessThan(2);
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
        /// Fold-weighted rods on a face-kept sheet read as <c>add_stiffness_rods</c> and are derived; even-split rods
        /// leave the switch off.
        /// </summary>
        [Test]
        public async Task AFaceKeptSheetsFoldsAreTheSwitchsAndNoSprings()
        {
            var folded = FoldedSheetModel(0.666667f);
            var declared = FoldedSheetModel(0.5f);

            static (bool Switch, HashSet<(int, int)> Derived) Read(FeModel feModel)
            {
                var proxies = feModel.BuildProxyMeshes().Select(static (proxy, i) => ($"p{i}.dmx", $"p{i}", proxy)).ToList();
                var rods = ClothExtract.ClothRodsFromSurface(feModel, proxies);
                return (rods.GeneratesBendRods, rods.Derived);
            }

            var (declaredSwitch, declaredDerived) = Read(declared);
            var (foldedSwitch, foldedDerived) = Read(folded);

            using (Assert.Multiple())
            {
                await Assert.That(declaredSwitch).IsFalse();
                await Assert.That(declaredDerived.Contains((2, 6))).IsFalse();

                await Assert.That(foldedSwitch).IsTrue();
                await Assert.That(foldedDerived.Contains((2, 6)) && foldedDerived.Contains((3, 7))).IsTrue();
            }
        }

        /// <summary>
        /// Three face-kept quads under a static top row and banded rods across the middle quad at <paramref
        /// name="weight"/>.
        /// </summary>
        private static FeModel FoldedSheetModel(float weight) => SyntheticCloth.Model(
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

        /// <summary>
        /// The paint solve holds only the hinges that set each rod, so every capped network rod folds back to its
        /// minimum; recoverable and unexplainable sheets behave as before.
        /// </summary>
        [Test]
        public async Task APaintSolveHoldsOnlyTheHingesThatSetItsRods()
        {
            List<int[]> faces = [[6, 0, 7], [16, 6, 7, 17], [1, 8, 9], [18, 17, 9, 8], [16, 17, 18], [2, 10, 11], [16, 19, 11, 10], [18, 12, 13, 19], [12, 3, 13], [16, 18, 19], [20, 22, 23, 21], [4, 14, 15, 5], [14, 20, 21, 15], [22, 24, 25, 23], [24, 26, 27, 25], [26, 28, 27]];
            HashSet<(int, int)> network = [(0, 16), (0, 17), (1, 17), (1, 18), (2, 16), (2, 19), (3, 18), (3, 19), (4, 20), (5, 21), (6, 18), (7, 18), (8, 16), (9, 16), (10, 18), (11, 18), (12, 16), (13, 16), (14, 22), (15, 23), (17, 19), (20, 24), (21, 25), (22, 26), (23, 27), (24, 28), (25, 28)];
            var sheet = PaintSolveSheet;
            var (paint, curvature) = ClothExtract.ClothBendStiffnessOverFold(sheet, faces, network, 0.9939643f, keepsCurvature: false);

            var gridFaces = HingeGridFaces;
            var gridNetwork = HingeGridNetwork;
            var (painted, paintedCurvature) = ClothExtract.ClothBendStiffnessOverFold(
                LeastFoldedHingeGrid, gridFaces, gridNetwork, 0.375f, keepsCurvature: false);
            var (bounded, boundedCurvature) = ClothExtract.ClothBendStiffnessOverFold(
                CappedAgainstFlatHingeGrid(20f), gridFaces, gridNetwork, 0f, keepsCurvature: false);

            using (Assert.Multiple())
            {
                await Assert.That(painted).IsNotNull();
                await Assert.That(paintedCurvature).IsEqualTo(0f);
                await Assert.That(bounded).IsNull();
                await Assert.That(boundedCurvature).IsEqualTo(1f).Within(0.01f);

                await Assert.That(FoldedRodMisses(sheet, faces, network, paint, curvature)).IsEmpty();
            }
        }

        /// <summary>
        /// The network rods whose minimum the paint and <paramref name="curvature"/> do not rebuild: each generating
        /// hinge folds by clamp((paint[u] + paint[v]) * pi / 2 + curvature * pi, 0, pi), and the rod takes the smallest
        /// fold, capped at its rest span.
        /// </summary>
        private static List<(int, int)> FoldedRodMisses(FeModel sheet, List<int[]> faces, HashSet<(int, int)> network,
            Dictionary<int, float>? paint, float curvature, bool tight = false)
        {
            var positions = sheet.InitPosePositions;
            var generators = new Dictionary<(int, int), List<(int, int)>>();
            foreach (var (hinge, a, b) in FeModel.BendRodGenerators(faces))
            {
                var pair = a < b ? (a, b) : (b, a);
                (generators.TryGetValue(pair, out var known) ? known : generators[pair] = []).Add(hinge);
            }

            var misses = new List<(int, int)>();
            foreach (var rod in sheet.Rods)
            {
                var pair = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                if (!network.Contains(pair))
                {
                    continue;
                }

                var rest = Vector3.Distance(positions[rod.NodeA], positions[rod.NodeB]);
                var span = rest;
                foreach (var hinge in generators.GetValueOrDefault(pair) ?? [])
                {
                    var axis = Vector3.Normalize(positions[hinge.Item2] - positions[hinge.Item1]);
                    var toA = positions[rod.NodeA] - positions[hinge.Item1];
                    var toB = positions[rod.NodeB] - positions[hinge.Item1];
                    var alongA = Vector3.Dot(toA, axis);
                    var alongB = Vector3.Dot(toB, axis);
                    var riseA = (toA - (alongA * axis)).Length();
                    var riseB = (toB - (alongB * axis)).Length();
                    var sum = (paint?.GetValueOrDefault(hinge.Item1) ?? 0f) + (paint?.GetValueOrDefault(hinge.Item2) ?? 0f);
                    var fold = Math.Clamp((sum * MathF.PI / 2f) + (curvature * MathF.PI), 0f, MathF.PI);
                    var folded = MathF.Sqrt(((alongA - alongB) * (alongA - alongB)) + (riseA * riseA) + (riseB * riseB)
                        - (2f * riseA * riseB * MathF.Cos(fold)));
                    span = MathF.Min(span, folded);
                }

                if (MathF.Abs(span - rod.MinDist) > (tight ? MathF.Max(1e-3f, 1e-4f * rod.MinDist) : 1e-3f * MathF.Max(1f, rod.MinDist)))
                {
                    misses.Add(pair);
                }
            }

            return misses;
        }

        private static FeModel PaintSolveSheet => SyntheticCloth.Load("cloth_sheet_paint_solve.kv3");

        /// <summary>
        /// A ClothNode on an <c>m_Ropes</c> run states alignment 2 for an element node and 1 for a bone node; nodes on
        /// no run, or a model with no ropes, keep 0.
        /// </summary>
        [Test]
        public async Task AClothNodeOnARopeRunStatesTheAlignmentThatLetsItRope()
        {
            var roped = RopeClothNodeModel("[ 3, 0, 2 ]", 1);
            var bare = RopeClothNodeModel("[ ]", 0);

            using (Assert.Multiple())
            {
                await Assert.That(RopeAlignments(bare)).IsEquivalentTo(NoRopeAlignments, CollectionOrdering.Matching);

                await Assert.That(RopeAlignments(roped)).IsEquivalentTo(RopedAlignments, CollectionOrdering.Matching);
            }
        }

        private static readonly int[] NoRopeAlignments = [0, 0, 0];
        private static readonly int[] RopedAlignments = [1, 0, 2];

        private static int[] RopeAlignments(FeModel feModel) =>
        [
            ClothExtract.MakeClothNode(feModel, "joint1", 0, isStaticNode: true).GetInt32Property("transform_alignment"),
            ClothExtract.MakeClothNode(feModel, "joint1", 1, elementName: "clothNode_joint2").GetInt32Property("transform_alignment"),
            ClothExtract.MakeClothNode(feModel, "joint1", 2, elementName: "clothNode_joint3").GetInt32Property("transform_alignment"),
        ];

        /// <summary>A static bone and two element nodes under it, the second on a rope to the bone.</summary>
        private static FeModel RopeClothNodeModel(string ropes, int ropeCount) => SyntheticCloth.Model(
            ["joint1", "$cloth_node_clothNode_joint2", "$cloth_node_clothNode_joint3"], staticNodes: 1,
            poses: [new(0f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, -10f)],
            body: $$"""
                m_nRopeCount = {{ropeCount}}
                m_Ropes = {{ropes}}
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
                await Assert.That(ClothExtract.ClothChainVersion(free, freeChain, hasOtherChains: false)).IsEqualTo(2);

                await Assert.That(ClothExtract.ClothChainVersion(locked, lockedChain, hasOtherChains: false)).IsLessThan(2);
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
        /// A fitless vertex whose offset network names a bone with a reverse offset keeps its paint although another
        /// vertex paints that bone; without the reverse offset it stays deferred.
        /// </summary>
        [Test]
        public async Task AFitlessVertexOnABackSolvedBoneKeepsItsPaintWhereAnotherVertexPaintsIt()
        {
            var backSolved = FitlessOnPaintedBone("{ vOffset = [ 0.0, 0.0, 0.0 ] nBoneCtrl = 6 nTargetNode = 4 }");
            var unmarked = FitlessOnPaintedBone(string.Empty);

            const int Fitless = 4;
            var recovered = backSolved.RecoveredSkinWeights.GetValueOrDefault(Fitless, []);

            using (Assert.Multiple())
            {
                await Assert.That(unmarked.RecoveredSkinWeights.ContainsKey(Fitless)).IsFalse();
                await Assert.That(unmarked.DeferredOffsetSkinWeights.ContainsKey(Fitless)).IsTrue();

                await Assert.That(recovered.Length).IsEqualTo(2);
                await Assert.That(recovered.Length == 2 && recovered[0].Bone == "bone_2" && recovered[1].Bone == "bone_1").IsTrue();
                await Assert.That(recovered.Length == 2 ? recovered[0].Weight : -1f).IsEqualTo(0.6f).Within(1e-4f);
            }
        }

        /// <summary>
        /// A sheet fit on bone_3 whose vertex 4 has no fit row and paints bone_2 0.6 and bone_1 0.4; <paramref
        /// name="reverseOffsets"/> holds <c>m_ReverseOffsets</c>.
        /// </summary>
        private static FeModel FitlessOnPaintedBone(string reverseOffsets) => SyntheticCloth.Model(
            ["bone_0", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "bone_1", "bone_2", "bone_3"], staticNodes: 2,
                parents: [-1, 0, 5, 7, 6, 0, 5, 6],
            poses: [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 0f, -8f), new(1f, 0f, -24f), new(1f, 0f, -16f), new(0f, 0f, -8f),
                new(0f, 0f, -16f), new(0f, 0f, -24f)],
            body: $$"""
                m_nFirstPositionDrivenNode = 5
                m_CtrlOffsets =
                [
                    { vOffset = [ 1.0, 0.0, 0.0 ] nCtrlParent = 0 nCtrlChild = 1 },
                    { vOffset = [ 1.0, 0.0, 0.0 ] nCtrlParent = 5 nCtrlChild = 2 },
                    { vOffset = [ 1.0, 0.0, 0.0 ] nCtrlParent = 7 nCtrlChild = 3 },
                    { vOffset = [ 1.0, 0.0, 0.0 ] nCtrlParent = 6 nCtrlChild = 4 },
                ]
                m_CtrlSoftOffsets =
                [
                    { nCtrlParent = 7 nCtrlChild = 2 vOffset = [ 1.0, 0.0, 16.0 ] flAlpha = 0.5 },
                    { nCtrlParent = 6 nCtrlChild = 3 vOffset = [ 1.0, 0.0, -8.0 ] flAlpha = 0.9 },
                    { nCtrlParent = 5 nCtrlChild = 4 vOffset = [ 1.0, 0.0, -8.0 ] flAlpha = 0.6 },
                ]
                m_FitMatrices = [ { nEnd = 2 nNode = 7 nBeginDynamic = 0 } ]
                m_FitWeights =
                [
                    { flWeight = 0.5 nNode = 2 nDummy = 0 },
                    { flWeight = 0.9 nNode = 3 nDummy = 0 },
                ]
                m_ReverseOffsets = [ {{reverseOffsets}} ]
                """);

        /// <summary>
        /// A proxy control bone turned from its bind rotation is written at its recorded rotation, its child turned
        /// back to keep its world rotation; a bone whose record agrees writes nothing.
        /// </summary>
        [Test]
        public async Task AProxyControlBoneIsWrittenAtItsRecordedRestRotation()
        {
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f);
            var root = new Bone(0, "root", Vector3.Zero, Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var cape = new Bone(1, "cape", new Vector3(0f, 0f, -10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var tip = new Bone(2, "tip", new Vector3(0f, 0f, -10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            cape.SetParent(root);
            tip.SetParent(cape);

            var into = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
            var turned = ClothExtract.ProxyRestRotations([root],
                new Dictionary<string, Quaternion> { ["root"] = Quaternion.Identity, ["cape"] = turn }, into);

            using (Assert.Multiple())
            {
                await Assert.That(into.Keys.Order()).IsEquivalentTo(["cape", "tip"]);
                await Assert.That(MathF.Abs(Quaternion.Dot(turned.GetValueOrDefault("cape"), turn))).IsEqualTo(1f).Within(1e-6f);
                await Assert.That(MathF.Abs(Quaternion.Dot(turn * into.GetValueOrDefault("tip"), Quaternion.Identity)))
                    .IsEqualTo(1f).Within(1e-6f);

                await Assert.That(into.ContainsKey("root")).IsFalse();
            }
        }

        /// <summary>
        /// Vortices compiled at zero speed read <c>time_multiplier</c> 0 with a positive vortex speed; moving vortices
        /// keep 1.
        /// </summary>
        [Test]
        public async Task VorticesCompiledAtZeroSpeedAreAZeroTimeMultiplier()
        {
            var moving = WindVortexEffect("[ 70.400002, 0.0, 0.0 ]", "1.0", "123.200005");
            var stilled = WindVortexEffect("[ -0.0, -0.0, -0.0 ]", "0.0", "0.0");
            var maps = new HashSet<string>();
            var movingNode = ClothExtract.MakeClothEffect(moving, moving.Effects.First(), maps)!;
            var stilledNode = ClothExtract.MakeClothEffect(stilled, stilled.Effects.First(), maps)!;

            using (Assert.Multiple())
            {
                await Assert.That(movingNode.GetFloatProperty("time_multiplier")).IsEqualTo(1f);
                await Assert.That(movingNode.GetFloatProperty("vortex_max_speed_mph")).IsEqualTo(7f).Within(1e-4f);

                await Assert.That(stilledNode.GetFloatProperty("time_multiplier")).IsEqualTo(0f);
                await Assert.That(stilledNode.GetFloatProperty("vortex_max_speed_mph")).IsGreaterThan(0f);
                await Assert.That(stilledNode.GetInt32Property("vortex_count")).IsEqualTo(2);
            }
        }

        private static FeModel WindVortexEffect(string strength, string choppiness, string maxSpeed) => SyntheticCloth.Parse($$"""
            {
                m_nNodeCount = 1
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0 ]
                m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} ]
                m_Effects =
                [
                    {
                        sName = "wind0"
                        nNameHash = 1456486
                        nType = 1
                        m_Params =
                        {
                            Strength = {{strength}}
                            AirToCloth = 0.249439
                            Choppiness = {{choppiness}}
                            Vortices = [ { MaxSpeed = {{maxSpeed}} MaxCell = 80.0 }, { MaxSpeed = {{maxSpeed}} MaxCell = 80.0 } ]
                        }
                    },
                ]
            }
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
        /// Proxy control bones are written at their recorded rest positions at any distance; bones already there or
        /// without a record keep their offsets.
        /// </summary>
        [Test]
        public async Task ALoneProxyControlBoneFarFromItsBindPoseIsWrittenAtItsRestPosition()
        {
            var root = new Bone(0, "root", Vector3.Zero, Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var cape = new Bone(1, "cape", new Vector3(0f, 0f, -10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var tip = new Bone(2, "tip", new Vector3(0f, 0f, -10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var tail = new Bone(3, "tail", new Vector3(5f, 0f, 0f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            cape.SetParent(root);
            tip.SetParent(cape);
            tail.SetParent(root);

            var into = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            ClothExtract.ProxyRestPositions([root], new Dictionary<string, Vector3>
            {
                ["root"] = Vector3.Zero,
                ["cape"] = new Vector3(0f, 0f, -10.5f),
                ["tail"] = new Vector3(5f, 0f, -7.5f),
            }, new Dictionary<string, Quaternion>(), into);

            using (Assert.Multiple())
            {
                await Assert.That(into.GetValueOrDefault("tail")).IsEqualTo(new Vector3(5f, 0f, -7.5f));

                await Assert.That(into.GetValueOrDefault("cape")).IsEqualTo(new Vector3(0f, 0f, -10.5f));
                await Assert.That(into.ContainsKey("root")).IsFalse();
                await Assert.That(into.ContainsKey("tip")).IsFalse();
            }
        }

        /// <summary>
        /// A culled cloth bone is nested under its compiled parent's DMX joint in that joint's frame; without compiled
        /// parents, or with a parent that is no joint, it stays a root joint.
        /// </summary>
        [Test]
        public async Task ACulledClothBoneIsNestedUnderItsCompiledParentJoint()
        {
            static (DmeModel Model, DmeJoint Parent, DmeJoint Culled) Append(string skelParents, string parentName)
            {
                var feModel = SyntheticCloth.Parse($$"""
                    {
                        m_CtrlName = [ "root", "{{parentName}}", "collar_1" ]
                        {{skelParents}}
                        m_nNodeCount = 3
                        m_nStaticNodes = 3
                        m_NodeInvMasses = [ 0.0, 0.0, 0.0 ]
                        m_InitPose =
                        [
                            {{SyntheticCloth.Pose(0f, 0f, 10f)}}
                            {{SyntheticCloth.Pose(0f, 5f, 10f)}}
                            {{SyntheticCloth.Pose(0f, 9f, 10f)}}
                        ]
                    }
                    """);

                var dmeModel = new DmeModel();
                var root = new DmeJoint { Name = "root" };
                root.Transform.Position = new Vector3(0f, 0f, 10f);
                root.Transform.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
                var parent = new DmeJoint { Name = "collar_0" };
                parent.Transform.Position = new Vector3(5f, 0f, 0f);
                root.Children.Add(parent);
                dmeModel.Children.Add(root);
                dmeModel.JointList.Add(root);
                dmeModel.JointList.Add(parent);

                var boneIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["root"] = 0, ["collar_0"] = 1 };
                ClothExtract.AppendCulledClothBoneJoints(dmeModel, boneIndexByName, feModel, [(2, "collar_1")]);
                return (dmeModel, parent, dmeModel.JointList.OfType<DmeJoint>().Single(joint => joint.Name == "collar_1"));
            }

            var unparented = Append(string.Empty, "collar_0");
            var foreign = Append("m_SkelParents = [ -1, 0, 1 ]", "ghost");
            var nested = Append("m_SkelParents = [ -1, 0, 1 ]", "collar_0");

            using (Assert.Multiple())
            {
                await Assert.That(unparented.Model.Children.Select(static c => c.Name)).Contains("collar_1");
                await Assert.That(unparented.Culled.Transform.Position).IsEqualTo(new Vector3(0f, 9f, 10f));
                await Assert.That(foreign.Model.Children.Select(static c => c.Name)).Contains("collar_1");
                await Assert.That(foreign.Parent.Children.Count).IsEqualTo(0);

                await Assert.That(nested.Model.Children.Select(static c => c.Name)).DoesNotContain("collar_1");
                await Assert.That(nested.Parent.Children.Select(static c => c.Name)).IsEquivalentTo(["collar_1"]);
                await Assert.That(Vector3.Distance(nested.Culled.Transform.Position, new Vector3(4f, 0f, 0f))).IsLessThan(1e-4f);
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
        /// A proxy joint whose compiled parent is another joint but not a DAG ancestor is moved under it at the same
        /// model-space transform; no compiled parents, an ancestor parent, or a descendant parent move nothing.
        /// </summary>
        [Test]
        public async Task AProxyJointIsNestedUnderItsCompiledParentJoint()
        {
            static (DmeModel Model, DmeJoint Pelvis, DmeJoint Upper, DmeJoint Lower) Nest(string skelParents)
            {
                var feModel = SyntheticCloth.Model(
                    ["pelvis", "leg_upper", "leg_lower"], staticNodes: 3,
                    poses: [new(0f, 0f, 40f), new(0f, 5f, 38f), new(0f, 5f, 18f)],
                    body: $$"""
                        {{skelParents}}
                        """);

                var dmeModel = new DmeModel();
                var pelvis = new DmeJoint { Name = "pelvis" };
                pelvis.Transform.Position = new Vector3(0f, 0f, 40f);
                pelvis.Transform.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
                var upper = new DmeJoint { Name = "leg_upper" };
                upper.Transform.Position = new Vector3(0f, -2f, 5f);
                var lower = new DmeJoint { Name = "leg_lower" };
                lower.Transform.Position = new Vector3(0f, -22f, 5f);
                pelvis.Children.Add(upper);
                pelvis.Children.Add(lower);
                dmeModel.Children.Add(pelvis);
                dmeModel.JointList.Add(pelvis);
                dmeModel.JointList.Add(upper);
                dmeModel.JointList.Add(lower);

                ClothExtract.NestProxyJointsUnderCompiledParents(dmeModel, feModel);
                return (dmeModel, pelvis, upper, lower);
            }

            var unparented = Nest(string.Empty);
            var ancestral = Nest("m_SkelParents = [ -1, 0, 0 ]");
            var cyclic = Nest("m_SkelParents = [ 2, 0, 0 ]");
            var nested = Nest("m_SkelParents = [ -1, 0, 1 ]");

            using (Assert.Multiple())
            {
                await Assert.That(unparented.Pelvis.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_upper", "leg_lower"]);
                await Assert.That(ancestral.Pelvis.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_upper", "leg_lower"]);
                await Assert.That(cyclic.Model.Children.Select(static c => c.Name)).IsEquivalentTo(["pelvis"]);
                await Assert.That(cyclic.Pelvis.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_upper", "leg_lower"]);

                await Assert.That(nested.Pelvis.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_upper"]);
                await Assert.That(nested.Upper.Children.Select(static c => c.Name)).IsEquivalentTo(["leg_lower"]);
                await Assert.That(Vector3.Distance(nested.Lower.Transform.Position, new Vector3(0f, -20f, 0f))).IsLessThan(1e-4f);
            }
        }

        /// <summary>
        /// A proxy control bone 0.57 degrees off its bind rotation is turned onto its record; one 0.18 degrees off is
        /// not.
        /// </summary>
        [Test]
        public async Task AProxyControlBoneHalfADegreeOffItsBindRotationIsTurned()
        {
            var root = new Bone(0, "root", Vector3.Zero, Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var neck = new Bone(1, "neck", new Vector3(0f, 0f, 10f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            var fish = new Bone(2, "fish", new Vector3(5f, 0f, 0f), Quaternion.Identity, ModelSkeletonBoneFlags.NoBoneFlags);
            neck.SetParent(root);
            fish.SetParent(root);

            var into = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
            var turned = ClothExtract.ProxyRestRotations([root], new Dictionary<string, Quaternion>
            {
                ["root"] = Quaternion.Identity,
                ["neck"] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, float.DegreesToRadians(0.5717f)),
                ["fish"] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, float.DegreesToRadians(0.1835f)),
            }, into);

            using (Assert.Multiple())
            {
                await Assert.That(turned.Keys.Order()).IsEquivalentTo(["neck"]);

                await Assert.That(into.ContainsKey("fish")).IsFalse();
            }
        }

        /// <summary>
        /// A full-slot vertex paints its unrecorded fit remainder on the nearest static ancestor it does not already
        /// name; the fit bone keeps its weight and the paint sums to 1.
        /// </summary>
        [Test]
        public async Task AFullSlotRemainderGoesToAStaticAncestorTheVertexDoesNotName()
        {
            const int Vertex = 11;
            var recovered = FullSlotRemainder().RecoveredSkinWeights.GetValueOrDefault(Vertex, []);
            float WeightOf(string bone) => recovered.Where(influence => influence.Bone == bone).Sum(influence => influence.Weight);

            using (Assert.Multiple())
            {
                await Assert.That(WeightOf("spine")).IsEqualTo(0.02f).Within(1e-4f);
                await Assert.That(WeightOf("neck")).IsEqualTo(0.98f * 0.0531441f).Within(1e-4f);

                await Assert.That(WeightOf("dyn")).IsEqualTo(0.2343655f).Within(1e-4f);
                await Assert.That(recovered.Sum(influence => influence.Weight)).IsEqualTo(1f).Within(1e-4f);
            }
        }

        /// <summary>
        /// A vertex anchored on hair with eight soft slots and a fit row on dyn at 0.98 of its expansion.
        /// </summary>
        private static FeModel FullSlotRemainder() => SyntheticCloth.Model(
            ["root", "spine", "neck", "hair", "s1", "s2", "s3", "s4", "s5", "s6", "dyn", "$cloth_m0p0"], staticNodes: 10,
                parents: [-1, 0, 1, 2, 0, 0, 0, 0, 0, 0, 3, -1],
            poses: [new(0f, 0f, 0f), new(0f, 0f, 10f), new(0f, 0f, 20f), new(0f, 0f, 30f), new(2f, 0f, 0f), new(4f, 0f, 0f),
                new(6f, 0f, 0f), new(8f, 0f, 0f), new(10f, 0f, 0f), new(12f, 0f, 0f), new(0f, 0f, 35f), new(1f, 0f, 32f)],
            body: """
                m_nFirstPositionDrivenNode = 10
                m_CtrlOffsets = [ { vOffset = [ 1.0, 0.0, 2.0 ] nCtrlParent = 3 nCtrlChild = 11 } ]
                m_CtrlSoftOffsets =
                [
                    { nCtrlParent = 10 nCtrlChild = 11 vOffset = [ 1.0, 0.0, -3.0 ] flAlpha = 0.5 },
                    { nCtrlParent = 2 nCtrlChild = 11 vOffset = [ 1.0, 0.0, 12.0 ] flAlpha = 0.9 },
                    { nCtrlParent = 4 nCtrlChild = 11 vOffset = [ -1.0, 0.0, 32.0 ] flAlpha = 0.9 },
                    { nCtrlParent = 5 nCtrlChild = 11 vOffset = [ -3.0, 0.0, 32.0 ] flAlpha = 0.9 },
                    { nCtrlParent = 6 nCtrlChild = 11 vOffset = [ -5.0, 0.0, 32.0 ] flAlpha = 0.9 },
                    { nCtrlParent = 7 nCtrlChild = 11 vOffset = [ -7.0, 0.0, 32.0 ] flAlpha = 0.9 },
                    { nCtrlParent = 8 nCtrlChild = 11 vOffset = [ -9.0, 0.0, 32.0 ] flAlpha = 0.9 },
                    { nCtrlParent = 9 nCtrlChild = 11 vOffset = [ -11.0, 0.0, 32.0 ] flAlpha = 0.9 },
                ]
                m_FitMatrices = [ { nEnd = 1 nNode = 10 nBeginDynamic = 0 } ]
                m_FitWeights = [ { flWeight = 0.2343655 nNode = 11 nDummy = 0 } ]
                """);

        /// <summary>
        /// A ring rod folded across a compiled quad weighs nothing in the mass pass, so the ring reads a multiplier of
        /// 1, as it does with no compiled quad.
        /// </summary>
        [Test]
        public async Task ARingFoldedAcrossItsCompiledQuadIsNoMassTimeRod()
        {
            using (Assert.Multiple())
            {
                await Assert.That(RingLinkQuads(compiledQuad: false).RecoverJointMassMultiplier(3) ?? float.NaN)
                    .IsEqualTo(1f).Within(1e-3f);

                await Assert.That(RingLinkQuads(compiledQuad: true).RecoverJointMassMultiplier(3) ?? float.NaN)
                    .IsEqualTo(1f).Within(1e-3f);
                await Assert.That(RingLinkQuads(compiledQuad: true, swapped: true).RecoverJointMassMultiplier(3) ?? float.NaN)
                    .IsEqualTo(1f).Within(1e-3f);
            }
        }

        /// <summary>
        /// A static root ring over an end ring turned half a turn, linked by two quads; <paramref name="compiledQuad"/>
        /// adds a compiled quad and <paramref name="swapped"/> leaves the end ring unturned.
        /// </summary>
        private static FeModel RingLinkQuads(bool compiledQuad, bool swapped = false) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "$ccroot_0", "$ccroot_1", "end", "$ccend_0", "$ccend_1" ]
                m_SkelParents = [ -1, 0, 0, 0, 3, 3 ]
                m_nNodeCount = 6
                m_nStaticNodes = 3
                m_NodeInvMasses = [ 0.0, 0.0, 0.0, 1.0, {{(compiledQuad ? "0.0023670762, 0.0023670762" : "0.003125, 0.003125")}} ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(5f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(-5f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(swapped ? 10f : -10f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(swapped ? -10f : 10f, 0f, -20f)}}
                ]
                m_Quads = [ {{(compiledQuad ? (swapped ? "{ nNode = [ 2, 1, 4, 5 ] }" : "{ nNode = [ 2, 1, 5, 4 ] }") : string.Empty)}} ]
                m_SourceElems = [ 0, 0, 0, 2, 2, 1, 4, 5, 1, 2, 5, 4 ]
                m_Rods =
                [
                    {{SyntheticCloth.BandedRod(5, 4, 20f, 44.72136f, 1f)}}
                    {{SyntheticCloth.RigidRod(4, 5, 20f, 1f)}}
                ]
            }
            """);

        /// <summary>
        /// A face-kept sheet's folds are predicted in the corner order the compiler meets its faces in, including the
        /// crosswise pairs a mid-cycle static order folds.
        /// </summary>
        [Test]
        public async Task AFaceKeptSheetFoldsInTheCornerOrderTheCompilerMeetsItsFacesIn()
        {
            var sheet = FaceKeptSheetCorner;
            var proxies = sheet.BuildProxyMeshes().Select(static (proxy, i) => ($"p{i}.dmx", $"p{i}", proxy)).ToList();
            var rods = ClothExtract.ClothRodsFromSurface(sheet, proxies);
            var (derived, bend) = (rods.Derived, rods.GeneratesBendRods);

            using (Assert.Multiple())
            {
                await Assert.That(bend).IsTrue();
                await Assert.That(derived.Contains((2, 5)) && derived.Contains((0, 3))).IsTrue();

                await Assert.That(derived.Contains((1, 5)) && derived.Contains((2, 4))).IsTrue();
            }
        }

        private static FeModel FaceKeptSheetCorner => SyntheticCloth.Load("cloth_sheet_face_kept_corner.kv3");

        /// <summary>
        /// A face-kept sheet whose folds are regenerated states no bend paint; with declared pairs it states 0.2.
        /// </summary>
        [Test]
        public async Task AFaceKeptSheetWhoseFoldsAreRegeneratedStatesNoBendPaint()
        {
            static float? Read(FeModel feModel)
                => ClothExtract.ClothFaceKeptBendStiffness(feModel, ClothExtract.ClothRodsFromSurface(feModel,
                    feModel.BuildProxyMeshes().Select(static (proxy, i) => ($"p{i}.dmx", $"p{i}", proxy)).ToList()));

            using (Assert.Multiple())
            {
                await Assert.That(Read(FoldedSheetModel(0.5f))).IsEqualTo(0.2f);

                await Assert.That(Read(FoldedSheetModel(0.666667f))).IsNull();
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

        /// <summary>
        /// A fitless vertex keeps its recorded paint when its only simulated influence is a small weight on a bone with
        /// its own fit matrix; without that fit matrix it stays deferred.
        /// </summary>
        [Test]
        public async Task AFitlessVertexKeepsASubThresholdWeightOnABoneTheOriginalFits()
        {
            const int Fitless = 3;
            var fitted = FitlessOnFitBone(boneOwnsFit: true);
            var unfitted = FitlessOnFitBone(boneOwnsFit: false);

            using (Assert.Multiple())
            {
                await Assert.That(unfitted.RecoveredSkinWeights.ContainsKey(Fitless)).IsFalse();
                await Assert.That(unfitted.DeferredOffsetSkinWeights.ContainsKey(Fitless)).IsTrue();

                await Assert.That(fitted.RecoveredSkinWeights.GetValueOrDefault(Fitless, []).Select(static influence => influence.Bone))
                    .IsEquivalentTo(["bone_1", "bone_2"]);
            }
        }

        /// <summary>
        /// A sheet whose vertex 3 has no fit row and paints bone_1 0.96 and bone_2 0.04; <paramref name="boneOwnsFit"/>
        /// gives bone_2 its own fit matrix.
        /// </summary>
        private static FeModel FitlessOnFitBone(bool boneOwnsFit) => SyntheticCloth.Model(
            ["bone_0", "bone_1", "$cloth_m0p0", "$cloth_m0p1", "bone_2", "bone_3"], staticNodes: 2, parents: [-1, 0, 5, 1, 1, 4],
            poses: [new(0f, 0f, 0f), new(0f, 0f, -8f), new(1f, 0f, -24f), new(1f, 0f, -10f), new(0f, 0f, -16f), new(0f, 0f, -24f)],
            body: $$"""
                m_nFirstPositionDrivenNode = 4
                m_CtrlOffsets =
                [
                    { vOffset = [ 1.0, 0.0, 0.0 ] nCtrlParent = 5 nCtrlChild = 2 },
                    { vOffset = [ 1.0, 0.0, -2.0 ] nCtrlParent = 1 nCtrlChild = 3 },
                ]
                m_CtrlSoftOffsets =
                [
                    { nCtrlParent = 4 nCtrlChild = 2 vOffset = [ 1.0, 0.0, -8.0 ] flAlpha = 0.7 },
                    { nCtrlParent = 4 nCtrlChild = 3 vOffset = [ 1.0, 0.0, 6.0 ] flAlpha = 0.96 },
                ]
                m_FitMatrices = [ { nEnd = 1 nNode = 5 nBeginDynamic = 0 }{{(boneOwnsFit ? ", { nEnd = 2 nNode = 4 nBeginDynamic = 0 }" : string.Empty)}} ]
                m_FitWeights = [ { flWeight = 0.7 nNode = 2 nDummy = 0 }{{(boneOwnsFit ? ", { flWeight = 0.3 nNode = 2 nDummy = 0 }" : string.Empty)}} ]
                """);

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

        /// <summary>
        /// The covering-hinge paint replaces a settled paint that misses network rods, so every rod folds back to its
        /// minimum; a settled paint that rebuilds every rod is kept.
        /// </summary>
        [Test]
        public async Task ACoveringPaintReplacesASettledPaintThatMissesRods()
        {
            var faces = CoveringPaintFaces;
            var network = CoveringPaintNetwork;
            var sheet = CoveringPaintSheet;
            var (paint, curvature) = ClothExtract.ClothBendStiffnessOverFold(sheet, faces, network, 0.7383068f, keepsCurvature: false);

            var gridFaces = HingeGridFaces;
            var gridNetwork = HingeGridNetwork;
            var (painted, paintedCurvature) = ClothExtract.ClothBendStiffnessOverFold(
                LeastFoldedHingeGrid, gridFaces, gridNetwork, 0.375f, keepsCurvature: false);
            var (bounded, boundedCurvature) = ClothExtract.ClothBendStiffnessOverFold(
                CappedAgainstFlatHingeGrid(20f), gridFaces, gridNetwork, 0f, keepsCurvature: false);

            using (Assert.Multiple())
            {
                await Assert.That(FoldedRodMisses(LeastFoldedHingeGrid, gridFaces, gridNetwork, painted, paintedCurvature)).IsEmpty();
                await Assert.That(paintedCurvature).IsEqualTo(0f);
                await Assert.That(bounded).IsNull();
                await Assert.That(boundedCurvature).IsEqualTo(1f).Within(0.01f);

                await Assert.That(FoldedRodMisses(sheet, faces, network, paint, curvature)).IsEmpty();
            }
        }

        private static List<int[]> CoveringPaintFaces => [[15, 12, 13], [14, 15, 13], [6, 7, 3, 4], [5, 8, 6, 4], [0, 5, 4, 1], [3, 2, 1, 4], [9, 10, 7, 6], [8, 11, 9, 6], [11, 14, 13, 9], [12, 10, 9, 13]];

        private static HashSet<(int, int)> CoveringPaintNetwork =>
            [(0, 8), (1, 6), (2, 7), (3, 5), (3, 10), (4, 9), (5, 11), (6, 13), (7, 8), (7, 12), (8, 14), (9, 15), (10, 11), (10, 15), (11, 15), (12, 14)];

        private static FeModel CoveringPaintSheet => SyntheticCloth.Load("cloth_sheet_covering_paint.kv3");

        /// <summary>
        /// A clique of equal bands over two chains' ring nodes is declared as one cluster of all its members at half
        /// the band; the same clique within one joint declares none.
        /// </summary>
        [Test]
        public async Task AClusterCliqueAcrossTwoChainsRingsIsOneCluster()
        {
            var model = ClusterCliqueRings;
            var across = KVObject.Array();
            var covered = ClothExtract.AddRingClusterCliques(across, model, new Dictionary<int, int> { [2] = 0, [3] = 0, [4] = 1, [5] = 1 });
            var single = KVObject.Array();
            var none = ClothExtract.AddRingClusterCliques(single, model, new Dictionary<int, int> { [2] = 0, [3] = 0, [4] = 0, [5] = 0 });

            using (Assert.Multiple())
            {
                await Assert.That(single.Count).IsEqualTo(0);
                await Assert.That(none.Count).IsEqualTo(0);

                await Assert.That(covered.Count).IsEqualTo(6);
                await Assert.That(across.Select(static child => string.Join("|", child.Value.GetSubCollection("chain").GetArray("joints")
                    .Select(static joint => $"{joint.GetStringProperty("joint_name")}:{joint.GetFloatProperty("collision_radius")}:{joint.GetFloatProperty("stray_radius")}"))).ToArray())
                    .IsEquivalentTo(ClusterCliqueMembers, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] ClusterCliqueMembers = ["$cca_0:4:8|$cca_1:4:8|$ccb_0:4:8|$ccb_1:4:8"];

        /// <summary>Joints a and b with two ring nodes each and the six 8 / 16 bands of a four-member cluster.</summary>
        private static FeModel ClusterCliqueRings => SyntheticCloth.Model(
            ["a", "b", "$cca_0", "$cca_1", "$ccb_0", "$ccb_1"], staticNodes: 2, parents: [-1, -1, 0, 0, 1, 1],
                invMasses: "0.0, 0.0, 0.01, 0.01, 0.01, 0.01",
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(0f, 3f, -5f), new(0f, -3f, -5f), new(10f, 3f, -5f), new(10f, -3f, -5f)],
            body: $$"""
                m_Rods =
                [
                    {{SyntheticCloth.BandedRod(2, 3, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 4, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 5, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(3, 4, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(3, 5, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(4, 5, 8f, 16f, 1f)}}
                ]
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

                await Assert.That(ClothExtract.ClothChainVersion(parentLocked, parentChain, hasOtherChains: false)).IsEqualTo(2);

                await Assert.That(ClothExtract.ClothChainVersion(goalLocked, goalChain, hasOtherChains: false)).IsLessThan(2);
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

        /// <summary>
        /// A chain joint whose node base names a free cloth node is declared a <c>ClothNode</c> at alignment 3, with
        /// every joint a static ClothNode in node order; bases naming no free cloth node declare none.
        /// </summary>
        [Test]
        public async Task AChainJointBasedThroughAFreeClothNodeIsDeclaredAClothNode()
        {
            var chain = new FeModel.BoneChain { RootBone = "a0" };
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 2, Name = "a0", ParentNode = -1, InvMass = 1f });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 3, Name = "a1", ParentNode = 2, InvMass = 1f });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 4, Name = "a2", ParentNode = 3, InvMass = 1f });

            var viaClothNode = ClothExtract.ChainJointClothNodes(ChainOverFreeClothNode(1), [chain]).ToList();
            var viaBone = ClothExtract.ChainJointClothNodes(ChainOverFreeClothNode(0), [chain]).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(viaClothNode.Select(static node => node.GetStringProperty("name")))
                    .IsEquivalentTo(["a0", "a1", "a2"], CollectionOrdering.Matching);
                await Assert.That(viaClothNode.Select(static node => node.GetStringProperty("cloth_node_root_bone")))
                    .IsEquivalentTo(["a0", "a1", "a2"], CollectionOrdering.Matching);
                await Assert.That(viaClothNode.Count(static node => node.ContainsKey("transform_alignment"))).IsEqualTo(2);
                await Assert.That(viaClothNode.Count > 0 && viaClothNode[0].GetInt32Property("transform_alignment") == 3).IsTrue();
                await Assert.That(viaClothNode.Count > 0 ? viaClothNode[0].GetStringProperty("node_base_x1") : null).IsEqualTo("side");
                await Assert.That(viaClothNode.Count > 0 ? viaClothNode[0].GetStringProperty("node_base_y1") : null).IsEqualTo("a1");
                await Assert.That(viaBone).IsEmpty();
            }
        }

        /// <summary>
        /// A chain a0 - a1 - a2 under a static head and a free cloth node, with a0 and a1 based through node <paramref
        /// name="xNode"/>.
        /// </summary>
        private static FeModel ChainOverFreeClothNode(int xNode) => SyntheticCloth.Model(
            ["head", "$cloth_node_side", "a0", "a1", "a2"], staticNodes: 2, parents: [-1, 0, 0, 2, 3],
            poses: [new(0f, 0f, 0f), new(0f, -50f, 0f), new(0f, 0f, -5f), new(0f, 0f, -10f), new(0f, 0f, -15f)],
            body: $$"""
                m_nRotLockStaticNodes = 2
                m_NodeBases =
                [
                    { nNode = 2 nNodeX0 = 2 nNodeX1 = {{xNode}} nNodeY0 = 3 nNodeY1 = 2 },
                    { nNode = 3 nNodeX0 = 3 nNodeX1 = {{xNode}} nNodeY0 = 4 nNodeY1 = 3 },
                ]
                """);

        /// <summary>
        /// A settled paint missing rods by more than 1e-4 of the span is replaced, so every rod folds back under the
        /// tight rule, also on the covering answer's sheet.
        /// </summary>
        [Test]
        public async Task ASettledPaintMissingRodsByThousandthsIsReplaced()
        {
            List<int[]> faces = [[66, 67, 72, 73], [30, 31, 36, 37], [18, 19, 24, 25], [25, 24, 31, 30], [48, 49, 54, 55], [37, 36, 42, 43], [43, 42, 49, 48], [55, 54, 60, 61], [61, 60, 67, 66], [12, 13, 19, 18], [6, 7, 13, 12], [0, 1, 7, 6], [68, 74, 75, 69], [32, 38, 39, 33], [20, 26, 27, 21], [26, 32, 33, 27], [50, 56, 57, 51], [38, 44, 45, 39], [44, 50, 51, 45], [56, 62, 63, 57], [62, 68, 69, 63], [14, 20, 21, 15], [8, 14, 15, 9], [9, 2, 3, 8], [82, 83, 84, 85], [40, 46, 47, 41], [22, 28, 29, 23], [10, 16, 17, 11], [16, 22, 23, 17], [28, 34, 35, 29], [34, 40, 41, 35], [58, 64, 65, 59], [46, 52, 53, 47], [52, 58, 59, 53], [70, 76, 77, 71], [64, 70, 71, 65], [76, 82, 85, 77], [4, 5, 10, 11], [10, 5, 2, 9], [15, 21, 22, 16], [27, 33, 34, 28], [39, 45, 46, 40], [51, 57, 58, 52], [63, 69, 70, 64], [78, 79, 83, 82], [72, 67, 80, 81], [67, 60, 65, 71], [54, 49, 53, 59], [42, 36, 41, 47], [31, 24, 29, 35], [19, 13, 17, 23], [7, 1, 4, 11], [69, 75, 79, 78], [81, 80, 85, 84]];
            HashSet<(int, int)> network = [(0, 12), (1, 13), (2, 15), (3, 14), (4, 17), (5, 16), (6, 11), (6, 18), (7, 10), (7, 19), (8, 10), (8, 20), (9, 11), (9, 21), (10, 22), (11, 23), (12, 17), (12, 25), (13, 16), (13, 24), (14, 16), (14, 26), (15, 17), (15, 27), (16, 28), (17, 29), (18, 23), (18, 30), (19, 22), (19, 31), (20, 22), (20, 32), (21, 23), (21, 33), (22, 34), (23, 35), (24, 28), (24, 36), (25, 29), (25, 37), (26, 28), (26, 38), (27, 29), (27, 39), (28, 40), (29, 41), (30, 35), (30, 43), (31, 34), (31, 42), (32, 34), (32, 44), (33, 35), (33, 45), (34, 46), (35, 47), (36, 40), (36, 49), (37, 41), (37, 48), (38, 40), (38, 50), (39, 41), (39, 51), (40, 52), (41, 53), (42, 46), (42, 54), (43, 47), (43, 55), (44, 46), (44, 56), (45, 47), (45, 57), (46, 58), (47, 59), (48, 53), (48, 61), (49, 52), (49, 60), (50, 52), (50, 62), (51, 53), (51, 63), (52, 64), (53, 65), (54, 58), (54, 67), (55, 59), (55, 66), (56, 58), (56, 68), (57, 59), (57, 69), (58, 70), (59, 71), (60, 64), (60, 72), (61, 65), (61, 73), (62, 64), (62, 74), (63, 65), (63, 75), (64, 76), (65, 77), (66, 71), (66, 80), (67, 70), (67, 85), (68, 70), (68, 78), (69, 71), (69, 82), (70, 82), (71, 85), (72, 84), (73, 81), (74, 79), (75, 83), (76, 83), (77, 84), (78, 85), (79, 84), (80, 82), (81, 83)];
            var sheet = SettledPaintSheet;
            var (paint, curvature) = ClothExtract.ClothBendStiffnessOverFold(sheet, faces, network, 0.800064f, keepsCurvature: false);

            var coveringFaces = CoveringPaintFaces;
            var coveringNetwork = CoveringPaintNetwork;
            var (coveringPaint, coveringCurvature) = ClothExtract.ClothBendStiffnessOverFold(CoveringPaintSheet, coveringFaces, coveringNetwork,
                0.7383068f, keepsCurvature: false);

            using (Assert.Multiple())
            {
                await Assert.That(FoldedRodMisses(CoveringPaintSheet, coveringFaces, coveringNetwork, coveringPaint, coveringCurvature, tight: true)).IsEmpty();

                await Assert.That(FoldedRodMisses(sheet, faces, network, paint, curvature, tight: true)).IsEmpty();
            }
        }

        private static FeModel SettledPaintSheet => SyntheticCloth.Load("cloth_sheet_settled_paint.kv3");

        /// <summary>
        /// A second rigid copy of a span with no two-corner source element is an unrecorded span copy; a recorded
        /// spring, a banded copy, a lone rod, or a model without <c>m_SkelParents</c> is not.
        /// </summary>
        [Test]
        public async Task ASecondRigidSpanCopyWithNoSourceElementIsAClusterRod()
        {
            static FeModel Pair(string rods, string sourceElems, string skelParents = "m_SkelParents = [ -1, 0 ]") => SyntheticCloth.Model(
                ["a", "b"], staticNodes: 1, poses: [new(0f, 0f, 0f), new(10f, 0f, 0f)], body: $$"""
                    m_Rods = [ {{rods}} ]
                    {{sourceElems}}
                    {{skelParents}}
                    """);

            var rigid = SyntheticCloth.RigidRod(0, 1, 10f, 1f);
            var doubled = Pair(rigid + rigid, string.Empty);
            var sprung = Pair(rigid + rigid, "m_SourceElems = [ 0, 1, 0, 0, 0, 1 ]");
            var banded = Pair(rigid + SyntheticCloth.BandedRod(0, 1, 8f, 10f, 1f), string.Empty);
            var single = Pair(rigid, string.Empty);
            var unparented = Pair(rigid + rigid, string.Empty, string.Empty);

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.IsUnrecordedSpanCopy(sprung, sprung.Rods[1])).IsFalse();
                await Assert.That(ClothExtract.IsUnrecordedSpanCopy(banded, banded.Rods[1])).IsFalse();
                await Assert.That(ClothExtract.IsUnrecordedSpanCopy(single, single.Rods[0])).IsFalse();
                await Assert.That(ClothExtract.IsUnrecordedSpanCopy(unparented, unparented.Rods[1])).IsFalse();

                await Assert.That(ClothExtract.IsUnrecordedSpanCopy(doubled, doubled.Rods[1])).IsTrue();
            }
        }

        /// <summary>
        /// A fold-weighted record beside a cluster band leaves the clique one cluster; the same records on unfolded
        /// pairs break it.
        /// </summary>
        [Test]
        public async Task AFoldBesideAClusterBandLeavesTheCliqueOneCluster()
        {
            var ringOwner = new Dictionary<int, int> { [2] = 0, [3] = 0, [4] = 1, [5] = 1 };
            var folded = KVObject.Array();
            ClothExtract.AddRingClusterCliques(folded, FoldedClusterClique(faces: true), ringOwner);
            var unfolded = KVObject.Array();
            ClothExtract.AddRingClusterCliques(unfolded, FoldedClusterClique(faces: false), ringOwner);

            using (Assert.Multiple())
            {
                await Assert.That(unfolded.Count).IsEqualTo(0);

                await Assert.That(folded.Select(static child => string.Join("|", child.Value.GetSubCollection("chain").GetArray("joints")
                    .Select(static joint => joint.GetStringProperty("joint_name")))).ToArray())
                    .IsEquivalentTo(FoldedCliqueMembers, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] FoldedCliqueMembers = ["$cca_0|$cca_1|$ccb_0|$ccb_1"];

        /// <summary>
        /// <see cref="ClusterCliqueRings"/> with unequal ring masses and a fold-weighted record on each ring pair;
        /// <paramref name="faces"/> adds the quads that fold them.
        /// </summary>
        private static FeModel FoldedClusterClique(bool faces) => SyntheticCloth.Model(
            ["a", "b", "$cca_0", "$cca_1", "$ccb_0", "$ccb_1"], staticNodes: 2, parents: [-1, -1, 0, 0, 1, 1],
                invMasses: "0.0, 0.0, 0.01, 0.02, 0.01, 0.02",
            poses: [new(0f, 0f, 0f), new(10f, 0f, 0f), new(0f, 3f, -5f), new(0f, -3f, -5f), new(10f, 3f, -5f), new(10f, -3f, -5f)],
            body: $$"""
                m_Quads = [ {{(faces ? "{ nNode = [ 0, 1, 4, 2 ] }, { nNode = [ 1, 0, 3, 5 ] }" : string.Empty)}} ]
                m_Rods =
                [
                    {{SyntheticCloth.BandedRod(2, 3, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 4, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(2, 5, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(3, 4, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(3, 5, 8f, 16f, 1f)}}
                    {{SyntheticCloth.BandedRod(4, 5, 8f, 16f, 1f)}}
                    { nNode = [ 2, 3 ] flMinDist = 1.0 flMaxDist = 12.0 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 1.0 flMaxDist = 12.0 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                ]
                """);

        /// <summary>
        /// On the proxy sheet route an authored <c>ClothSpring</c> between two chain joints is re-declared; the model
        /// without it declares none.
        /// </summary>
        [Test]
        public async Task AnAuthoredSpringBetweenChainJointsIsReDeclaredOnTheSheetRoute()
        {
            static string Extract(string fixture)
            {
                using var resource = new Resource();
                resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", fixture));
                return new ModelExtract(resource, new NullFileLoader()).ToValveModel();
            }

            var sprung = Extract("cloth_sheet_chain_spring.vmdl_c");
            var plain = Extract("cloth_sheet_chain_nospring.vmdl_c");

            using (Assert.Multiple())
            {
                await Assert.That(sprung).Contains("ClothProxyMeshFile");
                await Assert.That(plain).Contains("ClothProxyMeshFile");
                await Assert.That(plain).DoesNotContain("_class = \"ClothSpring\"");

                await Assert.That(sprung).Contains("_class = \"ClothSpring\"");
                await Assert.That(sprung).Contains("cloth_node_0 = \"coattail_1_L\"");
                await Assert.That(sprung).Contains("cloth_node_1 = \"coattail_1_R\"");
            }
        }

        /// <summary>
        /// A ClothNode with a node base but fewer than two rod neighbours declares alignment 4 with its references; a
        /// node two rods tie to keeps 0.
        /// </summary>
        [Test]
        public async Task ADynamicClothNodeNoRodTiesToTwoNodesDeclaresItsBasisPreset()
        {
            var feModel = SyntheticCloth.Model(
                ["root", "a", "b", "c", "d", "flap", "strap"], staticNodes: 1,
                poses: [new(0f, 0f, 60f), new(-4f, 4f, 55f), new(-4f, -4f, 55f), new(-6f, 4f, 45f), new(-6f, -4f, 45f),
                    new(-5f, 0f, 50f), new(-3f, 0f, 58f)],
                body: $$"""
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(1, 2, 8f, 1f)}}
                        {{SyntheticCloth.RigidRod(3, 4, 8f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 3, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(6, 1, 5f, 1f)}}
                        {{SyntheticCloth.RigidRod(6, 2, 5f, 1f)}}
                    ]
                    m_NodeBases =
                    [
                        { nNode = 5 nNodeX0 = 4 nNodeX1 = 1 nNodeY0 = 2 nNodeY1 = 3 },
                        { nNode = 6 nNodeX0 = 4 nNodeX1 = 1 nNodeY0 = 2 nNodeY1 = 3 },
                    ]
                    """);

            var flap = ClothExtract.MakeClothNode(feModel, "flap", 5);
            var strap = ClothExtract.MakeClothNode(feModel, "strap", 6);

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.RodNeighbourCount(feModel, 6)).IsEqualTo(2);
                await Assert.That(strap.GetInt32Property("transform_alignment")).IsEqualTo(0);

                await Assert.That(flap.GetInt32Property("transform_alignment")).IsEqualTo(4);
                await Assert.That(flap.GetStringProperty("node_base_x0")).IsEqualTo("d");
                await Assert.That(flap.GetStringProperty("node_base_x1")).IsEqualTo("a");
                await Assert.That(flap.GetStringProperty("node_base_y0")).IsEqualTo("b");
                await Assert.That(flap.GetStringProperty("node_base_y1")).IsEqualTo("c");
            }
        }

        /// <summary>
        /// A bone cloud's folds are read in the compiler's fold walk, so folds built after the mass pass neither weigh
        /// in the joint mass nor stay surplus; declared folds weigh.
        /// </summary>
        [Test]
        public async Task ABoneCloudFoldIsReadInTheCompilersFoldWalk()
        {
            var after = BoneCloud(afterMass: true);
            var declared = BoneCloud(afterMass: false);
            var surplusFolds = after.GetUngeneratedRods([], surfaceFansRegenerate: true).Count(rod => rod.MinDist < rod.MaxDist);

            using (Assert.Multiple())
            {
                await Assert.That(declared.RecoverJointMassMultiplier(1) ?? -1f).IsEqualTo(1f).Within(1e-3f);
                await Assert.That(declared.RecoverJointMassMultiplier(4) ?? -1f).IsEqualTo(1f).Within(1e-3f);

                await Assert.That(after.RecoverJointMassMultiplier(1) ?? -1f).IsEqualTo(1f).Within(1e-3f);
                await Assert.That(after.RecoverJointMassMultiplier(2) ?? -1f).IsEqualTo(1f).Within(1e-3f);
                await Assert.That(after.RecoverJointMassMultiplier(4) ?? -1f).IsEqualTo(1f).Within(1e-3f);
                await Assert.That(surplusFolds).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A static head and four cloud members with rigid rods on every pair and three folds, built after the mass pass
        /// or declared.
        /// </summary>
        private static FeModel BoneCloud(bool afterMass)
        {
            var inv = afterMass
                ? new[] { "0.0", "0.00446724811", "0.00380154087", "0.00380154087", "0.00392823592" }
                : new[] { "0.0", "0.00308050977", "0.00269810419", "0.00380154087", "0.0026542513" };
            var folds = afterMass ? new[] { "0.540254216", "0.491804741", "0.532101317" } : new[] { "0.5", "0.5", "0.5" };
            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "head", "hair_01", "ear_L_01", "ear_R_01", "muzzle_01" ]
                    m_SkelParents = [ -1, 0, 0, 0, 0 ]
                    m_nNodeCount = 5
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ {{string.Join(", ", inv)}} ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, 10f)}}
                        {{SyntheticCloth.Pose(5f, 0f, 8f)}}
                        {{SyntheticCloth.Pose(-5f, 0f, 8f)}}
                        {{SyntheticCloth.Pose(0f, 6f, 6f)}}
                    ]
                    m_SourceElems = [ 0, 0, 6, 0, 3, 2, 0, 3, 1, 0, 3, 4, 0, 2, 1, 0, 2, 4, 0, 1, 4, 0 ]
                    m_Rods =
                    [
                        { nNode = [ 0, 1 ] flMinDist = 10.0 flMaxDist = 10.0 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                        { nNode = [ 0, 2 ] flMinDist = 9.43398113 flMaxDist = 9.43398113 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                        { nNode = [ 0, 3 ] flMinDist = 9.43398113 flMaxDist = 9.43398113 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                        { nNode = [ 0, 4 ] flMinDist = 8.48528137 flMaxDist = 8.48528137 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                        {{SyntheticCloth.RigidRod(1, 2, 5.38516481f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 3, 5.38516481f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 4, 7.21110255f, 1f)}}
                        {{SyntheticCloth.RigidRod(2, 3, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(2, 4, 8.06225775f, 1f)}}
                        {{SyntheticCloth.RigidRod(3, 4, 8.06225775f, 1f)}}
                        { nNode = [ 1, 2 ] flMinDist = 2.6925824 flMaxDist = 5.38516481 flWeight0 = {{folds[0]}} flRelaxationFactor = 1.0 },
                        { nNode = [ 2, 4 ] flMinDist = 4.03112887 flMaxDist = 8.06225775 flWeight0 = {{folds[1]}} flRelaxationFactor = 1.0 },
                        { nNode = [ 1, 4 ] flMinDist = 3.60555128 flMaxDist = 7.21110255 flWeight0 = {{folds[2]}} flRelaxationFactor = 1.0 },
                    ]
                }
                """);
        }
    }
}
