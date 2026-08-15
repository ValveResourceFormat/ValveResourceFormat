using ValveResourceFormat.ResourceTypes.SmartProps;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Renders a solid, faceted SmartProp locator with colored arms on all six axis directions.
    /// </summary>
    public class SmartPropLocatorSceneNode : SmartPropSolidWidgetSceneNode
    {
        private const float ArmLength = 8f;
        private static readonly Color32 Red = new(0.85f, 0.12f, 0.12f, 1f);
        private static readonly Color32 Green = new(0f, 0.85f, 0f, 1f);
        private static readonly Color32 Blue = new(0f, 0.16f, 0.78f, 1f);

        /// <inheritdoc/>
        public override bool IsTranslucent => false;

        /// <summary>Initializes the node for an evaluated locator widget.</summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="widget">The evaluated locator widget.</param>
        public SmartPropLocatorSceneNode(Scene scene, in SmartPropLocatorWidget widget)
            : this(scene, widget, BuildSolidGeometry(widget))
        {
        }

        private SmartPropLocatorSceneNode(
            Scene scene,
            in SmartPropLocatorWidget widget,
            (List<SimpleVertexNormal> Vertices, List<int> Indices) geometry)
            : base(
                scene,
                geometry.Vertices,
                geometry.Indices,
                labelPosition: widget.Position + (BasisRows(widget.WorldMatrix).Up * ArmLength * widget.DisplayScale * 1.3f),
                labelText: widget.Name)
        {
        }

        /// <summary>Builds the filled locator mesh in world space.</summary>
        /// <param name="widget">The evaluated locator widget.</param>
        /// <returns>Solid vertices and triangle indices.</returns>
        public static (List<SimpleVertexNormal> Vertices, List<int> Indices) BuildSolidGeometry(in SmartPropLocatorWidget widget)
        {
            List<SimpleVertexNormal> vertices = [];
            List<int> indices = [];
            var (forward, left, up) = BasisRows(widget.WorldMatrix);
            var scale = ArmLength * MathF.Max(0f, widget.DisplayScale);

            AddFacetedAxisArm(vertices, indices, widget.Position, forward, left, up, scale, Red);
            AddFacetedAxisArm(vertices, indices, widget.Position, left, up, forward, scale, Green);
            AddFacetedAxisArm(vertices, indices, widget.Position, up, forward, left, scale, Blue);

            return (vertices, indices);
        }

        /// <summary>Builds a line representation of the locator mesh.</summary>
        /// <param name="widget">The evaluated locator widget.</param>
        /// <returns>Line vertices, two per triangle edge.</returns>
        public static List<SimpleVertex> BuildGeometry(in SmartPropLocatorWidget widget)
        {
            var geometry = BuildSolidGeometry(widget);
            return BuildTriangleEdges(geometry.Vertices, geometry.Indices);
        }

        private static void AddFacetedAxisArm(
            List<SimpleVertexNormal> vertices,
            List<int> indices,
            Vector3 origin,
            Vector3 direction,
            Vector3 axisU,
            Vector3 axisV,
            float scale,
            Color32 color)
        {
            var shoulder = 0.60f * scale;
            var positiveRadius = 0.22f * scale;
            var negativeLength = 0.70f * scale;
            var negativeRadius = 0.20f * scale;

            var s0 = origin + (direction * shoulder) + (axisU * positiveRadius);
            var s1 = origin + (direction * shoulder) + (axisV * positiveRadius);
            var s2 = origin + (direction * shoulder) - (axisU * positiveRadius);
            var s3 = origin + (direction * shoulder) - (axisV * positiveRadius);
            var tip = origin + (direction * scale);

            AddSolidTriangle(vertices, indices, origin, s1, s0, color);
            AddSolidTriangle(vertices, indices, origin, s2, s1, color);
            AddSolidTriangle(vertices, indices, origin, s3, s2, color);
            AddSolidTriangle(vertices, indices, origin, s0, s3, color);
            AddSolidTriangle(vertices, indices, tip, s0, s1, color);
            AddSolidTriangle(vertices, indices, tip, s1, s2, color);
            AddSolidTriangle(vertices, indices, tip, s2, s3, color);
            AddSolidTriangle(vertices, indices, tip, s3, s0, color);

            var e0 = origin - (direction * negativeLength) + (axisU * negativeRadius);
            var e1 = origin - (direction * negativeLength) + (axisV * negativeRadius);
            var e2 = origin - (direction * negativeLength) - (axisU * negativeRadius);
            var e3 = origin - (direction * negativeLength) - (axisV * negativeRadius);

            AddSolidTriangle(vertices, indices, origin, e0, e1, color);
            AddSolidTriangle(vertices, indices, origin, e1, e2, color);
            AddSolidTriangle(vertices, indices, origin, e2, e3, color);
            AddSolidTriangle(vertices, indices, origin, e3, e0, color);
            AddSolidTriangle(vertices, indices, e0, e2, e1, color);
            AddSolidTriangle(vertices, indices, e0, e3, e2, color);
        }
    }
}
