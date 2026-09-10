using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>
    /// Builds an <see cref="FeModel"/> out of hand-written KV3, so a compiler law can be exercised on
    /// inputs whose expected result is computed by hand rather than read off a shipped model.
    /// </summary>
    internal static class SyntheticCloth
    {
        private const string Header =
            "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} "
            + "format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->\n";

        public static FeModel Parse(string feModelBody)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Header + feModelBody));
            return new FeModel(KVDocumentExtensions.ParseKV3(stream).Root);
        }

        /// <summary>Formats a float so KV3 always reads it back as a floating point value.</summary>
        public static string Num(float value)
        {
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal)
                ? text
                : text + ".0";
        }

        /// <summary>An identity <c>m_InitPose</c> row at the given position.</summary>
        public static string Pose(float x, float y, float z)
            => $"[ {Num(x)}, {Num(y)}, {Num(z)}, 1.0, 0.0, 0.0, 0.0, 1.0 ],";

        /// <summary>A rigid rod, whose minimum equals its maximum.</summary>
        public static string RigidRod(int a, int b, float length, float relaxation)
            => Rod(a, b, length, length, relaxation);

        /// <summary>A length-banded rod, free to move between its two bounds.</summary>
        public static string BandedRod(int a, int b, float min, float max, float relaxation)
            => Rod(a, b, min, max, relaxation);

        private static string Rod(int a, int b, float min, float max, float relaxation)
            => $"{{ nNode = [ {a}, {b} ] flMinDist = {Num(min)} flMaxDist = {Num(max)} "
                + $"flWeight0 = 0.5 flRelaxationFactor = {Num(relaxation)} }},";
    }

    public class ClothLawTest
    {
        /// <summary>
        /// A chain rod's compiled relaxation factor is the authored slider scaled by
        /// <c>exp(-default_stretch)</c>, so recovering the slider divides that scale back out.
        /// exp(-0.985) = 0.3734400, and 0.5 * 0.3734400 = 0.1867200.
        /// </summary>
        [Test]
        public async Task ChainRodRelaxationDividesOutTheDefaultStretch()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1", "j2" ]
                    m_SkelParents = [ -1, 0, 1 ]
                    m_nNodeCount = 3
                    m_nStaticNodes = 1
                    m_flDefaultSurfaceStretch = 0.985
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 0.18672f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 0.18672f)}}
                    ]
                }
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
        /// With no <c>default_stretch</c> the scale is exp(0) = 1 and the slider is the compiled factor
        /// verbatim, which is what tells the two halves of the law apart.
        /// </summary>
        [Test]
        public async Task ChainRodRelaxationIsVerbatimWithoutDefaultStretch()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1" ]
                    m_SkelParents = [ -1, 0 ]
                    m_nNodeCount = 2
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    ]
                    m_Rods = [ {{SyntheticCloth.RigidRod(0, 1, 10f, 0.8f)}} ]
                }
                """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            await Assert.That(joint).IsNotNull();
            await Assert.That(joint!.StretchStiffness).IsEqualTo(0.8f).Within(1e-4f);
        }

        /// <summary>
        /// A bend rod across a joint's own extrude ring carries
        /// <c>flMinDist = flMaxDist * sin(add_curvature * pi / 2)</c>.
        /// sin(0.15 * pi / 2) = 0.23344536, so a rod at maximum 10 carries minimum 2.3344536.
        /// </summary>
        [Test]
        public async Task ChainRingCurvatureInvertsTheHalfSineLaw()
        {
            var feModel = RingCurvatureModel(SyntheticCloth.BandedRod(1, 2, 2.3344536f, 10f, 1f));

            await Assert.That(feModel.ChainRingCurvature).IsEqualTo(0.15f).Within(1e-4f);
        }

        /// <summary>
        /// Two ring rods reading different values are not one authored curvature, so the reading is
        /// refused. sin(0.4 * pi / 2) = 0.58778525, which reads back as 0.4 rather than the other rod's
        /// 0.15.
        /// </summary>
        [Test]
        public async Task ChainRingCurvatureRefusesADisagreeingRing()
        {
            var feModel = RingCurvatureModel(
                SyntheticCloth.BandedRod(1, 2, 2.3344536f, 10f, 1f)
                + SyntheticCloth.BandedRod(2, 3, 5.8778525f, 10f, 1f));

            await Assert.That(feModel.ChainRingCurvature).IsEqualTo(0f);
        }

        private static FeModel RingCurvatureModel(string rods) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "j", "$ccj_0", "$ccj_1", "$ccj_2" ]
                m_SkelParents = [ -1, 0, 0, 0 ]
                m_nNodeCount = 4
                m_nStaticNodes = 0
                m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 1f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, 1f)}}
                    {{SyntheticCloth.Pose(0f, -1f, 0f)}}
                ]
                m_Rods = [ {{rods}} ]
            }
            """);

        /// <summary>
        /// Every element credits both ends of each of its own corner pairs with 4 per unit of rest
        /// length, and the authored <c>mass</c> multiplier is squared into the result. On a 3x4
        /// rectangle every corner owns a side of 3, a side of 4 and the diagonal of 5, so its geometric
        /// term is 4 * 12 = 48; at multiplier 1.5 the node weighs 48 * 2.25 = 108.
        /// </summary>
        [Test]
        public async Task ElementMassCreditsFourPerUnitOfEachCornerPair()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "v0", "v1", "v2", "v3" ]
                    m_nNodeCount = 4
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ 0.009259259, 0.009259259, 0.009259259, 0.009259259 ]
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
        /// On a cloth with no proxy sheet a shipped rod credits both its ends with 8 per unit of rest
        /// length: a rod of length 3 gives 24, and at multiplier 1.5 the node weighs 24 * 2.25 = 54.
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
        /// A rod carrying the unbounded maximum joins the network after the mass pass, so it weighs
        /// nothing and the same shipped mass is no longer explained by a multiplier.
        /// </summary>
        [Test]
        public async Task AnUnboundedRodDoesNotWeigh()
        {
            var feModel = RodMassModel(SyntheticCloth.BandedRod(0, 1, 3f, FeModel.UnboundedRodDistance, 1f));

            await Assert.That(feModel.RecoverMassMultiplier(0)).IsNull();
        }

        private static FeModel RodMassModel(string rod) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "a", "b" ]
                m_SkelParents = [ -1, -1 ]
                m_nNodeCount = 2
                m_nStaticNodes = 0
                m_NodeInvMasses = [ 0.018518519, 0.018518519 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(3f, 0f, 0f)}}
                ]
                m_Rods = [ {{rod}} ]
            }
            """);

        /// <summary>
        /// A volumetrically solved selection credits every node it covers with 12 per unit of the summed
        /// bounding-box extent of its own members. The two nodes span (1, 2, 3), so the extent sums to 6
        /// and the term is 72; at multiplier 1.5 the node weighs 72 * 2.25 = 162.
        /// </summary>
        [Test]
        public async Task VolumetricSelectionCreditsTwelvePerUnitOfExtent()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "a", "b" ]
                    m_nNodeCount = 2
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ 0.006172839, 0.006172839 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(1f, 2f, 3f)}}
                    ]
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
                }
                """);

            using (Assert.Multiple())
            {
                await Assert.That(feModel.VertexMaps.Count).IsEqualTo(1);
                await Assert.That(feModel.RecoverMassMultiplier(0)!.Value).IsEqualTo(1.5f).Within(1e-3f);
                await Assert.That(feModel.RecoverMassMultiplier(1)!.Value).IsEqualTo(1.5f).Within(1e-3f);
            }
        }

        /// <summary>
        /// Where one node pair carries both a chain's own rod and a separate constraint, the chain claims
        /// the copy whose rigidity and relaxation factor match what it generates, whatever order the two
        /// stand in. The banded copy is listed first here and is still the one handed back as surplus.
        /// </summary>
        [Test]
        public async Task GetUngeneratedRodsKeepsTheChainRodAndReturnsTheBandedCopy()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1" ]
                    m_SkelParents = [ -1, 0 ]
                    m_nNodeCount = 2
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -3f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.BandedRod(0, 1, 1f, 5f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 3f, 0.6f)}}
                    ]
                }
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
        /// A planarized shape leaves one collision plane per node of its selection and no rigid, and each
        /// plane is the shape surface at that node. Six nodes six units out from a sphere of centre
        /// (1, 2, 3) and radius 4 therefore recover that sphere exactly.
        /// </summary>
        [Test]
        public async Task PlanarizedSphereRecoversItsCentreAndRadius()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "bone", "n0", "n1", "n2", "n3", "n4", "n5" ]
                    m_SkelParents = [ -1, 0, 0, 0, 0, 0, 0 ]
                    m_nNodeCount = 7
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(7f, 2f, 3f)}}
                        {{SyntheticCloth.Pose(-5f, 2f, 3f)}}
                        {{SyntheticCloth.Pose(1f, 8f, 3f)}}
                        {{SyntheticCloth.Pose(1f, -4f, 3f)}}
                        {{SyntheticCloth.Pose(1f, 2f, 9f)}}
                        {{SyntheticCloth.Pose(1f, 2f, -3f)}}
                    ]
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
                }
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
        /// Chains come back ordered by the lowest SIMULATED control node any of their joints occupies,
        /// which is the order the compiler lays their simulated nodes out in. Here the chain rooted at
        /// the HIGHER static node owns the lower simulated node, so it must come first.
        /// </summary>
        [Test]
        public async Task ChainsAreOrderedByTheirLowestSimulatedNode()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "rootA", "rootB", "jB", "jA" ]
                    m_SkelParents = [ -1, -1, 1, 0 ]
                    m_nNodeCount = 4
                    m_nStaticNodes = 2
                    m_NodeInvMasses = [ 0.0, 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(10f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(10f, 0f, -5f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -5f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 3, 5f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 5f, 1f)}}
                    ]
                }
                """);

            var chains = feModel.BuildBoneChains();

            await Assert.That(chains.Count).IsEqualTo(2);

            using (Assert.Multiple())
            {
                await Assert.That(chains[0].RootBone).IsEqualTo("rootB");
                await Assert.That(chains[1].RootBone).IsEqualTo("rootA");

                // The root node indices run the other way, so this cannot pass by accident.
                await Assert.That(chains[0].Joints[0].Node).IsEqualTo(1);
                await Assert.That(chains[1].Joints[0].Node).IsEqualTo(0);
            }
        }

        /// <summary>
        /// One declaration numbers its rings continuously, so a suffix index at or below one already seen
        /// starts a second declaration of the same bone and the chain splits in two, each carrying its
        /// own ring.
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
        /// The compiler writes a node's <c>flAnimationForceAttraction</c> as the cube of the authored
        /// goal strength: 0.7 cubed is 0.343 and 0.2 cubed is 0.008.
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
        /// The vertex attraction is the builder's solve over the force attraction and the authored
        /// damping, so the damping comes back out of the pair. A force attraction of 0.343 with a damping
        /// of 0.01 compiles to a vertex attraction of 0.370103, which is what the donkey fixture ships.
        /// </summary>
        [Test]
        public async Task GoalDampingInvertsTheAttractionSolve()
        {
            await Assert.That(FeModel.GoalDampingFromAttraction(0.343f, 0.370103f))
                .IsEqualTo(0.01f).Within(1e-4f);
        }

        /// <summary>
        /// Outside the solve range the compiler writes the damping through unchanged, so the inverse is
        /// the identity rather than the solve.
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

        private static FeModel TwistModel(int orient, int end, float relax) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "j1", "$ccj1_0" ]
                m_SkelParents = [ -1, 0, 1 ]
                m_nNodeCount = 3
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                m_Twists =
                [
                    { nNodeOrient = {{orient}} nNodeEnd = {{end}} flTwistRelax = {{SyntheticCloth.Num(relax)}} },
                ]
            }
            """);

        /// <summary>
        /// A stiff hinge spreads its stiffness over the bend as
        /// <c>stiffness * 3 * [-2 * mMid, mEnd0, mEnd1] / (4 * mMid + mEnd0 + mEnd1)</c>. With equal
        /// inverse masses that is <c>stiffness * [-1, 0.5, 0.5]</c>, and the height inverts to the
        /// authored angle: sqrt(2 + 2 - 2 * 2 * cos(120 degrees)) / 3 = 0.8164966.
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
        /// A fully biased joint drops the mass share and puts the whole stiffness on one end, leaving the
        /// bent node weightless: an end weight of 1.5 is a stiffness of 0.5 at full bias.
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

        private static FeModel KelagerModel(string weights, float height) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "mid", "end0", "end1" ]
                m_SkelParents = [ -1, 0, 0 ]
                m_nNodeCount = 3
                m_nStaticNodes = 0
                m_NodeInvMasses = [ 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 1f, 0f)}}
                    {{SyntheticCloth.Pose(-1f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(1f, 0f, 0f)}}
                ]
                m_KelagerBends =
                [
                    { nNode = [ 0, 1, 2 ] flWeight = [ {{weights}} ] flHeight0 = {{SyntheticCloth.Num(height)}} },
                ]
            }
            """);

        /// <summary>
        /// Each extra solver iteration repeats the rods a joint generates upward, so three rigid copies
        /// of the parent span are two extra iterations.
        /// </summary>
        [Test]
        public async Task ExtraIterationsCountsTheRigidCopiesOfASpan()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1" ]
                    m_SkelParents = [ -1, 0 ]
                    m_nNodeCount = 2
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -3f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 3f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 3f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 3f, 1f)}}
                    ]
                }
                """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.ExtraIterations).IsEqualTo(2);
                await Assert.That(joint.Suspender).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// A chain whose joints hold an <c>antishrink</c> below one repeats SLACK spans, so the copies
        /// of one are counted the same way: three identical slack rods on the parent span are two extra
        /// iterations, exactly as three rigid ones are.
        /// </summary>
        [Test]
        public async Task ExtraIterationsCountsIdenticalSlackCopiesOfASpan()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1" ]
                    m_SkelParents = [ -1, 0 ]
                    m_nNodeCount = 2
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -3f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.BandedRod(0, 1, 0f, 3f, 1f)}}
                        {{SyntheticCloth.BandedRod(0, 1, 0f, 3f, 1f)}}
                        {{SyntheticCloth.BandedRod(0, 1, 0f, 3f, 1f)}}
                    ]
                }
                """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.ExtraIterations).IsEqualTo(2);
                await Assert.That(joint.Suspender).IsEqualTo(0f);
            }
        }

        /// <summary>
        /// The compiler copies a chain joint's <c>antishrink</c> into the contraction factor of every rod
        /// its own spans generate, so a span rod holding a quarter of its rest span states 0.25.
        /// </summary>
        [Test]
        public async Task ChainJointAntishrinkIsTheSlackItsOwnSpanKeeps()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1" ]
                    m_SkelParents = [ -1, 0 ]
                    m_nNodeCount = 2
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -3f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.BandedRod(0, 1, 0.75f, 3f, 1f)}}
                    ]
                }
                """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            await Assert.That(joint!.Antishrink).IsEqualTo(0.25f);
        }

        /// <summary>
        /// Slack rods sharing a pair count as repeats of one another only when they are the same record:
        /// the importer copies one authored rod verbatim, so a pair whose slack rods disagree carries two
        /// different constraints rather than a repeated one, and states neither an extra iteration nor an
        /// antishrink.
        /// </summary>
        [Test]
        public async Task SlackRodsThatDisagreeAreNotExtraIterations()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1" ]
                    m_SkelParents = [ -1, 0 ]
                    m_nNodeCount = 2
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -3f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.BandedRod(0, 1, 0f, 3f, 1f)}}
                        {{SyntheticCloth.BandedRod(0, 1, 1f, 3f, 1f)}}
                    ]
                }
                """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(j => j.Name == "j1");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.ExtraIterations).IsEqualTo(0);
                await Assert.That(joint.Antishrink).IsEqualTo(1f);
            }
        }

        /// <summary>
        /// A joint's <c>child_sibling_spring</c> ties its own children to each other, one rod per
        /// unordered pair of them, and that rod carries the slider as its relaxation.
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
        /// The compiler springs EVERY pair of a joint's children or none of them, so a set missing one
        /// of its pairs was tied by something else and states no slider at all.
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

        private static FeModel.BoneChainJoint? SiblingChain(string rods) => SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1", "j2", "j3" ]
                    m_SkelParents = [ -1, 0, 0, 0 ]
                    m_nNodeCount = 4
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(3f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 3f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, 3f)}}
                    ]
                    m_Rods =
                    [
                        {{rods}}
                    ]
                }
                """).BuildBoneChains()[0].Joints.Find(j => j.Name == "root");

        /// The compiler splits a solve-element quad whose two halves are not coplanar enough and gives the
        /// diagonal it discards a rod of its own, so the exporter must not declare that pair a second time.
        /// The rod spans the LONGER diagonal, hinged about the shorter one the split keeps.
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

            var rods = FeModel.BentQuadRodsFromFaces([[0, 1, 2, 3]], corners, static _ => false);

            using (Assert.Multiple())
            {
                await Assert.That(rods.Count).IsEqualTo(1);
                await Assert.That(rods.Contains((0, 2))).IsTrue();
            }
        }

        /// <summary>
        /// A flat quad is kept whole and a quad with a static corner is never split at all, so neither
        /// hands the exporter a pair to leave undeclared.
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
                await Assert.That(FeModel.BentQuadRodsFromFaces([[0, 1, 2, 3]], flat, static _ => false))
                    .IsEmpty();
                await Assert.That(FeModel.BentQuadRodsFromFaces([[0, 1, 2, 3]], bent, static node => node == 3))
                    .IsEmpty();
            }
        }

        /// <summary>
        /// A rod joining a node to itself constrains nothing and cannot be re-authored, and one missing
        /// an endpoint index is not a rod at all, so neither survives the parse.
        /// </summary>
        [Test]
        public async Task SelfRodsAndDegenerateRodsAreDropped()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "a", "b", "c", "d" ]
                    m_nNodeCount = 4
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0 ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(3, 3, 1f, 1f)}}
                        { nNode = [ 2 ] flMinDist = 1.0 flMaxDist = 1.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                        {{SyntheticCloth.RigidRod(0, 1, 2f, 1f)}}
                    ]
                }
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
        /// A vertex belongs to every selection covering it, and a membership below the full 1.0 the bare
        /// name already means is written out with its weight.
        /// </summary>
        [Test]
        public async Task VertexMapNamesCarryAPartialMembershipWeight()
        {
            var feModel = SyntheticCloth.Parse("""
                {
                    m_CtrlName = [ "a", "b" ]
                    m_nNodeCount = 2
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ 1.0, 1.0 ]
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
                }
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
        /// A chain root generates no span of its own, so its iteration count is read off the only other
        /// rods that cross it: its child's span down to it. Three rigid copies of that span are two extra
        /// iterations. A root with two children has no unambiguous stand-in and stays at one copy.
        /// </summary>
        [Test]
        public async Task AChainRootCountsItsIterationsOnItsOnlyChildsSpan()
        {
            var oneChild = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1", "j2" ]
                    m_SkelParents = [ -1, 0, 1 ]
                    m_nNodeCount = 3
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    ]
                }
                """);

            var twoChildren = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1", "j2" ]
                    m_SkelParents = [ -1, 0, 0 ]
                    m_nNodeCount = 3
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(10f, 0f, 0f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 10f, 1f)}}
                    ]
                }
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
        /// A chain whose root is one of a joint's own upward targets carries the suspender companion as a
        /// single surplus rod on that pair: the parent span holds two rigid copies and the span to the root
        /// holds three, the odd one of which is the authored suspender.
        /// </summary>
        [Test]
        public async Task ASuspenderCompanionIsTheSurplusRodOnTheRootSpan()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "j1", "j2" ]
                    m_SkelParents = [ -1, 0, 1 ]
                    m_nNodeCount = 3
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    ]
                    m_Rods =
                    [
                        {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 20f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 20f, 1f)}}
                        {{SyntheticCloth.RigidRod(0, 2, 20f, 0.35f)}}
                    ]
                }
                """);

            var joint = feModel.BuildBoneChains()[0].Joints.Find(static j => j.Name == "j2");

            using (Assert.Multiple())
            {
                await Assert.That(joint!.Suspender).IsEqualTo(0.35f).Within(1e-4f);
                await Assert.That(joint.ExtraIterations).IsEqualTo(1);
            }
        }

        /// <summary>
        /// Nothing but a suspender reaches a joint further from the chain root than its own torsion span,
        /// so every rigid rod on that pair is a copy of the one companion and they all carry its value.
        /// Two agreeing copies name a suspender of 0.42 rather than refusing the pair for being doubled.
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
        /// The suspender companion is an iterated span like the parent, bend and torsion ones, so a joint
        /// at two iterations regenerates BOTH copies of it and neither is left over to be re-declared.
        /// </summary>
        [Test]
        public async Task ASuspenderCompanionIsRegeneratedOncePerIteration()
        {
            var feModel = LongChainWithSuspender();

            await Assert.That(feModel.GetUngeneratedRods(feModel.BuildBoneChains())).IsEmpty();
        }

        private static FeModel LongChainWithSuspender() => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "j1", "j2", "j3", "j4" ]
                m_SkelParents = [ -1, 0, 1, 2, 3 ]
                m_nNodeCount = 5
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -40f)}}
                ]
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
            }
            """);

        /// <summary>
        /// A two-corner source element between two chain JOINTS is an authored spring like one between two
        /// extrude rings, so it is re-declared with the rod's own fields and the chain's own copy of that
        /// span is removed by zeroing the joint's stretch slider.
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
                await Assert.That(springs[0]).IsEqualTo((1, 2));
            }
        }

        /// <summary>
        /// A compile that wrote no node base at all had its basis hint pairs filled by its ropes, so a
        /// roped node keeps the chain rods that carry the rope and the same two-corner element is left to
        /// the chain's own span rather than re-declared as a spring.
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

        private static FeModel SpringedChain(string ropes) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "j1", "j2" ]
                m_SkelParents = [ -1, 0, 1 ]
                m_nNodeCount = 3
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                ]
                m_SourceElems = [ 0, 1, 0, 0, 1, 2 ]
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(1, 2, 10f, 0.5f)}}
                ]
                {{ropes}}
            }
            """);

        /// <summary>
        /// A planarized shape whose two end caps coincide compiles as a sphere, which loses every node a
        /// capsule covers, so it is emitted with a short axis pointing away from the nodes it owns. Five
        /// nodes six units out from a sphere of centre (1, 2, 3) leave a summed normal of +Z, so the axis
        /// runs 0.01 along -Z from the recovered centre.
        /// </summary>
        [Test]
        public async Task APlanarizedEndCapIsGivenAShortAxisAwayFromItsNodes()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "bone", "n0", "n1", "n2", "n3", "n4" ]
                    m_SkelParents = [ -1, 0, 0, 0, 0, 0 ]
                    m_nNodeCount = 6
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(7f, 2f, 3f)}}
                        {{SyntheticCloth.Pose(-5f, 2f, 3f)}}
                        {{SyntheticCloth.Pose(1f, 8f, 3f)}}
                        {{SyntheticCloth.Pose(1f, -4f, 3f)}}
                        {{SyntheticCloth.Pose(1f, 2f, 9f)}}
                    ]
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
                }
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
        /// A compiled surface face every corner of which is a declared cloth node, at least one of them a
        /// free <c>$cloth_node_</c>, comes from an authored ClothTri or ClothQuad rather than from a proxy
        /// sheet, so the sheet reconstruction leaves it alone. A face over a sheet vertex still builds one.
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
        /// A proxy-sheet vertex the back-solve recovery defers, on a compile that ships no
        /// <c>m_SkelParents</c>, keeps the bones its own offset network names: the skeleton walk resolves
        /// no anchor for it, so the synthesised fallback has nothing and the vertex would otherwise be
        /// written unskinned. $cloth_m0p3 is bound 0.7 to bone_a and 0.3 to bone_c.
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
        /// The same vertex on a compile that DOES ship <c>m_SkelParents</c> keeps the synthesised chain
        /// paint, so the offset network is a last resort rather than a second recovery.
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

        // A sheet whose $cloth_m0p3 is fitless, carries a soft offset onto a bone no other vertex anchors,
        // and is therefore deferred by RecoverAuthoredSkinWeights.
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
        /// The anti-tunnelling probes are declared in the order their target slices are concatenated
        /// into <c>m_AntiTunnelTargetNodes</c>, which on this model is the reverse of the order
        /// <c>m_AntiTunnelProbes</c> ships in.
        /// </summary>
        [Test]
        public async Task AntiTunnelProbesAreDeclaredInTheOrderTheirTargetsAreConcatenated()
        {
            var children = KVObject.Array();
            ModelExtract.AddClothAntiTunnelProbes(children, SwappedAntiTunnelProbes(), null);

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
        /// A probe's own target list keeps the compiled slice order rather than being sorted by node.
        /// </summary>
        [Test]
        public async Task AnAntiTunnelProbeKeepsTheSliceOrderOfItsTargets()
        {
            var children = KVObject.Array();
            ModelExtract.AddClothAntiTunnelProbes(children, ShuffledAntiTunnelTargets(), null);

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

        // Two probes whose target slices are laid out in the reverse of the compiled probe order: the
        // tip probe ships first and owns the LAST target slot.
        private static FeModel SwappedAntiTunnelProbes() => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "a", "b", "c", "body", "tip" ]
                m_SkelParents = [ -1, 0, 0, 0, 0, 0 ]
                m_nNodeCount = 6
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                    {{SyntheticCloth.Pose(0f, 4f, -30f)}}
                    {{SyntheticCloth.Pose(0f, 8f, -30f)}}
                ]
                m_AntiTunnelTargetNodes = [ 1, 2, 3, 4 ]
                m_AntiTunnelProbes =
                [
                    { flWeight = 1.0 nFlags = 1 nProbeNode = 5 nCount = 1 nBegin = 3
                      flActivationDistance = 1.0 flCurvatureRadius = 0.0 flBias = 0.0 },
                    { flWeight = 1.0 nFlags = 0 nProbeNode = 4 nCount = 3 nBegin = 0
                      flActivationDistance = 1.0 flCurvatureRadius = 0.0 flBias = 0.0 },
                ]
            }
            """);

        // One probe whose slice is not in ascending node order.
        private static FeModel ShuffledAntiTunnelTargets() => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "a", "b", "c", "body" ]
                m_SkelParents = [ -1, 0, 0, 0, 0 ]
                m_nNodeCount = 5
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                    {{SyntheticCloth.Pose(0f, 4f, -30f)}}
                ]
                m_AntiTunnelTargetNodes = [ 3, 1, 2 ]
                m_AntiTunnelProbes =
                [
                    { flWeight = 1.0 nFlags = 0 nProbeNode = 4 nCount = 3 nBegin = 0
                      flActivationDistance = 1.0 flCurvatureRadius = 0.0 flBias = 0.0 },
                ]
            }
            """);

        /// <summary>
        /// A ClothChain of version 2 grades a preset basis for every joint with a child over the joint's own
        /// extrusion vector and its child's, where version 1 leaves the joint to the bulk pass and its
        /// neighbour set, which on a one-wide rope reaches the parent's ring as well. On the synthetic rope
        /// below the bulk grade of j2 is X = (j3, $ccj1_0), Y = ($ccj3_0, j1): the two diagonals across the
        /// parent-to-child span tie and the later pair wins. The preset grade over j2, $ccj2_0, j3, $ccj3_0
        /// is X = (j3, $ccj2_0), Y = ($ccj3_0, j2). The entry the original carries says which pass wrote it.
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
        /// A rope whose only entry is on a leaf joint, which no version presets, decides nothing.
        /// </summary>
        [Test]
        public async Task ALeafJointBasisDecidesNoChainVersion()
        {
            var leaf = OneWideRope("nNode = 5 nNodeX0 = 5 nNodeX1 = 2 nNodeY0 = 6 nNodeY1 = 3");

            await Assert.That(leaf.ChainBasesAreBulkGraded(leaf.BuildBoneChains()[0])).IsNull();
        }

        private static FeModel OneWideRope(string nodeBase) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "j1", "$ccj1_0", "j2", "$ccj2_0", "j3", "$ccj3_0" ]
                m_SkelParents = [ -1, 0, 1, 1, 3, 3, 5 ]
                m_nNodeCount = 7
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(3f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(3f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(3f, 0f, -20f)}}
                ]
                m_SourceElems = [ 0, 0, 0, 2, 1, 2, 4, 3, 3, 4, 6, 5 ]
                m_NodeBases = [ { {{nodeBase}} } ]
            }
            """);

        /// <summary>
        /// A chain ROOT has no parent span, so its <c>stretch_spring</c> is only recorded by the rods
        /// inside its own extrusion. The compiler's ring rod spans the two ring vertices at their rest
        /// distance, so it is rigid on a joint that does not shrink, while a surface rod across the same
        /// two vertices measures a fan and is not. Here the ring pair carries both, at 0.6 and at 1.0:
        /// the full reading is contradictory and the rigid rods alone name the slider.
        /// </summary>
        [Test]
        public async Task AChainRootReadsItsStretchSpringOffItsOwnRigidRingRod()
        {
            var joint = RingWithASurfaceRod(1.0f).BuildBoneChains()[0].Joints[0];

            await Assert.That(joint.StretchStiffness).IsEqualTo(0.6f).Within(1e-4f);
        }

        /// <summary>
        /// The rigid set is a fallback, not an override: where every rod inside the extrusion already
        /// agrees, that reading stands and the rigid rods add nothing.
        /// </summary>
        [Test]
        public async Task AChainRootWhoseExtrusionAgreesKeepsTheWholeReading()
        {
            var joint = RingWithASurfaceRod(0.6f).BuildBoneChains()[0].Joints[0];

            await Assert.That(joint.StretchStiffness).IsEqualTo(0.6f).Within(1e-4f);
        }

        // A chain root extruding one two-vertex ring, whose ring pair carries the chain's own rigid rod
        // at 0.6 beside a banded rod at the caller's relaxation.
        private static FeModel RingWithASurfaceRod(float surfaceRelaxation) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "$ccroot_0", "$ccroot_1" ]
                m_SkelParents = [ -1, 0, 0 ]
                m_nNodeCount = 3
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 2f, 0f)}}
                    {{SyntheticCloth.Pose(0f, -2f, 0f)}}
                ]
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(1, 2, 4f, 0.6f)}}
                    {{SyntheticCloth.BandedRod(1, 2, 1f, 8f, surfaceRelaxation)}}
                ]
            }
            """);

        /// <summary>
        /// A PINNED proxy-sheet vertex whose soft-offset expansion leaves its <c>m_CtrlOffsets</c> anchor
        /// TIED with its heaviest rival keeps the whole authored influence list, with the anchor lifted to
        /// a strict maximum. An author who paints two bones the same weight produces exactly that tie, and
        /// collapsing it to the single rigid anchor loses every <c>m_CtrlSoftOffsets</c> record the
        /// compiler wrote for the vertex.
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

        // A sheet whose pinned $cloth_m0p0 is covered by no fit weights and carries one soft offset at
        // flAlpha 0.5, so its expansion is 0.5 on the anchor bone_a and 0.5 on bone_c.
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
        /// The hint pass writes a twisted joint's X pair from its twist record and grades the rest only for a
        /// joint that has fit influences, which a chain stages from version 1 on. A hint left at
        /// {joint, twist end, 0, 0} therefore says the chain compiled at version 0; a graded hint says nothing.
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

        private static FeModel.BoneChain TwistedRopeChain()
        {
            var chain = new FeModel.BoneChain { RootBone = "j1" };
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "j1", ParentNode = -1 });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 2, Name = "j2", ParentNode = 1, InvMass = 1f });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 3, Name = "j3", ParentNode = 2, InvMass = 1f });
            return chain;
        }

        private static FeModel TwistedRope(string hint2, string hint3) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "j1", "j2", "j3" ]
                m_SkelParents = [ -1, 0, 1, 2 ]
                m_nNodeCount = 4
                m_nStaticNodes = 2
                m_NodeInvMasses = [ 0.0, 0.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                ]
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
            }
            """);

        /// <summary>
        /// With <c>rigid_edge_hinges</c> on, every rod the compiler generates comes back with its minimum
        /// equal to its maximum whatever the sheet was authored with, so the curvature has to be read off
        /// the ring bends the same switch turns on: an empty bend array states a curvature below the
        /// builder's own pi/8 gate, and a non-empty one is inverted from the height each hub records.
        /// </summary>
        [Test]
        public async Task RigidHingeSheetReadsItsCurvatureFromTheRingBends()
        {
            using (Assert.Multiple())
            {
                // No bends at all: the builder's pi/8 gate turned every hub away, which on a sheet with no
                // bend paint is a curvature of zero.
                await Assert.That(RigidSheet(bends: "").RigidHingeCurvature).IsEqualTo(0f);
                // The fixture's hub sits between two ring members ten units away on opposite sides, so
                // (3h)^2 = 200 + 200*cos(angle): a right angle records sqrt(200)/3 = 4.714045 ...
                await Assert.That(RigidSheet(bends: Bend(4.714045f)).RigidHingeCurvature)
                    .IsEqualTo(0.5f).Within(0.001f);
                // ... and an unfolded hub records 20/3 = 6.666667.
                await Assert.That(RigidSheet(bends: Bend(6.666667f)).RigidHingeCurvature)
                    .IsEqualTo(0f).Within(0.001f);
                // A fold that reaches pi opens all the way, and every authored value at or above one
                // compiles the same, so the reading saturates instead of reporting the fraction.
                await Assert.That(RigidSheet(bends: Bend(0.1f)).RigidHingeCurvature).IsEqualTo(1f);
                // A hub whose height has fallen to its own rest distance has stopped tracking the angle,
                // and a sheet with nothing left tracking keeps the saturating value.
                await Assert.That(RigidSheet(bends: Bend(0f)).RigidHingeCurvature).IsEqualTo(1f);
                // Two hubs that disagree name no single value, so the sheet keeps it too.
                await Assert.That(RigidSheet(bends: Bend(4.714045f) + Bend(6.666667f)).RigidHingeCurvature)
                    .IsEqualTo(1f);
            }
        }

        private static string Bend(float height)
            => $"{{ nNode = [ 1, 2, 3 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = {SyntheticCloth.Num(height)} }},";

        /// <summary>
        /// A sheet marked rigid-hinged by its axial edges, whose one hub (node 1) sits between two ring
        /// members (nodes 2 and 3) ten units away on opposite sides, so the hub's own rest distance from
        /// their centroid is zero and every fold it can record still tracks the angle.
        /// </summary>
        private static FeModel RigidSheet(string bends) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2" ]
                m_nNodeCount = 4
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(10f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(-10f, 0f, 0f)}}
                ]
                m_AxialEdges = [ { nNode = [ 1, 2, 3, 3, 2, 1 ] }, ]
                m_KelagerBends = [ {{bends}} ]
            }
            """);
    }
}
