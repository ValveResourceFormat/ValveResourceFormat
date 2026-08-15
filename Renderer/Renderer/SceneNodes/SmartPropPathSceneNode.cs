using ValveResourceFormat.ResourceTypes.SmartProps;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Renders a smart prop path curve: a smooth line strip through the sampled spline
    /// plus control point cross markers, with the control point indices billboarded.
    /// </summary>
    public class SmartPropPathSceneNode : SmartPropWidgetSceneNode
    {
        private static readonly Color32 CurveColor = new(0.3f, 1f, 0.9f, 1f);
        private static readonly Color32 ControlPointColor = new(1f, 0.9f, 0.3f, 1f);

        private readonly Vector3[] controlPoints;
        private readonly float markerSize;

        /// <summary>
        /// Initializes the node for the given path geometry.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="pathInfo">The evaluated path curve and control points.</param>
        public SmartPropPathSceneNode(Scene scene, in SmartPropPathInfo pathInfo)
            : this(scene, pathInfo, BuildGeometry(pathInfo))
        {
        }

        private SmartPropPathSceneNode(Scene scene, in SmartPropPathInfo pathInfo, List<SimpleVertex> vertices)
            : base(scene, vertices)
        {
            controlPoints = pathInfo.ControlPoints;
            markerSize = EstimateMarkerSize(pathInfo);
        }

        /// <summary>
        /// Builds the curve line strip and control point markers in world space.
        /// </summary>
        /// <param name="pathInfo">The evaluated path curve and control points.</param>
        /// <returns>Line vertices, two per segment.</returns>
        public static List<SimpleVertex> BuildGeometry(in SmartPropPathInfo pathInfo)
        {
            var markerSize = EstimateMarkerSize(pathInfo);
            List<SimpleVertex> vertices = [];

            for (var i = 1; i < pathInfo.CurveSamples.Length; i++)
            {
                AddLine(vertices, pathInfo.CurveSamples[i - 1], pathInfo.CurveSamples[i], CurveColor);
            }

            foreach (ref readonly var controlPoint in pathInfo.ControlPoints.AsSpan())
            {
                AddCross(vertices, controlPoint, markerSize, ControlPointColor);
            }

            return vertices;
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!ShowLabel)
            {
                return;
            }

            for (var i = 0; i < controlPoints.Length; i++)
            {
                context.TextRenderer.AddTextBillboard(controlPoints[i] + new Vector3(0f, 0f, markerSize * 2f), new TextRenderer.TextRenderRequest
                {
                    Scale = 10f,
                    Text = i.ToString(),
                    Color = ControlPointColor,
                }, context.Camera);
            }
        }

        private static float EstimateMarkerSize(in SmartPropPathInfo pathInfo)
        {
            var extent = pathInfo.ControlPoints.Length > 1
                ? Vector3.Distance(pathInfo.ControlPoints[0], pathInfo.ControlPoints[^1])
                : 0f;
            return MathF.Max(2f, extent * 0.01f);
        }
    }
}
