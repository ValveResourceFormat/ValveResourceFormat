using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that renders a list of line segments.
    /// </summary>
    public class LineSceneNode : SceneNode
    {
        readonly LineBuffer lineBuffer;

        /// <summary>
        /// Initializes a new instance of the <see cref="LineSceneNode"/> class rendering a single line segment.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="start">The start position of the line.</param>
        /// <param name="end">The end position of the line.</param>
        /// <param name="startColor">The color at the start of the line.</param>
        /// <param name="endColor">The color at the end of the line.</param>
        public LineSceneNode(Scene scene, Vector3 start, Vector3 end, Color32 startColor, Color32 endColor)
            : this(scene, [new(start, startColor), new(end, endColor)])
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LineSceneNode"/> class rendering a list of line segments.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="vertices">Pairs of vertices, one pair per line segment.</param>
        public LineSceneNode(Scene scene, SimpleVertex[] vertices)
            : base(scene)
        {
            var boundsMin = vertices.Length > 0 ? vertices[0].Position : Vector3.Zero;
            var boundsMax = boundsMin;
            foreach (var vertex in vertices)
            {
                boundsMin = Vector3.Min(boundsMin, vertex.Position);
                boundsMax = Vector3.Max(boundsMax, vertex.Position);
            }

            LocalBoundingBox = new AABB(boundsMin, boundsMax);

            lineBuffer = new LineBuffer(Scene.RendererContext, nameof(LineSceneNode));
            lineBuffer.Upload(vertices, BufferUsageHint.StaticDraw);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            lineBuffer.Delete();
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass is not RenderPass.Opaque and not RenderPass.Outline)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? lineBuffer.Shader;
            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);

            lineBuffer.Draw(Id, context.ReplacementShader);
        }
    }
}
