using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Draggable world space move and rotate handles, drawn as lines on top of the scene.
    /// </summary>
    sealed class TransformGizmos(RendererContext rendererContext) : LineDebugRenderer(rendererContext, nameof(TransformGizmos))
    {
        public enum Handle
        {
            None,
            MoveX,
            MoveY,
            MoveZ,
            MoveScreen,
            RotateX,
            RotateY,
            RotateZ,
        }

        public sealed class Gizmo(string name, bool allowRotation)
        {
            public string Name { get; } = name;
            public bool AllowRotation { get; } = allowRotation;
            public bool Visible { get; set; }
            public bool HasValue { get; set; }
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.Identity;
        }

        private const float SizeInPixels = 90f;
        private const float PickDistanceInPixels = 7f;
        private const int RingSegments = 48;
        private const float RingRadius = 0.7f;

        private static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        private static readonly Color32[] AxisColors = [new(0.9f, 0.25f, 0.25f, 1f), new(0.3f, 0.85f, 0.3f, 1f), new(0.3f, 0.5f, 1f, 1f)];
        private static readonly Color32 HighlightColor = Color32.Yellow;

        private readonly List<SimpleVertex> vertices = [];

        public List<Gizmo> Gizmos { get; } = [];

        public Gizmo? HoveredGizmo { get; private set; }
        public Handle HoveredHandle { get; private set; }
        public Gizmo? ActiveGizmo { get; private set; }
        private Handle activeHandle;

        private Vector3 dragStartPosition;
        private Quaternion dragStartRotation;
        private Vector3 dragStartHit;
        private float dragStartAxisParameter;
        private Vector2 dragStartMouse;

        public bool IsDragging => ActiveGizmo != null;

        public void UpdateHover(Camera camera, Vector2 mouse)
        {
            HoveredGizmo = null;
            HoveredHandle = Handle.None;

            var bestDistance = PickDistanceInPixels;

            foreach (var gizmo in Gizmos)
            {
                if (!gizmo.Visible || !gizmo.HasValue)
                {
                    continue;
                }

                var size = GetWorldSize(camera, gizmo.Position);
                if (size <= 0f)
                {
                    continue;
                }

                // The centre square wins whenever the cursor is on it, so a move along all axes stays reachable
                if (WorldToScreen(camera, gizmo.Position) is { } center && Vector2.Distance(center, mouse) <= PickDistanceInPixels + 2f)
                {
                    HoveredGizmo = gizmo;
                    HoveredHandle = Handle.MoveScreen;
                    return;
                }

                for (var axis = 0; axis < 3; axis++)
                {
                    var distance = DistanceToSegment(camera, mouse, gizmo.Position, gizmo.Position + (Axes[axis] * size));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        HoveredGizmo = gizmo;
                        HoveredHandle = Handle.MoveX + axis;
                    }

                    if (!gizmo.AllowRotation)
                    {
                        continue;
                    }

                    distance = DistanceToRing(camera, mouse, gizmo.Position, axis, size * RingRadius);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        HoveredGizmo = gizmo;
                        HoveredHandle = Handle.RotateX + axis;
                    }
                }
            }
        }

        public bool BeginDrag(Camera camera, Vector2 mouse)
        {
            if (HoveredGizmo == null || HoveredHandle == Handle.None)
            {
                return false;
            }

            ActiveGizmo = HoveredGizmo;
            activeHandle = HoveredHandle;
            dragStartPosition = ActiveGizmo.Position;
            dragStartRotation = ActiveGizmo.Rotation;
            dragStartMouse = mouse;

            var (origin, direction) = ScreenToRay(camera, mouse);

            switch (activeHandle)
            {
                case Handle.MoveX or Handle.MoveY or Handle.MoveZ:
                    dragStartAxisParameter = ClosestAxisParameter(dragStartPosition, Axes[activeHandle - Handle.MoveX], origin, direction) ?? 0f;
                    break;
                case Handle.MoveScreen:
                    dragStartHit = IntersectPlane(dragStartPosition, camera.Forward, origin, direction) ?? dragStartPosition;
                    break;
                default:
                    dragStartHit = IntersectPlane(dragStartPosition, Axes[activeHandle - Handle.RotateX], origin, direction) ?? dragStartPosition;
                    break;
            }

            return true;
        }

        public void Drag(Camera camera, Vector2 mouse)
        {
            if (ActiveGizmo == null)
            {
                return;
            }

            var (origin, direction) = ScreenToRay(camera, mouse);

            switch (activeHandle)
            {
                case Handle.MoveX or Handle.MoveY or Handle.MoveZ:
                {
                    var axis = Axes[activeHandle - Handle.MoveX];
                    if (ClosestAxisParameter(dragStartPosition, axis, origin, direction) is { } t)
                    {
                        ActiveGizmo.Position = dragStartPosition + (axis * (t - dragStartAxisParameter));
                    }

                    break;
                }

                case Handle.MoveScreen:
                    if (IntersectPlane(dragStartPosition, camera.Forward, origin, direction) is { } hit)
                    {
                        ActiveGizmo.Position = dragStartPosition + (hit - dragStartHit);
                    }

                    break;

                default:
                {
                    var axis = Axes[activeHandle - Handle.RotateX];
                    float angle;

                    // A ring seen edge on has no usable plane, so the horizontal mouse travel turns it instead
                    if (MathF.Abs(Vector3.Dot(axis, direction)) < 0.1f
                        || IntersectPlane(dragStartPosition, axis, origin, direction) is not { } ringHit)
                    {
                        angle = (mouse.X - dragStartMouse.X) * 0.01f;
                    }
                    else
                    {
                        var from = dragStartHit - dragStartPosition;
                        var to = ringHit - dragStartPosition;
                        angle = MathF.Atan2(Vector3.Dot(axis, Vector3.Cross(from, to)), Vector3.Dot(from, to));
                    }

                    ActiveGizmo.Rotation = Quaternion.Normalize(Quaternion.Concatenate(dragStartRotation, Quaternion.CreateFromAxisAngle(axis, angle)));
                    break;
                }
            }
        }

        public void EndDrag()
        {
            ActiveGizmo = null;
            activeHandle = Handle.None;
        }

        public void Render(Camera camera)
        {
            vertices.Clear();

            foreach (var gizmo in Gizmos)
            {
                if (!gizmo.Visible || !gizmo.HasValue)
                {
                    continue;
                }

                var size = GetWorldSize(camera, gizmo.Position);
                if (size <= 0f)
                {
                    continue;
                }

                var highlighted = ActiveGizmo ?? HoveredGizmo;
                var highlightedHandle = ActiveGizmo != null ? activeHandle : HoveredHandle;

                Color32 ColorFor(Handle handle, Color32 color) => highlighted == gizmo && highlightedHandle == handle ? HighlightColor : color;

                for (var axis = 0; axis < 3; axis++)
                {
                    var direction = Axes[axis];
                    var color = ColorFor(Handle.MoveX + axis, AxisColors[axis]);
                    var tip = gizmo.Position + (direction * size);

                    AddLine(gizmo.Position, tip, color);

                    // Arrow head
                    var side = Vector3.Normalize(Vector3.Cross(direction, camera.Forward));
                    if (float.IsFinite(side.X))
                    {
                        var back = tip - (direction * size * 0.15f);
                        AddLine(tip, back + (side * size * 0.06f), color);
                        AddLine(tip, back - (side * size * 0.06f), color);
                    }

                    if (gizmo.AllowRotation)
                    {
                        AddRing(gizmo.Position, axis, size * RingRadius, ColorFor(Handle.RotateX + axis, AxisColors[axis]));

                        // The current orientation, so roll is visible
                        var localAxis = Vector3.Transform(direction, gizmo.Rotation);
                        AddLine(gizmo.Position, gizmo.Position + (localAxis * size * 0.4f), Color32.White);
                    }
                }

                // Centre square, facing the camera
                var centerColor = ColorFor(Handle.MoveScreen, Color32.White);
                var half = size * 0.06f;
                var right = camera.Right * half;
                var up = camera.Up * half;
                AddLine(gizmo.Position - right - up, gizmo.Position + right - up, centerColor);
                AddLine(gizmo.Position + right - up, gizmo.Position + right + up, centerColor);
                AddLine(gizmo.Position + right + up, gizmo.Position - right + up, centerColor);
                AddLine(gizmo.Position - right + up, gizmo.Position - right - up, centerColor);
            }

            Upload(vertices);
            RenderLines(disableDepthTest: true);
        }

        private void AddLine(Vector3 from, Vector3 to, Color32 color)
        {
            vertices.Add(new SimpleVertex(from, color));
            vertices.Add(new SimpleVertex(to, color));
        }

        private void AddRing(Vector3 center, int axis, float radius, Color32 color)
        {
            var previous = RingPoint(center, axis, radius, 0);
            for (var i = 1; i <= RingSegments; i++)
            {
                var point = RingPoint(center, axis, radius, i);
                AddLine(previous, point, color);
                previous = point;
            }
        }

        private static Vector3 RingPoint(Vector3 center, int axis, float radius, int segment)
        {
            var (sin, cos) = MathF.SinCos(segment * MathF.Tau / RingSegments);
            var u = Axes[(axis + 1) % 3];
            var v = Axes[(axis + 2) % 3];
            return center + (((u * cos) + (v * sin)) * radius);
        }

        // World units covering a fixed number of pixels at the given point, so the handles keep their screen size
        private static float GetWorldSize(Camera camera, Vector3 position)
        {
            var depth = Vector3.Dot(position - camera.Location, camera.Forward);
            if (depth <= 0f || camera.WindowSize.Y <= 0f)
            {
                return 0f;
            }

            return SizeInPixels * 2f * depth / (camera.ProjectionMatrix.M22 * camera.WindowSize.Y);
        }

        private static Vector2? WorldToScreen(Camera camera, Vector3 position)
        {
            var clip = Vector4.Transform(new Vector4(position, 1f), camera.ViewProjectionMatrix);
            if (clip.W <= 0f)
            {
                return null;
            }

            return new Vector2(
                ((clip.X / clip.W * 0.5f) + 0.5f) * camera.WindowSize.X,
                (0.5f - (clip.Y / clip.W * 0.5f)) * camera.WindowSize.Y);
        }

        private static (Vector3 Origin, Vector3 Direction) ScreenToRay(Camera camera, Vector2 mouse)
        {
            var ndcX = (2f * mouse.X / camera.WindowSize.X) - 1f;
            var ndcY = 1f - (2f * mouse.Y / camera.WindowSize.Y);

            var direction = camera.Forward
                + (camera.Right * (ndcX / camera.ProjectionMatrix.M11))
                + (camera.Up * (ndcY / camera.ProjectionMatrix.M22));

            return (camera.Location, Vector3.Normalize(direction));
        }

        // Where along the axis line the ray passes closest, or null when they are near parallel
        private static float? ClosestAxisParameter(Vector3 axisOrigin, Vector3 axis, Vector3 rayOrigin, Vector3 rayDirection)
        {
            var w = axisOrigin - rayOrigin;
            var b = Vector3.Dot(axis, rayDirection);
            var denominator = 1f - (b * b);
            if (denominator < 1e-4f)
            {
                return null;
            }

            var d = Vector3.Dot(axis, w);
            var e = Vector3.Dot(rayDirection, w);
            return ((b * e) - d) / denominator;
        }

        private static Vector3? IntersectPlane(Vector3 planePoint, Vector3 normal, Vector3 rayOrigin, Vector3 rayDirection)
        {
            var denominator = Vector3.Dot(normal, rayDirection);
            if (MathF.Abs(denominator) < 1e-5f)
            {
                return null;
            }

            var t = Vector3.Dot(normal, planePoint - rayOrigin) / denominator;
            return t < 0f ? null : rayOrigin + (rayDirection * t);
        }

        private static float DistanceToSegment(Camera camera, Vector2 mouse, Vector3 from, Vector3 to)
        {
            if (WorldToScreen(camera, from) is not { } a || WorldToScreen(camera, to) is not { } b)
            {
                return float.MaxValue;
            }

            var ab = b - a;
            var lengthSquared = ab.LengthSquared();
            var t = lengthSquared > 0f ? Math.Clamp(Vector2.Dot(mouse - a, ab) / lengthSquared, 0f, 1f) : 0f;
            return Vector2.Distance(mouse, a + (ab * t));
        }

        private static float DistanceToRing(Camera camera, Vector2 mouse, Vector3 center, int axis, float radius)
        {
            var best = float.MaxValue;
            var previous = RingPoint(center, axis, radius, 0);
            for (var i = 1; i <= RingSegments; i++)
            {
                var point = RingPoint(center, axis, radius, i);
                best = MathF.Min(best, DistanceToSegment(camera, mouse, previous, point));
                previous = point;
            }

            return best;
        }
    }
}
