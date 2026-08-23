using System.Threading.Tasks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Utils;

namespace Tests.Renderer
{
    public class SmartPropWidgetSceneNodesTest
    {
        private const float Tolerance = 1e-3f;

        private static Matrix4x4 IdentityWorld(Vector3 translation) => Matrix4x4.CreateTranslation(translation);

        [Test]
        public async Task RotatorBuildsRingNeedleAndTab()
        {
            var position = new Vector3(5f, 0f, 0f);
            var widget = new SmartPropRotatorWidget(
                7, IdentityWorld(position), position, default, "rot",
                default, new Vector3(0f, 0f, 1f), 32f, 0f, new Vector3(0.5f, 0.5f, 1f));

            var (vertices, indices) = SmartPropRotatorSceneNode.BuildSolidGeometry(widget);
            var lines = SmartPropRotatorSceneNode.BuildGeometry(widget);

            await Assert.That(vertices.Count).IsEqualTo(1572);
            await Assert.That(indices.Count).IsEqualTo(1572);
            await Assert.That(vertices.Exists(v => v.Color == new Color32(0.95f, 0.90f, 0.10f, 0.95f))).IsTrue();

            var needleStart = lines[^2].Position;
            var needleEnd = lines[^1].Position;
            await Assert.That(needleStart).IsEqualTo(position);
            await Assert.That(needleEnd.X - position.X).IsEqualTo(32f).Within(Tolerance);
            await Assert.That(needleEnd.Y).IsEqualTo(position.Y).Within(Tolerance);
        }

        [Test]
        public async Task SizerBuildsBoxEdgesAndActiveHandles()
        {
            var widget = new SmartPropSizerWidget(
                7, Matrix4x4.Identity, Vector3.Zero, default, "size",
                new Vector3(-10f, -5f, -2f), new Vector3(10f, 5f, 2f),
                new SmartPropSizerHandles(MinX: true, MaxX: false, MinY: false, MaxY: false, MinZ: false, MaxZ: false),
                new SmartPropSizerAxes(X: true, Y: true, Z: true));

            var (vertices, indices) = SmartPropSizerSceneNode.BuildSolidGeometry(widget);
            var (volumeVertices, volumeIndices) = SmartPropSizerSceneNode.BuildVolumeGeometry(widget);
            var (arrowVertices, arrowIndices) = SmartPropSizerSceneNode.BuildArrowGeometry(widget);
            var guides = SmartPropSizerSceneNode.BuildGuideGeometry(widget);

            await Assert.That(indices.Count).IsEqualTo(180);
            await Assert.That(volumeIndices.Count).IsEqualTo(36);
            await Assert.That(arrowIndices.Count).IsEqualTo(144);
            await Assert.That(guides.Count).IsEqualTo(24);
            await Assert.That(vertices.Exists(v => v.Position.X <= -24f + Tolerance)).IsTrue();
            await Assert.That(vertices.Exists(v => v.Color == new Color32(0.90f, 0.15f, 0.15f, 1f))).IsTrue();
            await Assert.That(volumeVertices.Exists(v => v.Color == new Color32(0.22f, 0.65f, 0.95f, 0.30f))).IsTrue();
            await Assert.That(arrowVertices.TrueForAll(v => v.Color.A == byte.MaxValue)).IsTrue();
        }

        [Test]
        public async Task SizerWithoutHandlesDrawsOnlyTheBox()
        {
            var widget = new SmartPropSizerWidget(
                7, Matrix4x4.Identity, Vector3.Zero, default, "",
                new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f),
                new SmartPropSizerHandles(false, false, false, false, false, false),
                new SmartPropSizerAxes(true, true, true));

            var (_, indices) = SmartPropSizerSceneNode.BuildVolumeGeometry(widget);
            var (arrowVertices, arrowIndices) = SmartPropSizerSceneNode.BuildArrowGeometry(widget);
            var guides = SmartPropSizerSceneNode.BuildGuideGeometry(widget);
            await Assert.That(indices.Count).IsEqualTo(36);
            await Assert.That(arrowVertices).IsEmpty();
            await Assert.That(arrowIndices).IsEmpty();
            await Assert.That(guides.Count).IsEqualTo(24);
        }

        [Test]
        public async Task PickOneBuildsSquareDiamondAndCircleShapes()
        {
            var square = BuildPickOne("SQUARE");
            var diamond = BuildPickOne("DIAMOND");
            var circle = BuildPickOne("CIRCLE");

            await Assert.That(square.Count).IsEqualTo(8);
            await Assert.That(diamond.Count).IsEqualTo(8);
            await Assert.That(circle.Count).IsEqualTo(96);
        }

        [Test]
        public async Task PickOneGeometryFollowsViewPlane()
        {
            var position = new Vector3(1f, 2f, 3f);
            var widget = new SmartPropPickOneHandleWidget(
                7, IdentityWorld(position), position, default, "pick",
                default, 8f, new Vector3(1f, 1f, 1f), "CIRCLE");

            var vertices = SmartPropPickOneSceneNode.BuildGeometry(widget, Vector3.UnitY, Vector3.UnitZ);

            foreach (var vertex in vertices)
            {
                await Assert.That(vertex.Position.X).IsEqualTo(position.X).Within(0.0001f);
            }
        }

        [Test]
        public async Task PickOneScaleTracksCameraDistance()
        {
            await Assert.That(SmartPropPickOneSceneNode.CalculateScreenScale(10f, 8f)).IsEqualTo(2.4f).Within(Tolerance);
            await Assert.That(SmartPropPickOneSceneNode.CalculateScreenScale(200f, 8f)).IsEqualTo(3.9f).Within(Tolerance);
            await Assert.That(SmartPropPickOneSceneNode.CalculateScreenScale(200f, 4f)).IsEqualTo(1.95f).Within(Tolerance);
            await Assert.That(SmartPropPickOneSceneNode.CalculateScreenScale(float.NaN, 8f)).IsEqualTo(2.4f).Within(Tolerance);
        }

        private static List<SimpleVertex> BuildPickOne(string shape)
        {
            var position = new Vector3(1f, 2f, 3f);
            var widget = new SmartPropPickOneHandleWidget(
                7, IdentityWorld(position), position, default, "pick",
                default, 8f, new Vector3(1f, 1f, 1f), shape);

            return SmartPropPickOneSceneNode.BuildGeometry(widget);
        }

        [Test]
        public async Task PathBuildsCurveStripAndControlPointMarkers()
        {
            Vector3[] curve =
            [
                new(0f, 0f, 0f),
                new(1f, 0f, 0f),
                new(2f, 0f, 0f),
                new(3f, 0f, 0f),
            ];
            Vector3[] controlPoints = [new(0f, 0f, 0f), new(3f, 0f, 0f)];

            var pathInfo = new SmartPropPathInfo(30, curve, controlPoints, Matrix4x4.Identity);
            var vertices = SmartPropPathSceneNode.BuildGeometry(pathInfo);

            // 3 curve segments plus two crosses of 3 segments each
            await Assert.That(vertices.Count).IsEqualTo((3 + 6) * 2);

            // The strip visits the sample positions in order
            await Assert.That(vertices[0].Position).IsEqualTo(curve[0]);
            await Assert.That(vertices[1].Position).IsEqualTo(curve[1]);
        }
    }
}
