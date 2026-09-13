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
                await Assert.That(springs[0]).IsEqualTo((1, 2, 1));
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

        private static FeModel BiasedRope(string weight) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "j1", "j2", "j3" ]
                m_SkelParents = [ -1, 0, 1, 2 ]
                m_nNodeCount = 4
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                ]
                m_Rods =
                [
                    { nNode = [ 1, 2 ] flMaxDist = 10.0 flMinDist = 10.0 flWeight0 = {{weight}} flRelaxationFactor = 1.0 },
                ]
            }
            """);

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

        /// <summary>
        /// The repeats an <c>extra_iterations</c> reading rests on are the count every pair of a span
        /// reaches, not a count they all have to share: a pair some other construct declares as well
        /// carries a rod on top of the repeats, and the joint's own iteration count is still the floor.
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

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "$ccroot_0", "$ccroot_1", "j1", "$ccj1_0", "$ccj1_1" ]
                    m_SkelParents = [ -1, 0, 0, 0, 3, 3 ]
                    m_nNodeCount = 6
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 2f, 0f)}}
                        {{SyntheticCloth.Pose(0f, -2f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -4f)}}
                        {{SyntheticCloth.Pose(0f, 2f, -4f)}}
                        {{SyntheticCloth.Pose(0f, -2f, -4f)}}
                    ]
                    m_Rods =
                    [
                        {{rods}}
                    ]
                }
                """);
        }

        /// <summary>
        /// The compiler adds <c>goal_strength_bias</c> to a node's goal strength before cubing it into
        /// the force attraction, while the vertex attraction keeps the unbiased cube, so a model whose
        /// two attractions sit a constant cube root apart names the bias. Only a goal-damped node says
        /// anything: a raw-integrator node ships an unrelated pair.
        /// </summary>
        [Test]
        public async Task GoalStrengthBiasIsTheCubeRootGapTheGoalDampedNodesShare()
        {
            using (Assert.Multiple())
            {
                await Assert.That(BiasedGoals(bias: 0.02f, flags: "128").GoalStrengthBias)
                    .IsEqualTo(0.02f).Within(0.0001f);
                // No gap at all is no bias, and it has to recover EXACTLY zero or every paint moves.
                await Assert.That(BiasedGoals(bias: 0f, flags: "128").GoalStrengthBias).IsEqualTo(0f);
                // The same numbers on the RAW integrator constrain nothing.
                await Assert.That(BiasedGoals(bias: 0.02f, flags: "1024").GoalStrengthBias).IsEqualTo(0f);

                // The paint takes the bias back out below saturation, and leaves a SATURATED attraction
                // alone: the compiler clamped the sum before cubing it, so the bias is not in there to
                // take back out, and the node's own vertex attraction still pins the strength.
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
                integrators.Append($"{{ flPointDamping = 0.0 flAnimationForceAttraction = {SyntheticCloth.Num(force)} "
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

        /// <summary>
        /// A chain authored with <c>stretch_spring = 0</c> compiles no rod between consecutive joints. Its
        /// top link is then recorded only by the bend rod that SPANS the second joint, running from that
        /// joint's own skeleton parent to its child.
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

        private static FeModel StretchlessChain(string rods) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "j0", "j1", "j2", "j3" ]
                m_SkelParents = [ -1, 0, 1, 2 ]
                m_nNodeCount = 4
                m_nStaticNodes = 0
                m_nFirstPositionDrivenNode = 4
                m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                ]
                m_Rods =
                [
                    {{rods}}
                ]
            }
            """);

        /// <summary>
        /// A <c>ClothSelfCollisionCluster</c> puts one rod on every member pair, all of them sharing one
        /// length band and carrying the builder's own relaxation and weight. A complete clique of those is
        /// read back as the cluster; a triangle sits below the member floor and stays plain rods.
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
        /// The compiler builds a chain joint's parent rod only for a non-zero stretch slider, so a chain
        /// every span of which a self-collision cluster owns declared none. A chain with no cluster on it
        /// keeps the neutral default, whatever its spans read.
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
            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "j0", "j1", "j2", "j3", "j4" ]
                    m_SkelParents = [ -1, 0, 1, 2, 3 ]
                    m_nNodeCount = 5
                    m_nStaticNodes = 0
                    m_nFirstPositionDrivenNode = 5
                    m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0, 1.0 ]
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
                        {{rods}}
                    ]
                }
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
        /// One authored proxy sheet is exported as one mesh per island, so a selection wrapping the whole
        /// sheet covers the UNION of the islands rather than any single one. Matched against one island
        /// alone it is not the sheet's container and the export paints it as a vertex set instead.
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

        private static FeModel.ProxyMesh Island(int[] nodes)
        {
            var ones = new float[nodes.Length];
            Array.Fill(ones, 1f);
            var zeroes = new float[nodes.Length];
            return new FeModel.ProxyMesh
            {
                NodeIndices = nodes,
                Positions = new Vector3[nodes.Length],
                ClothEnable = ones,
                GoalStrength = zeroes,
                GoalDamping = zeroes,
                CollisionRadius = zeroes,
                Friction = zeroes,
                Drag = zeroes,
                GroundCollision = zeroes,
                GroundFriction = zeroes,
                Gravity = zeroes,
                VertexAttraction = zeroes,
                SkinInfluences = new (string, float)[nodes.Length][],
                Faces = [],
            };
        }

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
        /// A suspender puts one companion rod on the chain ROOT. On the joint whose parent IS the root
        /// that lands on the parent span, so the pair carries two rods - which is also what one extra
        /// iteration looks like, except that an iteration repeats every span and a suspender repeats
        /// only this one. Reading it as an iteration emits a second copy of every other span too.
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

        private static FeModel SuspendedRope() => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "j1", "j2", "j3" ]
                m_SkelParents = [ -1, 0, 1, 2 ]
                m_nNodeCount = 4
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -30f)}}
                ]
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 0.5f)}}
                    {{SyntheticCloth.RigidRod(1, 2, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(2, 3, 10f, 1f)}}
                    {{SyntheticCloth.RigidRod(0, 2, 20f, 0.5f)}}
                    {{SyntheticCloth.RigidRod(0, 3, 30f, 0.5f)}}
                ]
            }
            """);

        /// <summary>
        /// A ring three nodes wide closes into a triangle, so the bend rods predicted across the tube's
        /// shared edges land on the ring's own sides. Where a side carries only its one authored rod the
        /// compiler added no derived copy and that rod weighs, so the chain's geometric masses already
        /// match the shipped ones and no joint carries a mass multiplier. The fixture is the compiled
        /// synthetic chain of four joints at <c>extrude_sides 3</c> and default mass.
        /// </summary>
        [Test]
        public async Task AThreeWideRingWeighsItsOwnSidesWhenEachCarriesOneRod()
        {
            var feModel = RingThreeWideChain();

            using (Assert.Multiple())
            {
                await Assert.That(feModel.RecoverJointMassMultiplier(13)).IsNull();
                await Assert.That(feModel.RecoverJointMassMultiplier(14)).IsNull();
                await Assert.That(feModel.RecoverJointMassMultiplier(15)).IsNull();
            }
        }

        private static FeModel RingThreeWideChain() => SyntheticCloth.Parse("""
            {
                m_CtrlName = [ "$cccoattail_0_L_0", "$cccoattail_0_L_1", "$cccoattail_0_L_2", "coattail_0_L", "$cccoattail_1_L_0", "$cccoattail_1_L_1", "$cccoattail_1_L_2", "$cccoattail_2_L_0", "$cccoattail_2_L_1", "$cccoattail_2_L_2", "$cccoattail_end_L_0", "$cccoattail_end_L_1", "$cccoattail_end_L_2", "coattail_1_L", "coattail_2_L", "coattail_end_L" ]
                m_SkelParents = [ 3, 3, 3, -1, 13, 13, 13, 14, 14, 14, 15, 15, 15, 3, 13, 14 ]
                m_nNodeCount = 16
                m_nStaticNodes = 4
                m_NodeInvMasses = [ 0.0, 0.0, 0.0, 0.0, 0.00207, 0.002057, 0.002057, 0.002061, 0.002061, 0.002061, 0.0037, 0.0037, 0.0037, 1.0, 1.0, 1.0 ]
                m_SourceElems = [ 0, 0, 0, 9, 9, 7, 10, 12, 8, 9, 12, 11, 7, 8, 11, 10, 6, 4, 7, 9, 5, 6, 9, 8, 4, 5, 8, 7, 2, 0, 4, 6, 1, 2, 6, 5, 0, 1, 5, 4 ]
                m_InitPose =
                [
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -7.534945, 5.381207, 65.015862, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -8.487852, 2.057984, 65.235313, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -10.330053, 5.649839, 56.946922, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -11.282963, 2.326618, 57.166382, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -13.441967, 6.02051, 49.047699, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -14.394877, 2.697287, 49.267155, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -16.541292, 6.387115, 41.141346, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.494204, 3.063893, 41.360802, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                    { nNode = [ 0, 4 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 5 ] flMinDist = 9.218822 flMaxDist = 9.218822 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 6 ] flMinDist = 9.218816 flMaxDist = 9.218816 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 4 ] flMinDist = 9.097388 flMaxDist = 9.097388 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 5 ] flMinDist = 8.54357 flMaxDist = 8.54357 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 6 ] flMinDist = 9.219137 flMaxDist = 9.219137 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 4 ] flMinDist = 9.097388 flMaxDist = 9.097388 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 5 ] flMinDist = 9.219141 flMaxDist = 9.219141 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 6 ] flMinDist = 8.543563 flMaxDist = 8.543563 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 3.464102 flMaxDist = 3.464102 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 4 ] flMinDist = 3.464101 flMaxDist = 3.464101 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 7 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 8 ] flMinDist = 9.177078 flMaxDist = 9.177078 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 9 ] flMinDist = 9.177081 flMaxDist = 9.177081 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 6 ] flMinDist = 3.464102 flMaxDist = 3.464102 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 7 ] flMinDist = 9.181965 flMaxDist = 9.181965 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 8 ] flMinDist = 8.498184 flMaxDist = 8.498184 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 9 ] flMinDist = 9.177101 flMaxDist = 9.177101 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 7 ] flMinDist = 9.181965 flMaxDist = 9.181965 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 8 ] flMinDist = 9.177099 flMaxDist = 9.177099 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 9 ] flMinDist = 8.498188 flMaxDist = 8.498188 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 8 ] flMinDist = 3.464103 flMaxDist = 3.464103 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 9, 7 ] flMinDist = 3.464102 flMaxDist = 3.464102 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 10 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 11 ] flMinDist = 9.178824 flMaxDist = 9.178824 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 12 ] flMinDist = 9.178822 flMaxDist = 9.178822 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 8, 9 ] flMinDist = 3.464103 flMaxDist = 3.464103 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 8, 10 ] flMinDist = 9.178805 flMaxDist = 9.178805 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 8, 11 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 8, 12 ] flMinDist = 9.178812 flMaxDist = 9.178812 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 9, 10 ] flMinDist = 9.178808 flMaxDist = 9.178808 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 9, 11 ] flMinDist = 9.178818 flMaxDist = 9.178818 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 9, 12 ] flMinDist = 8.500038 flMaxDist = 8.500038 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 10, 11 ] flMinDist = 3.464103 flMaxDist = 3.464103 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 12, 10 ] flMinDist = 3.464102 flMaxDist = 3.464102 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 11, 12 ] flMinDist = 3.464102 flMaxDist = 3.464102 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
            }
            """);

        /// <summary>
        /// A <c>ClothVertexMap</c> container's own weight compiles into every value of the selection it
        /// wraps, so a selection whose covered nodes all share one partial value carries that weight.
        /// Full coverage and a mixed selection carry none.
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
        /// A bone chain compiles no surface faces, so in a model without a single proxy sheet node a
        /// quad over declared cloth nodes can only come from an authored <c>ClothQuad</c>. The same quad
        /// in a model that also carries a sheet node stays with the sheet.
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
        /// Under <c>explicit_masses</c> a node's inverse mass is the reciprocal of its authored mass and the
        /// rod pass weighs each rod by the final masses, <c>invA / (invA + invB)</c>. The fixture is the
        /// compiled synthetic chain with mass 0.5 on its second joint: that joint reads back as 0.5 and its
        /// mass-1 neighbour as the default. The same masses over flat 0.5 rod weights are a geometric chain.
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
                await Assert.That(explicitChain.RecoverJointMassMultiplier(2)).IsNull();
                await Assert.That(flatChain.HasExplicitMasses).IsFalse();
            }
        }

        private const string ExplicitMassChainText = """
            {
                m_CtrlName = [ "coattail_0_L", "$cccoattail_0_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_2_L", "$cccoattail_2_L_0", "coattail_end_L", "$cccoattail_end_L_0" ]
                m_SkelParents = [ -1, 0, 0, 2, 2, 4, 4, 6 ]
                m_nNodeCount = 8
                m_nStaticNodes = 2
                m_NodeInvMasses = [ 0.0, 0.0, 1.0, 1.0, 2.0, 2.0, 1.0, 1.0 ]
                m_SourceElems = [ 0, 0, 0, 6, 5, 4, 6, 7, 4, 5, 7, 6, 3, 2, 4, 5, 2, 3, 5, 4, 1, 0, 2, 3, 0, 1, 3, 2 ]
                m_InitPose =
                [
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 4 ] flMinDist = 8.499931 flMaxDist = 8.499931 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 5 ] flMinDist = 8.735466 flMaxDist = 8.735466 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 4 ] flMinDist = 8.732044 flMaxDist = 8.732044 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 5 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 3 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 6 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.666667 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 7 ] flMinDist = 8.732154 flMaxDist = 8.732154 flWeight0 = 0.666667 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 6 ] flMinDist = 8.732168 flMaxDist = 8.732168 flWeight0 = 0.666667 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 7 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.666667 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
            }
            """;

        /// <summary>
        /// A three-member <c>ClothSelfCollisionCluster</c> compiles to one rod triangle whose shared band is
        /// the members' summed collision and stray radii, whatever distance each pair sits apart. The fixture
        /// is the compiled synthetic cluster over three chain joints with radii 6 and 24, a 12 to 48 band over
        /// pairs 8.5, 17 and 8.5 apart. The same triangle banded at one pair's own rest distance is a
        /// stretched surface, not a cluster.
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
        /// A cluster pair's rod carries the product of its two members' own <c>stiffness</c> as its relaxation,
        /// so a cluster is read at any relaxation that factors that way and each member's stiffness comes back.
        /// The fixture is the compiled synthetic three-member cluster: members at 0.5 put 0.25 on every pair, and
        /// a pair set of 0.25 / 0.5 / 0.5 reads as two members at 0.5 and one at 1.0.
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

        private const string ThreeMemberClusterText = """
            {
                m_CtrlName = [ "coattail_0_L", "$cccoattail_0_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_2_L", "$cccoattail_2_L_0", "coattail_end_L", "$cccoattail_end_L_0" ]
                m_SkelParents = [ -1, 0, 0, 2, 2, 4, 4, 6 ]
                m_nNodeCount = 8
                m_nStaticNodes = 2
                m_NodeInvMasses = [ 0.0, 0.0, 0.002017, 0.003444, 0.002338, 0.003427, 0.002794, 0.0065 ]
                m_SourceElems = [ 0, 0, 0, 6, 5, 4, 6, 7, 4, 5, 7, 6, 3, 2, 4, 5, 2, 3, 5, 4, 1, 0, 2, 3, 0, 1, 3, 2 ]
                m_InitPose =
                [
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 3 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 4 ] flMinDist = 12.0 flMaxDist = 48.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 5 ] flMinDist = 8.735466 flMaxDist = 8.735466 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 6 ] flMinDist = 12.0 flMaxDist = 48.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 4 ] flMinDist = 8.732044 flMaxDist = 8.732044 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 5 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 4 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 7 ] flMinDist = 8.732154 flMaxDist = 8.732154 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 5 ] flMinDist = 8.732168 flMaxDist = 8.732168 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 7 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 4 ] flMinDist = 8.499931 flMaxDist = 8.499931 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 4 ] flMinDist = 12.0 flMaxDist = 48.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
            }
            """;

        /// <summary>
        /// A geometric chain's rod pass weights every node the same, so a joint's <c>motion_bias</c> is read
        /// off its span weights even where the final masses of the span's ends differ. The fixture is the
        /// compiled synthetic chain with bias 0.5 on every joint: the tip joint's span joins inverse masses
        /// 0.0034 and 0.0065 and still carries 1/3, which reads back as 0.5, and 2/3 reads back as -0.5.
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

        private const string BiasedChainText = """
            {
                m_CtrlName = [ "coattail_0_L", "$cccoattail_0_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_2_L", "$cccoattail_2_L_0", "coattail_end_L", "$cccoattail_end_L_0" ]
                m_SkelParents = [ -1, 0, 0, 2, 2, 4, 4, 6 ]
                m_nNodeCount = 8
                m_nStaticNodes = 2
                m_NodeInvMasses = [ 0.0, 0.0, 0.003428, 0.003444, 0.003428, 0.003427, 0.0065, 0.0065 ]
                m_SourceElems = [ 0, 0, 0, 6, 5, 4, 6, 7, 4, 5, 7, 6, 3, 2, 4, 5, 2, 3, 5, 4, 1, 0, 2, 3, 0, 1, 3, 2 ]
                m_InitPose =
                [
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 4 ] flMinDist = 8.499931 flMaxDist = 8.499931 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 5 ] flMinDist = 8.735466 flMaxDist = 8.735466 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 4 ] flMinDist = 8.732044 flMaxDist = 8.732044 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 5 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 3 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 6 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 7 ] flMinDist = 8.732154 flMaxDist = 8.732154 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 6 ] flMinDist = 8.732168 flMaxDist = 8.732168 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 7 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
            }
            """;

        /// <summary>
        /// A joint authored at <c>stretch_spring</c> 0 compiles no rod on its span to its parent and none to
        /// its own ring. The fixture is the compiled synthetic chain with the spring off on coattail_1_L and
        /// coattail_end_L, with coattail_2_L between them left on. Putting coattail_1_L's ring rod back is
        /// no longer a switched-off joint.
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

        private const string AlternatingStretchText = """
            {
                m_CtrlName = [ "coattail_0_L", "$cccoattail_0_L_0", "$cccoattail_end_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_2_L", "$cccoattail_2_L_0", "coattail_end_L" ]
                m_SkelParents = [ -1, 0, 7, 0, 3, 3, 5, 5 ]
                m_nNodeCount = 8
                m_nStaticNodes = 3
                m_NodeInvMasses = [ 0.0, 0.0, 0.0, 0.007253, 0.007252, 0.0065, 0.006497, 1.0 ]
                m_SourceElems = [ 0, 0, 0, 2, 4, 3, 5, 6, 3, 4, 6, 5 ]
                m_InitPose =
                [
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                    { nNode = [ 5, 3 ] flMinDist = 8.499931 flMaxDist = 8.499931 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 3 ] flMinDist = 8.735466 flMaxDist = 8.735466 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 4 ] flMinDist = 8.732044 flMaxDist = 8.732044 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 4 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 6 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
            }
            """;

        /// <summary>
        /// A joint authored with <c>animated_length</c> compiles no rod on its span to its parent, to its own
        /// ring or on its children's spans to it, while each child keeps its own ring rod. The fixture is the
        /// compiled synthetic chain with it on coattail_2_L only. A childless joint loses the same rods to a
        /// zero <c>stretch_spring</c>, and there only the node base the animated joint keeps tells the two
        /// apart: the second fixture is the chain with it on every joint.
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

        private const string AnimatedJointTwoText = """
            {
                m_CtrlName = [ "coattail_0_L", "$cccoattail_0_L_0", "$cccoattail_2_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_end_L", "$cccoattail_end_L_0", "coattail_2_L" ]
                m_SkelParents = [ -1, 0, 7, 0, 3, 7, 5, 3 ]
                m_nNodeCount = 8
                m_nStaticNodes = 3
                m_NodeInvMasses = [ 0.0, 0.0, 0.0, 0.0065, 0.006558, 0.0625, 0.0625, 1.0 ]
                m_SourceElems = [ 0, 0, 0, 6, 2, 7, 5, 6, 7, 2, 6, 5, 4, 3, 7, 2, 3, 4, 2, 7, 1, 0, 3, 4, 0, 1, 4, 3 ]
                m_InitPose =
                [
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                    { nNode = [ 0, 3 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 4 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 4 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 4 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 6 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
            }
            """;

        private const string AnimatedEveryJointText = """
            {
                m_CtrlName = [ "coattail_0_L", "$cccoattail_0_L_0", "$cccoattail_1_L_0", "$cccoattail_2_L_0", "$cccoattail_end_L_0", "coattail_1_L", "coattail_2_L", "coattail_end_L" ]
                m_SkelParents = [ -1, 0, 5, 6, 7, 0, 5, 6 ]
                m_nNodeCount = 8
                m_nStaticNodes = 5
                m_NodeInvMasses = [ 0.0, 0.0, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0 ]
                m_SourceElems = [ 0, 0, 0, 6, 3, 6, 7, 4, 6, 3, 4, 7, 2, 5, 6, 3, 5, 2, 3, 6, 1, 0, 5, 2, 0, 1, 2, 5 ]
                m_InitPose =
                [
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                ]
            }
            """;

        /// <summary>
        /// A cloth model compiled with <c>explicit_masses</c> keeps its masses where a joint's <c>motion_bias</c>
        /// moves its span off the proportional weight. The fixture is the compiled synthetic chain with mass 2
        /// and bias 0.5 on coattail_2_L: that span reads a flat 0.5 while the tip span stays proportional at 1/3.
        /// The same chain with the tip span flattened too carries no proportional rod at all.
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

        private const string ExplicitBiasedChainText = """
            {
                m_CtrlName = [ "coattail_0_L", "$cccoattail_0_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_2_L", "$cccoattail_2_L_0", "coattail_end_L", "$cccoattail_end_L_0" ]
                m_SkelParents = [ -1, 0, 0, 2, 2, 4, 4, 6 ]
                m_nNodeCount = 8
                m_nStaticNodes = 2
                m_NodeInvMasses = [ 0.0, 0.0, 1.0, 1.0, 0.5, 0.5, 1.0, 1.0 ]
                m_SourceElems = [ 0, 0, 0, 6, 5, 4, 6, 7, 4, 5, 7, 6, 3, 2, 4, 5, 2, 3, 5, 4, 1, 0, 2, 3, 0, 1, 3, 2 ]
                m_InitPose =
                [
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 3 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 4 ] flMinDist = 8.499931 flMaxDist = 8.499931 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 5 ] flMinDist = 8.735466 flMaxDist = 8.735466 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 4 ] flMinDist = 8.732044 flMaxDist = 8.732044 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 3, 5 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 6 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 7 ] flMinDist = 8.732154 flMaxDist = 8.732154 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 6 ] flMinDist = 8.732168 flMaxDist = 8.732168 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 7 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.333333 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
            }
            """;

        /// <summary>
        /// A suspender on a joint whose parent is the chain root is read off the root span's copies even where
        /// every span of the chain is repeated, so that no single rod is left to name the chain's own
        /// relaxation: the repeats compile at a flat 1.0. The fixture is the compiled synthetic chain with
        /// suspender 0.5 and extra_iterations 2 on every joint, where coattail_1_L's span to the root carries
        /// three copies at 0.5 and three at 1.0.
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

        private const string RepeatedSuspenderChainText = """
            {
                m_CtrlName = [ "coattail_0_L", "$cccoattail_0_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_2_L", "$cccoattail_2_L_0", "coattail_end_L", "$cccoattail_end_L_0" ]
                m_SkelParents = [ -1, 0, 0, 2, 2, 4, 4, 6 ]
                m_nNodeCount = 8
                m_nStaticNodes = 2
                m_NodeInvMasses = [ 0.0, 0.0, 0.000776, 0.000781, 0.000591, 0.000591, 0.000593, 0.000594 ]
                m_SourceElems = [ 0, 0, 0, 6, 5, 4, 6, 7, 4, 5, 7, 6, 3, 2, 4, 5, 2, 3, 5, 4, 1, 0, 2, 3, 0, 1, 3, 2 ]
                m_InitPose =
                [
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 4 ] flMinDist = 16.995832 flMaxDist = 16.995832 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 5 ] flMinDist = 17.073202 flMaxDist = 17.073202 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 6 ] flMinDist = 25.49473 flMaxDist = 25.49473 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 7 ] flMinDist = 25.546371 flMaxDist = 25.546371 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 4 ] flMinDist = 17.06971 flMaxDist = 17.06971 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 5 ] flMinDist = 16.912064 flMaxDist = 16.912064 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 6 ] flMinDist = 25.516155 flMaxDist = 25.516155 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 7 ] flMinDist = 25.410963 flMaxDist = 25.410963 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 2, 3 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 2 ] flMinDist = 8.499931 flMaxDist = 8.499931 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 2 ] flMinDist = 8.735466 flMaxDist = 8.735466 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 3 ] flMinDist = 8.732044 flMaxDist = 8.732044 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 3 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 4 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 4 ] flMinDist = 8.732154 flMaxDist = 8.732154 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 5 ] flMinDist = 8.732168 flMaxDist = 8.732168 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 5 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 4 ] flMinDist = 16.995832 flMaxDist = 16.995832 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 5 ] flMinDist = 17.073202 flMaxDist = 17.073202 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 6 ] flMinDist = 25.49473 flMaxDist = 25.49473 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 7 ] flMinDist = 25.546371 flMaxDist = 25.546371 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 4 ] flMinDist = 17.06971 flMaxDist = 17.06971 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 5 ] flMinDist = 16.912064 flMaxDist = 16.912064 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 6 ] flMinDist = 25.516155 flMaxDist = 25.516155 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 7 ] flMinDist = 25.410963 flMaxDist = 25.410963 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 2, 3 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 2 ] flMinDist = 8.499931 flMaxDist = 8.499931 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 2 ] flMinDist = 8.735466 flMaxDist = 8.735466 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 3 ] flMinDist = 8.732044 flMaxDist = 8.732044 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 3 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 4 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 4 ] flMinDist = 8.732154 flMaxDist = 8.732154 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 5 ] flMinDist = 8.732168 flMaxDist = 8.732168 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 5 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 4 ] flMinDist = 16.995832 flMaxDist = 16.995832 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 5 ] flMinDist = 17.073202 flMaxDist = 17.073202 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 6 ] flMinDist = 25.49473 flMaxDist = 25.49473 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 7 ] flMinDist = 25.546371 flMaxDist = 25.546371 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 4 ] flMinDist = 17.06971 flMaxDist = 17.06971 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 5 ] flMinDist = 16.912064 flMaxDist = 16.912064 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 6 ] flMinDist = 25.516155 flMaxDist = 25.516155 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 7 ] flMinDist = 25.410963 flMaxDist = 25.410963 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 2, 3 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 2 ] flMinDist = 8.499931 flMaxDist = 8.499931 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 2 ] flMinDist = 8.735466 flMaxDist = 8.735466 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 3 ] flMinDist = 8.732044 flMaxDist = 8.732044 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 3 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 4 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 4 ] flMinDist = 8.732154 flMaxDist = 8.732154 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 5 ] flMinDist = 8.732168 flMaxDist = 8.732168 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 5 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 0.5 },
                ]
            }
            """;

        /// <summary>
        /// Under chain version 1 a zero <c>stretch_spring</c> drops a joint's node base and <c>animated_length</c>
        /// keeps it, so a joint whose children are cut off from it reads as animated wherever it keeps a base. The
        /// fixture is the compiled synthetic version 1 chain with <c>animated_length</c> on every joint, which carries
        /// no rod at all; without a base on the tip, the tip reads as a zero stretch spring instead.
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
        /// Damping only raises a node's vertex attraction, so no goal-damped node's cube-root gap exceeds the
        /// <c>goal_strength_bias</c> and an undamped node sits exactly on it. Where damping spreads most of a sheet's
        /// gaps below the bias, the largest gap three nodes share still names it; a node the compiler ships at zero
        /// attraction is painted at minus the bias's cube root, since below zero the bias is added to the strength's cube.
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
                integrators.Append($"{{ flPointDamping = 0.0 flAnimationForceAttraction = {SyntheticCloth.Num(force)} "
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
        /// A cloth effect's <c>cloth_effect_version</c> compiles to its <c>Version</c> parameter, and an effect
        /// compiled without one declares none.
        /// </summary>
        [Test]
        public async Task AClothEffectVersionIsItsVersionParameter()
        {
            var versioned = ClothWindEffect("Version = 2");
            var unversioned = ClothWindEffect(string.Empty);
            var maps = new HashSet<string>();
            var node = ModelExtract.MakeClothEffect(versioned, versioned.Effects.First(), maps);
            var plain = ModelExtract.MakeClothEffect(unversioned, unversioned.Effects.First(), maps);

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
        /// A <c>leader_type</c> 1 follower compiles to an <c>m_BoneMergeLinks</c> entry that names its leader only by
        /// the string token of the leader's bone name, so the leader is the known bone whose token matches; a link
        /// whose hash names no known bone declares nothing.
        /// </summary>
        [Test]
        public async Task ABoneMergeFollowerNamesTheBoneWhoseTokenIsItsParentHash()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "coattail_0_L", "coattail_1_L", "coattail_2_L" ]
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
                    m_BoneMergeLinks =
                    [
                        { m_nParentHash = {{ValveResourceFormat.Utils.StringToken.Get("spine_2")}} m_nChildNode = 2 },
                        { m_nParentHash = 12345 m_nChildNode = 1 },
                    ]
                }
                """);
            feModel.SkeletonBoneNames = new HashSet<string>(["pelvis", "spine_2", "coattail_0_L", "coattail_1_L", "coattail_2_L"],
                StringComparer.OrdinalIgnoreCase);
            var children = KVObject.Array();
            ModelExtract.AddClothFollowBones(children, feModel,
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
        /// A suspender of 1.0 on a joint whose parent is the chain root compiles every root span copy at the chain's own
        /// relaxation, so no copy stands apart; the joint's ring rod carrying as many copies as its base span is what
        /// names the suspender, not a repeat. The fixture is the compiled synthetic chain with suspender 1.0 on every joint.
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

        private const string SuspenderAtNaturalRelaxationText = """
            {
                m_CtrlName = [ "coattail_0_L", "$cccoattail_0_L_0", "coattail_1_L", "$cccoattail_1_L_0", "coattail_2_L", "$cccoattail_2_L_0", "coattail_end_L", "$cccoattail_end_L_0" ]
                m_SkelParents = [ -1, 0, 0, 2, 2, 4, 4, 6 ]
                m_nNodeCount = 8
                m_nStaticNodes = 2
                m_NodeInvMasses = [ 0.0, 0.0, 0.002328, 0.002343, 0.001772, 0.001774, 0.00178, 0.001781 ]
                m_SourceElems = [ 0, 0, 0, 6, 5, 4, 6, 7, 4, 5, 7, 6, 3, 2, 4, 5, 2, 3, 5, 4, 1, 0, 2, 3, 0, 1, 3, 2 ]
                m_InitPose =
                [
                    [ -8.915481, 4.000124, 65.447983, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -10.723646, 4.561181, 66.092773, 1.0, 0.337553, -0.646323, -0.495776, -0.471731 ],
                    [ -11.695464, 4.267121, 57.419937, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -13.473376, 4.824905, 58.146507, 1.0, -0.323373, 0.653533, 0.505948, 0.460804 ],
                    [ -14.808016, 4.637866, 49.519089, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -16.587204, 5.195801, 50.242416, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -17.907341, 5.004471, 41.612736, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                    [ -19.686529, 5.562407, 42.336063, 1.0, -0.323943, 0.653251, 0.505547, 0.461245 ],
                ]
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 4 ] flMinDist = 16.995832 flMaxDist = 16.995832 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 5 ] flMinDist = 17.073202 flMaxDist = 17.073202 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 6 ] flMinDist = 25.49473 flMaxDist = 25.49473 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 7 ] flMinDist = 25.546371 flMaxDist = 25.546371 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 4 ] flMinDist = 17.06971 flMaxDist = 17.06971 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 5 ] flMinDist = 16.912064 flMaxDist = 16.912064 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 6 ] flMinDist = 25.516155 flMaxDist = 25.516155 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 7 ] flMinDist = 25.410963 flMaxDist = 25.410963 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 2, 3 ] flMinDist = 2.0 flMaxDist = 2.0 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 2 ] flMinDist = 8.499931 flMaxDist = 8.499931 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 2 ] flMinDist = 8.735466 flMaxDist = 8.735466 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 3 ] flMinDist = 8.732044 flMaxDist = 8.732044 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 3 ] flMinDist = 8.503419 flMaxDist = 8.503419 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 5 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 4 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 4 ] flMinDist = 8.732154 flMaxDist = 8.732154 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 5 ] flMinDist = 8.732168 flMaxDist = 8.732168 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 7, 5 ] flMinDist = 8.500037 flMaxDist = 8.500037 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 6, 7 ] flMinDist = 2.000001 flMaxDist = 2.000001 flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 2 ] flMinDist = 8.499948 flMaxDist = 8.499948 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 0, 3 ] flMinDist = 8.646747 flMaxDist = 8.646747 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 2 ] flMinDist = 8.732066 flMaxDist = 8.732066 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMinDist = 8.412711 flMaxDist = 8.412711 flWeight0 = 0.0 flRelaxationFactor = 1.0 },
                ]
            }
            """;

        /// <summary>
        /// Selections covering the same nodes at the same weights are aliases of one <c>ClothVertexMap</c>, whose
        /// <c>aliases</c> list compiles to one entry per alias in place of the container's name. A selection at other
        /// weights, and one registered as a vertex set by the sheet's paint, are not aliases.
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
        /// A selection solved as a volume over chain joints is declared as a container whose <c>data.nodes</c> table
        /// lists every covered joint, the static root included, at its membership weight: the compiler reads
        /// <c>volumetric_solve</c> only through that table. A selection solved as a surface declares no container.
        /// </summary>
        [Test]
        public async Task AVolumetricSelectionOverChainJointsListsThemInItsNodeTable()
        {
            var feModel = SyntheticCloth.Parse(SuspenderAtNaturalRelaxationText.Replace("m_Rods =",
                "m_VertexMaps = [ " + VertexMapEntry("vmap0", 4164734239, 0, 0, 8, 0.5f) + VertexMapEntry("surface", 5, 8, 2, 6)
                + " ]\nm_VertexMapValues = [ 255, 255, 255, 255, 128, 128, 255, 255, 255, 255, 255, 255, 255, 255 ]\nm_Rods =",
                StringComparison.Ordinal));
            var children = KVObject.Array();
            ModelExtract.AddClothChainVolumetricMaps(children, feModel, feModel.BuildBoneChains());

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
        /// A stiffen effect's <c>BoneOverlay</c> compiles to its own parameter beside <c>Stiffness</c>, and an effect
        /// compiled without one declares none.
        /// </summary>
        [Test]
        public async Task AStiffenEffectsBoneOverlayIsItsOwnParameter()
        {
            var overlaid = ClothStiffenEffect("BoneOverlay = 0.5");
            var plain = ClothStiffenEffect(string.Empty);
            var maps = new HashSet<string>();
            var node = ModelExtract.MakeClothEffect(overlaid, overlaid.Effects.First(), maps);
            var bare = ModelExtract.MakeClothEffect(plain, plain.Effects.First(), maps);

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
        /// <c>stiffness_on_ragdoll</c> and <c>cloth_sleep_enabled</c> compile into the model's key values as
        /// <c>cloth_stiffness_on_ragdoll</c> and <c>cloth_sleep_enabled</c>, and nowhere in the FeModel, so the
        /// Softbody node reads them back from there; a model whose key values carry neither declares neither.
        /// </summary>
        [Test]
        public async Task SoftbodyKeysComeBackFromTheModelKeyValues()
        {
            var keyValues = KVObject.Collection();
            keyValues.Add("cloth_stiffness_on_ragdoll", 0.5f);
            keyValues.Add("cloth_sleep_enabled", true);
            var softbody = KVObject.Collection();
            ModelExtract.AddSoftbodyModelKeyValues(softbody, keyValues);
            var bare = KVObject.Collection();
            ModelExtract.AddSoftbodyModelKeyValues(bare, KVObject.Collection());

            using (Assert.Multiple())
            {
                await Assert.That(softbody.GetFloatProperty("stiffness_on_ragdoll")).IsEqualTo(0.5f);
                await Assert.That(softbody.GetBooleanProperty("cloth_sleep_enabled")).IsTrue();
                await Assert.That(bare.Count).IsEqualTo(0);
            }
        }

        /// <summary>
        /// A joint a proxy sheet back-solves has no construct of its own to carry <c>lock_translation</c>, so a lock
        /// on it (in <c>m_LockToGoal</c> or <c>m_LockToParent</c>) is declared as a <c>ClothJointLock</c> naming it.
        /// A generated node and an unlocked joint get none, and a joint the caller declares elsewhere is left out.
        /// </summary>
        [Test]
        public async Task ALockedBackSolvedJointIsDeclaredAsAJointLock()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "$cloth_m0p0", "locked_goal", "locked_parent", "goal_with_parent", "declared" ]
                    m_SkelParents = [ -1, -1, -1, 2, 0, 4 ]
                    m_nNodeCount = 6
                    m_nStaticNodes = 3
                    m_NodeInvMasses = [ 0.0, 0.0, 0.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -1f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -2f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -3f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -4f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -5f)}}
                    ]
                    m_LockToGoal = [ 1, 2 ]
                    m_LockToParent = [ { vOffset = [ 0.0, 0.0, -1.0 ] nCtrlParent = 2 nCtrlChild = 3 }, { vOffset = [ 0.0, 0.0, -1.0 ] nCtrlParent = 4 nCtrlChild = 5 } ]
                }
                """);
            var children = KVObject.Array();
            ModelExtract.AddClothJointLocks(children, feModel, static (_, name) => name != "declared");

            await Assert.That(children.Select(static c => c.Value.GetStringProperty("feeder_bone")).ToArray())
                .IsEquivalentTo(LockedJoints, CollectionOrdering.Matching);
        }

        private static readonly string[] LockedJoints = ["locked_goal", "locked_parent"];

        /// <summary>
        /// The compiler sorts a rigid array into its priority groups, so a capsule declared first at a higher
        /// priority lands after a later one; the order the parent bones were numbered in still carries the
        /// declaration. The fixture is the compiled synthetic pair spine_2 at priority 2, pelvis at priority 0.
        /// Without priority groups the array's own order is the declaration order and is kept.
        /// </summary>
        [Test]
        public async Task ShapesSortedIntoPriorityGroupsAreDeclaredInTheirParentBonesOrder()
        {
            const string Groups = "m_RigidColliderPriorities = [ "
                + "{ m_nTaperedCapsuleRigidIndex = 0 m_nSphereRigidIndex = 0 m_nBoxRigidIndex = 0 m_nSDFRigidIndex = 0 m_nCollisionPlaneIndex = 0 }, "
                + "{ m_nTaperedCapsuleRigidIndex = 1 m_nSphereRigidIndex = 0 m_nBoxRigidIndex = 0 m_nSDFRigidIndex = 0 m_nCollisionPlaneIndex = 0 }, "
                + "{ m_nTaperedCapsuleRigidIndex = 2 m_nSphereRigidIndex = 0 m_nBoxRigidIndex = 0 m_nSDFRigidIndex = 0 m_nCollisionPlaneIndex = 0 } ]";
            var grouped = ModelExtract.AddClothCollisionShapes(KVObject.Array(), PriorityCapsules(Groups));
            var ungrouped = ModelExtract.AddClothCollisionShapes(KVObject.Array(), PriorityCapsules("m_RigidColliderPriorities = [ ]"));

            using (Assert.Multiple())
            {
                await Assert.That(grouped).IsEquivalentTo(DeclaredCapsules, CollectionOrdering.Matching);
                await Assert.That(ungrouped).IsEquivalentTo(ArrayOrderCapsules, CollectionOrdering.Matching);
            }
        }

        private static readonly string[] DeclaredCapsules = ["spine_2_clothCapsule", "pelvis_clothCapsule"];
        private static readonly string[] ArrayOrderCapsules = ["pelvis_clothCapsule", "spine_2_clothCapsule"];

        private static FeModel PriorityCapsules(string groups) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "spine_2", "pelvis", "coattail_0_L" ]
                m_SkelParents = [ -1, -1, -1 ]
                m_nNodeCount = 3
                m_nStaticNodes = 3
                m_NodeInvMasses = [ 0.0, 0.0, 0.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 40f)}}
                    {{SyntheticCloth.Pose(0f, 0f, 30f)}}
                    {{SyntheticCloth.Pose(-8f, 4f, 65f)}}
                ]
                m_TaperedCapsuleRigids =
                [
                    { vSphere = [ [ 0.0, 0.0, -4.0, 4.0 ], [ 0.0, 0.0, 4.0, 4.0 ] ] nNode = 1 nCollisionMask = 15 nVertexMapIndex = 65535 nFlags = 0 },
                    { vSphere = [ [ 0.0, 0.0, -4.0, 4.0 ], [ 0.0, 0.0, 4.0, 4.0 ] ] nNode = 0 nCollisionMask = 15 nVertexMapIndex = 65535 nFlags = 0 },
                ]
                {{groups}}
            }
            """);

        private static string VertexMapEntry(string name, uint hash, int offset, int vertexBase, int count, float volumetric = 0f)
            => $"{{ sName = \"{name}\" nNameHash = {hash} nVertexBase = {vertexBase} nVertexCount = {count} nMapOffset = {offset} "
                + $"vCenterOfMass = [ 0.0, 0.0, 0.0 ] flVolumetricSolveStrength = {SyntheticCloth.Num(volumetric)} nScaleSourceNode = -1 }},";

        /// <summary>
        /// A twist link a STATIC chain root authored carries no relaxation in either direction: the
        /// compiler scales an entry by the orient node's own value only where that node simulates. The
        /// control is the same link made by the simulated child instead, whose own entry carries
        /// 0.5 * 0.618 = 0.309.
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
        /// The static root's own twist_relax is re-declared at the top of the key's range, since every
        /// value above zero compiles the same pair of entries, and the root stays unsimulated: only a
        /// root carrying a non-zero entry of its own is one the source simulated and pinned.
        /// </summary>
        [Test]
        public async Task AStaticRootRedeclaresTheTwistItsLinkRecords()
        {
            var root = ModelExtract.MakeClothJoint(TwistPair("0.0", "0.0"), StaticRootJoint());
            var control = ModelExtract.MakeClothJoint(TwistPair("0.0", "0.309"), StaticRootJoint());

            using (Assert.Multiple())
            {
                await Assert.That(root.GetFloatProperty("twist_relax")).IsEqualTo(1f);
                await Assert.That(root.GetBooleanProperty("simulate")).IsFalse();
                await Assert.That(control.GetFloatProperty("twist_relax")).IsEqualTo(0f);
            }
        }

        private static FeModel.BoneChainJoint StaticRootJoint()
            => new() { Name = "coattail_0_L", Node = 0, ParentNode = -1, InvMass = 0f };

        private static FeModel TwistPair(string toChild, string toRoot) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "coattail_0_L", "coattail_1_L" ]
                m_SkelParents = [ -1, 0 ]
                m_nNodeCount = 2
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0 ]
                m_Twists =
                [
                    { nNodeOrient = 0 nNodeEnd = 1 flTwistRelax = {{toChild}} flSwingRelax = 1.0 },
                    { nNodeOrient = 1 nNodeEnd = 0 flTwistRelax = {{toRoot}} flSwingRelax = 0.0 },
                ]
            }
            """);

        /// <summary>
        /// A <c>ClothSpring</c>'s <c>extra_iterations</c> is its rod's multiplicity: the compile appends the
        /// rod <c>1 + extra_iterations</c> times and records ONE source element, so the copies a pair carries
        /// belong to the one spring on it and leave no rod for anything else to re-declare. The spring also
        /// keeps the corner order the source element names, which the rod's own endpoints reverse. The
        /// control is the same chain with a single copy.
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

        private static FeModel SpringCopies(int copies) => SyntheticCloth.Parse($$"""
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
                m_SourceElems = [ 0, 1, 0, 0, 2, 1 ]
                m_Rods =
                [
                    {{SyntheticCloth.RigidRod(0, 1, 10f, 1f)}}
                    {{string.Concat(Enumerable.Repeat(SyntheticCloth.RigidRod(1, 2, 10f, 0.5f), copies))}}
                ]
            }
            """);
    }
}
