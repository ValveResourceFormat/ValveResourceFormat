using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Declaring proxy sheet paints, flags and bend stiffness.</summary>
    public class FeModelExtractSheetTest : FeModelTestModels
    {
        /// <summary>
        /// <c>flex_cloth_borders</c> holds only where the pins a face frees carry node bases; a face with one simulated
        /// corner frees no pin.
        /// </summary>
        [Test]
        public async Task FlexClothBordersNeedsItsFreedPinsToCarryNodeBases()
        {
            static ClothReconstruction Model(params FeNodeBase[] bases) => new FeModelBuilder
            {
                Names = SheetNodes(4),
                StaticNodes = 2,
                RotLockStaticNodes = 0,
                Parents = [-1, -1, 0, 1],
                Positions = [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f)],
                NodeBases = bases,
            }.Reconstruct();
            static FeNodeBase Base(int node) => new(node, [0, 0, 0], 2, 3, 0, 1, Quaternion.Identity);
            static ProxyMesh Sheet(List<int[]> faces) => Proxy([0, 1, 2, 3], [0f, 0f, 1f, 1f], faces,
                [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f)]);

            var quad = Sheet([[0, 1, 3, 2]]);
            var painted = Model();
            var flexed = Model(Base(0), Base(1), Base(2), Base(3));

            using (Assert.Multiple())
            {
                await Assert.That(ClothExtract.FlexedPinsCarryNodeBases(painted, quad)).IsFalse();
                await Assert.That(ClothExtract.FlexedPinsCarryNodeBases(flexed, quad)).IsTrue();
                await Assert.That(ClothExtract.FlexedPinsCarryNodeBases(painted, Sheet([[0, 1, 2]]))).IsTrue();
            }
        }

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

        private static ClothReconstruction FoldStrip(float firstHingeMinDist, float secondHingeMinDist) => new FeModelBuilder
        {
            Names = SheetNodes(8),
            Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, -10f),
                new(20f, 0f, -10f), new(30f, 0f, -10f)],
            Rods =
            [
                Rod(0, 2, firstHingeMinDist, 20f),
                Rod(4, 6, firstHingeMinDist, 20f),
                Rod(1, 3, secondHingeMinDist, 20f),
                Rod(5, 7, secondHingeMinDist, 20f),
            ],
        }.Reconstruct();

        /// <summary>The names of <paramref name="count"/> proxy sheet nodes, <c>$cloth_m0p0</c> onwards.</summary>
        private static string[] SheetNodes(int count) => [.. Enumerable.Range(0, count).Select(static node => $"$cloth_m0p{node}")];

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

        private static ClothReconstruction LeastFoldedHingeGrid => (HingeGrid with
        {
            Rods =
            [
                Rod(0, 2, 3.901806f, 20f),
                Rod(1, 3, 3.901806f, 20f),
                Rod(4, 6, 3.901806f, 20f),
                Rod(5, 7, 3.901806f, 20f),
                Rod(8, 10, 11.111405f, 20f),
                Rod(9, 11, 11.111405f, 20f),
                Rod(0, 8, 7.653669f, 20f),
                Rod(1, 9, 7.653669f, 20f),
                Rod(2, 10, 7.653669f, 20f),
                Rod(3, 11, 7.653669f, 20f),
            ],
        }).Reconstruct();

        /// <summary>A flat 4x3 grid of nodes 10 apart.</summary>
        private static FeModelBuilder HingeGrid => new()
        {
            Names = SheetNodes(12),
            Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f), new(0f, 0f, -10f), new(10f, 0f, -10f),
                new(20f, 0f, -10f), new(30f, 0f, -10f), new(0f, 0f, -20f), new(10f, 0f, -20f), new(20f, 0f, -20f),
                new(30f, 0f, -20f)],
        };

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
            var (paint, curvature) = ClothExtract.ClothBendStiffnessOverFold(BentPaintedSheet, faces, network, 0.25f,
                keepsCurvature: false);
            var (plain, plainCurvature) = ClothExtract.ClothBendStiffnessOverFold(BentPlainSheet, faces, network, 0.25f,
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

        private static ClothReconstruction BentPaintedSheet => BentSheet([15.707473f, 15.697754f, 15.774404f, 15.697739f, 15.707446f],
            [3.1739345f, 3.1739397f, 3.3121321f]).Reconstruct();

        private static ClothReconstruction BentPlainSheet => BentSheet([6.5320888f, 6.5086966f, 6.9404755f, 6.508693f, 6.532084f],
            [1.740971f, 1.7409755f, 1.7736652f]).Reconstruct();

        /// <summary>
        /// A bent sheet of five strips of four nodes whose rods (0, 2), (4, 6), (8, 10), (12, 14) and (16, 18) have the minimum
        /// lengths <paramref name="longMins"/> and whose rods (17, 9), (9, 1) and (13, 5) have <paramref name="crossMins"/>.
        /// </summary>
        private static FeModelBuilder BentSheet(float[] longMins, float[] crossMins) => new()
        {
            Names = SheetNodes(20),
            Positions =
            [
                new(-8.915558f, -3.9999907f, 65.448204f),
                new(-11.695556f, -4.2669907f, 57.420155f),
                new(-14.808141f, -4.6377454f, 49.519295f),
                new(-17.90746f, -5.004356f, 41.613026f),
                new(-8.91556f, -1.9999869f, 65.4482f),
                new(-11.695561f, -2.1334882f, 57.42015f),
                new(-14.808148f, -2.3188667f, 49.519295f),
                new(-17.907467f, -2.502173f, 41.61303f),
                new(-8.915562f, 1.680851E-05f, 65.44819f),
                new(-12.695568f, 1.4543533E-05f, 57.42015f),
                new(-14.808155f, 1.1920929E-05f, 49.5193f),
                new(-17.907475f, 9.775162E-06f, 41.613037f),
                new(-8.915564f, 2.0000205f, 65.44818f),
                new(-11.695574f, 2.1335173f, 57.420147f),
                new(-14.808163f, 2.3188906f, 49.519302f),
                new(-17.907482f, 2.5021925f, 41.61304f),
                new(-8.9155655f, 4.0000243f, 65.44817f),
                new(-11.69558f, 4.2670197f, 57.420143f),
                new(-14.80817f, 4.637769f, 49.519302f),
                new(-17.907492f, 5.0043755f, 41.61305f),
            ],
            Rods =
            [
                Rod(0, 2, longMins[0], 16.999594f),
                Rod(4, 6, longMins[1], 16.990614f),
                Rod(8, 10, longMins[2], 17.040838f),
                Rod(12, 14, longMins[3], 16.990597f),
                Rod(16, 18, longMins[4], 16.999565f),
                Rod(1, 3, 6.5411534f, 16.99991f),
                Rod(17, 19, 6.5411453f, 16.999882f),
                Rod(9, 11, 6.3860846f, 16.670456f),
                Rod(5, 7, 6.509907f, 16.987907f),
                Rod(13, 15, 6.5099025f, 16.987896f),
                Rod(17, 9, crossMins[0], 4.4786596f),
                Rod(9, 1, crossMins[1], 4.4786654f),
                Rod(13, 5, crossMins[2], 4.684062f),
                Rod(18, 10, 1.7772001f, 4.637758f),
                Rod(10, 2, 1.7772f, 4.637758f),
                Rod(14, 6, 1.7747929f, 4.6377573f),
                Rod(19, 11, 1.917685f, 5.004366f),
                Rod(11, 3, 1.9176855f, 5.004366f),
                Rod(15, 7, 1.9150877f, 5.0043654f),
            ],
        };

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

        private static ClothReconstruction CorrugatedSheet => new FeModelBuilder
        {
            Names = SheetNodes(20),
            Positions =
            [
                new(-8.915558f, 8.00001f, 65.448204f),
                new(-6.91556f, 10.000013f, 65.4482f),
                new(-8.915562f, 12.000017f, 65.44819f),
                new(-6.9155636f, 14.000021f, 65.44818f),
                new(-8.9155655f, 16.000025f, 65.44817f),
                new(-5.6955614f, 9.866512f, 57.42015f),
                new(-7.6955557f, 7.7330093f, 57.420155f),
                new(-7.695568f, 12.000014f, 57.42015f),
                new(-5.695574f, 14.133517f, 57.420147f),
                new(-7.6955795f, 16.26702f, 57.420143f),
                new(-12.808148f, 9.681133f, 49.519295f),
                new(-14.808141f, 7.3622546f, 49.519295f),
                new(-14.808155f, 12.000011f, 49.5193f),
                new(-12.808163f, 14.318891f, 49.519302f),
                new(-14.80817f, 16.63777f, 49.519302f),
                new(-11.907467f, 9.497828f, 41.61303f),
                new(-13.907459f, 6.995644f, 41.613026f),
                new(-13.9074745f, 12.00001f, 41.613037f),
                new(-11.907482f, 14.5021925f, 41.61304f),
                new(-13.907492f, 17.004375f, 41.61305f),
            ],
            Rods =
            [
                Rod(0, 11, 16.995865f, 17.985956f),
                Rod(1, 10, 16.986883f, 17.995607f),
                Rod(2, 12, 16.983881f, 17.987026f),
                Rod(3, 13, 16.986866f, 17.995596f),
                Rod(4, 14, 16.995836f, 17.985922f),
                Rod(5, 15, 16.987902f, 17.972176f),
                Rod(8, 18, 16.987888f, 17.972157f),
                Rod(6, 16, 16.999905f, 17.989737f),
                Rod(9, 19, 16.999876f, 17.989702f),
                Rod(7, 17, 16.983892f, 17.96188f),
                Rod(9, 7, 4.267005f, 5.8177505f),
                Rod(7, 6, 4.267005f, 5.8177505f),
                Rod(8, 5, 4.267005f, 5.8177466f),
                Rod(14, 12, 4.6377583f, 6.1076894f),
                Rod(12, 11, 4.637757f, 6.107687f),
                Rod(13, 10, 4.6377573f, 6.1076837f),
                Rod(19, 17, 5.004366f, 6.39052f),
                Rod(17, 16, 5.0043654f, 6.3905187f),
                Rod(18, 15, 5.004365f, 6.3905153f),
            ],
        }.Reconstruct();

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

        private static ClothReconstruction PairedHingeSheet(float minDist) => new FeModelBuilder
        {
            Names = SheetNodes(6),
            Positions = [new(-1.7257074f, 20.404041f, 46.822933f), new(-1.7570662f, 20.385893f, 46.739212f),
                new(-2.2579334f, 20.29848f, 47.05001f), new(-2.1855373f, 20.323782f, 47.10828f),
                new(-2.3192735f, 20.206625f, 47.622124f), new(-2.404188f, 20.17036f, 47.61194f)],
            Rods = [Rod(0, 5, minDist, 1.0688722f)],
        }.Reconstruct();

        /// <summary>
        /// A sheet paints <c>cloth_collision_layer</c> streams only for the layers some vertex's tree mask clears, 0 on
        /// that vertex; all-set masks paint none.
        /// </summary>
        [Test]
        public async Task ASheetStatesTheCollisionLayersItsCompiledMasksClear()
        {
            var cleared = ClothExtract.ClothCollisionLayerPaints(LayerMasked(65533), [1, 2, 3], 3).ToList();
            var whole = ClothExtract.ClothCollisionLayerPaints(LayerMasked(65535), [1, 2, 3], 3).ToList();
            var lowFour = ClothExtract.ClothCollisionLayerPaints(LayerMasked(65520), [1, 2, 3], 3).ToList();

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

        /// <summary>A static root and a three-node sheet whose first tree collision mask is <paramref name="firstMask"/>.</summary>
        private static ClothReconstruction LayerMasked(int firstMask) => new FeModelBuilder
        {
            Names = ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"],
            StaticNodes = 1,
            Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f), new(30f, 0f, 0f)],
            TreeCollisionMasks = [firstMask, 65535, 65535, 65535, 65535],
        }.Reconstruct();

        /// <summary>
        /// A sheet simulating a rotation-locked static node paints <c>cloth_anchor_free_rotate</c> with or without
        /// <c>flex_cloth_borders</c>; a sheet pinning it or holding no static node paints nothing.
        /// </summary>
        [Test]
        public async Task ASheetPaintsTheRotationLockNoFlagCanState()
        {
            var cloth = SheetFitOverTipBone(8).Reconstruct();
            var sheet = cloth.BuildProxyMeshes().First(proxy => Array.Exists(proxy.NodeIndices,
                node => cloth.Fe.CtrlName[node].StartsWith("$cloth_m1p", StringComparison.Ordinal)));

            var lockedSimulated = RotationSheet(sheet, [0, 4, 5, 6], [1f, 1f, 1f, 1f]);
            var pinnedInstead = RotationSheet(sheet, [0, 4, 5, 6], [0f, 1f, 1f, 1f]);
            var allSimulated = RotationSheet(sheet, [4, 5, 6, 4], [1f, 1f, 1f, 1f]);

            using (Assert.Multiple())
            {
                await Assert.That(cloth.Index.AllowsRotation(0)).IsFalse();
                await Assert.That(cloth.Fe.StaticNodes).IsEqualTo(1);

                var flexed = ClothExtract.ClothAnchorFreeRotatePaint(cloth, lockedSimulated, sheetFlexes: true);
                var unflexed = ClothExtract.ClothAnchorFreeRotatePaint(cloth, lockedSimulated, sheetFlexes: false);
                await Assert.That(string.Join(",", flexed ?? [])).IsEqualTo("0,1,1,1");
                await Assert.That(string.Join(",", unflexed ?? [])).IsEqualTo("0,1,1,1");

                await Assert.That(ClothExtract.ClothAnchorFreeRotatePaint(cloth, pinnedInstead, sheetFlexes: false)).IsNull();
                await Assert.That(ClothExtract.ClothAnchorFreeRotatePaint(cloth, pinnedInstead, sheetFlexes: true)).IsNull();

                await Assert.That(ClothExtract.ClothAnchorFreeRotatePaint(cloth, allSimulated, sheetFlexes: true)).IsNull();
                await Assert.That(ClothExtract.ClothAnchorFreeRotatePaint(cloth, allSimulated, sheetFlexes: false)).IsNull();
            }
        }

        /// <summary><paramref name="sheet"/>'s faces over the given nodes and <c>cloth_enable</c> pattern.</summary>
        private static ProxyMesh RotationSheet(ProxyMesh sheet, int[] nodes, float[] enable)
            => Proxy(nodes, enable, sheet.Faces);

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

            static List<int[]> Padded(ProxyMesh sheet)
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

        /// <summary>A sheet with only a <c>cloth_enable</c> pattern and faces.</summary>
        private static ProxyMesh CornerSheet(float[] enable, List<int[]> faces)
            => Proxy([.. Enumerable.Range(0, enable.Length)], enable, faces);

        private static readonly string[] SecondDeclarationRunMembers =
            ["coattail_1_L", "coattail_2_L", "coattail_end_L"];

        private static readonly string[] SecondDeclarationRunSimulating = ["true", "true", "true"];

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

            var redeclared = Extract("FeModel/sheet_redeclared_run.vmdl_c");
            var single = Extract("FeModel/sheet_single_run.vmdl_c");
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
        /// A back-solving sheet whose freed pins carry node bases states <c>flex_cloth_borders</c>; pins without bases,
        /// or a face reaching no pin, do not.
        /// </summary>
        [Test]
        public async Task ABackSolvedSheetTakesFlexClothBordersFromItsPinsOwnNodeBases()
        {
            static ClothReconstruction Model(params FeNodeBase[] bases)
            {
                var model = new FeModelBuilder
                {
                    Names = ["anchor", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"],
                    StaticNodes = 3,
                    RotLockStaticNodes = 1,
                    Parents = [-1, 0, 0, 0, 0],
                    Positions = [new(0f, 0f, 0f), new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f)],
                    NodeBases = bases,
                }.Reconstruct();
                return WithSkeleton(model, new Dictionary<string, string?> { ["anchor"] = "spine" });
            }
            static FeNodeBase Base(int node) => new(node, [0, 0, 0], 3, 4, 1, 2, Quaternion.Identity);
            static ProxyMesh Sheet(List<int[]> faces) => Proxy([1, 2, 3, 4], [0f, 0f, 1f, 1f], faces,
                [new(0f, 0f, 0f), new(0f, 2f, 0f), new(0f, 0f, -8f), new(0f, 2f, -8f)]);

            var quad = Sheet([[0, 1, 3, 2]]);
            var flexed = Model(Base(1), Base(2));
            var painted = Model();
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
        private static ClothReconstruction CappedAgainstFlatHingeGrid(float lowerHingeMinDist) => (HingeGrid with
        {
            Rods =
            [
                Rod(0, 2, 0f, 20f),
                Rod(1, 3, 0f, 20f),
                Rod(4, 6, 0f, 20f),
                Rod(5, 7, 0f, 20f),
                Rod(8, 10, lowerHingeMinDist, 20f),
                Rod(9, 11, 0f, 20f),
                Rod(0, 8, 0f, 20f),
                Rod(1, 9, 0f, 20f),
                Rod(2, 10, 0f, 20f),
                Rod(3, 11, 0f, 20f),
            ],
        }).Reconstruct();

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
        private static List<(int, int)> FoldedRodMisses(ClothReconstruction sheet, List<int[]> faces, HashSet<(int, int)> network,
            Dictionary<int, float>? paint, float curvature, bool tight = false)
        {
            var positions = sheet.Index.InitPosePositions;
            var generators = new Dictionary<(int, int), List<(int, int)>>();
            foreach (var (hinge, a, b) in ClothReconstruction.BendRodGenerators(faces))
            {
                var pair = a < b ? (a, b) : (b, a);
                (generators.TryGetValue(pair, out var known) ? known : generators[pair] = []).Add(hinge);
            }

            var misses = new List<(int, int)>();
            foreach (var rod in sheet.Index.Rods)
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

        private static ClothReconstruction PaintSolveSheet => new FeModelBuilder
        {
            Names = SheetNodes(29),
            Positions =
            [
                new(96.23977f, -21.352524f, 193.65706f),
                new(101.10704f, -12.243159f, 155.27309f),
                new(96.23977f, 21.352524f, 193.65706f),
                new(101.10704f, 12.243159f, 155.27309f),
                new(-86.62791f, 7.855209f, 90.85877f),
                new(-86.62791f, -7.855209f, 90.85877f),
                new(101.527275f, -8.785593f, 187.25053f),
                new(98.7905f, -17.215473f, 177.5718f),
                new(104.68011f, -4.0255046f, 154.1219f),
                new(101.48694f, -12.619385f, 162.70032f),
                new(101.527275f, 8.785593f, 187.25053f),
                new(98.7905f, 17.215473f, 177.5718f),
                new(104.68011f, 4.0255046f, 154.1219f),
                new(101.48694f, 12.619385f, 162.70032f),
                new(-98.67452f, 10.1297655f, 73.70374f),
                new(-98.67452f, -10.1297655f, 73.70374f),
                new(102.20527f, 8.774978E-30f, 177.97769f),
                new(102.544426f, -10.05127f, 165.99724f),
                new(104.51593f, 7.777337E-30f, 157.74313f),
                new(102.544426f, 10.05127f, 165.99724f),
                new(-113.71193f, 13.470117f, 59.25505f),
                new(-113.71193f, -13.470117f, 59.25505f),
                new(-132.6082f, 15.324176f, 50.38886f),
                new(-132.6082f, -15.324176f, 50.38886f),
                new(-153.33545f, 11.337616f, 49.07927f),
                new(-153.33545f, -11.337616f, 49.07927f),
                new(-173.14297f, 5.5178976f, 55.008205f),
                new(-173.14297f, -5.5178976f, 55.008205f),
                new(-190.56068f, -2.4031326E-15f, 66.387886f),
            ],
            Rods =
            [
                Rod(0, 16, 27.154373f, 27.351889f),
                Rod(0, 17, 30.5374f, 30.538563f),
                Rod(1, 17, 11.039832f, 11.044836f),
                Rod(1, 18, 12.946683f, 13.106691f),
                Rod(2, 16, 27.154373f, 27.351889f),
                Rod(2, 19, 30.5374f, 30.538563f),
                Rod(3, 18, 12.946683f, 13.106691f),
                Rod(3, 19, 11.039832f, 11.044836f),
                Rod(4, 20, 41.998413f, 42.191517f),
                Rod(5, 21, 41.998413f, 42.191517f),
                Rod(9, 16, 19.828339f, 19.952848f),
                Rod(13, 16, 19.828339f, 19.95285f),
                Rod(8, 16, 24.319298f, 24.320848f),
                Rod(12, 16, 24.319298f, 24.320848f),
                Rod(7, 18, 26.876177f, 26.927683f),
                Rod(11, 18, 26.876177f, 26.927685f),
                Rod(6, 18, 30.932272f, 30.959124f),
                Rod(10, 18, 30.932272f, 30.959124f),
                Rod(14, 22, 41.497715f, 42.048958f),
                Rod(15, 23, 41.497715f, 42.048958f),
                Rod(20, 24, 40.964832f, 41.69606f),
                Rod(21, 25, 40.964832f, 41.69606f),
                Rod(19, 17, 20.10254f, 20.206247f),
                Rod(22, 26, 41.95914f, 42.588764f),
                Rod(23, 27, 41.95914f, 42.588764f),
                Rod(24, 28, 35.4227f, 43.00294f),
                Rod(25, 28, 35.422703f, 43.002945f),
            ],
        }.Reconstruct();

        /// <summary>
        /// A face-kept sheet whose folds are regenerated states no bend paint; with declared pairs it states 0.2.
        /// </summary>
        [Test]
        public async Task AFaceKeptSheetWhoseFoldsAreRegeneratedStatesNoBendPaint()
        {
            static float? Read(ClothReconstruction cloth)
                => ClothExtract.ClothFaceKeptBendStiffness(cloth, ClothExtract.ClothRodsFromSurface(cloth,
                    [.. cloth.BuildProxyMeshes().Select(static (proxy, i) => new ClothExtract.ClothProxyFile($"p{i}.dmx", $"p{i}", proxy))]));

            using (Assert.Multiple())
            {
                await Assert.That(Read(FoldedSheetModel(0.5f).Reconstruct())).IsEqualTo(0.2f);

                await Assert.That(Read(FoldedSheetModel(0.666667f).Reconstruct())).IsNull();
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

        private static ClothReconstruction CoveringPaintSheet => new FeModelBuilder
        {
            Names = SheetNodes(16),
            Positions =
            [
                new(-10.815558f, 5.645328f, 132.523f),
                new(-11.083033f, 2.5336894E-06f, 132.60832f),
                new(-10.815558f, -5.645328f, 132.523f),
                new(-16.881643f, -7.930124f, 122.44642f),
                new(-20.313026f, 5.300744E-07f, 123.969406f),
                new(-16.881643f, 7.930124f, 122.44642f),
                new(-27.958843f, 4.2594985E-07f, 117.86752f),
                new(-23.512804f, -12.685439f, 113.22655f),
                new(-23.512804f, 12.685439f, 113.22655f),
                new(-36.28462f, -3.9803567E-07f, 112.21493f),
                new(-32.75763f, -13.32593f, 106.65457f),
                new(-32.75763f, 13.32593f, 106.65457f),
                new(-42.748714f, -7.3693547f, 103.30281f),
                new(-44.183052f, -1.4517694E-06f, 106.34426f),
                new(-42.748714f, 7.3693547f, 103.30281f),
                new(-51.528694f, 3.4769377E-07f, 99.68677f),
            ],
            Rods =
            [
                Rod(0, 8, 23.581099f, 24.224367f),
                Rod(1, 6, 21.832897f, 22.42169f),
                Rod(2, 7, 23.581099f, 24.224367f),
                Rod(3, 10, 20.917269f, 23.07899f),
                Rod(5, 11, 20.91727f, 23.07899f),
                Rod(4, 9, 17.894537f, 19.84255f),
                Rod(5, 3, 15.860248f, 17.301552f),
                Rod(7, 12, 13.51877f, 22.288712f),
                Rod(8, 14, 13.51877f, 22.288712f),
                Rod(6, 13, 11.3243475f, 19.903667f),
                Rod(8, 7, 21.56542f, 28.417582f),
                Rod(9, 15, 7.7820816f, 19.751091f),
                Rod(10, 15, 15.032627f, 24.094904f),
                Rod(11, 15, 15.032627f, 24.094902f),
                Rod(11, 10, 13.903145f, 29.725107f),
                Rod(14, 12, 6.154204f, 16.146252f),
            ],
        }.Reconstruct();

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

        private static ClothReconstruction SettledPaintSheet => new FeModelBuilder
        {
            Names = SheetNodes(86),
            Positions =
            [
                new(-15.095473f, -19.140547f, 130.98946f),
                new(-17.029062f, -12.326554f, 131.71419f),
                new(-17.029062f, 12.326554f, 131.71419f),
                new(-15.095473f, 19.140547f, 130.98946f),
                new(-16.927298f, -5f, 134.21313f),
                new(-16.927298f, 5f, 134.21313f),
                new(-15.453802f, -18.726608f, 126.14155f),
                new(-17.38739f, -11.912616f, 126.86628f),
                new(-15.453802f, 18.726608f, 126.14155f),
                new(-17.38739f, 11.912616f, 126.86628f),
                new(-17.417427f, 4.288621f, 127.515175f),
                new(-17.4238f, -4.352967f, 127.44563f),
                new(-15.442624f, -18.550873f, 117.500336f),
                new(-17.620644f, -11.585511f, 118.80725f),
                new(-15.442624f, 18.550873f, 117.500336f),
                new(-17.620644f, 11.585511f, 118.80725f),
                new(-18.058758f, 4.3145194f, 120.048f),
                new(-18.06181f, -4.330307f, 120.03876f),
                new(-16.281216f, -18.380278f, 108.942444f),
                new(-19.581396f, -11.001704f, 110.337555f),
                new(-16.281216f, 18.380278f, 108.942444f),
                new(-19.581396f, 11.001704f, 110.337555f),
                new(-19.63091f, 4.0465436f, 111.76572f),
                new(-19.641083f, -4.1034117f, 111.743225f),
                new(-21.097715f, -11.253855f, 102.12577f),
                new(-17.58324f, -18.773373f, 100.43261f),
                new(-17.58324f, 18.773373f, 100.43261f),
                new(-21.097715f, 11.253855f, 102.12577f),
                new(-21.508125f, 4.0544286f, 103.74217f),
                new(-21.514303f, -4.125722f, 103.70929f),
                new(-18.874908f, -19.943903f, 91.99936f),
                new(-22.518084f, -12.08624f, 93.964714f),
                new(-18.874908f, 19.943903f, 91.99936f),
                new(-22.518509f, 12.07997f, 93.96892f),
                new(-23.65737f, 4.197114f, 95.702576f),
                new(-23.654007f, -4.2468295f, 95.6693f),
                new(-24.871672f, -13.537416f, 86.23957f),
                new(-20.354761f, -21.892418f, 83.729774f),
                new(-20.354761f, 21.892418f, 83.729774f),
                new(-24.875015f, 13.527921f, 86.250404f),
                new(-26.215334f, 4.523812f, 88.192955f),
                new(-26.202063f, -4.5583467f, 88.1507f),
                new(-27.697893f, -14.868223f, 78.120155f),
                new(-21.908436f, -24.482763f, 75.63206f),
                new(-21.908436f, 24.482763f, 75.63206f),
                new(-27.698162f, 14.867822f, 78.12089f),
                new(-28.945234f, 4.8091106f, 80.239655f),
                new(-28.93305f, -4.827322f, 80.20618f),
                new(-23.630163f, -27.454828f, 67.684265f),
                new(-30.370392f, -16.224058f, 70.444695f),
                new(-23.630163f, 27.454828f, 67.684265f),
                new(-30.371344f, 16.222658f, 70.44734f),
                new(-32.783222f, 5.254915f, 72.88018f),
                new(-32.779633f, -5.2601976f, 72.870125f),
                new(-33.26719f, -18.109873f, 63.249275f),
                new(-25.921503f, -30.499273f, 59.90693f),
                new(-25.921503f, 30.499273f, 59.90693f),
                new(-33.270027f, 18.116179f, 63.259033f),
                new(-36.39569f, 5.5990515f, 65.964874f),
                new(-36.38238f, -5.5659337f, 65.91849f),
                new(-36.907703f, -19.78023f, 56.44651f),
                new(-29.05777f, -33.431023f, 52.421375f),
                new(-29.05777f, 33.431023f, 52.421375f),
                new(-36.908386f, 19.781742f, 56.44885f),
                new(-40.283592f, 6.2215867f, 59.182056f),
                new(-39.54406f, -5.9728603f, 58.647816f),
                new(-33.607037f, -36.344795f, 45.724716f),
                new(-44.630447f, -22.514893f, 50.455086f),
                new(-33.607037f, 36.344795f, 45.724716f),
                new(-44.630447f, 22.514893f, 50.455086f),
                new(-44.325043f, 6.7631593f, 51.686f),
                new(-44.29262f, -6.6636477f, 51.62983f),
                new(-50.80507f, -24.826012f, 45.327374f),
                new(-39.55794f, -39.334583f, 40.263012f),
                new(-39.55794f, 39.334583f, 40.263012f),
                new(-50.80507f, 24.826012f, 45.327374f),
                new(-48.623943f, 4.972157f, 45.63817f),
                new(-48.623943f, -4.9717803f, 45.638012f),
                new(-50.9665f, 11.740841f, 44.881863f),
                new(-53.694984f, 13.968832f, 41.172085f),
                new(-50.9665f, -11.740841f, 44.881863f),
                new(-53.694984f, -13.968832f, 41.172085f),
                new(-51.661655f, 4.015462f, 39.88339f),
                new(-54.638794f, 2.6906853f, 32.051174f),
                new(-54.638794f, -2.6906853f, 32.051174f),
                new(-51.632256f, -4.109575f, 39.997013f),
            ],
            Rods =
            [
                Rod(0, 12, 13.506465f, 13.511056f),
                Rod(1, 13, 12.941721f, 12.943155f),
                Rod(2, 15, 12.941721f, 12.943155f),
                Rod(3, 14, 13.506465f, 13.511056f),
                Rod(4, 17, 14.23547f, 14.235754f),
                Rod(5, 16, 14.226778f, 14.22706f),
                Rod(10, 8, 14.6355095f, 14.771625f),
                Rod(11, 6, 14.566506f, 14.701759f),
                Rod(6, 18, 17.222477f, 17.241678f),
                Rod(8, 20, 17.222477f, 17.241678f),
                Rod(7, 19, 16.698568f, 16.766262f),
                Rod(9, 21, 16.698568f, 16.766262f),
                Rod(7, 10, 16.214254f, 16.214476f),
                Rod(9, 11, 16.275938f, 16.27614f),
                Rod(10, 22, 15.906085f, 15.92668f),
                Rod(11, 23, 15.8601465f, 15.880789f),
                Rod(16, 14, 14.6972275f, 14.799914f),
                Rod(17, 12, 14.68088f, 14.783166f),
                Rod(13, 16, 15.954383f, 15.970775f),
                Rod(15, 17, 15.969486f, 15.985617f),
                Rod(15, 27, 17.043186f, 17.043236f),
                Rod(13, 24, 17.043186f, 17.043236f),
                Rod(14, 26, 17.202879f, 17.215567f),
                Rod(12, 25, 17.202879f, 17.215565f),
                Rod(17, 29, 16.691708f, 16.695202f),
                Rod(16, 28, 16.668705f, 16.672426f),
                Rod(22, 20, 14.988238f, 15.2781515f),
                Rod(23, 18, 14.931911f, 15.218898f),
                Rod(21, 33, 16.664474f, 16.66601f),
                Rod(19, 31, 16.668955f, 16.670488f),
                Rod(21, 23, 15.170497f, 15.174184f),
                Rod(20, 32, 17.211632f, 17.215261f),
                Rod(18, 30, 17.211632f, 17.215261f),
                Rod(19, 22, 15.115948f, 15.120086f),
                Rod(22, 34, 16.560783f, 16.562822f),
                Rod(23, 35, 16.5679f, 16.569895f),
                Rod(28, 26, 15.588626f, 15.844732f),
                Rod(29, 25, 15.515913f, 15.769419f),
                Rod(24, 36, 16.48723f, 16.530216f),
                Rod(27, 39, 16.476244f, 16.519558f),
                Rod(26, 38, 17.206646f, 17.225016f),
                Rod(25, 37, 17.206646f, 17.22502f),
                Rod(29, 41, 16.247787f, 16.26424f),
                Rod(28, 40, 16.245552f, 16.262007f),
                Rod(24, 28, 15.398854f, 15.423134f),
                Rod(27, 29, 15.466495f, 15.490952f),
                Rod(34, 32, 16.868525f, 17.034456f),
                Rod(35, 30, 16.813873f, 16.979628f),
                Rod(31, 42, 16.795053f, 16.901127f),
                Rod(33, 45, 16.799227f, 16.90526f),
                Rod(32, 44, 17.161642f, 17.259474f),
                Rod(30, 43, 17.161573f, 17.259474f),
                Rod(35, 47, 16.244411f, 16.349758f),
                Rod(34, 46, 16.248133f, 16.353544f),
                Rod(33, 35, 16.454332f, 16.532751f),
                Rod(31, 34, 16.415413f, 16.491009f),
                Rod(40, 38, 18.866234f, 19.12972f),
                Rod(41, 37, 18.82035f, 19.085712f),
                Rod(36, 49, 16.616133f, 16.939163f),
                Rod(37, 48, 17.00754f, 17.299665f),
                Rod(38, 50, 17.007551f, 17.299664f),
                Rod(39, 51, 16.624199f, 16.947311f),
                Rod(41, 53, 16.365307f, 16.698957f),
                Rod(40, 52, 16.390566f, 16.724844f),
                Rod(36, 40, 18.087948f, 18.31071f),
                Rod(39, 41, 18.11267f, 18.335766f),
                Rod(46, 44, 21.39624f, 21.79924f),
                Rod(47, 43, 21.368298f, 21.774797f),
                Rod(43, 55, 16.790268f, 17.316538f),
                Rod(44, 56, 16.79033f, 17.316536f),
                Rod(42, 54, 15.670904f, 16.219826f),
                Rod(45, 57, 15.665446f, 16.214092f),
                Rod(45, 47, 19.68747f, 19.92954f),
                Rod(42, 46, 19.668507f, 19.910376f),
                Rod(47, 59, 15.551066f, 16.130024f),
                Rod(46, 58, 15.543045f, 16.121496f),
                Rod(53, 48, 23.889105f, 24.80634f),
                Rod(52, 50, 23.896862f, 24.813835f),
                Rod(49, 60, 15.178283f, 15.864326f),
                Rod(51, 63, 15.178635f, 15.864528f),
                Rod(50, 62, 16.593784f, 17.279312f),
                Rod(48, 61, 16.593584f, 17.27933f),
                Rod(52, 64, 14.940874f, 15.6499605f),
                Rod(53, 65, 15.058713f, 15.774211f),
                Rod(49, 52, 21.093855f, 21.977966f),
                Rod(51, 53, 21.099783f, 21.983469f),
                Rod(54, 67, 17.137953f, 17.984236f),
                Rod(57, 69, 17.14258f, 17.989162f),
                Rod(59, 55, 26.91018f, 27.94628f),
                Rod(58, 56, 26.893541f, 27.927618f),
                Rod(54, 58, 23.373707f, 24.349617f),
                Rod(57, 59, 23.356167f, 24.331064f),
                Rod(55, 66, 16.443f, 17.2088f),
                Rod(56, 68, 16.443022f, 17.208788f),
                Rod(58, 70, 15.586753f, 16.375494f),
                Rod(59, 71, 15.648998f, 16.439903f),
                Rod(65, 77, 15.127306f, 15.898323f),
                Rod(64, 76, 15.212775f, 15.985682f),
                Rod(62, 74, 16.346193f, 17.181273f),
                Rod(61, 73, 16.346193f, 17.181273f),
                Rod(60, 72, 17.612371f, 18.500582f),
                Rod(63, 75, 17.61281f, 18.501034f),
                Rod(64, 62, 28.905521f, 30.360897f),
                Rod(65, 61, 28.867134f, 30.316704f),
                Rod(60, 64, 25.186462f, 26.455357f),
                Rod(63, 65, 25.183352f, 26.453959f),
                Rod(67, 85, 21.672626f, 22.71888f),
                Rod(69, 82, 21.809128f, 22.864706f),
                Rod(67, 70, 27.91113f, 29.306902f),
                Rod(69, 71, 27.808922f, 29.20625f),
                Rod(71, 85, 13.36168f, 14.021007f),
                Rod(70, 82, 13.530628f, 14.195804f),
                Rod(66, 80, 30.123344f, 31.626125f),
                Rod(68, 78, 30.123344f, 31.626125f),
                Rod(71, 66, 31.804646f, 33.396805f),
                Rod(70, 68, 31.714428f, 33.30114f),
                Rod(72, 84, 25.148506f, 26.42306f),
                Rod(75, 83, 25.148504f, 26.42306f),
                Rod(79, 74, 29.053463f, 30.99962f),
                Rod(81, 73, 29.053463f, 30.99962f),
                Rod(77, 84, 14.360423f, 15.0623455f),
                Rod(76, 83, 14.349007f, 15.060103f),
                Rod(80, 82, 15.946761f, 16.587448f),
                Rod(78, 85, 15.9900255f, 16.644718f),
                Rod(79, 84, 18.553465f, 19.094738f),
                Rod(81, 83, 18.559027f, 19.094467f),
            ],
        }.Reconstruct();
    }
}
