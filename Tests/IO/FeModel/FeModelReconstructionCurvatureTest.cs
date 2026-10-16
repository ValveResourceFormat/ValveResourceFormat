using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Reading bends, stiff hinges and ring curvature off a compiled FeModel.</summary>
    public class FeModelReconstructionCurvatureTest
    {
        /// <summary>
        /// A ring bend rod carries <c>flMinDist = flMaxDist * sin(add_curvature * pi / 2)</c>: minimum 2.3344536 at
        /// maximum 10 reads 0.15.
        /// </summary>
        [Test]
        public async Task ChainRingCurvatureInvertsTheHalfSineLaw()
        {
            var cloth = RingCurvatureModel(Rod(1, 2, 2.3344536f, 10f));

            await Assert.That(cloth.ChainRingCurvature).IsEqualTo(0.15f).Within(1e-4f);
        }

        /// <summary>
        /// Two ring rods reading different curvatures (0.4 and 0.15) are refused.
        /// </summary>
        [Test]
        public async Task ChainRingCurvatureRefusesADisagreeingRing()
        {
            var cloth = RingCurvatureModel(Rod(1, 2, 2.3344536f, 10f), Rod(2, 3, 5.8778525f, 10f));

            await Assert.That(cloth.ChainRingCurvature).IsEqualTo(0f);
        }

        private static ClothReconstruction RingCurvatureModel(params FeRodConstraint[] rods) => new FeModelBuilder
        {
            Names = ["j", "$ccj_0", "$ccj_1", "$ccj_2"],
            Parents = [-1, 0, 0, 0],
            Positions = [new(0f, 0f, 0f), new(0f, 1f, 0f), new(0f, 0f, 1f), new(0f, -1f, 0f)],
            Rods = rods,
        }.Reconstruct();

        /// <summary>A mid node above the line between its two ends, all free, with no bend.</summary>
        private static FeModelBuilder ThreeNodeBend => new()
        {
            Names = ["mid", "end0", "end1"],
            Parents = [-1, 0, 0],
            Positions = [new(0f, 1f, 0f), new(-1f, 0f, 0f), new(1f, 0f, 0f)],
        };

        /// <summary>
        /// A stiff hinge's bend weights are <c>stiffness * [-1, 0.5, 0.5]</c> at equal inverse masses, and its height
        /// inverts to the angle: sqrt(4 - 4 cos 120) / 3 = 0.8164966.
        /// </summary>
        [Test]
        public async Task StiffHingeInvertsTheKelagerWeightSpread()
        {
            var hinge = KelagerModel([-1f, 0.5f, 0.5f], 0.8164966f).GetStiffHinge(1);

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
            var hinge = KelagerModel([0f, 1.5f, 0f], 0.8164966f).GetStiffHinge(1);

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
            var hinge = KelagerModel([-1f, 0.5f, 0.5f], 0.5f).GetStiffHinge(1);

            await Assert.That(hinge!.Value.Angle).IsEqualTo(0f);
        }

        private static ClothReconstruction KelagerModel(float[] weights, float height)
            => (ThreeNodeBend with { KelagerBends = [KelagerBend(0, 1, 2, height, weights)] }).Reconstruct();

        /// <summary>
        /// With <c>rigid_edge_hinges</c> the curvature is read off the ring bends' heights: none means zero, a folded
        /// hub inverts to its angle, and a saturated or disagreeing set reads 1.
        /// </summary>
        [Test]
        public async Task RigidHingeSheetReadsItsCurvatureFromTheRingBends()
        {
            using (Assert.Multiple())
            {
                await Assert.That(RigidSheet().RigidHingeCurvature).IsEqualTo(0f);
                await Assert.That(RigidSheet(4.714045f).RigidHingeCurvature)
                    .IsEqualTo(0.5f).Within(0.001f);
                await Assert.That(RigidSheet(6.666667f).RigidHingeCurvature)
                    .IsEqualTo(0f).Within(0.001f);
                await Assert.That(RigidSheet(0.1f).RigidHingeCurvature).IsEqualTo(1f);
                await Assert.That(RigidSheet(0f).RigidHingeCurvature).IsEqualTo(1f);
                await Assert.That(RigidSheet(4.714045f, 6.666667f).RigidHingeCurvature)
                    .IsEqualTo(1f);
            }
        }

        /// <summary>
        /// A sheet marked rigid-hinged by its axial edges, whose one hub (node 1) sits between two ring
        /// members (nodes 2 and 3) ten units away on opposite sides, so the hub's own rest distance from
        /// their centroid is zero and every fold it can record still tracks the angle. Each of <paramref name="heights"/>
        /// is one bend over the hub.
        /// </summary>
        private static ClothReconstruction RigidSheet(params float[] heights) => new FeModelBuilder
        {
            Names = ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"],
            StaticNodes = 1,
            Positions = [new(0f, 0f, 0f), new(0f, 0f, 0f), new(10f, 0f, 0f), new(-10f, 0f, 0f)],
            AxialEdges = [RigidHingeEdge],
            KelagerBends = [.. heights.Select(static height => KelagerBend(1, 2, 3, height, [-1f, 0.5f, 0.5f]))],
        }.Reconstruct();

        private static FeAxialEdgeBend RigidHingeEdge => new(0f, 0f, 0f, [], [1, 2, 3, 3, 2, 1]);

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
                await Assert.That(apart.RigidHingeCurvature).IsEqualTo(ClothReconstruction.SaturatedCurvature);
                await Assert.That(agreeing.RigidHingeBendPaint).IsNull();
                await Assert.That(agreeing.RigidHingeCurvature).IsEqualTo(0.5f).Within(0.001f);
            }
        }

        private static ClothReconstruction ThreeHubRigidSheet(float firstHeight, float secondHeight) => new FeModelBuilder
        {
            Names = ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5", "$cloth_m0p6", "$cloth_m0p7",
                "$cloth_m0p8"],
            StaticNodes = 1,
            Positions = [new(0f, 0f, 0f), new(0f, 0f, 0f), new(10f, 0f, 0f), new(-10f, 0f, 0f), new(0f, 0f, -20f), new(10f, 0f, -20f),
                new(-10f, 0f, -20f), new(0f, 0f, -40f), new(10f, 0f, -40f), new(-10f, 0f, -40f)],
            AxialEdges = [RigidHingeEdge],
            KelagerBends =
            [
                KelagerBend(1, 2, 3, firstHeight, [-1f, 0.5f, 0.5f]),
                KelagerBend(4, 5, 6, secondHeight, [-1f, 0.5f, 0.5f]),
                KelagerBend(7, 8, 9, 0f, [-1f, 0.5f, 0.5f]),
            ],
        }.Reconstruct();

        /// <summary>
        /// A Kelager bend whose hub lies between its ends is a chain ring bend, not a stiff hinge; one bending its
        /// first end's parent is a stiff hinge.
        /// </summary>
        [Test]
        public async Task ARingBendOverAJointIsNotAStiffHingeOnItsParent()
        {
            static ClothReconstruction Model(int[] bendNodes) => new FeModelBuilder
            {
                Names = ["root", "j1", "j2"],
                StaticNodes = 1,
                Parents = [-1, 0, 1],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -8.5f), new(0f, 0f, -17f)],
                KelagerBends = [new FeKelagerBend([-1f, 0f, 0.9f], 4f, bendNodes, 0)],
            }.Reconstruct();

            var ring = Model([1, 0, 2]);
            var hinge = Model([1, 2, 0]);

            using (Assert.Multiple())
            {
                await Assert.That(ring.IsChainRingBend(ring.Index.KelagerBends[0])).IsTrue();
                await Assert.That(ring.HasChainRingBends).IsTrue();
                await Assert.That(ring.GetStiffHinge(0)).IsNull();
                await Assert.That(hinge.IsChainRingBend(hinge.Index.KelagerBends[0])).IsFalse();
                await Assert.That(hinge.HasChainRingBends).IsFalse();
                await Assert.That(hinge.GetStiffHinge(2)).IsNotNull();
            }
        }

        /// <summary>
        /// A stiff hinge's stiffness is <c>(End0Weight + End1Weight - 2 * MidWeight) / 3</c>, biased or not, with the
        /// angle and motion bias unchanged.
        /// </summary>
        [Test]
        public async Task AStiffHingeStiffnessIsTheBendWeightsLinearCombination()
        {
            var biased = BiasedKelagerModel([-0.5f, 0.25f, 0.25f]).GetStiffHinge(1);
            var unbiased = BiasedKelagerModel([-0.4615385f, 0.1153846f, 0.4615385f]).GetStiffHinge(1);

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
        private static ClothReconstruction BiasedKelagerModel(float[] weights) => (ThreeNodeBend with
        {
            InvMasses = [2f, 1f, 4f],
            KelagerBends = [KelagerBend(0, 1, 2, 0.8164966f, weights)],
        }).Reconstruct();

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
        private static ClothReconstruction FlatKelagerModel(float height) => (ThreeNodeBend with
        {
            Positions = [new(0f, 0f, 0f), new(-1f, 0f, 0f), new(1f, 0f, 0f)],
            KelagerBends = [KelagerBend(0, 1, 2, height, [-1f, 0.5f, 0.5f])],
        }).Reconstruct();

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
        private static ClothReconstruction TwiceBentKelagerModel(float first, float? second) => (ThreeNodeBend with
        {
            Names = ["mid", "joint", "end1"],
            Positions = [new(0f, 0.2f, 0f), new(-1f, 0f, 0f), new(1f, 0f, 0f)],
            KelagerBends =
            [
                KelagerBend(0, 1, 2, first, [-1f, 0.5f, 0.5f]),
                .. second is { } height ? [KelagerBend(0, 1, 2, height, [-1f, 0.5f, 0.5f])] : Array.Empty<FeKelagerBend>(),
            ],
        }).Reconstruct();
    }
}
