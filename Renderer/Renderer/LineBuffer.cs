using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Vertex array/buffer pair for drawing a colored line list with the default shader.
    /// </summary>
    public class LineBuffer
    {
        /// <summary>The default shader the vertex layout is bound to.</summary>
        public Shader Shader { get; }

        /// <summary>Number of vertices currently uploaded.</summary>
        public int VertexCount { get; private set; }

        private readonly int vaoHandle;
        private readonly int vboHandle;

        /// <summary>Creates the GL objects and binds the default shader layout.</summary>
        public LineBuffer(RendererContext rendererContext, string label)
        {
            Shader = rendererContext.ShaderLoader.LoadShader("vrf.default");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, Shader.Program);

#if DEBUG
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, label.Length, label);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vboHandle, label.Length, label);
#endif
        }

        /// <summary>Uploads the line vertices, two per segment.</summary>
        public void Upload(List<SimpleVertex> vertices, BufferUsageHint usageHint = BufferUsageHint.DynamicDraw)
            => Upload(CollectionsMarshal.AsSpan(vertices), usageHint);

        /// <summary>Uploads the line vertices, two per segment.</summary>
        public unsafe void Upload(ReadOnlySpan<SimpleVertex> vertices, BufferUsageHint usageHint = BufferUsageHint.DynamicDraw)
        {
            VertexCount = vertices.Length;

            fixed (SimpleVertex* data = vertices)
            {
                GL.NamedBufferData(vboHandle, VertexCount * SimpleVertex.SizeInBytes, (nint)data, usageHint);
            }
        }

        /// <summary>Drops the uploaded vertices.</summary>
        public void Clear()
        {
            VertexCount = 0;
        }

        /// <summary>Draws the lines, with the object id as instancing base for picking.</summary>
        public void Draw(uint objectId = 0)
        {
            GL.BindVertexArray(vaoHandle);
            GL.DrawArraysInstancedBaseInstance(PrimitiveType.Lines, 0, VertexCount, 1, objectId);
            GL.BindVertexArray(0);
        }

        /// <summary>Deletes the GL objects.</summary>
        public void Delete()
        {
            GL.DeleteBuffer(vboHandle);
            GL.DeleteVertexArray(vaoHandle);
        }
    }
}
