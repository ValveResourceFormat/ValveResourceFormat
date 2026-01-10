using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    public class TransformGizmo
    {
        private readonly Shader shader;
        private readonly int vaoHandle;
        private readonly int vboHandle;
        private int vertexCount;
        private readonly List<SimpleVertex> vertices = new(256);

        public enum GizmoAxis
        {
            None,
            X,
            Y,
            Z
        }


        public enum GizmoMode
        {
            Translate,
            // Future: Rotate, Scale
        }

        public GizmoMode Mode { get; set; } = GizmoMode.Translate;
        public GizmoAxis HoveredAxis { get; private set; } = GizmoAxis.None;
        public GizmoAxis ActiveAxis { get; private set; } = GizmoAxis.None;
        public bool IsActive => ActiveAxis != GizmoAxis.None;

        private Vector3 gizmoPosition;
        private Vector3 dragStartPosition;
        private Vector3 dragCurrentPosition;
        private Plane dragPlane;

        private const float GizmoSize = 1.5f;
        private const float ArrowLength = 0.8f;
        private const float ArrowHeadRadius = 0.08f;
        private const float ScreenSizeScaleFactor = 0.1f;
        private const float PickingThresholdFactor = 0.1f;
        private const int ArrowHeadSegments = 6;
        private const float RayLineIntersectionEpsilon = 0.0001f;
        private const float RayPlaneIntersectionEpsilon = 0.0001f;

        private static readonly Dictionary<GizmoAxis, Vector3> AxisToDirection = new()
        {
            { GizmoAxis.X, Vector3.UnitX },
            { GizmoAxis.Y, Vector3.UnitY },
            { GizmoAxis.Z, Vector3.UnitZ }
        };

        public TransformGizmo(RendererContext rendererContext)
        {
            shader = rendererContext.ShaderLoader.LoadShader("vrf.default");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

#if DEBUG
            var vaoLabel = nameof(TransformGizmo);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        public void SetPosition(Vector3 position)
        {
            gizmoPosition = position;
        }

        public void Update(Camera camera, List<SceneNode> selectedNodes)
        {
            if (selectedNodes.Count == 0)
            {
                vertexCount = 0;
                return;
            }

            // Calculate gizmo position at the center of all selected nodes
            var center = Vector3.Zero;
            var count = 0;
            foreach (var node in selectedNodes)
            {
                center += node.BoundingBox.Center;
                count++;
            }
            gizmoPosition = center / count;

            vertices.Clear();

            var scale = CalculateGizmoScale(camera);

            // Draw three axes
            DrawAxis(camera, GizmoAxis.X, AxisToDirection[GizmoAxis.X], new Color32(1.0f, 0.2f, 0.2f, 1.0f), scale);
            DrawAxis(camera, GizmoAxis.Y, AxisToDirection[GizmoAxis.Y], new Color32(0.2f, 1.0f, 0.2f, 1.0f), scale);
            DrawAxis(camera, GizmoAxis.Z, AxisToDirection[GizmoAxis.Z], new Color32(0.2f, 0.4f, 1.0f, 1.0f), scale);

            vertexCount = vertices.Count;

            if (vertexCount > 0)
            {
                GL.NamedBufferData(vboHandle, vertexCount * SimpleVertex.SizeInBytes, ListAccessors<SimpleVertex>.GetBackingArray(vertices), BufferUsageHint.DynamicDraw);
            }
        }

        private void DrawAxis(Camera camera, GizmoAxis axis, Vector3 direction, Color32 color, float scale)
        {
            var isHovered = HoveredAxis == axis;
            var isActive = ActiveAxis == axis;
            var axisScale = 1.0f;

            var scaledDirection = direction * scale * GizmoSize * axisScale;
            var start = gizmoPosition;
            var end = gizmoPosition + scaledDirection * ArrowLength;
            var arrowTip = gizmoPosition + scaledDirection;

            // Draw shaft as a cylinder (approximated with rectangular prism)
            var perpendicular1 = Vector3.Normalize(GetPerpendicularVector(direction));
            var perpendicular2 = Vector3.Normalize(Vector3.Cross(direction, perpendicular1));

            var shaftRadius = ArrowHeadRadius * 0.3f * scale * axisScale;

            // Create shaft as quads (2 triangles each)
            for (var i = 0; i < 4; i++)
            {
                var angle1 = i * MathF.PI * 0.5f;
                var angle2 = (i + 1) * MathF.PI * 0.5f;

                var offset1 = (perpendicular1 * MathF.Cos(angle1) + perpendicular2 * MathF.Sin(angle1)) * shaftRadius;
                var offset2 = (perpendicular1 * MathF.Cos(angle2) + perpendicular2 * MathF.Sin(angle2)) * shaftRadius;

                var p1 = start + offset1;
                var p2 = start + offset2;
                var p3 = end + offset2;
                var p4 = end + offset1;

                // First triangle
                vertices.Add(new SimpleVertex { Position = p1, Color = color });
                vertices.Add(new SimpleVertex { Position = p2, Color = color });
                vertices.Add(new SimpleVertex { Position = p3, Color = color });

                // Second triangle
                vertices.Add(new SimpleVertex { Position = p1, Color = color });
                vertices.Add(new SimpleVertex { Position = p3, Color = color });
                vertices.Add(new SimpleVertex { Position = p4, Color = color });
            }

            // Draw arrow head as a cone
            var headBaseRadius = ArrowHeadRadius * scale * axisScale;
            var headBase = end;

            // Create cone triangles
            for (var i = 0; i < ArrowHeadSegments; i++)
            {
                var angle1 = i * MathF.PI * 2 / ArrowHeadSegments;
                var angle2 = (i + 1) * MathF.PI * 2 / ArrowHeadSegments;

                var point1 = headBase + (perpendicular1 * MathF.Cos(angle1) + perpendicular2 * MathF.Sin(angle1)) * headBaseRadius;
                var point2 = headBase + (perpendicular1 * MathF.Cos(angle2) + perpendicular2 * MathF.Sin(angle2)) * headBaseRadius;

                // Side triangle
                vertices.Add(new SimpleVertex { Position = point1, Color = color });
                vertices.Add(new SimpleVertex { Position = point2, Color = color });
                vertices.Add(new SimpleVertex { Position = arrowTip, Color = color });

                // Base triangle (to close the cone)
                vertices.Add(new SimpleVertex { Position = headBase, Color = color });
                vertices.Add(new SimpleVertex { Position = point2, Color = color });
                vertices.Add(new SimpleVertex { Position = point1, Color = color });
            }
        }

        private static Vector3 GetPerpendicularVector(Vector3 v)
        {
            var absX = Math.Abs(v.X);
            var absY = Math.Abs(v.Y);
            var absZ = Math.Abs(v.Z);

            if (absX < absY && absX < absZ)
            {
                return Vector3.UnitX;
            }
            else if (absY < absZ)
            {
                return Vector3.UnitY;
            }
            else
            {
                return Vector3.UnitZ;
            }
        }

        public void UpdateHover(Camera camera, int mouseX, int mouseY, Vector2 screenSize)
        {
            if (IsActive)
            {
                return; // Don't change hover state while dragging
            }

            var ray = GetMouseRay(camera, mouseX, mouseY, screenSize);
            HoveredAxis = PickAxis(ray, camera);
        }

        private GizmoAxis PickAxis(Ray ray, Camera camera)
        {
            var scale = CalculateGizmoScale(camera);

            var minDistance = float.MaxValue;
            var closestAxis = GizmoAxis.None;
            var threshold = PickingThresholdFactor * scale;

            // Check each axis
            foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                var direction = AxisToDirection[axis];

                var axisEnd = gizmoPosition + direction * scale * GizmoSize;
                var distance = DistanceFromRayToLineSegment(ray, gizmoPosition, axisEnd);

                if (distance < threshold && distance < minDistance)
                {
                    minDistance = distance;
                    closestAxis = axis;
                }
            }

            return closestAxis;
        }

        private static float DistanceFromRayToLineSegment(Ray ray, Vector3 lineStart, Vector3 lineEnd)
        {
            var lineDir = lineEnd - lineStart;
            var lineLength = lineDir.Length();
            lineDir = Vector3.Normalize(lineDir);

            // Find closest points on ray and line
            var w = ray.Origin - lineStart;
            var a = Vector3.Dot(ray.Direction, ray.Direction);
            var b = Vector3.Dot(ray.Direction, lineDir);
            var c = Vector3.Dot(lineDir, lineDir);
            var d = Vector3.Dot(ray.Direction, w);
            var e = Vector3.Dot(lineDir, w);

            var denom = a * c - b * b;
            if (Math.Abs(denom) < RayLineIntersectionEpsilon)
            {
                return float.MaxValue;
            }

            var t = (b * e - c * d) / denom;
            var s = (a * e - b * d) / denom;

            // Clamp s to line segment
            s = Math.Clamp(s, 0, lineLength);

            var pointOnRay = ray.Origin + ray.Direction * t;
            var pointOnLine = lineStart + lineDir * s;

            return Vector3.Distance(pointOnRay, pointOnLine);
        }

        public bool StartDrag(Camera camera, int mouseX, int mouseY, Vector2 screenSize)
        {
            if (HoveredAxis == GizmoAxis.None)
            {
                return false;
            }

            ActiveAxis = HoveredAxis;
            var ray = GetMouseRay(camera, mouseX, mouseY, screenSize);

            // Create a plane perpendicular to the camera view but containing the axis direction
            var axisDirection = GetAxisDirection(ActiveAxis);

            // Use a plane that faces the camera but allows movement along the axis
            var planeNormal = Vector3.Cross(axisDirection, camera.Right);
            if (planeNormal.LengthSquared() < RayLineIntersectionEpsilon)
            {
                // If axis is parallel to camera right, use camera up instead
                planeNormal = Vector3.Cross(axisDirection, camera.Up);
            }
            planeNormal = Vector3.Normalize(planeNormal);

            dragPlane = new Plane(planeNormal, gizmoPosition);

            if (RayPlaneIntersection(ray, dragPlane, out var hitPoint))
            {
                dragStartPosition = hitPoint;
                dragCurrentPosition = hitPoint;
                return true;
            }

            ActiveAxis = GizmoAxis.None;
            return false;
        }

        public Vector3 UpdateDrag(Camera camera, int mouseX, int mouseY, Vector2 screenSize)
        {
            if (!IsActive)
            {
                return Vector3.Zero;
            }

            var ray = GetMouseRay(camera, mouseX, mouseY, screenSize);

            if (RayPlaneIntersection(ray, dragPlane, out var hitPoint))
            {
                var axisDirection = GetAxisDirection(ActiveAxis);
                var totalDelta = hitPoint - dragStartPosition;

                // Project delta onto axis direction
                var projectedDelta = Vector3.Dot(totalDelta, axisDirection) * axisDirection;

                var frameDelta = projectedDelta - (dragCurrentPosition - dragStartPosition);
                dragCurrentPosition = dragStartPosition + projectedDelta;

                return frameDelta;
            }

            return Vector3.Zero;
        }

        public void EndDrag()
        {
            ActiveAxis = GizmoAxis.None;
        }

        private static Vector3 GetAxisDirection(GizmoAxis axis)
        {
            return AxisToDirection.TryGetValue(axis, out var direction) ? direction : Vector3.Zero;
        }

        private float CalculateGizmoScale(Camera camera)
        {
            // Calculate scale based on distance from camera to keep gizmo size constant on screen
            var distanceFromCamera = Vector3.Distance(camera.Location, gizmoPosition);
            return distanceFromCamera * ScreenSizeScaleFactor;
        }

        private static Ray GetMouseRay(Camera camera, int mouseX, int mouseY, Vector2 screenSize)
        {
            // Convert mouse position to normalized device coordinates
            var x = (2.0f * mouseX) / screenSize.X - 1.0f;
            var y = 1.0f - (2.0f * mouseY) / screenSize.Y;

            // Unproject near and far points (matching grid.vert.slang unprojection)
            Matrix4x4.Invert(camera.CameraViewMatrix, out var viewInv);
            Matrix4x4.Invert(camera.ViewProjectionMatrix, out var viewProjInv);

            // Near plane point (z = 0 in reverse-Z, becomes 1.0 after 1.0 - z)
            var nearClip = new Vector4(x, y, 1.0f, 1.0f); // 1.0 - 0.0
            var nearPoint = Vector4.Transform(nearClip, viewProjInv);
            nearPoint /= nearPoint.W;

            // Far plane point (z = 0.99 in reverse-Z, becomes 0.01 after 1.0 - z)
            var farClip = new Vector4(x, y, 0.01f, 1.0f); // 1.0 - 0.99
            var farPoint = Vector4.Transform(farClip, viewProjInv);
            farPoint /= farPoint.W;

            // Calculate ray direction from camera to far point
            var direction = Vector3.Normalize(new Vector3(
                farPoint.X - nearPoint.X,
                farPoint.Y - nearPoint.Y,
                farPoint.Z - nearPoint.Z
            ));

            return new Ray(new Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z), direction);
        }

        private static bool RayPlaneIntersection(Ray ray, Plane plane, out Vector3 hitPoint)
        {
            var denom = Vector3.Dot(plane.Normal, ray.Direction);

            if (Math.Abs(denom) > RayPlaneIntersectionEpsilon)
            {
                var t = Vector3.Dot(plane.Normal, plane.Point - ray.Origin) / denom;

                if (t >= 0)
                {
                    hitPoint = ray.Origin + ray.Direction * t;
                    return true;
                }
            }

            hitPoint = Vector3.Zero;
            return false;
        }

        public void Render()
        {
            if (vertexCount == 0)
            {
                return;
            }

            GL.Enable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Use();
            shader.SetUniform3x4("transform", Matrix4x4.Identity);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);
            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
        }

        public readonly struct Ray
        {
            public Vector3 Origin { get; init; }
            public Vector3 Direction { get; init; }

            public Ray(Vector3 origin, Vector3 direction)
            {
                Origin = origin;
                Direction = direction;
            }
        }

        public readonly struct Plane
        {
            public Vector3 Normal { get; init; }
            public Vector3 Point { get; init; }

            public Plane(Vector3 normal, Vector3 point)
            {
                Normal = normal;
                Point = point;
            }
        }
    }
}
