using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Draggable world space move and rotate handles, drawn as lines on top of the scene.
    /// </summary>
    sealed class TransformGizmos(RendererContext rendererContext) : LineDebugRenderer(rendererContext, nameof(TransformGizmos))
    {
        public sealed class Gizmo(string name, bool allowRotation)
        {
            public string Name { get; } = name;
            public bool AllowRotation { get; } = allowRotation;
            public bool Visible { get; set; }
            public bool HasValue { get; set; }
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.Identity;
        }

        // Handles 0 to 2 move along an axis, 3 moves in the screen plane, and 4 to 6 rotate around an axis
        private const int NoHandle = -1;
        private const int MoveScreen = 3;
        private const int Rotate = 4;

        private const float SizeInPixels = 90f;
        private const float PickDistanceInPixels = 7f;
        private const int RingSegments = 48;
        private const float RingRadius = 0.7f;

        private static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        private static readonly Color32[] AxisColors = [new(0.9f, 0.25f, 0.25f, 1f), new(0.3f, 0.85f, 0.3f, 1f), new(0.3f, 0.5f, 1f, 1f)];

        private readonly List<SimpleVertex> vertices = [];

        public List<Gizmo> Gizmos { get; } = [];
        public Gizmo? HoveredGizmo { get; private set; }
        public Gizmo? ActiveGizmo { get; private set; }

        private int hoveredHandle = NoHandle;
        private int activeHandle = NoHandle;
        private (Vector3 Position, Quaternion Rotation, Vector3 Hit, Vector2 Mouse) dragStart;

        public void UpdateHover(Camera camera, Vector2 mouse)
        {
            HoveredGizmo = null;
            hoveredHandle = NoHandle;

            var bestDistance = PickDistanceInPixels;

            foreach (var gizmo in Gizmos)
            {
                foreach (var (handle, from, to) in Lines(camera, gizmo))
                {
                    // The centre square gets a head start, so a move along all axes stays reachable where they meet
                    var distance = DistanceToLine(camera, mouse, from, to) - (handle == MoveScreen ? PickDistanceInPixels : 0f);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        HoveredGizmo = gizmo;
                        hoveredHandle = handle;
                    }
                }
            }
        }

        public bool BeginDrag(Camera camera, Vector2 mouse)
        {
            if (HoveredGizmo == null)
            {
                return false;
            }

            ActiveGizmo = HoveredGizmo;
            activeHandle = hoveredHandle;
            dragStart = (ActiveGizmo.Position, ActiveGizmo.Rotation, ActiveGizmo.Position, mouse);
            dragStart.Hit = DragPoint(camera, mouse) ?? ActiveGizmo.Position;
            return true;
        }

        public void Drag(Camera camera, Vector2 mouse)
        {
            if (ActiveGizmo == null)
            {
                return;
            }

            var point = DragPoint(camera, mouse);

            if (activeHandle <= MoveScreen)
            {
                if (point is { } hit)
                {
                    ActiveGizmo.Position = dragStart.Position + (hit - dragStart.Hit);
                }

                return;
            }

            var axis = Axes[activeHandle - Rotate];

            // A ring seen edge on has no usable plane, so the horizontal mouse travel turns it instead
            var angle = (mouse.X - dragStart.Mouse.X) * 0.01f;

            if (point is { } ringHit && MathF.Abs(Vector3.Dot(axis, ScreenToRay(camera, mouse))) >= 0.1f)
            {
                var from = dragStart.Hit - dragStart.Position;
                var to = ringHit - dragStart.Position;
                angle = MathF.Atan2(Vector3.Dot(axis, Vector3.Cross(from, to)), Vector3.Dot(from, to));
            }

            ActiveGizmo.Rotation = Quaternion.Normalize(Quaternion.Concatenate(dragStart.Rotation, Quaternion.CreateFromAxisAngle(axis, angle)));
        }

        public void EndDrag()
        {
            ActiveGizmo = null;
            activeHandle = NoHandle;
        }

        public void Render(Camera camera)
        {
            vertices.Clear();

            var (highlighted, highlightedHandle) = ActiveGizmo != null ? (ActiveGizmo, activeHandle) : (HoveredGizmo, hoveredHandle);

            foreach (var gizmo in Gizmos)
            {
                foreach (var (handle, from, to) in Lines(camera, gizmo))
                {
                    var color = gizmo == highlighted && handle == highlightedHandle
                        ? Color32.Yellow
                        : handle == MoveScreen ? Color32.White : AxisColors[handle % Rotate];

                    vertices.Add(new SimpleVertex(from, color));
                    vertices.Add(new SimpleVertex(to, color));
                }
            }

            Upload(vertices);
            RenderLines(disableDepthTest: true);
        }

        // The lines a gizmo is drawn with and picked by, each with the handle it belongs to
        private static IEnumerable<(int Handle, Vector3 From, Vector3 To)> Lines(Camera camera, Gizmo gizmo)
        {
            var center = gizmo.Position;
            var depth = Vector3.Dot(center - camera.Location, camera.Forward);

            if (!gizmo.Visible || !gizmo.HasValue || depth <= 0f || camera.WindowSize.Y <= 0f)
            {
                yield break;
            }

            // World units covering a fixed number of pixels here, so the handles keep their screen size
            var size = SizeInPixels * 2f * depth / (camera.ProjectionMatrix.M22 * camera.WindowSize.Y);

            for (var axis = 0; axis < 3; axis++)
            {
                yield return (axis, center, center + (Axes[axis] * size));

                if (!gizmo.AllowRotation)
                {
                    continue;
                }

                var u = Axes[(axis + 1) % 3] * size * RingRadius;
                var v = Axes[(axis + 2) % 3] * size * RingRadius;
                var previous = center + u;

                for (var i = 1; i <= RingSegments; i++)
                {
                    var (sin, cos) = MathF.SinCos(i * MathF.Tau / RingSegments);
                    var point = center + (u * cos) + (v * sin);
                    yield return (Rotate + axis, previous, point);
                    previous = point;
                }
            }

            var right = camera.Right * size * 0.06f;
            var up = camera.Up * size * 0.06f;
            Vector3[] corners = [center - right - up, center + right - up, center + right + up, center - right + up];

            for (var i = 0; i < corners.Length; i++)
            {
                yield return (MoveScreen, corners[i], corners[(i + 1) % corners.Length]);
            }
        }

        // Where the cursor is on what the active handle moves in: its axis, the screen plane or the plane of its ring
        private Vector3? DragPoint(Camera camera, Vector2 mouse)
        {
            var origin = camera.Location;
            var direction = ScreenToRay(camera, mouse);
            var start = dragStart.Position;

            if (activeHandle < MoveScreen)
            {
                var axis = Axes[activeHandle];
                var along = Vector3.Dot(axis, direction);
                var denominator = 1f - (along * along);

                if (denominator < 1e-4f)
                {
                    return null;
                }

                var toStart = start - origin;
                return start + (axis * (((along * Vector3.Dot(direction, toStart)) - Vector3.Dot(axis, toStart)) / denominator));
            }

            var normal = activeHandle == MoveScreen ? camera.Forward : Axes[activeHandle - Rotate];
            var facing = Vector3.Dot(normal, direction);
            var distance = MathF.Abs(facing) < 1e-5f ? -1f : Vector3.Dot(normal, start - origin) / facing;
            return distance < 0f ? null : origin + (direction * distance);
        }

        private static Vector3 ScreenToRay(Camera camera, Vector2 mouse)
        {
            var ndcX = (2f * mouse.X / camera.WindowSize.X) - 1f;
            var ndcY = 1f - (2f * mouse.Y / camera.WindowSize.Y);

            return Vector3.Normalize(camera.Forward
                + (camera.Right * (ndcX / camera.ProjectionMatrix.M11))
                + (camera.Up * (ndcY / camera.ProjectionMatrix.M22)));
        }

        private static float DistanceToLine(Camera camera, Vector2 mouse, Vector3 from, Vector3 to)
        {
            var a = Vector4.Transform(new Vector4(from, 1f), camera.ViewProjectionMatrix);
            var b = Vector4.Transform(new Vector4(to, 1f), camera.ViewProjectionMatrix);

            if (a.W <= 0f || b.W <= 0f)
            {
                return float.MaxValue;
            }

            var scale = camera.WindowSize * new Vector2(0.5f, -0.5f);
            var start = (new Vector2(a.X, a.Y) / a.W * scale) + (camera.WindowSize * 0.5f);
            var along = (new Vector2(b.X, b.Y) / b.W * scale) + (camera.WindowSize * 0.5f) - start;
            var lengthSquared = along.LengthSquared();
            var t = lengthSquared > 0f ? Math.Clamp(Vector2.Dot(mouse - start, along) / lengthSquared, 0f, 1f) : 0f;
            return Vector2.Distance(mouse, start + (along * t));
        }
    }
}
