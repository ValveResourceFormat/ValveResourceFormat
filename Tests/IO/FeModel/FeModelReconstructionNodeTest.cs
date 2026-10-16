using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Reading node masses, mass paints and goal integrators off a compiled FeModel.</summary>
    public class FeModelReconstructionNodeTest : FeModelTestModels
    {
        /// <summary>
        /// Every element credits both ends of each corner pair with 4 per unit of rest length, times the squared
        /// <c>mass</c> multiplier: a 3x4 rectangle's corner weighs 4 * 12 * 1.5^2 = 108.
        /// </summary>
        [Test]
        public async Task ElementMassCreditsFourPerUnitOfEachCornerPair()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["v0", "v1", "v2", "v3"],
                InvMasses = [0.009259259f, 0.009259259f, 0.009259259f, 0.009259259f],
                Positions = [new(0f, 0f, 0f), new(3f, 0f, 0f), new(3f, 4f, 0f), new(0f, 4f, 0f)],
                Quads = [Quad(0, 1, 2, 3)],
            }.Reconstruct();

            using (Assert.Multiple())
            {
                for (var node = 0; node < 4; node++)
                {
                    var multiplier = cloth.RecoverMassMultiplier(node);
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
            var cloth = RodMassModel(RigidRod(0, 1, 3f));

            using (Assert.Multiple())
            {
                await Assert.That(cloth.RecoverMassMultiplier(0)).IsNotNull();
                await Assert.That(cloth.RecoverMassMultiplier(0)!.Value).IsEqualTo(1.5f).Within(1e-3f);
                await Assert.That(cloth.RecoverMassMultiplier(1)!.Value).IsEqualTo(1.5f).Within(1e-3f);
            }
        }

        /// <summary>
        /// A rod with the unbounded maximum weighs nothing, so the same mass reads no multiplier.
        /// </summary>
        [Test]
        public async Task AnUnboundedRodDoesNotWeigh()
        {
            var cloth = RodMassModel(Rod(0, 1, 3f, ClothReconstruction.UnboundedRodDistance));

            await Assert.That(cloth.RecoverMassMultiplier(0)).IsNull();
        }

        private static ClothReconstruction RodMassModel(FeRodConstraint rod) => new FeModelBuilder
        {
            Names = ["a", "b"],
            Parents = [-1, -1],
            InvMasses = [0.018518519f, 0.018518519f],
            Positions = [new(0f, 0f, 0f), new(3f, 0f, 0f)],
            Rods = [rod],
        }.Reconstruct();

        /// <summary>
        /// A volume-solved selection credits each covered node 12 per unit of its members' summed extent: 72 for extent
        /// 6, and 162 at multiplier 1.5.
        /// </summary>
        [Test]
        public async Task VolumetricSelectionCreditsTwelvePerUnitOfExtent()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["a", "b"],
                InvMasses = [0.006172839f, 0.006172839f],
                Positions = [new(0f, 0f, 0f), new(1f, 2f, 3f)],
                VertexMapValues = [255, 255],
                VertexMaps = [VertexMap("body", 1, 0, 0, 2, 1f)],
            }.Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(cloth.VertexMaps.Count).IsEqualTo(1);
                await Assert.That(cloth.RecoverMassMultiplier(0)!.Value).IsEqualTo(1.5f).Within(1e-3f);
                await Assert.That(cloth.RecoverMassMultiplier(1)!.Value).IsEqualTo(1.5f).Within(1e-3f);
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
                await Assert.That(ClothReconstruction.GoalStrengthFromAttraction(0.343f)).IsEqualTo(0.7f).Within(1e-5f);
                await Assert.That(ClothReconstruction.GoalStrengthFromAttraction(0.008f)).IsEqualTo(0.2f).Within(1e-5f);
                await Assert.That(ClothReconstruction.GoalStrengthFromAttraction(1f)).IsEqualTo(1f).Within(1e-6f);
            }
        }

        /// <summary>
        /// The damping comes back out of the force and vertex attraction pair: 0.343 with a vertex attraction of
        /// 0.370103 was authored at 0.01.
        /// </summary>
        [Test]
        public async Task GoalDampingInvertsTheAttractionSolve()
        {
            await Assert.That(ClothReconstruction.GoalDampingFromAttraction(0.343f, 0.370103f))
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
                await Assert.That(ClothReconstruction.GoalDampingFromAttraction(0.99995f, 0.42f)).IsEqualTo(0.42f);
                await Assert.That(ClothReconstruction.GoalDampingFromAttraction(0f, 0.42f)).IsEqualTo(0.42f);
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
                await Assert.That(BiasedGoals(bias: 0.02f, flags: 128).GoalStrengthBias)
                    .IsEqualTo(0.02f).Within(0.0001f);
                await Assert.That(BiasedGoals(bias: 0f, flags: 128).GoalStrengthBias).IsEqualTo(0f);
                await Assert.That(BiasedGoals(bias: 0.02f, flags: 1024).GoalStrengthBias).IsEqualTo(0f);

                var biased = BiasedGoals(bias: 0.02f, flags: 128);
                await Assert.That(biased.GoalStrengthPaint(0.140608f)).IsEqualTo(0.5f).Within(0.0005f);
                await Assert.That(biased.GoalStrengthPaint(1f)).IsEqualTo(1f);
                await Assert.That(biased.GoalDampingPaint(1f, 0f))
                    .IsEqualTo(ClothReconstruction.GoalDampingFromAttraction(1f, 0f));
            }
        }

        /// <summary>
        /// Ten goal nodes whose force attraction is <c>(g + bias)^3</c> against a vertex attraction of
        /// <c>g^3</c>, with <paramref name="flags"/> as the dynamic band's own mode word (128 = goal
        /// damped, 1024 = raw).
        /// </summary>
        private static ClothReconstruction BiasedGoals(float bias, uint flags)
        {
            float[] strengths = [0.5f, 0.4f, 0.3f, 0.35f, 0.5f, 0.4f, 0.3f, 0.35f, 0.45f, 0.25f];
            return GoalNodes(flags, [.. strengths.Select(g => ((g + bias) * (g + bias) * (g + bias), g * g * g))]);
        }

        /// <summary>
        /// A static node above a column of goal nodes, one per entry of <paramref name="attractions"/> as its force and
        /// vertex attraction, under the dynamic mode word <paramref name="flags"/>.
        /// </summary>
        private static ClothReconstruction GoalNodes(uint flags, (float Force, float Vertex)[] attractions) => new FeModelBuilder
        {
            NodeCount = attractions.Length + 1,
            StaticNodes = 1,
            DynamicNodeFlags = flags,
            InvMasses = [0f, .. attractions.Select(static _ => 1f)],
            Positions = [Vector3.Zero, .. attractions.Select(static (_, i) => new Vector3(0f, 0f, -1f * (i + 1)))],
            NodeIntegrator =
            [
                new FeNodeIntegrator(0f, 0f, 0f, 360f),
                .. attractions.Select(static goal => new FeNodeIntegrator(0f, goal.Force, goal.Vertex, 360f)),
            ],
        }.Reconstruct();

        /// <summary>
        /// On a three-wide ring whose sides each carry one rod, the chain's geometric masses match the shipped ones and
        /// no joint states a mass multiplier.
        /// </summary>
        [Test]
        public async Task AThreeWideRingWeighsItsOwnSidesWhenEachCarriesOneRod()
        {
            var cloth = RingThreeWideChain();

            using (Assert.Multiple())
            {
                foreach (var joint in (int[])[13, 14, 15])
                {
                    await Assert.That(cloth.RecoverJointMassMultiplier(joint)!.Value).IsEqualTo(1f).Within(1e-3f);
                    await Assert.That(cloth.RecoverJointMass(joint, 1f)).IsNull();
                }
            }
        }

        /// <summary>
        /// A coattail of four joints, each with a three-node ring, the first joint and its ring static, held by one rod
        /// per ring side.
        /// </summary>
        private static ClothReconstruction RingThreeWideChain() => new FeModelBuilder
        {
            Names = ["$cccoattail_0_L_0", "$cccoattail_0_L_1", "$cccoattail_0_L_2", "coattail_0_L", "$cccoattail_1_L_0", "$cccoattail_1_L_1",
                "$cccoattail_1_L_2", "$cccoattail_2_L_0", "$cccoattail_2_L_1", "$cccoattail_2_L_2", "$cccoattail_end_L_0", "$cccoattail_end_L_1",
                "$cccoattail_end_L_2", "coattail_1_L", "coattail_2_L", "coattail_end_L"],
            StaticNodes = 4,
            InvMasses = [0f, 0f, 0f, 0f, 0.00207f, 0.002057f, 0.002057f, 0.002061f, 0.002061f, 0.002061f, 0.0037f, 0.0037f, 0.0037f, 1f, 1f, 1f],
            Poses =
            [
                Pose(-10.723646f, 4.561181f, 66.092773f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-7.534945f, 5.381207f, 65.015862f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-8.487852f, 2.057984f, 65.235313f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-8.915481f, 4.000124f, 65.447983f, 0.337553f, -0.646323f, -0.495776f, -0.471731f),
                Pose(-13.473376f, 4.824905f, 58.146507f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-10.330053f, 5.649839f, 56.946922f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-11.282963f, 2.326618f, 57.166382f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-16.587204f, 5.195801f, 50.242416f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-13.441967f, 6.02051f, 49.047699f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-14.394877f, 2.697287f, 49.267155f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-19.686529f, 5.562407f, 42.336063f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-16.541292f, 6.387115f, 41.141346f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-17.494204f, 3.063893f, 41.360802f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-11.695464f, 4.267121f, 57.419937f, -0.323373f, 0.653533f, 0.505948f, 0.460804f),
                Pose(-14.808016f, 4.637866f, 49.519089f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
                Pose(-17.907341f, 5.004471f, 41.612736f, -0.323943f, 0.653251f, 0.505547f, 0.461245f),
            ],
            Parents = [3, 3, 3, -1, 13, 13, 13, 14, 14, 14, 15, 15, 15, 3, 13, 14],
            SourceElems = [0, 0, 0, 9, 9, 7, 10, 12, 8, 9, 12, 11, 7, 8, 11, 10, 6, 4, 7, 9, 5, 6, 9, 8, 4, 5, 8, 7, 2, 0, 4, 6, 1, 2, 6, 5, 0, 1, 5, 4],
            Rods =
            [
                RigidRod(0, 4, 8.412711f, 1f, 0f),
                RigidRod(0, 5, 9.218822f, 1f, 0f),
                RigidRod(0, 6, 9.218816f, 1f, 0f),
                RigidRod(1, 4, 9.097388f, 1f, 0f),
                RigidRod(1, 5, 8.54357f, 1f, 0f),
                RigidRod(1, 6, 9.219137f, 1f, 0f),
                RigidRod(2, 4, 9.097388f, 1f, 0f),
                RigidRod(2, 5, 9.219141f, 1f, 0f),
                RigidRod(2, 6, 8.543563f, 1f, 0f),
                RigidRod(4, 5, 3.464102f),
                RigidRod(6, 4, 3.464101f),
                RigidRod(4, 7, 8.503419f),
                RigidRod(4, 8, 9.177078f),
                RigidRod(4, 9, 9.177081f),
                RigidRod(5, 6, 3.464102f),
                RigidRod(5, 7, 9.181965f),
                RigidRod(5, 8, 8.498184f),
                RigidRod(5, 9, 9.177101f),
                RigidRod(6, 7, 9.181965f),
                RigidRod(6, 8, 9.177099f),
                RigidRod(6, 9, 8.498188f),
                RigidRod(7, 8, 3.464103f),
                RigidRod(9, 7, 3.464102f),
                RigidRod(7, 10, 8.500037f),
                RigidRod(7, 11, 9.178824f),
                RigidRod(7, 12, 9.178822f),
                RigidRod(8, 9, 3.464103f),
                RigidRod(8, 10, 9.178805f),
                RigidRod(8, 11, 8.500037f),
                RigidRod(8, 12, 9.178812f),
                RigidRod(9, 10, 9.178808f),
                RigidRod(9, 11, 9.178818f),
                RigidRod(9, 12, 8.500038f),
                RigidRod(10, 11, 3.464103f),
                RigidRod(12, 10, 3.464102f),
                RigidRod(11, 12, 3.464102f),
            ],
        }.Reconstruct();

        /// <summary>
        /// An <c>explicit_masses</c> chain is read off its mass-proportional rod weights: the mass 0.5 joint reads 0.5
        /// and its neighbour 1, while flat 0.5 weights read as a geometric chain.
        /// </summary>
        [Test]
        public async Task AnExplicitMassChainIsReadOffItsMassProportionalRodWeights()
        {
            var explicitChain = ExplicitMassChain.Reconstruct();
            var flatChain = FlatWeighted(ExplicitMassChain).Reconstruct();

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
            var biased = ExplicitBiasedChain.Reconstruct();
            var flat = FlatWeighted(ExplicitBiasedChain).Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(biased.HasExplicitMasses).IsTrue();
                await Assert.That(biased.RecoverJointMassMultiplier(4)!.Value).IsEqualTo(2f).Within(1e-3f);
                await Assert.That(flat.HasExplicitMasses).IsFalse();
            }
        }

        /// <summary>
        /// <see cref="FeModelTestModels.Coattail"/> with explicit masses 1, 0.5 and 1 down the chain, the first span's rods
        /// flattened by a motion bias.
        /// </summary>
        private static FeModelBuilder ExplicitBiasedChain => Coattail with
        {
            InvMasses = [0f, 0f, 1f, 1f, 0.5f, 0.5f, 1f, 1f],
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
                RigidRod(4, 6, 8.500037f, 1f, 0.333333f),
                RigidRod(4, 7, 8.732154f, 1f, 0.333333f),
                RigidRod(5, 6, 8.732168f, 1f, 0.333333f),
                RigidRod(5, 7, 8.500037f, 1f, 0.333333f),
                RigidRod(4, 5, 2.000001f),
                RigidRod(6, 7, 2.000001f),
            ],
        };

        /// <summary><paramref name="chain"/> with every mass-proportional rod weight of 1/3 or 2/3 flattened to 0.5.</summary>
        private static FeModelBuilder FlatWeighted(FeModelBuilder chain) => chain with
        {
            Rods = [.. chain.Rods!.Select(static rod => rod.Weight0 is 0.333333f or 0.666667f ? rod with { Weight0 = 0.5f } : rod)],
        };

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

        private static ClothReconstruction DampedBiasedGoals(float bias, int undamped)
        {
            float[] strengths = [0.3f, 0.4f, 0.5f, 0.35f, 0.45f, 0.55f, 0.6f, 0.38f, 0.48f, 0.58f];
            float[] dampedGaps = [0.05f, 0.08f, 0.11f, 0.14f, 0.17f, 0.2f, 0.23f, 0.02f, 0.035f, 0.065f];
            return GoalNodes(128, [.. strengths.Select((g, i) =>
            {
                var vertexRoot = i < undamped ? g : g + bias - dampedGaps[i];
                return ((g + bias) * (g + bias) * (g + bias), vertexRoot * vertexRoot * vertexRoot);
            })]);
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
                await Assert.That(ClothReconstruction.UniformMassPaint(uniform)).IsEqualTo(2.0000017f);
                await Assert.That(ClothReconstruction.UniformMassPaint(gradient)).IsNull();
                await Assert.That(ClothReconstruction.UniformMassPaint([(2.0f, Step)])).IsNull();
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
            var refined = ClothReconstruction.RefineMassPaint(readings, [(0, 1e-5f), (1, 1e-5f), (2, 1e-5f)], rods);
            var kept = ClothReconstruction.RefineMassPaint(readings, [(0, 1e-9f), (1, 1e-9f), (2, 1e-9f)], rods);

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
        private static ClothReconstruction MassedSheet(bool leftDiagonals)
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

            var rods = new List<FeRodConstraint>();
            var geometric = new float[9];
            foreach (var (a, b, diagonal) in built)
            {
                var length = diagonal ? MathF.Sqrt(200f) : 10f;
                rods.Add(diagonal ? Rod(a, b, length * 0.75f, length, 0.5f, 0.125f) : RigidRod(a, b, length));
                geometric[a] += 8f * length;
                geometric[b] += 8f * length;
            }

            return (ThreeByThreeSheet with
            {
                InvMasses = [.. geometric.Select(static mass => 1f / (mass + MathF.E))],
                Rods = [.. rods],
            }).Reconstruct();
        }

        /// <summary>
        /// A chain's <c>mass</c> default is the multiplier most of its simulating joints carry, sheet or no sheet, and
        /// only the others state their own; an even split states 1 and every joint's own value.
        /// </summary>
        [Test]
        public async Task AChainStatesTheMassMostOfItsJointsCarry()
        {
            static ClothReconstruction Sheeted(params float[] multipliers) => Chain(true, multipliers);

            static ClothReconstruction Chain(bool sheet, params float[] multipliers) => new FeModelBuilder
            {
                Names = ["root", "j1", "j2", "j3", "j4", sheet ? "$cloth_m0p0" : "spare"],
                StaticNodes = 1,
                Parents = [-1, 0, 1, 2, 3, -1],
                InvMasses = [0f, .. multipliers.Select((m, i) => 1f / ((i == multipliers.Length - 1 ? 24f : 48f) * m * m)), 0.5f],
                Positions = [new(0f, 0f, 0f), new(3f, 0f, 0f), new(6f, 0f, 0f), new(9f, 0f, 0f), new(12f, 0f, 0f), new(0f, 20f, 0f)],
                Rods = [RigidRod(0, 1, 3f), RigidRod(1, 2, 3f), RigidRod(2, 3, 3f), RigidRod(3, 4, 3f)],
            }.Reconstruct();

            static float Default(ClothReconstruction cloth)
                => cloth.RecoverChainMassDefault(cloth.BuildBoneChains()[0]);

            static string Stated(ClothReconstruction cloth)
            {
                var chain = cloth.BuildBoneChains()[0];
                var chainDefault = cloth.RecoverChainMassDefault(chain);
                return string.Join(" ", chain.Joints
                    .Where(joint => cloth.RecoverJointMass(joint.Node, chainDefault) is not null)
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
            var explicitChain = ExplicitMassChain.Reconstruct();
            var geometric = FlatWeighted(ExplicitMassChain).Reconstruct();

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
        private static ClothReconstruction FoldedPairMassModel(bool folded) => new FeModelBuilder
        {
            Names = ["root", "a", "b", "c"],
            StaticNodes = 1,
            Parents = [-1, 0, 0, 2],
            InvMasses = folded ? [0f, 0.0559017f, 0.02421412f, 0.03952847f] : [0f, 0.02139819f, 0.02421412f, 0.01846971f],
            Positions = [new(0f, 0f, 0f), new(-1f, 0f, -2f), new(0f, 0f, -2f), new(1f, 0f, -5f)],
            SourceElems = [0, 0, 2, 0, 0, 1, 2, 0, 2, 3],
            Rods = [RigidRod(0, 1, 2.236068f), RigidRod(0, 2, 2f), RigidRod(2, 3, 3.1622777f), Rod(1, 3, 2f, 3.6055512f, folded ? 0.585786f : 0.5f)],
        }.Reconstruct();

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
        private static ClothReconstruction RingLinkQuads(bool compiledQuad, bool swapped = false) => new FeModelBuilder
        {
            Names = ["root", "$ccroot_0", "$ccroot_1", "end", "$ccend_0", "$ccend_1"],
            StaticNodes = 3,
            Parents = [-1, 0, 0, 0, 3, 3],
            InvMasses = compiledQuad ? [0f, 0f, 0f, 1f, 0.0023670762f, 0.0023670762f] : [0f, 0f, 0f, 1f, 0.003125f, 0.003125f],
            Positions = [new(0f, 0f, 0f), new(5f, 0f, 0f), new(-5f, 0f, 0f), new(0f, 0f, -20f), new(swapped ? 10f : -10f, 0f, -20f),
                new(swapped ? -10f : 10f, 0f, -20f)],
            Quads = compiledQuad ? [swapped ? Quad(2, 1, 4, 5) : Quad(2, 1, 5, 4)] : [],
            SourceElems = [0, 0, 0, 2, 2, 1, 4, 5, 1, 2, 5, 4],
            Rods = [Rod(5, 4, 20f, 44.72136f), RigidRod(4, 5, 20f)],
        }.Reconstruct();

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
        private static ClothReconstruction BoneCloud(bool afterMass)
        {
            float[] inv = afterMass
                ? [0f, 0.00446724811f, 0.00380154087f, 0.00380154087f, 0.00392823592f]
                : [0f, 0.00308050977f, 0.00269810419f, 0.00380154087f, 0.0026542513f];
            float[] folds = afterMass ? [0.540254216f, 0.491804741f, 0.532101317f] : [0.5f, 0.5f, 0.5f];
            return new FeModelBuilder
            {
                Names = ["head", "hair_01", "ear_L_01", "ear_R_01", "muzzle_01"],
                StaticNodes = 1,
                Parents = [-1, 0, 0, 0, 0],
                InvMasses = inv,
                Positions = [new(0f, 0f, 0f), new(0f, 0f, 10f), new(5f, 0f, 8f), new(-5f, 0f, 8f), new(0f, 6f, 6f)],
                SourceElems = [0, 0, 6, 0, 3, 2, 0, 3, 1, 0, 3, 4, 0, 2, 1, 0, 2, 4, 0, 1, 4, 0],
                Rods =
                [
                    RigidRod(0, 1, 10f, 1f, 0f),
                    RigidRod(0, 2, 9.43398113f, 1f, 0f),
                    RigidRod(0, 3, 9.43398113f, 1f, 0f),
                    RigidRod(0, 4, 8.48528137f, 1f, 0f),
                    RigidRod(1, 2, 5.38516481f),
                    RigidRod(1, 3, 5.38516481f),
                    RigidRod(1, 4, 7.21110255f),
                    RigidRod(2, 3, 10f),
                    RigidRod(2, 4, 8.06225775f),
                    RigidRod(3, 4, 8.06225775f),
                    Rod(1, 2, 2.6925824f, 5.38516481f, folds[0]),
                    Rod(2, 4, 4.03112887f, 8.06225775f, folds[1]),
                    Rod(1, 4, 3.60555128f, 7.21110255f, folds[2]),
                ],
            }.Reconstruct();
        }

        /// <summary>
        /// A sheet compiled without <c>m_SkelParents</c> whose masses lie below the corner-pair element term reads its
        /// <c>cloth_mass</c> over the mixed Voronoi term, with or without an authored rod face, and a vertex with no reading
        /// states a negligible bias; with <c>m_SkelParents</c> the same masses read against the corner-pair term leave no
        /// paint.
        /// </summary>
        [Test]
        public async Task AVoronoiMassSheetReadsItsPaintOverTheLumpedArea()
        {
            float[] authored = [2f, 1f, 0.5f, -30f, -30f, -30f, -30f];
            var modern = RhombusSheet(authored, parents: true, rodFace: true);

            using (Assert.Multiple())
            {
                await Assert.That(PaintError(RhombusSheet(authored, parents: false, rodFace: true))).IsLessThanOrEqualTo(1e-4f);
                await Assert.That(PaintError(RhombusSheet(authored, parents: false, rodFace: false))).IsLessThanOrEqualTo(1e-4f);
                await Assert.That(modern.RecoverMassPaint(modern.BuildProxyMeshes()[0])).IsNull();
            }

            float PaintError(ClothReconstruction sheet)
            {
                var proxy = sheet.BuildProxyMeshes()[0];
                var paint = sheet.RecoverMassPaint(proxy);
                return paint is null || paint.Length != proxy.NodeIndices.Length
                    ? float.PositiveInfinity
                    : proxy.NodeIndices.Select((node, vertex) => MathF.Abs(paint[vertex] - authored[node])).Max();
            }
        }

        /// <summary>
        /// One dynamic rhombus quad with diagonals 40 and 20, whose fan over the long diagonal lumps an area of 100 on
        /// every corner, and with <paramref name="rodFace"/> a 30-40-50 rod triangle weighing 8 per unit of its edges; each
        /// inverse mass is 1 / (geometric + e^paint).
        /// </summary>
        private static ClothReconstruction RhombusSheet(float[] paint, bool parents, bool rodFace)
        {
            var count = rodFace ? 7 : 4;
            float[] geometric = [100f, 100f, 100f, 100f, 560f, 640f, 720f];
            Vector3[] poses = [new(-20f, 0f, 0f), new(0f, -10f, 0f), new(20f, 0f, 0f), new(0f, 10f, 0f),
                new(0f, 0f, 50f), new(30f, 0f, 50f), new(0f, 40f, 50f)];
            var sheet = new FeModelBuilder
            {
                Names = [.. Enumerable.Range(0, count).Select(static node => $"$cloth_m0p{node}")],
                Parents = parents ? [.. Enumerable.Repeat(-1, count)] : null,
                InvMasses = [.. Enumerable.Range(0, count).Select(node => 1f / (geometric[node] + MathF.Exp(paint[node])))],
                Positions = poses[..count],
                Quads = [Quad(0, 1, 2, 3)],
                SourceElems = [0, 0, 0, 0],
            };

            return (rodFace
                ? sheet with { SourceElems = [0, 0, 1, 1, 4, 5, 6, 0, 1, 2, 3], Rods = [RigidRod(4, 5, 30f), RigidRod(4, 6, 40f), RigidRod(5, 6, 50f)] }
                : sheet).Reconstruct();
        }

        /// <summary>
        /// On a mixed Voronoi sheet the face rods' weights, fixed from the mass bias alone, carry an exact node's paint onto
        /// the nodes a static-cornered element leaves bracketed; a bracketed node no face rod reaches keeps its reading.
        /// </summary>
        [Test]
        public async Task AVoronoiSheetPinsBracketedPaintByItsFaceRods()
        {
            float[] authored = [2f, 1f, 0.5f, -30f, 1.2f, 0.7f, 0.3f, 0f];
            float[] rodMass = [0f, 0f, 8f * (60f + 80.62258f), 0f, 8f * (60f + 22.36068f), 8f * (22.36068f + 80.62258f), 0f, 0f];
            float FaceWeight(int a, int b) => MathF.Exp(authored[b]) / (MathF.Exp(authored[a]) + MathF.Exp(authored[b]));

            var sheet = new FeModelBuilder
            {
                Names = [.. Enumerable.Range(0, 8).Select(static node => $"$cloth_m0p{node}")],
                InvMasses = [.. authored.Select((value, node) => node == 7 ? 0f : 1f / (100f + rodMass[node] + MathF.Exp(value)))],
                Positions = [new(-20f, 0f, 0f), new(0f, -10f, 0f), new(20f, 0f, 0f), new(0f, 10f, 0f),
                    new(80f, 0f, 0f), new(100f, -10f, 0f), new(120f, 0f, 0f), new(100f, 10f, 0f)],
                Quads = [Quad(0, 1, 2, 3), Quad(4, 5, 6, 7)],
                SourceElems = [0, 0, 1, 0, 2, 4, 5],
                Rods =
                [
                    RigidRod(2, 4, 60f, 1f, FaceWeight(2, 4)),
                    RigidRod(4, 5, 22.36068f, 1f, FaceWeight(4, 5)),
                    RigidRod(2, 5, 80.62258f, 1f, FaceWeight(2, 5)),
                ],
            }.Reconstruct();
            var proxy = sheet.BuildProxyMeshes()[0];
            var paint = sheet.RecoverMassPaint(proxy);
            float PaintOf(int node)
            {
                var vertex = Array.IndexOf(proxy.NodeIndices, node);
                return paint is null || vertex < 0 ? float.NaN : paint[vertex];
            }

            using (Assert.Multiple())
            {
                await Assert.That(MathF.Abs(PaintOf(4) - 1.2f)).IsLessThanOrEqualTo(1e-3f);
                await Assert.That(MathF.Abs(PaintOf(5) - 0.7f)).IsLessThanOrEqualTo(1e-3f);
                await Assert.That(MathF.Abs(PaintOf(0) - 2f)).IsLessThanOrEqualTo(1e-4f);
                await Assert.That(MathF.Abs(PaintOf(1) - 1f)).IsLessThanOrEqualTo(1e-4f);
                await Assert.That(MathF.Abs(PaintOf(2) - 0.5f)).IsLessThanOrEqualTo(1e-3f);
                await Assert.That(MathF.Abs(PaintOf(6) - MathF.Log(18.75f + MathF.Exp(0.3f)))).IsLessThanOrEqualTo(1e-3f);
            }
        }

        /// <summary>
        /// A joint with a control name but no inverse mass reads no mass.
        /// </summary>
        [Test]
        public async Task AJointWithoutAnInverseMassReadsNoMass()
        {
            var cloth = new FeModelBuilder { Names = ["root", "joint"], StaticNodes = 1, InvMasses = [0f] }.Reconstruct();

            await Assert.That(cloth.RecoverJointMassMultiplier(1)).IsNull();
        }

        /// <summary>A face rod with a NaN weight states no difference, and the rods beside it still shape the mass paint.</summary>
        [Test]
        public async Task ANaNFaceRodWeightIsIgnoredByTheMassPaint()
        {
            float[] readings = [1.000003f, 1.399998f, 2.0f];
            (int, int, float)[] rods = [(0, 1, MathF.Exp(1.4f) / (MathF.Exp(1.0f) + MathF.Exp(1.4f))), (1, 2, float.NaN)];
            var refined = ClothReconstruction.RefineMassPaint(readings, [(0, 1e-5f), (1, 1e-5f), (2, 1e-5f)], rods);

            using (Assert.Multiple())
            {
                await Assert.That(Array.TrueForAll(refined, float.IsFinite)).IsTrue();
                await Assert.That(refined[1] - refined[0]).IsEqualTo(0.4f).Within(2e-6f);
                await Assert.That(refined[2]).IsEqualTo(readings[2]);
            }
        }
    }
}
