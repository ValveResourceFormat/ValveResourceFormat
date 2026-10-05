using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Reconstructing proxy sheets: faces, paints, selections and skin weights.</summary>
    public class FeModelReconstructionSheetTest : FeModelTestModels
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

            var rods = ClothReconstruction.BentQuadRodsFromFaces([[0, 1, 2, 3]], corners, static _ => false, 0.05f);

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
                await Assert.That(ClothReconstruction.BentQuadRodsFromFaces([[0, 1, 2, 3]], flat, static _ => false, 0.05f))
                    .IsEmpty();
                await Assert.That(ClothReconstruction.BentQuadRodsFromFaces([[0, 1, 2, 3]], bent, static node => node == 3, 0.05f))
                    .IsEmpty();
            }
        }

        /// <summary>
        /// A vertex lists every selection covering it, with the weight where membership is below 1.
        /// </summary>
        [Test]
        public async Task VertexMapNamesCarryAPartialMembershipWeight()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["a", "b"],
                VertexMapValues = [255, 128],
                VertexMaps = [VertexMap("skirt", 1, 0, 0, 2)],
            }.Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(cloth.GetVertexMapNames(0)).IsEqualTo("skirt");
                await Assert.That(cloth.GetVertexMapNames(1)).Contains("skirt=");
                await Assert.That(cloth.VertexMapWeight("skirt", 1)).IsEqualTo(128f / 255f).Within(1e-6f);
                await Assert.That(ClothReconstruction.VertexMapName("skirt=0.5")).IsEqualTo("skirt");
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
            var cloth = DeferredOffsetSheet(skelParents: null);
            var proxies = cloth.BuildProxyMeshes();

            await Assert.That(proxies.Count).IsEqualTo(1);
            var influences = proxies[0].SkinInfluences[3];

            using (Assert.Multiple())
            {
                await Assert.That(cloth.DeferredOffsetSkinWeights.ContainsKey(7)).IsTrue();
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
            var cloth = DeferredOffsetSheet(skelParents: [-1, 0, 0, 0, 1, 1, 1, 1]);
            var influences = cloth.BuildProxyMeshes()[0].SkinInfluences[3];

            using (Assert.Multiple())
            {
                await Assert.That(cloth.DeferredOffsetSkinWeights.ContainsKey(7)).IsTrue();
                await Assert.That(influences.Length).IsNotEqualTo(0);
                await Assert.That(Array.Exists(influences, i => i.Bone == "bone_c")).IsFalse();
            }
        }

        /// <summary>
        /// A sheet whose fitless $cloth_m0p3 carries a soft offset onto a bone no other vertex anchors.
        /// </summary>
        private static ClothReconstruction DeferredOffsetSheet(int[]? skelParents) => new FeModelBuilder
        {
            Names = ["root", "bone_a", "bone_b", "bone_c", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"],
            Parents = skelParents,
            StaticNodes = 1,
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(0f, 4f, -10f), new(4f, 4f, -10f),
                new(4f, 4f, -20f), new(0f, 4f, -20f)],
            Tris = [Tri(4, 5, 6), Tri(4, 6, 7)],
            CtrlOffsets = [Offset(2, 4, 0f, 4f, 0f), Offset(2, 5, 4f, 4f, 0f), Offset(2, 6, 4f, 4f, 0f), Offset(1, 7, 0f, 4f, 0f)],
            CtrlSoftOffsets = [new FeCtrlSoftOffset(3, 7, new Vector3(0f, 4f, 0f), 0.7f)],
            FitMatrices = [FitMatrix(2, 3, 0, bone: Pose(1f, 0f, 0f))],
            FitWeights = [FitWeight(4, 0.5f), FitWeight(5, 0.5f), FitWeight(6, 0.5f)],
        }.Reconstruct();

        private static ClothReconstruction FaceOverNode(string third) => new FeModelBuilder
        {
            Names = ["bone_a", "bone_b", third],
            Parents = [-1, -1, -1],
            Positions = [new(0f, 0f, 0f), new(4f, 0f, 0f), new(0f, 3f, 0f)],
            Tris = [Tri(0, 1, 2)],
        }.Reconstruct();

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
        private static ClothReconstruction TiedPinSheet() => new FeModelBuilder
        {
            Names = ["$cloth_m0p0", "root", "bone_a", "bone_b", "bone_c", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"],
            Parents = [2, -1, 1, 1, 1, 3, 3, 3],
            StaticNodes = 2,
            Positions = [new(0f, 4f, -10f), new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f), new(0f, 0f, -30f), new(4f, 4f, -10f),
                new(4f, 4f, -20f), new(0f, 4f, -20f)],
            Tris = [Tri(0, 5, 6), Tri(0, 6, 7)],
            CtrlOffsets = [Offset(2, 0, 0f, 4f, 0f), Offset(3, 5, 4f, 4f, 0f), Offset(3, 6, 4f, 4f, 0f), Offset(3, 7, 0f, 4f, 0f)],
            CtrlSoftOffsets = [new FeCtrlSoftOffset(4, 0, new Vector3(0f, 4f, 0f), 0.5f)],
            FitMatrices = [FitMatrix(3, 3, 0, bone: Pose(1f, 0f, 0f))],
            FitWeights = [FitWeight(5, 0.5f), FitWeight(6, 0.5f), FitWeight(7, 0.5f)],
        }.Reconstruct();

        /// <summary>
        /// A selection covering the union of a sheet's exported islands is still the sheet's container.
        /// </summary>
        [Test]
        public async Task ASelectionOverSeveralExportedProxiesIsStillTheSheetsContainer()
        {
            var model = WeightedSheet(255, 255, 255, 255);
            var islands = new[] { Island([0, 1]), Island([2, 3]) };

            using (Assert.Multiple())
            {
                await Assert.That(model.GetProxyVertexMapName(islands[0], islands)).IsEqualTo("sheet");
                await Assert.That(model.GetProxyVertexMapName(islands[1], islands)).IsEqualTo("sheet");
                await Assert.That(model.GetProxyVertexMapName(islands[0])).IsNull();
            }
        }

        private static ProxyMesh Island(int[] nodes) => Proxy(nodes, [.. nodes.Select(static _ => 1f)], []);

        /// <summary>
        /// A selection whose covered nodes share one partial value carries it as the container weight; full coverage or
        /// mixed values carry none.
        /// </summary>
        [Test]
        public async Task AContainerWeightIsTheOnePartialWeightItsSelectionShares()
        {
            using (Assert.Multiple())
            {
                await Assert.That(WeightedSheet(128, 128, 128, 128).UniformVertexMapWeight("sheet")!.Value)
                    .IsEqualTo(128f / 255f).Within(1e-4f);
                await Assert.That(WeightedSheet(255, 255, 255, 255).UniformVertexMapWeight("sheet")).IsNull();
                await Assert.That(WeightedSheet(128, 255, 128, 255).UniformVertexMapWeight("sheet")).IsNull();
            }
        }

        /// <summary>Four poseless free nodes all in the selection "sheet" at <paramref name="values"/>.</summary>
        private static ClothReconstruction WeightedSheet(params byte[] values) => new FeModelBuilder
        {
            Names = ["v0", "v1", "v2", "v3"],
            InvMasses = [],
            VertexMaps = [VertexMap("sheet", 1, 0, 0, 4)],
            VertexMapValues = values,
            VertexSetNames = [],
        }.Reconstruct();

        /// <summary>
        /// A quad over declared cloth nodes in a model with no sheet node is an authored <c>ClothQuad</c>; beside a
        /// sheet node it stays with the sheet.
        /// </summary>
        [Test]
        public async Task AQuadOverDeclaredNodesInASheetlessModelIsAnAuthoredElement()
        {
            using (Assert.Multiple())
            {
                await Assert.That(QuadOverBones().GetAuthoredElementFaces().Count).IsEqualTo(1);
                await Assert.That(QuadOverBones("$cloth_m0p0").GetAuthoredElementFaces().Count).IsEqualTo(0);
            }
        }

        private static ClothReconstruction QuadOverBones(params string[] extraNames) => new FeModelBuilder
        {
            Names = ["a", "b", "c", "d", .. extraNames],
            NodeCount = 4,
            InvMasses = [],
            Positions = [new(0f, 0f, 0f), new(3f, 0f, 0f), new(3f, 4f, 0f), new(0f, 4f, 0f)],
            Quads = [Quad(0, 1, 2, 3)],
        }.Reconstruct();

        /// <summary>
        /// Selections over the same nodes at the same weights are aliases of one container; other weights and
        /// registered vertex sets are not.
        /// </summary>
        [Test]
        public async Task SelectionsOverTheSameNodesAndWeightsAreAliasesOfOneContainer()
        {
            var cloth = new FeModelBuilder
            {
                NodeCount = 3,
                StaticNodes = 1,
                InvMasses = [0f, 1f, 1f],
                Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(0f, 0f, -20f)],
                VertexMaps =
                [
                    VertexMap("alias0", 1548087834, 0, 1, 2),
                    VertexMap("alias1", 2540411548, 0, 1, 2),
                    VertexMap("half", 7, 2, 1, 2),
                    VertexMap("painted", 99, 0, 1, 2),
                ],
                VertexMapValues = [255, 255, 255, 128],
                VertexSetNames = [99],
            }.Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(cloth.VertexMapAliases("alias1")).IsEquivalentTo(AliasPair, CollectionOrdering.Matching);
                await Assert.That(cloth.VertexMapAliases("half")).IsEquivalentTo(HalfOnly, CollectionOrdering.Matching);
                await Assert.That(cloth.VertexMapAliases("missing").Count).IsEqualTo(0);
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
            var body = JiggleSets(modelHash);
            static string[] Names(ClothReconstruction cloth) => [.. cloth.VertexMaps.Select(static map => map.Name)];

            var defaultSet = body.Reconstruct();
            var rebuilt = Names(defaultSet);
            defaultSet.DropModelNameVertexSet(ModelFileName);
            var otherModel = body.Reconstruct();
            otherModel.DropModelNameVertexSet("another_model");
            var shipped = (body with { VertexMapValues = [255, 255], VertexMaps = [VertexMap("chain", modelHash, 0, 1, 2)] }).Reconstruct();
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
            var body = JiggleSets(91207372);
            static string[] Names(ClothReconstruction cloth) => [.. cloth.VertexMaps.Select(static map => map.Name)];

            var rebuilt = body.Reconstruct();
            rebuilt.DropUnnamedVertexSet();
            var shipped = (body with { VertexMapValues = [255], VertexMaps = [VertexMap("jiggles", 0, 0, 3, 1)] }).Reconstruct();
            shipped.DropUnnamedVertexSet();

            using (Assert.Multiple())
            {
                await Assert.That(Names(rebuilt)).IsEquivalentTo(["vertex_set_1"]);
                await Assert.That(Names(shipped)).IsEquivalentTo(["jiggles"]);
            }
        }

        /// <summary>
        /// A static root over a two-joint chain and a jiggle bone, the chain's joints in the vertex set
        /// <paramref name="secondSet"/> and the jiggle bone in the set hashed 0.
        /// </summary>
        private static FeModelBuilder JiggleSets(uint secondSet) => new()
        {
            Names = ["root", "a", "b", "jiggle"],
            StaticNodes = 1,
            Parents = [-1, 0, 1, 0],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -5f), new(0f, 0f, -10f), new(5f, 0f, 0f)],
            VertexSetNames = [0, secondSet],
            DynNodeVertexSet = [1, 1, 0],
        };

        /// <summary>
        /// A nearly planar quad whose diagonal ships as a rigid rod reads <c>quad_bend_tolerance</c> 0; without the
        /// rod, or bent past the default, it reads 0.05.
        /// </summary>
        [Test]
        public async Task TheQuadBendToleranceIsReadOffTheSplit()
        {
            static ClothReconstruction Model(float bend, params FeRodConstraint[] rods) => new FeModelBuilder
            {
                Names = ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"],
                StaticNodes = 1,
                Parents = [-1, 0, 0, 0, 0],
                Positions = [new(0f, 0f, 10f), new(0f, 0f, 0f), new(2f, 0f, bend), new(2f, 3f, 0f), new(0f, 3f, 0f)],
                Tris = [Tri(1, 2, 3), Tri(1, 3, 4)],
                Rods = rods,
            }.Reconstruct();

            var rod = RigidRod(2, 4, 3.6056f);
            var nearlyPlanar = Model(0.01f, rod);
            List<int[]> faces = [[1, 2, 3, 4]];
            (int, int)[] splitRod = [(2, 4)];

            using (Assert.Multiple())
            {
                await Assert.That(nearlyPlanar.QuadBendTolerance).IsEqualTo(0f);
                await Assert.That(Model(0.01f).QuadBendTolerance).IsEqualTo(0.05f);
                await Assert.That(Model(1f, rod).QuadBendTolerance).IsEqualTo(0.05f);
                await Assert.That(ClothReconstruction.BentQuadRodsFromFaces(faces, nearlyPlanar.Index.InitPosePositions, static node => node == 0, 0f))
                    .IsEquivalentTo(splitRod);
                await Assert.That(ClothReconstruction.BentQuadRodsFromFaces(faces, nearlyPlanar.Index.InitPosePositions, static node => node == 0, 0.05f))
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
            static List<int[]> Choose(List<int[]> faces, IReadOnlyList<int> nodes, ClothReconstruction pins)
                => pins.ChooseFaceDeclarationOrder(faces, faces.Count, nodes);
            static ClothReconstruction Pinned(int nodes, int pinned) => new FeModelBuilder
            {
                Names = [.. Enumerable.Range(0, nodes).Select(static node => $"n{node}")],
                StaticNodes = pinned,
                RotLockStaticNodes = 13,
            }.Reconstruct();
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

        private static ClothReconstruction StretchQuad(float edge, float diagonal) => new FeModelBuilder
        {
            Names = ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"],
            Positions = [new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 0f, -10f), new(0f, 0f, -10f)],
            DefaultSurfaceStretch = 0f,
            SourceElems = [0, 0, 0, 1, 0, 1, 2, 3],
            Rods =
            [
                Rod(0, 1, 7.5f, 10f, 0.5f, edge),
                Rod(1, 2, 7.5f, 10f, 0.5f, edge),
                Rod(2, 3, 7.5f, 10f, 0.5f, edge),
                Rod(0, 3, 7.5f, 10f, 0.5f, edge),
                Rod(0, 2, 10.606602f, 14.142136f, 0.5f, diagonal),
                Rod(1, 3, 10.606602f, 14.142136f, 0.5f, diagonal),
            ],
        }.Reconstruct();

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
                => Order(ClothReconstruction.RotateQuadsToShippedMasses(declared, declared.Count, nodes, positions, InvMasses(simulated)));

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

        private static ClothReconstruction StretchedSheet(float[] rowPaint)
        {
            const float ShearFactor = 0.5f;
            var rods = new List<FeRodConstraint>();
            foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(0, 1), (1, 2), (3, 4), (4, 5), (6, 7), (7, 8), (0, 3), (1, 4), (2, 5), (3, 6), (4, 7), (5, 8),
                (0, 4), (1, 3), (1, 5), (2, 4), (3, 7), (4, 6), (4, 8), (5, 7)])
            {
                var edge = a / 3 == b / 3 || a % 3 == b % 3;
                var rest = edge ? 10f : MathF.Sqrt(200f);
                var open = 1f - (0.5f * (rowPaint[a / 3] + rowPaint[b / 3]));
                rods.Add(Rod(a, b, rest * 0.5f, rest, 0.5f, (edge ? 1f : ShearFactor) * open * open * open));
            }

            return (ThreeByThreeSheet with { Rods = [.. rods] }).Reconstruct();
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

        private static ClothReconstruction ShearedSheet(bool leftEdges)
        {
            var rods = new List<FeRodConstraint>();
            foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(1, 2), (4, 5), (7, 8), (1, 4), (2, 5), (4, 7), (5, 8)])
            {
                rods.Add(RigidRod(a, b, 10f));
            }

            if (leftEdges)
            {
                foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(0, 1), (3, 4), (6, 7), (0, 3), (3, 6)])
                {
                    rods.Add(RigidRod(a, b, 10f));
                }
            }

            foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(1, 5), (2, 4), (4, 8), (5, 7)])
            {
                rods.Add(Rod(a, b, MathF.Sqrt(200f) * 0.75f, MathF.Sqrt(200f), 0.5f, 0.125f));
            }

            return (ThreeByThreeSheet with { Rods = [.. rods] }).Reconstruct();
        }

        /// <summary>
        /// A proxy sheet no fit range names, beside a fit-covered one, is not back-solved and keeps its skin paint
        /// verbatim; a fit-named sheet defers it, and with no fits the paint is recovered anyway.
        /// </summary>
        [Test]
        public async Task ASheetTheOriginalDidNotBackSolveKeepsItsAuthoredProxyPaint()
        {
            var oneBackSolving = (TwoProxySheets with
            {
                FitMatrices = [FitMatrix(6, 2, 0), FitMatrix(7, 4, 0)],
                FitWeights = [FitWeight(2, 0.75f), FitWeight(3, 0.5f), FitWeight(2, 0.25f), FitWeight(3, 0.5f)],
            }).Reconstruct();
            var bothBackSolving = (TwoProxySheets with
            {
                FitMatrices = [FitMatrix(6, 3, 0), FitMatrix(7, 5, 0)],
                FitWeights = [FitWeight(2, 0.75f), FitWeight(3, 0.5f), FitWeight(5, 0.6f), FitWeight(2, 0.25f), FitWeight(3, 0.5f)],
            }).Reconstruct();
            var noFits = TwoProxySheets.Reconstruct();

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
            var ordered = ClothReconstruction.OrderByFirstWriterWins(names,
                [("alpha", "bravo"), ("alpha", "charlie"), ("bravo", "charlie")]);
            var unconstrained = ClothReconstruction.OrderByFirstWriterWins(names, []);
            var cyclic = ClothReconstruction.OrderByFirstWriterWins(names, [("alpha", "bravo"), ("bravo", "alpha")]);
            var partial = ClothReconstruction.OrderByFirstWriterWins(names, [("alpha", "charlie")]);

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
            var cloth = SelectionsOverNoVertex;

            using (Assert.Multiple())
            {
                await Assert.That(cloth.ZeroVertexSelectionNames.Count).IsEqualTo(1);
                await Assert.That(cloth.ZeroVertexSelectionNames[0]).IsEqualTo("ghost");
                await Assert.That(cloth.VertexMaps.Count).IsEqualTo(3);
                await Assert.That(cloth.ZeroVertexSelectionNames.Contains("real")).IsFalse();
            }
        }

        /// <summary>
        /// Three <c>m_VertexMaps</c> records: "ghost" named over no vertex, "real" over two, and an unnamed one over
        /// none.
        /// </summary>
        private static ClothReconstruction SelectionsOverNoVertex => new FeModelBuilder
        {
            Names = ["bone_0", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"],
            StaticNodes = 2,
            Parents = [-1, 0, 0, 0],
            Positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 0f, -8f), new(1f, 0f, -16f)],
            VertexMapValues = [255, 255],
            VertexMaps =
            [
                VertexMap("ghost", 2018973841, 0, 0, 0),
                new FeVertexMapDesc("real", 2081852616, 0, 0, 2, 2, 0, 0, Vector3.Zero, 0f, -1, 2),
                VertexMap(string.Empty, 0, 0, 0, 0),
            ],
        }.Reconstruct();

        /// <summary>
        /// Two proxy sheets over a two-bone chain: mesh 0's vertices are fit targets, and mesh 1's vertex 4 has a
        /// two-bone paint and no fit entry. No fit arrays are set.
        /// </summary>
        private static FeModelBuilder TwoProxySheets => new()
        {
            Names = ["bone_0", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m1p1", "$cloth_m1p2", "bone_1", "bone_2"],
            StaticNodes = 2,
            FirstPositionDrivenNode = 6,
            Parents = [-1, 0, 6, 7, 6, 6, 0, 6],
            Positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 0f, -8f), new(1f, 0f, -16f), new(-1f, 0f, -8f), new(-1f, 0f, -16f),
                new(0f, 0f, -8f), new(0f, 0f, -16f)],
            CtrlOffsets = [Offset(6, 2, 1f, 0f, 0f), Offset(7, 3, 1f, 0f, 0f), Offset(6, 4, -1f, 0f, 0f), Offset(6, 5, -1f, 0f, -8f)],
            CtrlSoftOffsets = [new FeCtrlSoftOffset(7, 4, new Vector3(-1f, 0f, 8f), 0.75f)],
        };

        /// <summary>
        /// A recorded set member its selection weighs 0 is painted at <see cref="ClothReconstruction.SubQuantumMembershipWeight"/>,
        /// which still rounds to byte 0; non-members, static nodes, positive weights and models without a set array are
        /// unchanged.
        /// </summary>
        [Test]
        public async Task ASetMemberItsSelectionWeighsZeroIsPaintedBelowAQuantum()
        {
            var recorded = SubQuantumMembers(recorded: true).BuildProxyMeshes()[0];
            var unrecorded = SubQuantumMembers(recorded: false).BuildProxyMeshes()[0];

            static float Weight(ProxyMesh proxy, string map, int node)
                => Array.Find(proxy.VertexMaps, m => m.Name == map).Weights[Array.IndexOf(proxy.NodeIndices, node)];

            var inRange = Weight(recorded, "qb", 5);

            using (Assert.Multiple())
            {
                await Assert.That(Weight(recorded, "qb", 3)).IsEqualTo(ClothReconstruction.SubQuantumMembershipWeight);
                await Assert.That(inRange).IsEqualTo(ClothReconstruction.SubQuantumMembershipWeight);
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
        /// <paramref name="recorded"/> records node 6 in the second vertex set.
        /// </summary>
        private static ClothReconstruction SubQuantumMembers(bool recorded) => new FeModelBuilder
        {
            Names = ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "$cloth_m0p4", "$cloth_m0p5"],
            StaticNodes = 3,
            Parents = [-1, 0, 0, 0, 0, 0, 0],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -10f), new(4f, 0f, -10f), new(0f, 0f, -20f), new(4f, 0f, -20f), new(0f, 0f, -30f),
                new(4f, 0f, -30f)],
            Tris = [Tri(1, 2, 4), Tri(1, 4, 3), Tri(3, 4, 6), Tri(3, 6, 5)],
            VertexSetNames = [3919779763, 51193340],
            DynNodeVertexSet = recorded ? [0, 0, 0, 1] : null,
            VertexMapValues = [255, 0, 128, 0, 255],
            VertexMaps =
            [
                new FeVertexMapDesc("qb", 3919779763, 0, 0, 4, 3, 0, 0, Vector3.Zero, 0f, -1, 2),
                new FeVertexMapDesc("qz", 51193340, 0, 0, 5, 2, 3, 2, Vector3.Zero, 0f, -1, 1),
            ],
        }.Reconstruct();

        /// <summary>
        /// <c>RegistersVertexSet</c> is true only for hashes in <c>m_VertexSetNames</c>, not for a selection's own
        /// unregistered hash.
        /// </summary>
        [Test]
        public async Task ASelectionTheModelRegistersNoSetForIsNotPainted()
        {
            var cloth = UnregisteredSelection();

            using (Assert.Multiple())
            {
                await Assert.That(cloth.RegistersVertexSet(3027761651)).IsTrue();
                await Assert.That(cloth.RegistersVertexSet(4042757229)).IsFalse();
                await Assert.That(cloth.VertexMaps.Count).IsEqualTo(1);
                await Assert.That(cloth.RegistersVertexSet(cloth.VertexMaps[0].NameHash)).IsFalse();
            }
        }

        /// <summary>
        /// A static root over three sheet vertices, registering one vertex set and carrying one selection record in a
        /// layout whose keys the reader does not know.
        /// </summary>
        private static ClothReconstruction UnregisteredSelection()
        {
            var data = new FeModelBuilder
            {
                Names = ["root", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"],
                StaticNodes = 1,
                VertexSetNames = [3027761651],
            }.ToKV();
            data["m_VertexMaps"] = KVObject.Array([Object(("m_Name", "coat_clothVertMap"), ("m_nNameHash", 4042757229L), ("m_nVertexBase", 1),
                ("m_nVertexCount", 3), ("m_Weights", Ints(255, 255, 0)))]);
            return new ClothReconstruction(new FeModel(data));
        }

        /// <summary>
        /// A proxy sheet fit over a bone outside the position-driven suffix reads as
        /// <c>back_solve_joints_drive_meshes</c> alone; a driven bone, an unfit sheet, or no compiled boundary do not.
        /// </summary>
        [Test]
        public async Task ASheetFittingAnUndrivenBoneStatesDriveMeshesAlone()
        {
            var undriven = SheetFitOverTipBone(8).Reconstruct();
            var driven = SheetFitOverTipBone(7).Reconstruct();
            var unbounded = SheetFitOverTipBone(null).Reconstruct();

            static ProxyMesh Sheet(ClothReconstruction cloth, int mesh)
                => cloth.BuildProxyMeshes().First(proxy => Array.Exists(proxy.NodeIndices,
                    node => cloth.Fe.CtrlName[node].StartsWith($"$cloth_m{mesh}p", StringComparison.Ordinal)));

            using (Assert.Multiple())
            {
                await Assert.That(undriven.Index.FitMatrixTargets.Count).IsEqualTo(1);
                await Assert.That(string.Join(",", undriven.Index.FitMatrixTargets[7])).IsEqualTo("4,5,6");
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
            var backSolved = FitlessOnPaintedBone(ReverseOffset(6, 4, 0f, 0f, 0f));
            var unmarked = FitlessOnPaintedBone();

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
        private static ClothReconstruction FitlessOnPaintedBone(params FeNodeReverseOffset[] reverseOffsets) => new FeModelBuilder
        {
            Names = ["bone_0", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3", "bone_1", "bone_2", "bone_3"],
            StaticNodes = 2,
            FirstPositionDrivenNode = 5,
            Parents = [-1, 0, 5, 7, 6, 0, 5, 6],
            Positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 0f, -8f), new(1f, 0f, -24f), new(1f, 0f, -16f), new(0f, 0f, -8f),
                new(0f, 0f, -16f), new(0f, 0f, -24f)],
            CtrlOffsets = [Offset(0, 1, 1f, 0f, 0f), Offset(5, 2, 1f, 0f, 0f), Offset(7, 3, 1f, 0f, 0f), Offset(6, 4, 1f, 0f, 0f)],
            CtrlSoftOffsets =
            [
                new FeCtrlSoftOffset(7, 2, new Vector3(1f, 0f, 16f), 0.5f),
                new FeCtrlSoftOffset(6, 3, new Vector3(1f, 0f, -8f), 0.9f),
                new FeCtrlSoftOffset(5, 4, new Vector3(1f, 0f, -8f), 0.6f),
            ],
            FitMatrices = [FitMatrix(7, 2, 0)],
            FitWeights = [FitWeight(2, 0.5f), FitWeight(3, 0.9f)],
            ReverseOffsets = reverseOffsets,
        }.Reconstruct();

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
        private static ClothReconstruction FullSlotRemainder() => new FeModelBuilder
        {
            Names = ["root", "spine", "neck", "hair", "s1", "s2", "s3", "s4", "s5", "s6", "dyn", "$cloth_m0p0"],
            StaticNodes = 10,
            FirstPositionDrivenNode = 10,
            Parents = [-1, 0, 1, 2, 0, 0, 0, 0, 0, 0, 3, -1],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, 10f), new(0f, 0f, 20f), new(0f, 0f, 30f), new(2f, 0f, 0f), new(4f, 0f, 0f),
                new(6f, 0f, 0f), new(8f, 0f, 0f), new(10f, 0f, 0f), new(12f, 0f, 0f), new(0f, 0f, 35f), new(1f, 0f, 32f)],
            CtrlOffsets = [Offset(3, 11, 1f, 0f, 2f)],
            CtrlSoftOffsets =
            [
                new FeCtrlSoftOffset(10, 11, new Vector3(1f, 0f, -3f), 0.5f),
                new FeCtrlSoftOffset(2, 11, new Vector3(1f, 0f, 12f), 0.9f),
                new FeCtrlSoftOffset(4, 11, new Vector3(-1f, 0f, 32f), 0.9f),
                new FeCtrlSoftOffset(5, 11, new Vector3(-3f, 0f, 32f), 0.9f),
                new FeCtrlSoftOffset(6, 11, new Vector3(-5f, 0f, 32f), 0.9f),
                new FeCtrlSoftOffset(7, 11, new Vector3(-7f, 0f, 32f), 0.9f),
                new FeCtrlSoftOffset(8, 11, new Vector3(-9f, 0f, 32f), 0.9f),
                new FeCtrlSoftOffset(9, 11, new Vector3(-11f, 0f, 32f), 0.9f),
            ],
            FitMatrices = [FitMatrix(10, 1, 0)],
            FitWeights = [FitWeight(11, 0.2343655f)],
        }.Reconstruct();

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
        private static ClothReconstruction FitlessOnFitBone(bool boneOwnsFit) => new FeModelBuilder
        {
            Names = ["bone_0", "bone_1", "$cloth_m0p0", "$cloth_m0p1", "bone_2", "bone_3"],
            StaticNodes = 2,
            FirstPositionDrivenNode = 4,
            Parents = [-1, 0, 5, 1, 1, 4],
            Positions = [new(0f, 0f, 0f), new(0f, 0f, -8f), new(1f, 0f, -24f), new(1f, 0f, -10f), new(0f, 0f, -16f), new(0f, 0f, -24f)],
            CtrlOffsets = [Offset(5, 2, 1f, 0f, 0f), Offset(1, 3, 1f, 0f, -2f)],
            CtrlSoftOffsets = [new FeCtrlSoftOffset(4, 2, new Vector3(1f, 0f, -8f), 0.7f), new FeCtrlSoftOffset(4, 3, new Vector3(1f, 0f, 6f), 0.96f)],
            FitMatrices = [FitMatrix(5, 1, 0), .. boneOwnsFit ? [FitMatrix(4, 2, 0)] : Array.Empty<FeFitMatrix>()],
            FitWeights = [FitWeight(2, 0.7f), .. boneOwnsFit ? [FitWeight(2, 0.3f)] : Array.Empty<FeFitWeight>()],
        }.Reconstruct();

        /// <summary>
        /// A proxy vertex whose slot is not a number, or lies beyond any mesh a cloth can hold, leaves its sheet unpadded.
        /// </summary>
        [Test]
        public async Task AnUnreadableProxySlotLeavesTheSheetUnpadded()
        {
            static ClothReconstruction Sheet(string lastSlot) => new FeModelBuilder
            {
                Names = ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", $"$cloth_m0p{lastSlot}"],
                Positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 1f, 0f), new(0f, 1f, 0f)],
                Quads = [Quad(0, 1, 2, 3)],
            }.Reconstruct();

            var unnumbered = Sheet("foo").BuildProxyMeshes();
            var huge = Sheet("2000000000").BuildProxyMeshes();

            using (Assert.Multiple())
            {
                await Assert.That(unnumbered.Count).IsEqualTo(1);
                await Assert.That(unnumbered[0].NodeIndices.Length).IsEqualTo(4);
                await Assert.That(huge.Count).IsEqualTo(1);
                await Assert.That(huge[0].NodeIndices.Length).IsEqualTo(4);
            }
        }

        /// <summary>
        /// A solve-element corner that has a rest pose but no control name stays out of the sheet.
        /// </summary>
        [Test]
        public async Task ASheetCornerWithoutAControlNameStaysOutOfTheSheet()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"],
                NodeCount = 4,
                InvMasses = [1f, 1f, 1f, 1f],
                Positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 1f, 0f), new(0f, 1f, 0f)],
                Quads = [Quad(0, 1, 2, 3)],
            }.Reconstruct();

            var meshes = cloth.BuildProxyMeshes();

            await Assert.That(meshes.SelectMany(static mesh => mesh.NodeIndices).All(static node => node < 3)).IsTrue();
        }

        /// <summary>
        /// A simulated sheet vertex skinned over a skeleton whose parents form a cycle takes each bone of the cycle once.
        /// </summary>
        [Test]
        public async Task ASheetOverCyclicParentsSkinsEachBoneOnce()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["a", "b", "$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2", "$cloth_m0p3"],
                Parents = [1, 0, 0, 0, 0, 0],
                Positions = [new(0f, 0f, 10f), new(0f, 0f, 20f), new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 1f, 0f), new(0f, 1f, 0f)],
                Quads = [Quad(2, 3, 4, 5)],
            }.Reconstruct();

            var influences = cloth.BuildProxyMeshes().SelectMany(static mesh => mesh.SkinInfluences).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(influences.Count).IsEqualTo(4);
                await Assert.That(influences.All(static vertex => vertex.Select(static i => i.Bone).Distinct().Count() == vertex.Length))
                    .IsTrue();
            }
        }

        /// <summary>
        /// A soft offset towards a negative parent leaves only the primary bone on the vertex.
        /// </summary>
        [Test]
        public async Task ASoftOffsetToANegativeParentIsSkipped()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["bone_0", "$cloth_m0p0"],
                StaticNodes = 1,
                CtrlOffsets = [Offset(0, 1, 0f, 0f, 0f)],
                CtrlSoftOffsets = [new FeCtrlSoftOffset(-1, 1, Vector3.Zero, 0.5f)],
            }.Reconstruct();

            await Assert.That(cloth.RecoveredSkinWeights[1].Select(static influence => influence.Bone)).IsEquivalentTo(["bone_0"]);
        }

        /// <summary>
        /// A rods-only sheet over an <c>m_SourceElems</c> whose element counts are negative takes no authored faces.
        /// </summary>
        [Test]
        public async Task NegativeSourceElementCountsGiveARodsOnlySheetNoAuthoredFaces()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["$cloth_m0p0", "$cloth_m0p1", "$cloth_m0p2"],
                Positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f)],
                SourceElems = [-10, 0, 1, 0],
                Rods = [RigidRod(0, 1, 1f), RigidRod(1, 2, 1.4142135f)],
            }.Reconstruct();

            var meshes = cloth.BuildProxyMeshes();

            using (Assert.Multiple())
            {
                await Assert.That(meshes.Count).IsEqualTo(1);
                await Assert.That(meshes[0].UsesAuthoredFaces).IsFalse();
            }
        }

        /// <summary>
        /// A fitted vertex whose remaining weight would go to a static ancestor gets none when every static ancestor on a
        /// parent cycle already carries an influence.
        /// </summary>
        [Test]
        public async Task AFitRemainderFindsNoAnchorOnACycleOfBoundStaticBones()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["s0", "s1", "b", "$cloth_m0p0"],
                StaticNodes = 2,
                Parents = [1, 0, 0, 2],
                CtrlOffsets = [Offset(2, 3, 0f, 0f, 0f)],
                CtrlSoftOffsets =
                [
                    new FeCtrlSoftOffset(0, 3, Vector3.Zero, 0.5f),
                    new FeCtrlSoftOffset(1, 3, Vector3.Zero, 0.5f),
                    .. Enumerable.Repeat(new FeCtrlSoftOffset(0, 3, Vector3.Zero, 1f), 6),
                ],
                FitMatrices = [FitMatrix(2, 1, 0)],
                FitWeights = [FitWeight(3, 0.2f)],
            }.Reconstruct();

            await Assert.That(cloth.RecoveredSkinWeights[3].Select(static influence => influence.Bone))
                .IsEquivalentTo(["b", "s0", "s1"]);
        }
    }
}
