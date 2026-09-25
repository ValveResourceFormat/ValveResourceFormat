using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace Tests
{
    /// <summary>Reconstructing proxy sheets: faces, paints, selections and skin weights.</summary>
    public class ClothFeModelSheetTest : ClothTestFixtures
    {
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
    }
}
