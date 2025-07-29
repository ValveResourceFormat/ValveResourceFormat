using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class OctreeDebugRenderer<T>
        where T : class
    {
        private readonly Shader shader;
        private readonly Octree<T> octree;
        private readonly int vaoHandle;
        private readonly int vboHandle;
        private readonly bool dynamic;
        private bool built;
        private int vertexCount;

        public OctreeDebugRenderer(Octree<T> octree, VrfGuiContext guiContext, bool dynamic)
        {
            this.octree = octree;
            this.dynamic = dynamic;

            shader = shader = guiContext.ShaderLoader.LoadShader("vrf.default");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

#if DEBUG
            var vaoLabel = nameof(OctreeDebugRenderer<T>);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        public static void AddLine(List<SimpleVertex> vertices, Vector3 from, Vector3 to, Color32 color)
        {
            vertices.Add(new SimpleVertex(from, color));
            vertices.Add(new SimpleVertex(to, color));
        }

        public static void AddBox(List<SimpleVertex> vertices, in AABB box, Color32 color)
        {
            // Adding a box will add many vertices, so ensure the required capacity for it up front
            vertices.EnsureCapacity(vertices.Count + 2 * 12);

            AddLine(vertices, new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Min.Y, box.Min.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Min.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Max.Y, box.Min.Z), new Vector3(box.Min.X, box.Max.Y, box.Min.Z), color);
            AddLine(vertices, new Vector3(box.Min.X, box.Max.Y, box.Min.Z), new Vector3(box.Min.X, box.Min.Y, box.Min.Z), color);

            AddLine(vertices, new Vector3(box.Min.X, box.Min.Y, box.Max.Z), new Vector3(box.Max.X, box.Min.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Min.Y, box.Max.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Max.Y, box.Max.Z), new Vector3(box.Min.X, box.Max.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Min.X, box.Max.Y, box.Max.Z), new Vector3(box.Min.X, box.Min.Y, box.Max.Z), color);

            AddLine(vertices, new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Min.X, box.Min.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Min.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Max.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Min.X, box.Max.Y, box.Min.Z), new Vector3(box.Min.X, box.Max.Y, box.Max.Z), color);
        }

        private static void AddOctreeNode(List<SimpleVertex> vertices, Octree<T>.Node node, int depth)
        {
            AddBox(vertices, node.Region, Color32.White with { A = node.HasElements ? (byte)255 : (byte)64 });

            if (node.HasElements)
            {
                foreach (var element in node.Elements!)
                {
                    var shading = Math.Min(1.0f, depth * 0.1f);
                    AddBox(vertices, element.BoundingBox, new(1.0f, shading, 0.0f, 1.0f));

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

        public void StaticBuild()
        {
            if (!built)
            {
                built = true;
                Rebuild();
            }
        }

        public void Rebuild()
        {
            var vertices = new List<SimpleVertex>();
            AddOctreeNode(vertices, octree.Root, 0);
            vertexCount = vertices.Count;

            GL.NamedBufferData(vboHandle, vertexCount * SimpleVertex.SizeInBytes, ListAccessors<SimpleVertex>.GetBackingArray(vertices), dynamic ? BufferUsageHint.DynamicDraw : BufferUsageHint.StaticDraw);
        }

        public void Render()
        {
            if (dynamic)
            {
                Rebuild();
            }

            GL.Enable(EnableCap.Blend);
            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Use();
            shader.SetUniform3x4("transform", Matrix4x4.Identity);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
            GL.UseProgram(0);
            GL.BindVertexArray(0);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }
    }
}
