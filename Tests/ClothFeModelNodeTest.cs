using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace Tests
{
    /// <summary>Reading node masses, mass paints and goal integrators off a compiled FeModel.</summary>
    public class ClothFeModelNodeTest : ClothTestFixtures
    {
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
