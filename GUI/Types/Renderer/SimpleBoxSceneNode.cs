using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace GUI.Types.Renderer
{
    class SimpleBoxSceneNode : SceneNode
    {
        readonly Shader shader;
        readonly int vaoHandle;
        const int vertexCount = 8;

        static readonly byte[] CubeIndices =
        [
#pragma warning disable format
            0, 3, 2, 0, 1, 3, // Face1
            4, 1, 0, 4, 5, 1, // Face2
            4, 2, 6, 4, 0, 2, // Face3
            6, 2, 3, 6, 3, 7, // Face4
            5, 7, 3, 5, 3, 1, // Face5
            4, 6, 7, 4, 7, 5, // Face6
#pragma warning restore format
        ];

        public SimpleBoxSceneNode(Scene scene, Color32 color, Vector3 scale)
            : base(scene)
        {
            var v = scale / 2f;

            LocalBoundingBox = new AABB(-v, v);

            var vertices = new SimpleVertex[vertexCount];

            for (var i = 0; i < vertexCount; i++)
            {
                var unitVector = new Vector3((i & 4) >> 2, (i & 2) >> 1, i & 1) * 2f - Vector3.One;
                vertices[i].Position = v * unitVector;
                vertices[i].Color = color;
            }

            // triangles: 12
            // vertex data: 32 floats / 128 bytes
            // indices: 9 uints / 36 bytes

            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.default");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out int vboHandle);
            GL.CreateBuffers(1, out int iboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            GL.VertexArrayElementBuffer(vaoHandle, iboHandle);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

            GL.NamedBufferData(vboHandle, vertexCount * SimpleVertex.SizeInBytes, vertices, BufferUsageHint.StaticDraw);
            GL.NamedBufferData(iboHandle, CubeIndices.Length, CubeIndices, BufferUsageHint.StaticDraw);

#if DEBUG
            var vaoLabel = nameof(SimpleBoxSceneNode);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.AfterOpaque)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;

            GL.UseProgram(renderShader.Program);

            renderShader.SetUniform4x4("transform", Transform);
            renderShader.SetUniform1("bAnimated", 0.0f);
            renderShader.SetUniform1("sceneObjectId", Id);

            GL.BindVertexArray(vaoHandle);
            GL.DrawElements(PrimitiveType.Triangles, CubeIndices.Length, DrawElementsType.UnsignedByte, 0);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }

        public override void Update(Scene.UpdateContext context)
        {
            //
        }
    }
}
