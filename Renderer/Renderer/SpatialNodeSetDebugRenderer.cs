using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Debug visualization for the dynamic node set, drawing each node's bounding box. There is no
    /// hierarchy to show: the boxes are all the set holds.
    /// </summary>
    public class SpatialNodeSetDebugRenderer : LineDebugRenderer
    {
        private readonly SpatialNodeSet nodeSet;
        private readonly List<SimpleVertex> vertices = [];

        /// <summary>Initializes the debug renderer and creates GPU resources.</summary>
        /// <param name="nodeSet">The set to visualize.</param>
        /// <param name="rendererContext">Renderer context for loading shaders.</param>
        public SpatialNodeSetDebugRenderer(SpatialNodeSet nodeSet, RendererContext rendererContext)
            : base(rendererContext, nameof(SpatialNodeSetDebugRenderer))
        {
            this.nodeSet = nodeSet;
        }

        /// <summary>Rebuilds the line geometry from the current bounds and draws it.</summary>
        public void Render()
        {
            ArgumentNullException.ThrowIfNull(nodeSet);

            vertices.Clear();

            foreach (var node in nodeSet.GetNodes())
            {
                ShapeSceneNode.AddBox(vertices, node.BoundingBox, new Color32(1.0f, 0.6f, 0.0f, 1.0f));
            }

            Upload(vertices, BufferUsage.Dynamic);
            RenderLines();
        }
    }
}
