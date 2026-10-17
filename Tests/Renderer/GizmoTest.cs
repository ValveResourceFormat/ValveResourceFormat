using System.Threading.Tasks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor.Gizmos;
using ValveResourceFormat.Renderer.Editor.Selection;
using ValveResourceFormat.Utils;

namespace Tests.Renderer
{
    /// <summary>
    /// The gizmos move what is selected the way Hammer does: these pin how a drag is picked and snapped on the screen,
    /// and how each kind of map node takes the result of a drag.
    /// </summary>
    public class GizmoTest
    {
        private const float Tolerance = 1e-3f;

        private static Camera MakeCamera()
        {
            var camera = new Camera { Location = new Vector3(0f, -600f, 200f) };
            camera.SetViewportSize(1280, 720);
            camera.LookAt(Vector3.Zero);
            camera.RecalculateMatrices();

            return camera;
        }

        private static GizmoTool MakeSnapping() => new(new SelectionSet(), static _ => null);

        private static GizmoSetup MakeSetup(Vector3 pivot)
        {
            var bounds = new AABB(pivot - new Vector3(16f), pivot + new Vector3(16f));
            return new GizmoSetup(GizmoSelectionKind.Single, IsLocalSpace: false, pivot, pivot, bounds, bounds, Matrix4x4.Identity, Matrix4x4.Identity);
        }

        private static async Task AssertNear(Vector3 actual, Vector3 expected, string because)
        {
            await Assert.That(Vector3.Distance(actual, expected)).IsLessThan(Tolerance).Because($"{because}: {actual} instead of {expected}");
        }

        /// <summary>The arrow of an axis is picked by the band along it, and the center by its handle.</summary>
        [Test]
        public async Task TranslateGizmoPicksTheAxisUnderTheCursor()
        {
            var camera = MakeCamera();
            var gizmo = new TranslateGizmo(null);
            gizmo.Setup(MakeSetup(Vector3.Zero));

            var alongX = camera.WorldToScreenPoint(new Vector3(60f, 0f, 0f))!.Value;
            var center = camera.WorldToScreenPoint(Vector3.Zero)!.Value;

            await Assert.That(gizmo.HitTest(camera, alongX)).IsEqualTo(GizmoPart.AxisX);
            await Assert.That(gizmo.HitTest(camera, center)).IsEqualTo(GizmoPart.Center);
            await Assert.That(gizmo.HitTest(camera, new Vector2(5f, 5f))).IsEqualTo(GizmoPart.None);
        }

        /// <summary>
        /// Dragging an axis moves along it only, and with snapping it is where the selection ends up that lands on
        /// the grid, not how far it moved.
        /// </summary>
        [Test]
        public async Task TranslateGizmoSnapsWhereTheSelectionEndsUp()
        {
            var camera = MakeCamera();
            var gizmo = new TranslateGizmo(MakeSnapping());
            gizmo.Setup(MakeSetup(new Vector3(10f, 0f, 0f)));

            var grab = camera.WorldToScreenPoint(new Vector3(70f, 0f, 0f))!.Value;
            var drop = camera.WorldToScreenPoint(new Vector3(170f, 30f, 0f))!.Value;

            await Assert.That(gizmo.BeginDrag(camera, grab)).IsTrue();
            await Assert.That(gizmo.ActivePart).IsEqualTo(GizmoPart.AxisX);

            gizmo.UpdateDrag(camera, drop);

            var moved = Vector3.Transform(new Vector3(10f, 0f, 0f), gizmo.Result.Transform);

            await AssertNear(moved, new Vector3(128f, 0f, 0f), "the pivot after the drag");
        }

        /// <summary>A ring turns right handed about its axis, by where the cursor goes around it.</summary>
        [Test]
        public async Task RotateGizmoTurnsByTheAngleAroundTheRing()
        {
            var camera = new Camera { Location = new Vector3(0f, 0f, 600f) };
            camera.SetViewportSize(1280, 720);
            camera.LookAt(Vector3.Zero);
            camera.RecalculateMatrices();

            var gizmo = new RotateGizmo(null);
            gizmo.Setup(MakeSetup(Vector3.Zero));
            gizmo.SetActivePart(GizmoPart.AxisZ);

            await Assert.That(gizmo.BeginDrag(camera, camera.WorldToScreenPoint(new Vector3(50f, 0f, 0f))!.Value)).IsTrue();
            gizmo.UpdateDrag(camera, camera.WorldToScreenPoint(new Vector3(0f, 50f, 0f))!.Value);

            await Assert.That(gizmo.Result.Angle).IsEqualTo(90f).Within(0.5f);
            await AssertNear(Vector3.Transform(Vector3.UnitX, gizmo.Result.Transform), Vector3.UnitY, "X turned about Z");
        }

        /// <summary>The angle of a ring follows the cursor past a whole turn rather than jumping back.</summary>
        [Test]
        public async Task UnwrapAngleDeltaFollowsTheCursorPastZero()
        {
            await Assert.That(RotateGizmo.UnwrapAngleDelta(10f, 0f, 350f)).IsEqualTo(20f).Within(Tolerance);
            await Assert.That(RotateGizmo.UnwrapAngleDelta(340f, -10f, 10f)).IsEqualTo(-30f).Within(Tolerance);
            await Assert.That(RotateGizmo.UnwrapAngleDelta(100f, 80f, 10f)).IsEqualTo(90f).Within(Tolerance);
        }

        /// <summary>Snapped turns are truncated towards zero, as Hammer's rotation snap does.</summary>
        [Test]
        public async Task RotationSnapTruncatesTowardsZero()
        {
            var snapping = MakeSnapping();

            await Assert.That(snapping.SnapAngle(29f)).IsEqualTo(15f);
            await Assert.That(snapping.SnapAngle(-29f)).IsEqualTo(-15f);
            await Assert.That(snapping.SnapAngle(30f)).IsEqualTo(30f);
        }

        /// <summary>The grid size and rotation snap step through Hammer's values.</summary>
        [Test]
        public async Task SnapSettingsStepThroughHammersValues()
        {
            var snapping = MakeSnapping();

            snapping.IncreaseRotationSnapAngle();
            await Assert.That(snapping.RotationSnapAngle).IsEqualTo(22.5f);

            snapping.DecreaseRotationSnapAngle();
            snapping.DecreaseRotationSnapAngle();
            await Assert.That(snapping.RotationSnapAngle).IsEqualTo(11.25f);

            for (var i = 0; i < 20; i++)
            {
                snapping.DecreaseGridSize();
            }

            await Assert.That(snapping.GridSize).IsEqualTo(0.125f);
        }

        /// <summary>Turning about a pivot moves an entity around it and turns it by the same angle.</summary>
        [Test]
        public async Task EntityTurnsAboutThePivot()
        {
            var result = new GizmoResult
            {
                Flags = GizmoResultFlags.Rotate,
                Axis = Vector3.UnitZ,
                Angle = 90f,
                Pivot = Vector3.Zero,
            };

            var placement = GizmoTransforms.TransformEntity(new MapPlacement(new Vector3(100f, 0f, 0f), Vector3.Zero, Vector3.One), result, isPath: false, isSolid: false);

            await AssertNear(placement.Origin, new Vector3(0f, 100f, 0f), "origin");
            await AssertNear(placement.Angles, new Vector3(0f, 90f, 0f), "angles");
        }

        /// <summary>
        /// Scaling a turned entity along a world axis is kept along the entity's own axes: a yaw of 90 degrees puts
        /// world X along its Y.
        /// </summary>
        [Test]
        public async Task EntityScaleIsKeptAlongItsOwnAxes()
        {
            var entity = new MapPlacement(Vector3.Zero, new Vector3(0f, 90f, 0f), Vector3.One);
            var result = new GizmoResult
            {
                Flags = GizmoResultFlags.Scale,
                Scale = new Vector3(2f, 1f, 1f),
                ScaleStart = new Vector3(50f, 0f, 0f),
                ScaleCurrent = new Vector3(100f, 0f, 0f),
                Orientation = entity.RigidTransform,
            };

            var placement = GizmoTransforms.TransformEntity(entity, result, isPath: false, isSolid: false);

            await AssertNear(placement.Scales, new Vector3(1f, 2f, 1f), "scales");
            await AssertNear(placement.Origin, Vector3.Zero, "origin");
        }

        /// <summary>
        /// Dragging the X axis of the scale gizmo on an entity turned only by multiples of a quarter turn scales the
        /// one axis of the entity along world X.
        /// </summary>
        [Test]
        public async Task ScaleGizmoAxisScalesOneAxisOfTheEntity()
        {
            foreach (var yaw in new[] { 0f, 90f })
            {
                var camera = MakeCamera();
                var entity = new MapPlacement(Vector3.Zero, new Vector3(0f, yaw, 0f), Vector3.One);
                var bounds = new AABB(new Vector3(-16f), new Vector3(16f));
                var gizmo = new ScaleGizmo(null);

                gizmo.Setup(new GizmoSetup(GizmoSelectionKind.Single, IsLocalSpace: false, Vector3.Zero, Vector3.Zero, bounds, bounds, entity.RigidTransform, Matrix4x4.Identity));
                gizmo.SetActivePart(GizmoPart.AxisX);

                await Assert.That(gizmo.BeginDrag(camera, camera.WorldToScreenPoint(new Vector3(50f, 0f, 0f))!.Value)).IsTrue();
                gizmo.UpdateDrag(camera, camera.WorldToScreenPoint(new Vector3(300f, 0f, 0f))!.Value);

                var placement = GizmoTransforms.TransformEntity(entity, gizmo.Result, isPath: false, isSolid: false);
                var scaledAxis = yaw == 0f ? placement.Scales.X : placement.Scales.Y;
                var otherAxis = yaw == 0f ? placement.Scales.Y : placement.Scales.X;

                await Assert.That(scaledAxis).IsGreaterThan(1.5f).Because($"the axis along world X at yaw {yaw}");
                await Assert.That(otherAxis).IsEqualTo(1f).Within(Tolerance).Because($"the axis across world X at yaw {yaw}");
                await Assert.That(placement.Scales.Z).IsEqualTo(1f).Within(Tolerance).Because($"Z at yaw {yaw}");
            }
        }

        /// <summary>
        /// Scaling a prop turned by other than a quarter turn along a world axis scales only its own axis nearest that
        /// world axis, unlike Hammer, which scales every axis the world axis has a part along.
        /// </summary>
        [Test]
        public async Task WorldScaleOfATurnedEntityScalesItsNearestAxis()
        {
            var entity = new MapPlacement(Vector3.Zero, new Vector3(0f, 30f, 0f), Vector3.One);
            var result = new GizmoResult
            {
                Flags = GizmoResultFlags.Scale,
                Scale = new Vector3(2f, 1f, 1f),
                Orientation = entity.RigidTransform,
            };

            await AssertNear(GizmoTransforms.TransformEntity(entity, result, isPath: false, isSolid: false).Scales, new Vector3(2f, 1f, 1f), "dragging world X");

            result.Scale = new Vector3(1f, 3f, 1f);

            await AssertNear(GizmoTransforms.TransformEntity(entity, result, isPath: false, isSolid: false).Scales, new Vector3(1f, 3f, 1f), "dragging world Y");
        }

        /// <summary>A scaled mesh stores no scale: its vertices take it, and end up where the scale put them in the world.</summary>
        [Test]
        public async Task MeshScaleGoesIntoItsVertices()
        {
            var mesh = new MapPlacement(new Vector3(10f, 0f, 0f), new Vector3(0f, 90f, 0f), Vector3.One);
            var result = new GizmoResult
            {
                Flags = GizmoResultFlags.Scale,
                Scale = new Vector3(2f, 1f, 1f),
                Pivot = Vector3.Zero,
            };

            var (placement, vertexTransform) = GizmoTransforms.TransformMesh(mesh, result);

            await Assert.That(vertexTransform).IsNotNull();
            await AssertNear(placement.Origin, new Vector3(20f, 0f, 0f), "origin");

            var vertex = new Vector3(3f, 4f, 5f);
            var expected = Vector3.Transform(Vector3.Transform(vertex, mesh.RigidTransform), Matrix4x4.CreateScale(2f, 1f, 1f));
            var actual = Vector3.Transform(Vector3.Transform(vertex, vertexTransform!.Value), placement.RigidTransform);

            await AssertNear(actual, expected, "a vertex in the world");
        }

        /// <summary>A scale along the axes of a frame keeps the pivot where it is.</summary>
        [Test]
        public async Task ScaleAboutPointInFrameKeepsThePivot()
        {
            var frame = Matrix4x4.CreateRotationZ(0.5f) * Matrix4x4.CreateTranslation(5f, 6f, 7f);
            var pivot = new Vector3(30f, -20f, 4f);
            var scale = GizmoTransforms.ScaleAboutPointInFrame(new Vector3(2f, 3f, 4f), frame, pivot);

            await AssertNear(Vector3.Transform(pivot, scale), pivot, "pivot");
        }
    }
}
