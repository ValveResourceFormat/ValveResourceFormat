using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace Tests
{
    /// <summary>Declaring proxy sheet paints, flags and bend stiffness.</summary>
    public class ClothExtractSheetTest : ClothTestFixtures
    {
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

        /// <summary>A sheet with only a <c>cloth_enable</c> pattern and faces.</summary>
        private static FeModel.ProxyMesh CornerSheet(float[] enable, List<int[]> faces)
            => SyntheticCloth.Proxy([.. Enumerable.Range(0, enable.Length)], enable, faces);

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
        /// A face-kept sheet whose folds are regenerated states no bend paint; with declared pairs it states 0.2.
        /// </summary>
        [Test]
        public async Task AFaceKeptSheetWhoseFoldsAreRegeneratedStatesNoBendPaint()
        {
            static float? Read(FeModel feModel)
                => ClothExtract.ClothFaceKeptBendStiffness(feModel, ClothExtract.ClothRodsFromSurface(feModel,
                    feModel.BuildProxyMeshes().Select(static (proxy, i) => new ClothExtract.ClothProxyFile($"p{i}.dmx", $"p{i}", proxy)).ToList()));

            using (Assert.Multiple())
            {
                await Assert.That(Read(FoldedSheetModel(0.5f))).IsEqualTo(0.2f);

                await Assert.That(Read(FoldedSheetModel(0.666667f))).IsNull();
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
    }
}
