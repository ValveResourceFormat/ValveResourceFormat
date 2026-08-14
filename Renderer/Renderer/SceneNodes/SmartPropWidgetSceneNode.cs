using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Base class for smart prop editing widget scene nodes. Widgets are baked into a
    /// static line buffer in world space; subclasses build their geometry and supply an
    /// optional text label that is billboarded every update. The node id doubles as the
    /// picking texture object id.
    /// </summary>
    public abstract class SmartPropWidgetSceneNode : SceneNode
    {
        private readonly LineBuffer lineBuffer;
        private readonly Vector3 labelPosition;
        private readonly string labelText;
        private readonly Color32 labelColor;

        /// <summary>Gets or sets whether the widget's text label is drawn.</summary>
        public bool ShowLabel { get; set; } = true;

        /// <summary>
        /// Initializes the node with world space line geometry and an optional label.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="vertices">World space line vertices, two per segment.</param>
        /// <param name="labelPosition">World space anchor for the label billboard.</param>
        /// <param name="labelText">Label text, empty for none.</param>
        /// <param name="labelColor">Label color.</param>
        protected SmartPropWidgetSceneNode(
            Scene scene,
            List<SimpleVertex> vertices,
            Vector3 labelPosition = default,
            string labelText = "",
            Color32 labelColor = default)
            : base(scene)
        {
            var boundsMin = vertices.Count > 0 ? vertices[0].Position : Vector3.Zero;
            var boundsMax = boundsMin;
            foreach (ref readonly var vertex in CollectionsMarshal.AsSpan(vertices))
            {
                boundsMin = Vector3.Min(boundsMin, vertex.Position);
                boundsMax = Vector3.Max(boundsMax, vertex.Position);
            }

            LocalBoundingBox = new AABB(boundsMin, boundsMax);
            this.labelPosition = labelPosition;
            this.labelText = labelText;
            this.labelColor = labelColor == default ? Color32.White : labelColor;

            lineBuffer = new LineBuffer(Scene.RendererContext, nameof(SmartPropWidgetSceneNode));
            lineBuffer.Upload(vertices, BufferUsage.Static);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            lineBuffer.Delete();
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (ShowLabel && labelText.Length > 0)
            {
                context.TextRenderer.AddTextBillboard(labelPosition, new TextRenderer.TextRenderRequest
                {
                    Scale = 10f,
                    Text = labelText,
                    Color = labelColor,
                }, context.Camera);
            }
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass is not RenderPass.Opaque and not RenderPass.Outline)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? lineBuffer.Shader;

            using var _ = GraphicsContext.RenderState.Scope();

            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);

            lineBuffer.Draw(Id);
        }

        /// <summary>Adds a world space line segment to a vertex list.</summary>
        protected static void AddLine(List<SimpleVertex> vertices, Vector3 from, Vector3 to, Color32 color)
            => ShapeSceneNode.AddLine(vertices, from, to, color);

        /// <summary>Adds a world space circle to a vertex list.</summary>
        protected static void AddCircle(List<SimpleVertex> vertices, Vector3 center, Vector3 axisU, Vector3 axisV, float radius, int segments, Color32 color)
        {
            Vector3 previous = default;
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * MathF.Tau / segments;
                var point = center + ((axisU * MathF.Cos(angle)) + (axisV * MathF.Sin(angle))) * radius;
                if (i > 0)
                {
                    AddLine(vertices, previous, point, color);
                }

                previous = point;
            }
        }

        /// <summary>Adds a small three axis cross marker to a vertex list.</summary>
        protected static void AddCross(List<SimpleVertex> vertices, Vector3 center, float size, Color32 color)
        {
            AddLine(vertices, center - new Vector3(size, 0, 0), center + new Vector3(size, 0, 0), color);
            AddLine(vertices, center - new Vector3(0, size, 0), center + new Vector3(0, size, 0), color);
            AddLine(vertices, center - new Vector3(0, 0, size), center + new Vector3(0, 0, size), color);
        }

        /// <summary>
        /// Builds two unit vectors orthogonal to each other and to the given unit axis.
        /// </summary>
        protected static (Vector3 U, Vector3 V) OrthonormalBasis(Vector3 axis)
        {
            var helper = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
            var u = Vector3.Normalize(Vector3.Cross(axis, helper));
            var v = Vector3.Cross(axis, u);
            return (u, v);
        }

        /// <summary>
        /// Reads the forward, left and up basis rows of a row-vector Source 2 matrix.
        /// </summary>
        protected static (Vector3 Forward, Vector3 Left, Vector3 Up) BasisRows(Matrix4x4 matrix)
            => (
                new(matrix.M11, matrix.M12, matrix.M13),
                new(matrix.M21, matrix.M22, matrix.M23),
                new(matrix.M31, matrix.M32, matrix.M33));
    }
}
