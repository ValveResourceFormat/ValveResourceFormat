using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
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

        /// <summary>
        /// Each declaration carries the ring IT extruded, which is what says how many nodes it created
        /// and in what order. Read by name instead, both declarations of the bone would claim all four
        /// ring nodes, and one ring of four is not the same creation order as two rings of two.
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

            var rods = FeModel.BentQuadRodsFromFaces([[0, 1, 2, 3]], corners, static _ => false, 0.05f);

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
                await Assert.That(FeModel.BentQuadRodsFromFaces([[0, 1, 2, 3]], flat, static _ => false, 0.05f))
                    .IsEmpty();
                await Assert.That(FeModel.BentQuadRodsFromFaces([[0, 1, 2, 3]], bent, static node => node == 3, 0.05f))
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

        private static FeModel OneWideRope(string nodeBase) => SyntheticCloth.Parse(OneWideRopeDocument(nodeBase));

        private static string OneWideRopeDocument(string nodeBase) => $$"""
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
            """;

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
        /// A joint authored with <c>animated_length</c> moves the rods on its span to its parent, on its own
        /// ring and on its children's spans to it out of <c>m_Rods</c> and into <c>m_SimdRodsAnim</c>, while
        /// each child keeps its own ring rod. The fixture is the compiled synthetic chain with it on
        /// coattail_2_L only. A childless joint loses the same rods from <c>m_Rods</c> to a zero
        /// <c>stretch_spring</c>, and there the node base the animated joint keeps tells the two apart: the
        /// second fixture is the chain with it on every joint.
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
                m_SimdRodsAnim =
                [
                    { nNode = [ [ 2, 2, 2, 2 ], [ 7, 7, 7, 7 ] ] f4Weight0 = [ 0.0, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
                    { nNode = [ [ 3, 2, 2, 2 ], [ 7, 4, 4, 4 ] ] f4Weight0 = [ 0.5, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
                    { nNode = [ [ 4, 2, 2, 2 ], [ 7, 3, 3, 3 ] ] f4Weight0 = [ 0.5, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
                    { nNode = [ [ 5, 2, 2, 2 ], [ 7, 6, 6, 6 ] ] f4Weight0 = [ 0.5, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
                    { nNode = [ [ 6, 2, 2, 2 ], [ 7, 5, 5, 5 ] ] f4Weight0 = [ 0.5, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
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
                m_SimdRodsAnim =
                [
                    { nNode = [ [ 2, 0, 4, 4 ], [ 6, 5, 7, 7 ] ] f4Weight0 = [ 0.0, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
                    { nNode = [ [ 3, 1, 4, 4 ], [ 6, 5, 7, 7 ] ] f4Weight0 = [ 0.0, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
                    { nNode = [ [ 4, 3, 2, 2 ], [ 6, 7, 5, 5 ] ] f4Weight0 = [ 0.0, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
                    { nNode = [ [ 6, 4, 4, 4 ], [ 5, 7, 7, 7 ] ] f4Weight0 = [ 0.5, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
                    { nNode = [ [ 7, 3, 3, 3 ], [ 6, 5, 5, 5 ] ] f4Weight0 = [ 0.5, 0.0, 0.0, 0.0 ] f4RelaxationFactor = [ 1.0, 1.0, 1.0, 1.0 ] },
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
        /// no <c>m_Rods</c> entry at all; without a base on the tip, the tip reads as a zero stretch spring instead.
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
        /// A joint's <c>animated_length</c> routes its rods to <c>m_SimdRodsAnim</c> and does nothing else,
        /// so a joint whose extrusion carries no entry there had no rod built for it at all - a zero
        /// <c>stretch_spring</c>, not an animated length the rods would have recorded. The fixture is the
        /// compiled synthetic chain with <c>animated_length</c> on every joint and its animated rods taken
        /// away; the control is the same chain, node bases included, with them.
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

        /// <summary>
        /// A compiled skeleton is a lossy re-expression of the pose the model was authored in, while the
        /// same file's <c>m_InitPose</c> keeps the authored world position of every cloth control node.
        /// The exported skeleton is therefore posed from the rest pose, so that accumulating the emitted
        /// joint chain puts every control bone back where the file records it rather than where its own
        /// bone table accumulates to. The control is a model with no cloth, whose bones are emitted
        /// exactly as compiled.
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

        /// <summary>The control: a model with no cloth registers no correction at all.</summary>
        [Test]
        public async Task AModelWithoutClothEmitsItsCompiledSkeleton()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "townsfolk_03.vmdl_c"));
            var extract = new ModelExtract(resource, new NullFileLoader());

            using (Assert.Multiple())
            {
                await Assert.That(((Model)resource.DataBlock!).Skeleton.Bones.Length).IsEqualTo(56);
                await Assert.That(extract.ClothRestBonePositions.Count).IsEqualTo(0);
                await Assert.That(extract.ClothProxyRestBonePositions.Count).IsEqualTo(0);
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
                    + Vector3.Transform(ModelExtract.BonePosition(bone, extract.ClothRestBonePositions), parentRotation);
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
        /// A joint's <c>motion_bias</c> weights the rods of its span to its parent, and where the parent declared
        /// <c>animated_length</c> those rods compile into <c>m_SimdRodsAnim</c> alone, with the same weights. The
        /// fixture is the compiled synthetic chain with <c>animated_length</c> on coattail_2_L and bias 0.5 on
        /// coattail_end_L, whose two span lanes read 2/3; the control is the same chain with the bias left off.
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
        /// A two-member self-collision cluster over two chain joints compiles one banded rod beside the chain's
        /// own rigid span and records no source element, so the banded rod left once the chain claims the rigid
        /// one is re-declared as that cluster, half its band per member. The control carries two banded copies
        /// beside the span, which no single cluster accounts for, and stays two springs.
        /// </summary>
        [Test]
        public async Task AClusterTieBesideAChainSpanIsItsTwoMemberCluster()
        {
            var tiedModel = ClusterTieChain(1);
            var tied = KVObject.Array();
            ModelExtract.AddClothChainSurplusRods(tied, tiedModel, tiedModel.BuildBoneChains());
            var doubledModel = ClusterTieChain(2);
            var doubled = KVObject.Array();
            ModelExtract.AddClothChainSurplusRods(doubled, doubledModel, doubledModel.BuildBoneChains());

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

        private static FeModel ClusterTieChain(int ties) => SyntheticCloth.Parse($$"""
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
                    {{string.Concat(Enumerable.Repeat(SyntheticCloth.BandedRod(1, 2, 12f, 48f, 1f), ties))}}
                ]
            }
            """);

        /// <summary>
        /// From <c>chain.version</c> 1 on, the chain importer tops up a joint whose fit-influence table holds
        /// one or two entries, so a one-wide chain's tip joint owns a fit group and compiles to a reverse
        /// offset against its own ring node. At version 0 the tip's influences fall under the compiler's
        /// three-entry floor and the record is missing, which is the only difference between the two
        /// compiled synthetic originals. The controls are the version-1 original, which carries the record,
        /// and the version-0 chain with a second, two-wide leaf: a version-0 compile drops that leaf's group
        /// too, so a reverse offset on it rules version 0 out, while the same leaf without one reads version 0 as well.
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
        /// A planarized box writes no rigid, only one collision plane per node of its selection: in the
        /// parent's frame the plane passes through the node's nearest point on the box and faces the node.
        /// Nodes on the box's upper x face, its y and z faces, one edge and one corner recover the box, and the
        /// lower x face no node reaches is mirrored about the parent. The control is planes at a sphere's six
        /// axis points, which a box reproduces as well and the planarized capsule fit keeps.
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
        /// A rotation-locked static ClothNode keeps the preset node base no neighbour scan would grade for it. An
        /// entry whose X0 and one Y reference are the node itself declares <c>transform_alignment</c> 3 with the
        /// other two references, whichever side of the Y pair the compiler's handedness flip left the node on;
        /// any other entry declares 4 with its references as stored. The controls are a static node free to
        /// rotate, which the scan can grade and which keeps alignment 0, and an entry whose X1 is the node, which
        /// alignment 3 cannot compile.
        /// </summary>
        [Test]
        public async Task AStaticClothNodeDeclaresThePresetItsNodeBaseCompiledFrom()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "spine", "hip", "pin", "a", "b", "c", "d" ]
                    m_nNodeCount = 7
                    m_nStaticNodes = 3
                    m_nRotLockStaticNodes = 2
                    m_NodeInvMasses = [ 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 60f)}}
                        {{SyntheticCloth.Pose(0f, 0f, 40f)}}
                        {{SyntheticCloth.Pose(0f, 5f, 40f)}}
                        {{SyntheticCloth.Pose(-4f, 4f, 55f)}}
                        {{SyntheticCloth.Pose(-4f, -4f, 55f)}}
                        {{SyntheticCloth.Pose(-6f, 4f, 45f)}}
                        {{SyntheticCloth.Pose(-6f, -4f, 45f)}}
                    ]
                    m_NodeBases =
                    [
                        { nNode = 0 nNodeX0 = 0 nNodeX1 = 3 nNodeY0 = 4 nNodeY1 = 0 },
                        { nNode = 1 nNodeX0 = 3 nNodeX1 = 4 nNodeY0 = 6 nNodeY1 = 5 },
                        { nNode = 2 nNodeX0 = 2 nNodeX1 = 3 nNodeY0 = 2 nNodeY1 = 4 },
                        { nNode = 3 nNodeX0 = 4 nNodeX1 = 3 nNodeY0 = 3 nNodeY1 = 5 },
                    ]
                }
                """);

            var spine = ModelExtract.MakeClothNode(feModel, "spine", 0, isStaticNode: true);
            var hip = ModelExtract.MakeClothNode(feModel, "hip", 1, isStaticNode: true);
            var pin = ModelExtract.MakeClothNode(feModel, "pin", 2, isStaticNode: true);
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
        /// An effect authored under a static <c>ClothNode</c> records the node's root bone as its <c>Node</c> and keeps its
        /// direction unrotated, so it is declared under the static node rooted on that bone, here inside a folder. An
        /// AddGravity effect declares its <c>strength</c> and the <c>angles</c> of its direction. The control is an
        /// effect with no <c>Node</c>: it stays at the top level.
        /// </summary>
        [Test]
        public async Task AnEffectRecordingANodeIsDeclaredUnderThatStaticClothNode()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "spine_2", "coattail_0_L" ]
                    m_SkelParents = [ -1, 0 ]
                    m_nNodeCount = 2
                    m_nStaticNodes = 2
                    m_NodeInvMasses = [ 0.0, 0.0 ]
                    m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} {{SyntheticCloth.Pose(0f, 0f, -10f)}} ]
                    m_Effects =
                    [
                        { sName = "gravity0" nNameHash = 1 nType = 4 m_Params = { Node = 0 Strength = [ 0.353553, 0.353553, -0.0 ] } },
                        { sName = "gravity1" nNameHash = 2 nType = 4 m_Params = { Strength = [ 0.0, 0.0, -2.0 ] } },
                    ]
                }
                """);
            var (folder, folderChildren) = KVHelpers.MakeListNode("Folder");
            folderChildren.Add(EffectParentNode("spine_2", isStatic: true));
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(folder);

            ModelExtract.AddClothEffects(softbodyChildren, feModel, new HashSet<string>());

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
        /// A planarized box turned in its parent's frame is recovered in its own axes. One group stands off faces,
        /// edges and a corner of a (2, 3, 4) half-extent box; a second touches only one edge and the corner below
        /// it, so its axes come from the edge normals alone. Each recovered box reproduces every plane. The control
        /// is the first group unturned, which keeps the parent's own axes.
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
        /// A static <c>ClothNode</c> on a bone a chain claims compiles onto that bone's own node and still gives its
        /// effects that bone as <c>Node</c>, even on a simulated joint, while the export emits no static node for it.
        /// Such an effect is declared under a bare static <c>ClothNode</c> rooted on the bone, one per bone, whether the
        /// bone has no ClothNode at all or only a dynamic one. The controls: an effect whose bone has an emitted static
        /// node joins it and adds no bare node, one with no <c>Node</c> stays at the top level, and one naming a
        /// generated node gets no parent.
        /// </summary>
        [Test]
        public async Task AnEffectWhoseNodeHasNoStaticClothNodeGetsABareStaticOne()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "spine_2", "coattail_0_L", "coattail_1_L", "$cccoattail_1_L_0" ]
                    m_SkelParents = [ -1, 0, 1, 2 ]
                    m_nNodeCount = 4
                    m_nStaticNodes = 2
                    m_NodeInvMasses = [ 0.0, 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}} {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -20f)}} {{SyntheticCloth.Pose(0f, 2f, -20f)}}
                    ]
                    m_Effects =
                    [
                        { sName = "gravity_root" nNameHash = 1 nType = 4 m_Params = { Node = 1 Strength = [ 1.0, 0.0, 0.0 ] } },
                        { sName = "gravity_root2" nNameHash = 2 nType = 4 m_Params = { Node = 1 Strength = [ 0.0, 1.0, 0.0 ] } },
                        { sName = "gravity_joint" nNameHash = 3 nType = 4 m_Params = { Node = 2 Strength = [ 0.0, 0.0, 1.0 ] } },
                        { sName = "gravity_static" nNameHash = 4 nType = 4 m_Params = { Node = 0 Strength = [ 1.0, 0.0, 0.0 ] } },
                        { sName = "gravity_top" nNameHash = 5 nType = 4 m_Params = { Strength = [ 1.0, 0.0, 0.0 ] } },
                        { sName = "gravity_generated" nNameHash = 6 nType = 4 m_Params = { Node = 3 Strength = [ 1.0, 0.0, 0.0 ] } },
                    ]
                }
                """);
            var softbodyChildren = KVObject.Array();
            softbodyChildren.Add(EffectParentNode("spine_2", isStatic: true));
            softbodyChildren.Add(EffectParentNode("coattail_1_L", isStatic: false));

            ModelExtract.AddClothEffects(softbodyChildren, feModel, new HashSet<string>());

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
        /// A <c>ClothNode</c> with <c>angles</c> of its own compiles to a <c>$cloth_node_</c> element whose rest rotation
        /// is its root bone's times those angles, and it is not folded into the bone even at a zero origin. The angles
        /// come back as the rotation relative to the bone, with the origin left at zero. The compiler also turns an
        /// effect's Strength by its parent node's angles, so an effect recorded on that bone is declared under the
        /// element with its direction expressed in the element's frame. The controls: an unrotated element keeps angles 0
        /// and has its zero origin pushed off the bone, and an effect on a bone with only an unrotated node keeps the
        /// compiled direction.
        /// </summary>
        [Test]
        public async Task ARotatedClothNodeGetsItsAnglesBackAndItsEffectsInItsFrame()
        {
            var bone = Quaternion.Normalize(new Quaternion(-0.435361f, -0.55719f, -0.55719f, 0.435361f));
            var turned = bone * EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(0f, 90f, 0f));
            static string RotatedPose(Quaternion q) => $"[ 0.0, 0.0, 0.0, 1.0, {SyntheticCloth.Num(q.X)}, "
                + $"{SyntheticCloth.Num(q.Y)}, {SyntheticCloth.Num(q.Z)}, {SyntheticCloth.Num(q.W)} ],";
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "spine_2", "$cloth_node_node_spine", "$cloth_node_node_flat", "pelvis" ]
                    m_SkelParents = [ -1, 0, 0, -1 ]
                    m_nNodeCount = 4
                    m_nStaticNodes = 4
                    m_NodeInvMasses = [ 0.0, 0.0, 0.0, 0.0 ]
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
                }
                """);
            var anchors = feModel.CtrlOffsets.ToDictionary(static offset => offset.CtrlChild);
            var rotatedFound = ModelExtract.TryResolveClothNodeAnchor(feModel, anchors, 1, out var rotatedRoot, out var rotatedOrigin,
                out var rotatedAngles);
            var flatFound = ModelExtract.TryResolveClothNodeAnchor(feModel, anchors, 2, out _, out var flatOrigin, out var flatAngles);
            var element = ModelExtract.MakeClothNode(feModel, rotatedRoot!, 1, isStaticNode: true, elementName: "node_spine",
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

            ModelExtract.AddClothEffects(softbodyChildren, feModel, new HashSet<string>());

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
        /// A proxy sheet painted with one <c>cloth_mass</c> value reads back scattered by the float32 step of each
        /// shipped inverse mass, as <c>s4_mapfilter_mass_bias_2p0</c> does (2.0 painted, read 1.9999976 to
        /// 2.0000057 against a step of about 6.6e-6). Such readings collapse to their median. The controls are a
        /// painted gradient, whose readings stay as read, and a sheet with a single painted vertex.
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
        /// A colliding jiggle bone compiles its four <c>cloth_collision_layer</c> booleans into <c>m_nCollisionMask</c>:
        /// <c>s12_jiggle_collision_tip_mass</c> leaves layer 1 out and ships 13 (flags 802, rigid, length-limited,
        /// colliding), so the bone declares that layer false and the other three true. The controls are a colliding bone
        /// with all four layers (15), and a bone that does not collide, whose mask is 0.
        /// </summary>
        [Test]
        public async Task AJiggleBoneDeclaresTheCollisionLayersItsMaskLeavesOut()
        {
            static KVObject? Declare(uint flags, int mask) => ModelExtract.ProcessJiggleBone(
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
        /// A compile with no named selection ships only the vertex-set registration, which VRF rebuilds into
        /// selections. The S12 jiggle rows register two sets: hash 0 for the jiggle bone and the hash of the model's own
        /// file name, the compiler's default set, for the chain. The default set is dropped, so nothing re-declares it.
        /// The controls: another file name keeps both sets, and a selection read from <c>m_VertexMaps</c> is kept even
        /// under the matching hash.
        /// </summary>
        [Test]
        public async Task TheVertexSetNamedAfterTheModelIsNotRedeclared()
        {
            const string ModelFileName = "chain_extrude_sides_1";
            var modelHash = ValveResourceFormat.Utils.StringToken.Get(ModelFileName);
            string Body(string maps) => $$"""
                {
                    m_CtrlName = [ "root", "a", "b", "jiggle" ]
                    m_SkelParents = [ -1, 0, 1, 0 ]
                    m_nNodeCount = 4
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -5f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(5f, 0f, 0f)}}
                    ]
                    m_VertexSetNames = [ 0, {{modelHash}} ]
                    m_DynNodeVertexSet = [ 1, 1, 0 ]
                    {{maps}}
                }
                """;
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
        /// A <c>ClothAntiTunnelColliderGroup</c> naming a capsule and a chain compiles to <c>m_AntiTunnelBytecode</c> on a
        /// model with no proxy sheet (<c>s12_antitunnel_group_capsule_chain</c>: capsule 0, mask 0, then the chain's own
        /// joint and ring pairs), so the chain phase declares the group with its chains as the cloth members, each chain
        /// named once. The control: a model with no bytecode declares no group.
        /// </summary>
        [Test]
        public async Task AChainModelWithAntiTunnelBytecodeDeclaresItsColliderGroup()
        {
            static FeModel Model(string bytecode) => SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "spine_2", "coattail_0_L", "coattail_1_L" ]
                    m_SkelParents = [ -1, -1, 1 ]
                    m_nNodeCount = 3
                    m_nStaticNodes = 2
                    m_NodeInvMasses = [ 0.0, 0.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 5f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 5f, -8f)}}
                    ]
                    m_AntiTunnelBytecode = [ {{bytecode}} ]
                }
                """);

            var withBytecode = KVObject.Array();
            ModelExtract.AddClothAntiTunnelGroup(withBytecode, Model("131072, 805306368, 2, 131073, 196609"), ["spine_2_clothCapsule"],
                ["coattail_0_L", "coattail_0_L"]);
            var withoutBytecode = KVObject.Array();
            ModelExtract.AddClothAntiTunnelGroup(withoutBytecode, Model(string.Empty), ["spine_2_clothCapsule"], ["coattail_0_L"]);

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
        /// A <c>ClothStiffHinge</c> over three free cloth nodes compiles one <c>m_KelagerBends</c> record in its authored node
        /// order, measured from the hinge: <c>s12_stiffhinge_max_angle_30p0</c> hangs its nodes 4 and 8 below the hinge and
        /// ships flHeight0 1.652419, and the 90 degree row ships 2.981424. Both come back as the hinge over those element
        /// names with their angle. The control: a bend over chain joints declares no ClothStiffHinge.
        /// </summary>
        [Test]
        public async Task ABendOverFreeClothNodesComesBackAsAStiffHinge()
        {
            var feModel = SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "spine_2", "$cloth_node_hinge_n0", "$cloth_node_hinge_n1", "$cloth_node_hinge_n2", "coattail_0_L", "coattail_1_L", "coattail_2_L" ]
                    m_SkelParents = [ -1, 0, 0, 0, -1, 4, 5 ]
                    m_nNodeCount = 7
                    m_nStaticNodes = 3
                    m_NodeInvMasses = [ 0.0, 0.0, 0.0, 1.0, 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -4f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -8f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -12f)}}
                        {{SyntheticCloth.Pose(0f, 5f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 5f, -8f)}}
                        {{SyntheticCloth.Pose(0f, 5f, -16f)}}
                    ]
                    m_KelagerBends =
                    [
                        { flWeight = [ -0.0, 1.0, 2.0 ] flHeight0 = 1.652419 nNode = [ 1, 2, 3 ] nReserved = 0 },
                        { flWeight = [ -0.0, 1.0, 2.0 ] flHeight0 = 2.981424 nNode = [ 1, 2, 3 ] nReserved = 0 },
                        { flWeight = [ -2.0, 1.0, 1.0 ] flHeight0 = 0.5 nNode = [ 5, 4, 6 ] nReserved = 0 },
                    ]
                }
                """);

            var softbodyChildren = KVObject.Array();
            ModelExtract.AddClothStiffHinges(softbodyChildren, feModel);
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
        /// A jiggle bone beside a <c>Softbody</c> that holds only a <c>ClothParams</c> has no cloth node for the export to
        /// declare, and the ClothParams survives only as its iteration counts: <c>s12_jiggle_only_params</c> ships
        /// <c>m_nExtraIterations</c> and <c>m_nExtraGoalIterations</c> 1, so the model keeps its Softbody. The controls: the
        /// pak jiggle-bone models (cs2 <c>bomb_site_tarp</c>, <c>tarp_a</c>, <c>pedestal_patch</c>) ship 0 and 0, and a model
        /// with those counts but no jiggle bone is not this case.
        /// </summary>
        [Test]
        public async Task AJiggleBoneModelKeepsTheClothParamsItsIterationCountsRecord()
        {
            static FeModel Model(int extraIterations, string jiggleBones) => SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "tophat" ]
                    m_SkelParents = [ -1 ]
                    m_nNodeCount = 1
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ 1.0 ]
                    m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 60f)}} ]
                    m_nExtraIterations = {{extraIterations}}
                    m_nExtraGoalIterations = {{extraIterations}}
                    m_JiggleBones = [ {{jiggleBones}} ]
                }
                """);
            const string Jiggle = "{ m_nNode = 0 m_nJiggleParent = 0 m_jiggleBone = { m_nFlags = 38 m_flLength = 5.0 } },";

            using (Assert.Multiple())
            {
                await Assert.That(ModelExtract.HasJiggleBoneClothParams(Model(1, Jiggle))).IsTrue();
                await Assert.That(ModelExtract.HasJiggleBoneClothParams(Model(0, Jiggle))).IsFalse();
                await Assert.That(ModelExtract.HasJiggleBoneClothParams(Model(1, string.Empty))).IsFalse();
            }
        }

        /// <summary>
        /// The vertex set registered with no name (hash 0) holds the jiggle bones' nodes: the one jiggle node of the S12
        /// jiggle rows, and on dl's <c>tf2medic</c> exactly its three jiggle bones, which are also chain joints there. Rebuilt
        /// into a selection, the joints would declare <c>vertex_set_0</c> and register a named set the original does not
        /// ship, so it is dropped. The control: a selection read from <c>m_VertexMaps</c> under hash 0 is kept.
        /// </summary>
        [Test]
        public async Task TheUnnamedJiggleBoneVertexSetIsNotRedeclared()
        {
            string Body(string maps) => $$"""
                {
                    m_CtrlName = [ "root", "a", "b", "jiggle" ]
                    m_SkelParents = [ -1, 0, 1, 0 ]
                    m_nNodeCount = 4
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -5f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                        {{SyntheticCloth.Pose(5f, 0f, 0f)}}
                    ]
                    m_VertexSetNames = [ 0, 91207372 ]
                    m_DynNodeVertexSet = [ 1, 1, 0 ]
                    {{maps}}
                }
                """;
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
        /// A <c>ClothRigidCloudCluster</c> of algorithm 0 compiles its <c>parent_node</c> into <c>m_LockToGoal</c> and nothing
        /// else: <c>s12_rigid_cloud_algorithm_0_fork_children</c> equals its fork control but for <c>m_LockToGoal = [ 0 ]</c>. A
        /// lock beside version 2's preset-graded bases is therefore the cluster's, and it is declared back over the locked
        /// joint's chain children. The controls: a lock whose bases say nothing (no <c>m_NodeBases</c>, so neither grade
        /// can be read) stays format evidence, and a chain without a lock has no locked joint to declare.
        /// </summary>
        [Test]
        public async Task ALockBesidePresetBasesIsDeclaredAsARigidCloudCluster()
        {
            static FeModel Model(string locks) => SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "coattail_0_L", "coattail_1_L", "coattail_1_R" ]
                    m_SkelParents = [ -1, 0, 0 ]
                    m_nNodeCount = 3
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 60f)}}
                        {{SyntheticCloth.Pose(0f, 4f, 52f)}}
                        {{SyntheticCloth.Pose(0f, -4f, 52f)}}
                    ]
                    m_LockToGoal = [ {{locks}} ]
                }
                """);
            var chain = new FeModel.BoneChain { RootBone = "coattail_0_L" };
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 0, Name = "coattail_0_L", ParentNode = -1 });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 1, Name = "coattail_1_L", ParentNode = 0, ParentName = "coattail_0_L", InvMass = 1f });
            chain.Joints.Add(new FeModel.BoneChainJoint { Node = 2, Name = "coattail_1_R", ParentNode = 0, ParentName = "coattail_0_L", InvMass = 1f });

            var locked = Model("0");
            var lockedJoints = ModelExtract.LockedJointsWithChildren(locked, chain).ToArray();
            var cluster = ModelExtract.MakeClothRigidCloudCluster(lockedJoints[0].Joint.Name,
                lockedJoints[0].Children.Select(static child => child.Name));
            string[] members = [.. cluster.GetSubCollection("chain").GetArray("joints").Select(static joint => joint.GetStringProperty("joint_name"))];

            using (Assert.Multiple())
            {
                await Assert.That(lockedJoints.Length).IsEqualTo(1);
                await Assert.That(cluster.GetStringProperty("_class")).IsEqualTo("ClothRigidCloudCluster");
                await Assert.That(cluster.GetInt32Property("algorithm")).IsEqualTo(0);
                await Assert.That(cluster.GetStringProperty("parent_node")).IsEqualTo("coattail_0_L");
                await Assert.That(members).IsEquivalentTo(["coattail_1_L", "coattail_1_R"], CollectionOrdering.Matching);
                await Assert.That(ModelExtract.IsRigidCloudClusterLock(locked, chain)).IsFalse();
                await Assert.That(ModelExtract.LockedJointsWithChildren(Model(string.Empty), chain).Any()).IsFalse();

                // chainver reads chain_version_1 as a lock beside a root base and bulk-graded bases, and the
                // cluster row as the same lock beside preset-graded ones.
                await Assert.That(ModelExtract.ClothChainVersion(jointCount: 4, hasOtherChains: false, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: true, rigidCloudClusterLock: false, locksJoints: false, basesBulkGraded: true,
                    hintsTwistWritten: false, hasUnstagedThinJoint: false)).IsEqualTo(1);
                await Assert.That(ModelExtract.ClothChainVersion(jointCount: 5, hasOtherChains: false, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: true, rigidCloudClusterLock: true, locksJoints: false, basesBulkGraded: false,
                    hintsTwistWritten: false, hasUnstagedThinJoint: false)).IsEqualTo(2);
                await Assert.That(ModelExtract.ClothChainVersion(jointCount: 5, hasOtherChains: false, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: true, rigidCloudClusterLock: false, locksJoints: false, basesBulkGraded: false,
                    hintsTwistWritten: false, hasUnstagedThinJoint: false)).IsEqualTo(1);
            }
        }

        /// <summary>
        /// The fit pass locks a static joint that owns a fit group to a parent that is simulated or free-rotating
        /// whether or not the joint carries <c>lock_translation</c>, and a second pass locks every keyed joint to its
        /// parent, so a static joint's parent lock proves the key only where the fit pass could not have written
        /// it, or where the parent's fit covers every node the joint's own group reads, since a keyed joint stages
        /// its influences into its parent's group as well. A chain of version 2 stages no group for a static joint,
        /// so there the parent lock proves the key. The fixture's two static first joints hang off a free-rotating
        /// static root in a version-1 chain: the one owning a node base reads no key, the one owning no group reads
        /// the key. The controls: the same joint in a version-2 chain reads the key, a root fit over the first
        /// joint's basis nodes reads the key on it, and a rotation-locked root, which the fit pass never locks a
        /// child to, proves the key on both.
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
        /// A joint two rings wide lists its ring alone in the fit stager, so a simulated two-ring leaf's influence
        /// table holds its two ring nodes and nothing else, like a one-wide leaf's joint and ring, and falls under the
        /// three-entry floor unless the version-1 top-up reaches it. The fixture's two-ring leaf without a reverse
        /// offset reads as staged at version 0; the control, the same leaf owning one, as staged at version 1.
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
        /// Two sub-chains under one static root, each starting at a static joint, so no rod joins either to the root:
        /// a one-wide sub-chain whose thin tip owns no group, staged at version 0, and a two-wide one whose two-ring
        /// leaf owns a reverse offset, staged at version 1. One declaration cannot compile both, so the root is
        /// declared twice, extruding it over the sub-chain whose ring was created right after the root's and
        /// restating it ringless over the other. The controls: with the root's ring created after the thin
        /// sub-chain's ring the extruding declaration moves to the wide sub-chain; with the root rotation-locked its
        /// ring sorts apart from both and the sub-chain whose ring comes first extrudes it; with the groups swapped the
        /// ungrouped two-ring leaf reads version 0; with both ungrouped the tree stays one declaration.
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
                => (chain, hasOtherChains) => ModelExtract.ClothChainVersion(feModel, chain, hasOtherChains);

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
                await Assert.That(ModelExtract.ClothChainVersion(split, Owning(splitChains, "a1"), hasOtherChains: true)).IsEqualTo(0);
                await Assert.That(ModelExtract.ClothChainVersion(split, Owning(splitChains, "b1"), hasOtherChains: true)).IsEqualTo(1);
                await Assert.That(movedChains.Count).IsEqualTo(2);
                await Assert.That(Owning(movedChains, "a1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(Owning(movedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(1);
                await Assert.That(lockedChains.Count).IsEqualTo(2);
                await Assert.That(Owning(lockedChains, "a1").Joints[0].RingNodes.Count).IsEqualTo(1);
                await Assert.That(Owning(lockedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(flippedChains.Count).IsEqualTo(2);
                await Assert.That(ModelExtract.ClothChainVersion(flipped, Owning(flippedChains, "a1"), hasOtherChains: true)).IsEqualTo(1);
                await Assert.That(ModelExtract.ClothChainVersion(flipped, Owning(flippedChains, "b1"), hasOtherChains: true)).IsEqualTo(0);
                await Assert.That(Owning(flippedChains, "b1").Joints[0].RingNodes.Count).IsEqualTo(0);
                await Assert.That(mergedChains.Count).IsEqualTo(1);
                await Assert.That(mergedChains[0].Joints.Count).IsEqualTo(5);
                await Assert.That(ModelExtract.ClothChainVersion(merged, mergedChains[0], hasOtherChains: false)).IsEqualTo(0);
            }
        }

        private static readonly string[] ThinSubChain = ["root", "a0", "a1"];
        private static readonly string[] WideSubChain = ["root", "b0", "b1"];

        /// <summary>
        /// A keyed joint that owns a fit group stages its one-wide entry, the joint and its direct children, into its
        /// parent's group, while its own group also reads its second ring. So below version 2 a parent fit holding a
        /// fit-owning static joint and each of its direct children proves <c>lock_translation</c> although it misses the
        /// joint's grandchildren. The fixture's free-rotating static root holds a0 and a1 but, of the other sub-chain,
        /// only b0: a0 reads the key, b0 reads none, and b0 in a version-2 chain reads the key.
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
        /// <c>flex_cloth_borders</c> frees the pins a face joins to two or more simulated corners and, on a sheet that adds
        /// bones to the render mesh, gives each of them a node base; the <c>cloth_anchor_free_rotate</c> paint frees the
        /// same pins without one. The S16 row paints its pinned row free, so those pins carry no base and the flag is
        /// refused. The controls: the same pins with bases (an authored flex sheet) keep it, and a face with a single
        /// simulated corner reaches no pin at all.
        /// </summary>
        [Test]
        public async Task FlexClothBordersNeedsItsFreedPinsToCarryNodeBases()
        {
            static FeModel Model(string bases) => SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3" ]
                    m_SkelParents = [ -1, -1, 0, 1 ]
                    m_nNodeCount = 4
                    m_nStaticNodes = 2
                    m_nRotLockStaticNodes = 0
                    m_NodeInvMasses = [ 0.0, 0.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 2f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 0f, -8f)}}
                        {{SyntheticCloth.Pose(0f, 2f, -8f)}}
                    ]
                    m_NodeBases = [ {{bases}} ]
                }
                """);
            static string Base(int node)
                => $"{{ nNode = {node} nDummy = [ 0, 0, 0 ] nNodeX0 = 2 nNodeX1 = 3 nNodeY0 = 0 nNodeY1 = 1 qAdjust = [ 0.0, 0.0, 0.0, 1.0 ] }},";
            static FeModel.ProxyMesh Sheet(List<int[]> faces) => new()
            {
                NodeIndices = [0, 1, 2, 3],
                Positions = [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f)],
                ClothEnable = [0f, 0f, 1f, 1f],
                GoalStrength = new float[4],
                GoalDamping = new float[4],
                CollisionRadius = new float[4],
                Friction = new float[4],
                Drag = new float[4],
                GroundCollision = new float[4],
                GroundFriction = new float[4],
                Gravity = new float[4],
                VertexAttraction = new float[4],
                SkinInfluences = [[], [], [], []],
                Faces = faces,
            };

            var quad = Sheet([[0, 1, 3, 2]]);
            var painted = Model(string.Empty);
            var flexed = Model(Base(0) + Base(1) + Base(2) + Base(3));

            using (Assert.Multiple())
            {
                await Assert.That(ModelExtract.FlexedPinsCarryNodeBases(painted, quad)).IsFalse();
                await Assert.That(ModelExtract.FlexedPinsCarryNodeBases(flexed, quad)).IsTrue();
                await Assert.That(ModelExtract.FlexedPinsCarryNodeBases(painted, Sheet([[0, 1, 2]]))).IsTrue();
            }
        }

        /// <summary>
        /// The compiled cloth keeps no <c>quad_bend_tolerance</c>, so it is read off the split: a nearly planar quad split
        /// into a triangle pair whose discarded diagonal ships as a rigid rod was compiled below the 0.05 default (the S16
        /// make_rods row authors 0.0), and its split rod is then predicted at the recovered tolerance. The controls: the
        /// same pair without its rod, and a quad bent well past the default, keep 0.05.
        /// </summary>
        [Test]
        public async Task TheQuadBendToleranceIsReadOffTheSplit()
        {
            static FeModel Model(float bend, string rods) => SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3" ]
                    m_SkelParents = [ -1, 0, 0, 0, 0 ]
                    m_nNodeCount = 5
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 10f)}}
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(2f, 0f, bend)}}
                        {{SyntheticCloth.Pose(2f, 3f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 3f, 0f)}}
                    ]
                    m_Tris = [ { nNode = [ 1, 2, 3 ] }, { nNode = [ 1, 3, 4 ] } ]
                    m_Rods = [ {{rods}} ]
                }
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
        /// The importer creates a sheet's simulated vertices by first appearance over the declared faces and its pins by the
        /// faces that introduce them, and the node sort breaks ties within each distance from the pins by that creation
        /// order. The SIMD lanes the faces are read back in do not keep the authored order: the S16 make_rods grid, declared
        /// with its pins already in order, still creates p2 before p6 and p14 before p10. Its faces sorted by their shipped
        /// node indices reproduce the shipped order. The controls: a node-sorted order stays as it is, and an order no face
        /// sort can fix (a face holding pins 0 and 4 together) keeps the lane order.
        /// </summary>
        [Test]
        public async Task FacesAreDeclaredInTheShippedNodeOrder()
        {
            int[] gridNodes = [0, 14, 19, 24, 1, 13, 18, 23, 2, 15, 20, 25, 3, 16, 21, 26, 4, 17, 22, 27];
            static bool GridStatic(int node) => node < 13;
            static string Order(List<int[]> faces) => string.Join(" | ", faces.Select(static face => string.Join(",", face)));
            List<int[]> Choose(List<int[]> faces, IReadOnlyList<int> nodes, Func<int, bool> isStatic)
                => FeModel.ChooseFaceDeclarationOrder(faces, faces.Count, nodes, isStatic, 13, static _ => { });

            List<int[]> lanes =
            [
                [0, 4, 5, 1], [4, 8, 9, 5], [8, 12, 13, 9], [12, 16, 17, 13], [2, 6, 7, 3], [13, 14, 10, 9],
                [18, 19, 15, 14], [6, 10, 11, 7], [17, 18, 14, 13], [14, 15, 11, 10], [5, 9, 10, 6], [1, 5, 6, 2],
            ];
            const string NodeSorted = "0,4,5,1 | 4,8,9,5 | 8,12,13,9 | 12,16,17,13 | 1,5,6,2 | 5,9,10,6 | 13,14,10,9 | "
                + "17,18,14,13 | 2,6,7,3 | 6,10,11,7 | 14,15,11,10 | 18,19,15,14";
            var sorted = Choose(lanes, gridNodes, GridStatic);
            var again = Choose(sorted, gridNodes, GridStatic);

            int[] smallNodes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
            static bool SmallStatic(int node) => node < 5;
            List<int[]> unfixable = [[0, 4, 9, 5], [2, 3, 8, 7], [1, 2, 7, 6]];

            using (Assert.Multiple())
            {
                await Assert.That(Order(sorted)).IsEqualTo(NodeSorted);
                await Assert.That(Order(again)).IsEqualTo(NodeSorted);
                await Assert.That(Order(Choose(unfixable, smallNodes, SmallStatic))).IsEqualTo(Order(unfixable));
            }
        }

        /// <summary>
        /// A ClothChain of version 2 records a simulated joint's reverse offset against the Y1 node of the preset basis it
        /// grades over the joint's extrusion vector and its child's, where version 1 records it from the joint's fit group
        /// alone. On the two-wide rope below the two ring diagonals tie for X and the later pair wins, so j0's preset basis
        /// is X = ($ccj1_0, $ccj0_1), Y = ($ccj1_1, $ccj0_0), and j1's is the same one ring along. Offsets naming each
        /// joint's ring node _0 read as version 2, offsets naming node _1 as version 1, and a rope without reverse offsets
        /// keeps version 2.
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
                await Assert.That(ModelExtract.ClothChainVersion(preset, presetChain, hasOtherChains: false)).IsEqualTo(2);
                await Assert.That(ModelExtract.ClothChainVersion(fitted, fittedChain, hasOtherChains: false)).IsEqualTo(1);
                await Assert.That(ModelExtract.ClothChainVersion(bare, bareChain, hasOtherChains: false)).IsEqualTo(2);
            }
        }

        // A simulated rope of three joints 8.5 apart, each extruding a two-node ring across Y, whose joints' reverse
        // offsets name node _ringNode of their own ring, or which carries none.
        private static FeModel TwoWideRope(int? ringNode)
        {
            var offsets = ringNode is { } side
                ? string.Concat(Enumerable.Range(0, 3).Select(joint => "{ vOffset = [ 0.0, 2.0, 0.0 ] nBoneCtrl = " + (joint * 3)
                    + " nTargetNode = " + ((joint * 3) + 1 + side) + " }, "))
                : string.Empty;

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "j0", "$ccj0_0", "$ccj0_1", "j1", "$ccj1_0", "$ccj1_1", "j2", "$ccj2_0", "$ccj2_1" ]
                    m_SkelParents = [ -1, 0, 0, 0, 3, 3, 3, 6, 6 ]
                    m_nNodeCount = 9
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                    m_InitPose =
                    [
                        {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(0f, 2f, 0f)}}
                        {{SyntheticCloth.Pose(0f, -2f, 0f)}}
                        {{SyntheticCloth.Pose(-8.5f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(-8.5f, 2f, 0f)}}
                        {{SyntheticCloth.Pose(-8.5f, -2f, 0f)}}
                        {{SyntheticCloth.Pose(-17f, 0f, 0f)}}
                        {{SyntheticCloth.Pose(-17f, 2f, 0f)}}
                        {{SyntheticCloth.Pose(-17f, -2f, 0f)}}
                    ]
                    m_SourceElems = [ 1, 2, 5, 4, 4, 5, 8, 7 ]
                    m_ReverseOffsets = [ {{offsets}} ]
                }
                """);
        }

        /// <summary>
        /// A rigid-hinged sheet whose hubs fold by different angles states a <c>cloth_bend_stiffness</c> per hub. One
        /// hub records a right angle (paint 0.5), one a quarter turn (0.25), and one has fallen to its rest distance
        /// (the full fold), so no single <c>add_curvature</c> accounts for them and each hub's reading is its paint.
        /// The CONTROL is the same sheet with its two tracking hubs agreeing, which keeps the one model-wide value and
        /// no paint.
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

        private static FeModel ThreeHubRigidSheet(float firstHeight, float secondHeight) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7", "$cloth_m0p8" ]
                m_nNodeCount = 10
                m_nStaticNodes = 1
                m_NodeInvMasses = [ 0.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(10f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(-10f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(10f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(-10f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -40f)}}
                    {{SyntheticCloth.Pose(10f, 0f, -40f)}}
                    {{SyntheticCloth.Pose(-10f, 0f, -40f)}}
                ]
                m_AxialEdges = [ { nNode = [ 1, 2, 3, 3, 2, 1 ] }, ]
                m_KelagerBends =
                [
                    { nNode = [ 1, 2, 3 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = {{SyntheticCloth.Num(firstHeight)}} },
                    { nNode = [ 4, 5, 6 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = {{SyntheticCloth.Num(secondHeight)}} },
                    { nNode = [ 7, 8, 9 ] flWeight = [ -1.0, 0.5, 0.5 ] flHeight0 = 0.0 },
                ]
            }
            """);

        /// <summary>
        /// A proxy sheet painted with one <c>cloth_stretch</c> of 0.5 compiles every face rod, edges and diagonals
        /// alike, to a relaxation of (1 - 0.5)^3 = 0.125. The edges state the paint, and with its factor taken back
        /// out the diagonals carry no shear stretch of their own. The CONTROL is the same quad with rigid edges and
        /// 0.125 on the diagonals alone, which is <c>additional_shear_stretch = ln 8</c> and no stretch paint.
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

        private static FeModel StretchQuad(float edge, float diagonal) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3" ]
                m_nNodeCount = 4
                m_nStaticNodes = 0
                m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0 ]
                m_flDefaultSurfaceStretch = 0.0
                m_SourceElems = [ 0, 0, 0, 1, 0, 1, 2, 3 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(10f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(10f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                ]
                m_Rods =
                [
                    { nNode = [ 0, 1 ] flMaxDist = 10.0 flMinDist = 7.5 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(edge)}} },
                    { nNode = [ 1, 2 ] flMaxDist = 10.0 flMinDist = 7.5 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(edge)}} },
                    { nNode = [ 2, 3 ] flMaxDist = 10.0 flMinDist = 7.5 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(edge)}} },
                    { nNode = [ 0, 3 ] flMaxDist = 10.0 flMinDist = 7.5 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(edge)}} },
                    { nNode = [ 0, 2 ] flMaxDist = 14.142136 flMinDist = 10.606602 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(diagonal)}} },
                    { nNode = [ 1, 3 ] flMaxDist = 14.142136 flMinDist = 10.606602 flWeight0 = 0.5 flRelaxationFactor = {{SyntheticCloth.Num(diagonal)}} },
                ]
            }
            """);

        /// <summary>
        /// A bend network whose hinges fold apart carries every fold in its <c>cloth_bend_stiffness</c> paint. The strip's
        /// first hinge lies flat and its second folds a right angle, so a model-wide value read off the second (0.5)
        /// leaves the first a negative residual; the paint then states both folds with <c>add_curvature</c> at zero, a
        /// pair sum of 0 across the flat hinge and 1 across the folded one. CONTROLS: with both hinges folded alike
        /// the model-wide value accounts for the sheet and no paint is written, and a sheet whose suspenders read the
        /// same value keeps it.
        /// </summary>
        [Test]
        public async Task ABendNetworkWhoseHingesFoldApartCarriesEveryFoldInItsPaint()
        {
            List<int[]> faces = [[0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6]];
            HashSet<(int, int)> network = [(0, 2), (4, 6), (1, 3), (5, 7)];
            var (apart, apartCurvature) = ModelExtract.ClothBendStiffnessOverFold(FoldStrip(0f, 14.142136f), faces,
                network, 0.5f, keepsCurvature: false);
            var (alike, alikeCurvature) = ModelExtract.ClothBendStiffnessOverFold(FoldStrip(14.142136f, 14.142136f),
                faces, network, 0.5f, keepsCurvature: false);
            var (kept, keptCurvature) = ModelExtract.ClothBendStiffnessOverFold(FoldStrip(0f, 14.142136f), faces,
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

        private static FeModel FoldStrip(float firstHingeMinDist, float secondHingeMinDist) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7" ]
                m_nNodeCount = 8
                m_nStaticNodes = 0
                m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(10f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(20f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(30f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(10f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(20f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(30f, 0f, -10f)}}
                ]
                m_Rods =
                [
                    { nNode = [ 0, 2 ] flMaxDist = 20.0 flMinDist = {{SyntheticCloth.Num(firstHingeMinDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 4, 6 ] flMaxDist = 20.0 flMinDist = {{SyntheticCloth.Num(firstHingeMinDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 1, 3 ] flMaxDist = 20.0 flMinDist = {{SyntheticCloth.Num(secondHingeMinDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                    { nNode = [ 5, 7 ] flMaxDist = 20.0 flMinDist = {{SyntheticCloth.Num(secondHingeMinDist)}} flWeight0 = 0.5 flRelaxationFactor = 1.0 },
                ]
            }
            """);

        /// <summary>
        /// A bend rod that several hinges generate keeps the longest minimum any of them builds, so it states the fold of
        /// the least folded one. The grid's middle-row rods each cross a vertical hinge above them (sum 0.25) and one below
        /// (sum 0.75), and read 0.25; the bottom row's rods cross only the lower hinges and read 0.75. Taking every hinge
        /// at the tightest lower bound its rods give it recovers all three hinge sums with <c>add_curvature</c> at zero,
        /// where a model-wide 0.25 leaves nothing to paint and the rods below keep their negative residual.
        /// </summary>
        [Test]
        public async Task ARodSeveralHingesGenerateStatesTheLeastFoldedOne()
        {
            List<int[]> faces = [[0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [4, 5, 9, 8], [5, 6, 10, 9], [6, 7, 11, 10]];
            HashSet<(int, int)> network = [(0, 2), (1, 3), (4, 6), (5, 7), (8, 10), (9, 11), (0, 8), (1, 9), (2, 10), (3, 11)];
            var (paint, curvature) = ModelExtract.ClothBendStiffnessOverFold(LeastFoldedHingeGrid, faces, network, 0.375f,
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

        private static FeModel LeastFoldedHingeGrid => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7", "$cloth_m0p8", "$cloth_m0p9", "$cloth_m0p10", "$cloth_m0p11" ]
                m_nNodeCount = 12
                m_nStaticNodes = 0
                m_NodeInvMasses = [ 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(10f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(20f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(30f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(10f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(20f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(30f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(10f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(20f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(30f, 0f, -20f)}}
                ]
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
            }
            """);

        /// <summary>
        /// A painted <c>cloth_mass</c> gradient reads back from the shipped inverse masses only to their float32 step, while
        /// the face rods weigh their endpoints by the painted biases to the precision of their own weights. A 1.0 / 1.4 /
        /// 2.0 chain whose readings drifted by a few millionths comes back with the rods' own differences, 0.4 and 0.6,
        /// anchored on the readings' mean. CONTROL: with a tolerance far below that drift the rods' answer leaves the band
        /// and the readings are kept as read.
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
        /// The mass pass sums each node's corner-pair terms in declared corner order, and the quad split rotates a fully
        /// dynamic quad one corner onto its shorter diagonal, so VRF reads such a quad back one corner past the corner it
        /// was declared from. The S16 make_rods grid's last two faces, declared from the read-back corner, miss the shipped
        /// inverse masses of nodes 25 to 27 by one or two ulps, and declared one corner back they match all fifteen. The
        /// controls: the masses the read-back declaration compiles to keep the faces, and so does a shipped mass no
        /// rotation reaches, though a flip would still fix the other two nodes.
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
            ModelExtract.CompilerTransform? parent = null;
            foreach (var (name, origin, angles) in chain)
            {
                Vector3? position = positionTargets is not null && positionTargets.TryGetValue(name, out var p) ? p : null;
                Quaternion? rotation = rotationTargets is not null && rotationTargets.TryGetValue(name, out var r) ? r : null;
                var world = ModelExtract.ComposeChainBone(parent, origin, angles, position, rotation, out var landedOrigin,
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
        /// A ClothChain joint's rest pose is not the compiled skeleton accumulated. The compiler reads each Bone's
        /// printed <c>origin</c> and <c>angles</c> back as float32, makes the quaternion from float32 half angles with
        /// double sine and cosine, and composes the chain with its CTransform concat. Over the document text of
        /// <c>s12_rigid_cloud_algorithm_1_chain_stiffness_0p5</c>, that chain puts all four coattail joints on their
        /// compiled <c>m_InitPose</c> positions bit for bit, for the authored document and for VRF's rebuild alike. The
        /// control accumulates the same text as <c>System.Numerics</c> transforms, as the rest-pose correction did, and
        /// misses.
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
        /// VRF's rebuild misses the original's coattail rest pose by a few ulps. That is enough to swing an algorithm-1
        /// tri built over three of the joints, whose <c>v2.y</c> moves by up to 3.8e-3 per ulp, and a collision tree
        /// built over the joints and their rings. Keeping every ancestor as printed, six-decimal <c>angles</c> and then a
        /// six-decimal <c>origin</c> a few grid steps away land each joint on the original's rotation and position
        /// exactly, and every landed value survives the six-decimal print. Of several exact candidates the one fewest
        /// grid steps from the real-valued solution wins, the first in ascending step order on a tie. Controls:
        /// <list type="bullet">
        /// <item>coattail_0_L, whose rotation no grid angle within the search reaches under the unlanded spine, keeps its
        /// printed angles, and its position still lands, since no grid origin under its children reaches their positions
        /// from its printed one;</item>
        /// <item>coattail_end_L, whose printed angles already give the original's rotation once coattail_2_L is turned,
        /// keeps them;</item>
        /// <item>the rebuild against its own compiled rest pose, where every joint is already there, moves nothing;</item>
        /// <item>a target one ulp off the original's coattail_1_L position, which no grid origin reaches, keeps that
        /// joint's printed origin.</item>
        /// </list>
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
                    await Assert.That(SameBits(ModelExtract.CompilerTextFloat(landing.Landed[name]), landing.Landed[name])).IsTrue();
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
                    await Assert.That(SameBits(ModelExtract.CompilerTextFloat(landing.LandedAngles[name]),
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
        /// The node-base tie roll (<see cref="FeModel.BoneChainJoint.ExtrudeTwistTieNudge"/>) is chosen against the drift a
        /// rebuilt ring carries. A chain joint whose Bone origin or angles were re-solved onto the compiler's own rest pose
        /// carries no such drift, so its extrude_twist is written without the roll: order_ring_root2_joints0 prints -0.012008
        /// with it and 0.0 without, and only the latter reproduces the original's ring and wind bases. The control is the
        /// same joint left unresolved, which keeps its roll.
        /// </summary>
        [Test]
        public async Task AChainJointReSolvedOntoTheRestPoseIsWrittenWithoutItsTieRoll()
        {
            var feModel = TwistPair("0.0", "0.0");
            FeModel.BoneChainJoint RolledJoint()
                => new() { Name = "coattail_0_L", Node = 0, ParentNode = -1, InvMass = 0f, ExtrudeSides = 2, ExtrudeRadius = 2f,
                    ExtrudeTwist = 90f, ExtrudeTwistTieNudge = -0.012008f };

            var relanded = ModelExtract.MakeClothJoint(feModel, RolledJoint(), chainExtrudes: true, rollTies: false);
            var control = ModelExtract.MakeClothJoint(feModel, RolledJoint(), chainExtrudes: true);

            using (Assert.Multiple())
            {
                await Assert.That(relanded.GetFloatProperty("extrude_twist")).IsEqualTo(0f);
                await Assert.That(control.GetFloatProperty("extrude_twist")).IsEqualTo(-0.012008f).Within(1e-6f);
            }
        }

        /// <summary>
        /// A rope's hint pass writes each run node's X pair (an interior node's two neighbours, the tail its own node
        /// and the previous) and grades the rest only for a joint with fit influences, which a chain stages from version 1
        /// on. A hint left at that pair with Y (0, 0) therefore says version 0, as a twist-written one does. Controls: the
        /// same run graded, and a Y (0, 0) hint whose X pair is not the rope's, say nothing; and version 0 locks the
        /// joints format 1 locks, so an extruding chain whose original locks none of them keeps version 2.
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

                await Assert.That(ModelExtract.ClothChainVersion(jointCount: 5, hasOtherChains: true, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: false, rigidCloudClusterLock: false, locksJoints: false, basesBulkGraded: null,
                    hintsTwistWritten: true, hasUnstagedThinJoint: false)).IsEqualTo(0);
                await Assert.That(ModelExtract.ClothChainVersion(jointCount: 5, hasOtherChains: true, rootAllowsRotation: true,
                    rootHasBase: true, lockedJoint: false, rigidCloudClusterLock: false, locksJoints: true, basesBulkGraded: null,
                    hintsTwistWritten: true, hasUnstagedThinJoint: false)).IsEqualTo(2);
            }
        }

        private static FeModel RopeHinted(string hint2, string hint3) => SyntheticCloth.Parse($$"""
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
                m_nRopeCount = 1
                m_Ropes = [ 4, 1, 2, 3 ]
                m_DynNodeWindBases =
                [
                    { {{hint2}} },
                    { {{hint3}} },
                ]
            }
            """);

        /// <summary>
        /// A two-sided chain's leaf joint sits in none of its rings' elements, so the bulk grade never bases it, and
        /// from version 1 on the chain bases and stages it itself. A simulated leaf with no entry and no reverse offset
        /// therefore says version 0. Controls: a based leaf, a leaf that owns a reverse offset, and a leaf whose zero
        /// <c>stretch_spring</c> drops its base at every version, say nothing; and version 0 locks the joints format 1
        /// locks, so an original that locks none of them keeps version 2.
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

                await Assert.That(ModelExtract.ClothChainVersion(jointCount: 3, hasOtherChains: false, rootAllowsRotation: true,
                    rootHasBase: false, lockedJoint: false, rigidCloudClusterLock: false, locksJoints: false, basesBulkGraded: null,
                    hintsTwistWritten: false, hasUnstagedThinJoint: false, hasUnbasedLeaf: true)).IsEqualTo(0);
                await Assert.That(ModelExtract.ClothChainVersion(jointCount: 3, hasOtherChains: false, rootAllowsRotation: true,
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

        private static FeModel TwoSidedStrip(string nodeBases, string reverseOffsets) => SyntheticCloth.Parse($$"""
            {
                m_CtrlName = [ "root", "$ccroot_0", "$ccroot_1", "j1", "$ccj1_0", "$ccj1_1", "j2", "$ccj2_0", "$ccj2_1" ]
                m_SkelParents = [ -1, 0, 0, 0, 3, 3, 3, 6, 6 ]
                m_nNodeCount = 9
                m_nStaticNodes = 3
                m_NodeInvMasses = [ 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 ]
                m_InitPose =
                [
                    {{SyntheticCloth.Pose(0f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(3f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(-3f, 0f, 0f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(3f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(-3f, 0f, -10f)}}
                    {{SyntheticCloth.Pose(0f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(3f, 0f, -20f)}}
                    {{SyntheticCloth.Pose(-3f, 0f, -20f)}}
                ]
                m_SourceElems = [ 0, 0, 0, 2, 1, 2, 5, 4, 4, 5, 8, 7 ]
                m_NodeBases = [ {{nodeBases}} ]
                m_ReverseOffsets = [ {{reverseOffsets}} ]
            }
            """);

        /// <summary>
        /// A version-2 preset over a one-wide joint scans only the joint, its ring, its child and the child's ring, so an
        /// entry naming the parent or the parent's ring was written by the bulk pass even where its scan is not predicted
        /// exactly: the parent-ring entry below is not the pair the bulk scan picks, and it still says bulk. The same holds
        /// where the compiled data carries no skeleton parents for the ring nodes, as old-era originals do: the rings are
        /// the ones the chain reconstruction assigned. Control: an entry inside the preset's candidates that no scan
        /// predicts says nothing.
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
        /// Below version 2 the fit pass writes a static joint's parent lock only through a group its chain stages for it, and the
        /// chain stages one only where the joint's fit table (its own node lists and its children's) holds three entries, or where
        /// version 1 tops a smaller table up from the joint's parent. A joint narrower than two ring nodes lists itself. A one-wide
        /// static joint alone in its chain at version 0 (new_years_gift_shoudler's AlchNewYearsGiftStarBase_end, table 2) and a
        /// ringless static joint over one ringless child at version 0 (lion_dungeon_poacher_shoulder's tag_base, table 2) therefore
        /// hold their parent locks by <c>lock_translation</c> although they own node bases. The fit pass locks only a node that does
        /// not simulate, so a declaration simulating the joint (ds_manipulator_of_warsituation_back's clothA0_decl3) holds the lock
        /// by the key whatever its table. Controls: the lone joint at version 1 (topped up), the joint with its one-wide child at
        /// version 0 (table 4), and no chain given, each read the lock as the fit pass's.
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
                        Node = Array.IndexOf(feModel.CtrlNames, "kid"), Name = "kid", ParentNode = tip, ParentName = "tip", InvMass = 1f,
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
        /// A reverse offset states its chain's version against the preset basis over the joint's ring and its child's, so
        /// the reading needs those rings. Where the compiled data parents none of the <c>$cc</c> nodes, as old-era originals
        /// do, the chain's own rings give the same reading as the parented rope gives.
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
    }
}
