using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
    }
}
