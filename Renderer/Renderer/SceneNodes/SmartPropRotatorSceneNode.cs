using ValveResourceFormat.ResourceTypes.SmartProps;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Renders a SmartProp rotator as a filled ring band with an angle needle and solid tab.
    /// </summary>
    public class SmartPropRotatorSceneNode : SmartPropSolidWidgetSceneNode
    {
        private const int RingSegments = 64;
        private static readonly Color32 Yellow = new(0.95f, 0.90f, 0.10f, 1f);
        private static readonly Color32 DefaultRingColor = new(0.72f, 0.74f, 0.48f, 0.88f);

        /// <inheritdoc/>
        public override bool IsTranslucent => true;

        /// <inheritdoc/>
        protected override bool Shaded => false;

        /// <inheritdoc/>
        protected override bool DepthTested => false;

        /// <summary>Initializes the node for an evaluated rotator widget.</summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="widget">The evaluated rotator widget.</param>
        public SmartPropRotatorSceneNode(Scene scene, in SmartPropRotatorWidget widget)
            : this(scene, widget, BuildSolidGeometry(widget))
        {
        }

        private SmartPropRotatorSceneNode(
            Scene scene,
            in SmartPropRotatorWidget widget,
            (List<SimpleVertexNormal> Vertices, List<int> Indices) geometry)
            : base(
                scene,
                geometry.Vertices,
                geometry.Indices,
                BuildNeedleGeometry(widget),
                widget.Position + (NormalizeOr(widget.Axis, Vector3.UnitZ) * widget.Radius * 0.35f),
                widget.Name,
                GetRingColor(widget.Color))
        {
        }

        /// <summary>Builds the filled ring band and marker tab in world space.</summary>
        /// <param name="widget">The evaluated rotator widget.</param>
        /// <returns>Solid vertices and triangle indices.</returns>
        public static (List<SimpleVertexNormal> Vertices, List<int> Indices) BuildSolidGeometry(in SmartPropRotatorWidget widget)
        {
            List<SimpleVertexNormal> vertices = [];
            List<int> indices = [];
            var axis = NormalizeOr(widget.Axis, Vector3.UnitZ);
            var (axisU, axisV) = PlaneBasis(axis);
            var color = GetRingColor(widget.Color);
            var radius = MathF.Max(0f, widget.Radius);
            var innerRadius = radius * 0.90f;
            var halfHeight = radius * 0.02f;

            for (var i = 0; i < RingSegments; i++)
            {
                var angle1 = i * MathF.Tau / RingSegments;
                var angle2 = (i + 1) * MathF.Tau / RingSegments;
                var direction1 = (axisU * MathF.Cos(angle1)) + (axisV * MathF.Sin(angle1));
                var direction2 = (axisU * MathF.Cos(angle2)) + (axisV * MathF.Sin(angle2));

                var innerTop1 = widget.Position + (direction1 * innerRadius) + (axis * halfHeight);
                var innerTop2 = widget.Position + (direction2 * innerRadius) + (axis * halfHeight);
                var outerTop1 = widget.Position + (direction1 * radius) + (axis * halfHeight);
                var outerTop2 = widget.Position + (direction2 * radius) + (axis * halfHeight);
                var innerBottom1 = widget.Position + (direction1 * innerRadius) - (axis * halfHeight);
                var innerBottom2 = widget.Position + (direction2 * innerRadius) - (axis * halfHeight);
                var outerBottom1 = widget.Position + (direction1 * radius) - (axis * halfHeight);
                var outerBottom2 = widget.Position + (direction2 * radius) - (axis * halfHeight);

                AddSolidQuad(vertices, indices, innerTop1, outerTop1, outerTop2, innerTop2, color);
                AddSolidQuad(vertices, indices, innerBottom1, innerBottom2, outerBottom2, outerBottom1, color);
                AddSolidQuad(vertices, indices, outerTop1, outerBottom1, outerBottom2, outerTop2, color);
                AddSolidQuad(vertices, indices, innerTop1, innerTop2, innerBottom2, innerBottom1, color);
            }

            AddMarkerTab(vertices, indices, widget, axis, axisU, axisV, radius);
            return (vertices, indices);
        }

        /// <summary>Builds a line representation of the ring, needle, and tab.</summary>
        /// <param name="widget">The evaluated rotator widget.</param>
        /// <returns>Line vertices, two per segment.</returns>
        public static List<SimpleVertex> BuildGeometry(in SmartPropRotatorWidget widget)
        {
            var geometry = BuildSolidGeometry(widget);
            var lines = BuildTriangleEdges(geometry.Vertices, geometry.Indices);
            lines.AddRange(BuildNeedleGeometry(widget));
            return lines;
        }

        private static List<SimpleVertex> BuildNeedleGeometry(in SmartPropRotatorWidget widget)
        {
            List<SimpleVertex> vertices = [];
            var axis = NormalizeOr(widget.Axis, Vector3.UnitZ);
            var (axisU, axisV) = PlaneBasis(axis);
            var angle = float.DegreesToRadians(widget.Angle);
            var direction = (axisU * MathF.Cos(angle)) + (axisV * MathF.Sin(angle));
            AddGuideLine(vertices, widget.Position, widget.Position + (direction * widget.Radius), Yellow);
            return vertices;
        }

        private static void AddMarkerTab(
            List<SimpleVertexNormal> vertices,
            List<int> indices,
            in SmartPropRotatorWidget widget,
            Vector3 axis,
            Vector3 axisU,
            Vector3 axisV,
            float radius)
        {
            var angle = float.DegreesToRadians(widget.Angle);
            var direction = (axisU * MathF.Cos(angle)) + (axisV * MathF.Sin(angle));
            var tangent = NormalizeOr(Vector3.Cross(axis, direction), axisV);
            var center = widget.Position + (direction * radius * 0.95f);
            var halfWidth = radius * 0.045f;
            var halfHeight = radius * 0.035f;
            var halfDepth = radius * 0.03f;

            Span<Vector3> corners =
            [
                center - (direction * halfWidth) - (tangent * halfHeight) - (axis * halfDepth),
                center + (direction * halfWidth) - (tangent * halfHeight) - (axis * halfDepth),
                center + (direction * halfWidth) + (tangent * halfHeight) - (axis * halfDepth),
                center - (direction * halfWidth) + (tangent * halfHeight) - (axis * halfDepth),
                center - (direction * halfWidth) - (tangent * halfHeight) + (axis * halfDepth),
                center + (direction * halfWidth) - (tangent * halfHeight) + (axis * halfDepth),
                center + (direction * halfWidth) + (tangent * halfHeight) + (axis * halfDepth),
                center - (direction * halfWidth) + (tangent * halfHeight) + (axis * halfDepth),
            ];

            var tabColor = new Color32(0.95f, 0.90f, 0.10f, 0.95f);
            AddSolidQuad(vertices, indices, corners[0], corners[3], corners[2], corners[1], tabColor);
            AddSolidQuad(vertices, indices, corners[4], corners[5], corners[6], corners[7], tabColor);
            AddSolidQuad(vertices, indices, corners[0], corners[1], corners[5], corners[4], tabColor);
            AddSolidQuad(vertices, indices, corners[3], corners[7], corners[6], corners[2], tabColor);
            AddSolidQuad(vertices, indices, corners[0], corners[4], corners[7], corners[3], tabColor);
            AddSolidQuad(vertices, indices, corners[1], corners[2], corners[6], corners[5], tabColor);
        }

        private static (Vector3 U, Vector3 V) PlaneBasis(Vector3 axis)
        {
            var reference = MathF.Abs(Vector3.Dot(axis, Vector3.UnitX)) < 0.99f
                ? Vector3.UnitX
                : Vector3.UnitY;
            var u = Vector3.Normalize(reference - (axis * Vector3.Dot(reference, axis)));
            return (u, Vector3.Normalize(Vector3.Cross(axis, u)));
        }

        private static Color32 GetRingColor(Vector3 color)
            => color == Vector3.Zero
                ? DefaultRingColor
                : new Color32(color.X, color.Y, color.Z, 0.88f);
    }
}
