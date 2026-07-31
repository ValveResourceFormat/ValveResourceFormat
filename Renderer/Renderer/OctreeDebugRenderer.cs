using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Debug visualization renderer for octree spatial partitioning structure.
    /// </summary>
    public class OctreeDebugRenderer : LineDebugRenderer
    {
        private readonly Octree octree;
        private readonly bool dynamic;

        /// <summary>Initializes the octree debug renderer and creates GPU resources.</summary>
        /// <param name="octree">The octree to visualize.</param>
        /// <param name="rendererContext">Renderer context for loading shaders.</param>
        /// <param name="dynamic">When <see langword="true"/>, the vertex buffer is rebuilt every frame.</param>
        public OctreeDebugRenderer(Octree octree, RendererContext rendererContext, bool dynamic)
            : base(rendererContext, nameof(OctreeDebugRenderer))
        {
            this.octree = octree;
            this.dynamic = dynamic;
        }

        private static void AddOctreeNode(List<SimpleVertex> vertices, Octree.Node node, int depth)
        {
            ShapeSceneNode.AddBox(vertices, node.Region, Color32.White with { A = node.HasElements ? (byte)255 : (byte)64 });

            if (node.HasElements)
            {
                foreach (var element in node.Elements!)
                {
                    var shading = Math.Min(1.0f, depth * 0.1f);
                    ShapeSceneNode.AddBox(vertices, element.BoundingBox, new(1.0f, shading, 0.0f, 1.0f));

                    // AddLine(vertices, element.BoundingBox.Min, node.Region.Min, new Vector4(1.0f, shading, 0.0f, 0.5f));
                    // AddLine(vertices, element.BoundingBox.Max, node.Region.Max, new Vector4(1.0f, shading, 0.0f, 0.5f));
                }
            }

            if (node.HasChildren)
            {
                foreach (var child in node.Children)
                {
                    AddOctreeNode(vertices, child, depth + 1);
                }
            }
        }

        /// <summary>Builds the static vertex buffer once for a non-dynamic octree visualization.</summary>
        public void StaticBuild()
        {
            Rebuild();
        }

        /// <summary>Traverses the octree and uploads fresh line geometry to the GPU vertex buffer.</summary>
        public void Rebuild()
        {
            var vertices = new List<SimpleVertex>();
            AddOctreeNode(vertices, octree.Root, 0);

            Upload(vertices, dynamic ? BufferUsageHint.DynamicDraw : BufferUsageHint.StaticDraw);
        }

        /// <summary>Renders the octree visualization for the current frame, rebuilding geometry if dynamic.</summary>
        public void Render()
        {
            if (dynamic)
            {
                Rebuild();
            }

            RenderLines();
        }
    }
}
