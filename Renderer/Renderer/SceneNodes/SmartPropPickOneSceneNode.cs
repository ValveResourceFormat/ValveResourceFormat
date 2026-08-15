using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Renders a camera-facing SmartProp PickOne handle with adaptive screen-space scale.
    /// </summary>
    public class SmartPropPickOneSceneNode : SmartPropSolidWidgetSceneNode
    {
        private const int CircleSegments = 48;
        private const float MinimumScreenScale = 1.6f;
        private const float DistanceScale = 0.013f;
        private const float DisplayScaleMultiplier = 1.5f;
        private static readonly Color32 DefaultFillColor = new(0.6f, 0.6f, 0.6f, 0.55f);
        private static readonly Color32 DefaultOutlineColor = new(0.6f, 0.6f, 0.6f, 1f);

        private readonly Vector3 position;
        private readonly float size;
        private Vector3 previousCameraLocation;
        private Vector3 previousCameraRight;
        private Vector3 previousCameraUp;

        /// <inheritdoc/>
        public override bool IsTranslucent => true;

        /// <inheritdoc/>
        protected override bool Shaded => false;

        /// <inheritdoc/>
        protected override bool DepthTested => false;

        /// <summary>Initializes the node for an evaluated PickOne handle.</summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="widget">The evaluated PickOne handle widget.</param>
        public SmartPropPickOneSceneNode(Scene scene, in SmartPropPickOneHandleWidget widget)
            : this(scene, widget, BuildSolidGeometry(widget))
        {
        }

        private SmartPropPickOneSceneNode(
            Scene scene,
            in SmartPropPickOneHandleWidget widget,
            (List<SimpleVertexNormal> Vertices, List<int> Indices) geometry)
            : base(
                scene,
                geometry.Vertices,
                geometry.Indices,
                BuildLocalOutlineGeometry(widget),
                widget.Position + new Vector3(0f, 0f, MathF.Max(1f, widget.Size)),
                widget.Name,
                GetOutlineColor(widget.Color))
        {
            position = widget.Position;
            size = NormalizeSize(widget.Size);
            var (forward, left, _) = BasisRows(widget.WorldMatrix);
            Transform = CreateBillboardTransform(position, forward, left, CalculateScreenScale(0f, size));
        }

        /// <summary>Builds the inset solid fill in local billboard space.</summary>
        /// <param name="widget">The evaluated PickOne handle widget.</param>
        /// <returns>Solid vertices and triangle indices.</returns>
        public static (List<SimpleVertexNormal> Vertices, List<int> Indices) BuildSolidGeometry(in SmartPropPickOneHandleWidget widget)
        {
            List<SimpleVertexNormal> vertices = [];
            List<int> indices = [];
            var color = GetFillColor(widget.Color);

            if (widget.Shape.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase))
            {
                vertices.Add(new(Vector3.Zero, color, Vector3.UnitZ));
                for (var i = 0; i <= CircleSegments; i++)
                {
                    var angle = i * MathF.Tau / CircleSegments;
                    vertices.Add(new(new Vector3(MathF.Cos(angle) * 0.30f, MathF.Sin(angle) * 0.30f, 0f), color, Vector3.UnitZ));
                }

                for (var i = 0; i < CircleSegments; i++)
                {
                    AddTriangle(indices, 0, 0, i + 1, i + 2);
                }

                return (vertices, indices);
            }

            if (widget.Shape.Equals("DIAMOND", StringComparison.OrdinalIgnoreCase))
            {
                AddSolidQuad(
                    vertices,
                    indices,
                    new(0f, 0.55f, 0f),
                    new(-0.28f, 0f, 0f),
                    new(0f, -0.55f, 0f),
                    new(0.28f, 0f, 0f),
                    color);
            }
            else
            {
                AddSolidQuad(
                    vertices,
                    indices,
                    new(-0.26f, -0.26f, 0f),
                    new(0.26f, -0.26f, 0f),
                    new(0.26f, 0.26f, 0f),
                    new(-0.26f, 0.26f, 0f),
                    color);
            }

            return (vertices, indices);
        }

        /// <summary>Builds the marker outline in a supplied world-space view plane.</summary>
        /// <param name="widget">The evaluated PickOne handle widget.</param>
        /// <param name="right">The horizontal view-plane axis.</param>
        /// <param name="up">The vertical view-plane axis.</param>
        /// <returns>Line vertices, two per segment.</returns>
        public static List<SimpleVertex> BuildGeometry(in SmartPropPickOneHandleWidget widget, Vector3 right, Vector3 up)
        {
            right = NormalizeOr(right, Vector3.UnitX);
            up = NormalizeOr(up, Vector3.UnitY);
            var localLines = BuildLocalOutlineGeometry(widget);
            var scale = NormalizeSize(widget.Size) / 8f;
            List<SimpleVertex> vertices = new(localLines.Count);
            foreach (ref readonly var vertex in CollectionsMarshal.AsSpan(localLines))
            {
                var worldPosition = widget.Position
                    + (right * vertex.Position.X * scale)
                    + (up * vertex.Position.Y * scale);
                vertices.Add(new(worldPosition, vertex.Color));
            }

            return vertices;
        }

        /// <summary>Builds the marker outline in the widget's initial plane.</summary>
        /// <param name="widget">The evaluated PickOne handle widget.</param>
        /// <returns>Line vertices, two per segment.</returns>
        public static List<SimpleVertex> BuildGeometry(in SmartPropPickOneHandleWidget widget)
        {
            var (forward, left, _) = BasisRows(widget.WorldMatrix);
            return BuildGeometry(widget, forward, left);
        }

        /// <inheritdoc/>
        public override void UpdateBuffers(Camera camera)
        {
            if (camera.Location == previousCameraLocation
                && camera.Right == previousCameraRight
                && camera.Up == previousCameraUp)
            {
                return;
            }

            previousCameraLocation = camera.Location;
            previousCameraRight = camera.Right;
            previousCameraUp = camera.Up;
            var screenScale = CalculateScreenScale(Vector3.Distance(camera.Location, position), size);
            Transform = CreateBillboardTransform(position, camera.Right, camera.Up, screenScale);
        }

        /// <summary>Calculates the camera-distance-adaptive scale for a PickOne billboard.</summary>
        /// <param name="distance">Distance from the camera to the handle.</param>
        /// <param name="size">Configured handle size.</param>
        /// <returns>The uniform billboard scale.</returns>
        public static float CalculateScreenScale(float distance, float size)
        {
            distance = float.IsFinite(distance) ? MathF.Max(0f, distance) : 0f;
            return MathF.Max(distance * DistanceScale, MinimumScreenScale)
                * (NormalizeSize(size) / 8f)
                * DisplayScaleMultiplier;
        }

        private static List<SimpleVertex> BuildLocalOutlineGeometry(in SmartPropPickOneHandleWidget widget)
        {
            List<SimpleVertex> vertices = [];
            var color = GetOutlineColor(widget.Color);

            if (widget.Shape.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase))
            {
                var previous = new Vector3(0.5f, 0f, 0f);
                for (var i = 1; i <= CircleSegments; i++)
                {
                    var angle = i * MathF.Tau / CircleSegments;
                    var point = new Vector3(MathF.Cos(angle) * 0.5f, MathF.Sin(angle) * 0.5f, 0f);
                    AddGuideLine(vertices, previous, point, color);
                    previous = point;
                }

                return vertices;
            }

            if (widget.Shape.Equals("DIAMOND", StringComparison.OrdinalIgnoreCase))
            {
                AddLoop(
                    vertices,
                    [new(0f, 0.55f, 0f), new(0.28f, 0f, 0f), new(0f, -0.55f, 0f), new(-0.28f, 0f, 0f)],
                    color);
            }
            else
            {
                AddLoop(
                    vertices,
                    [new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f)],
                    color);
            }

            return vertices;
        }

        private static void AddLoop(List<SimpleVertex> vertices, ReadOnlySpan<Vector3> points, Color32 color)
        {
            for (var i = 0; i < points.Length; i++)
            {
                AddGuideLine(vertices, points[i], points[(i + 1) % points.Length], color);
            }
        }

        private static Matrix4x4 CreateBillboardTransform(Vector3 position, Vector3 right, Vector3 up, float scale)
        {
            right = NormalizeOr(right, Vector3.UnitX) * scale;
            up = NormalizeOr(up, Vector3.UnitZ) * scale;
            var normal = NormalizeOr(Vector3.Cross(right, up), Vector3.UnitY) * scale;

            return new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                normal.X, normal.Y, normal.Z, 0f,
                position.X, position.Y, position.Z, 1f);
        }

        private static Color32 GetFillColor(Vector3 color)
            => color == Vector3.Zero
                ? DefaultFillColor
                : new Color32(color.X, color.Y, color.Z, 0.55f);

        private static Color32 GetOutlineColor(Vector3 color)
            => color == Vector3.Zero
                ? DefaultOutlineColor
                : new Color32(color.X, color.Y, color.Z, 1f);

        private static float NormalizeSize(float value)
            => float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }
}
