using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

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

        private readonly StreamingVertexBuffer vertexBuffer;
        private readonly int vao;

        /// <summary>Creates the GL objects and binds the default shader layout.</summary>
        public LineBuffer(RendererContext rendererContext, string label)
        {
            Shader = rendererContext.ShaderLoader.LoadShader("default");

            vertexBuffer = new StreamingVertexBuffer(label);
            vao = SimpleVertex.InputLayout.CreateVertexArray(label, vertexBuffer.Handle);
            vertexBuffer.AttachTo(vao, SimpleVertex.InputLayout.Stride);
        }

        /// <summary>Uploads the line vertices, two per segment.</summary>
        public void Upload(List<SimpleVertex> vertices)
            => Upload(CollectionsMarshal.AsSpan(vertices));

        /// <summary>Uploads the line vertices, two per segment.</summary>
        public void Upload(ReadOnlySpan<SimpleVertex> vertices)
        {
            VertexCount = vertices.Length;

            vertexBuffer.Upload(MemoryMarshal.AsBytes(vertices));
        }

        /// <summary>Drops the uploaded vertices.</summary>
        public void Clear()
        {
            VertexCount = 0;
        }

        /// <summary>Draws the lines, with the object id as instancing base for picking.</summary>
        /// <param name="objectId">Object id used as instancing base for picking.</param>
        public void Draw(uint objectId = 0)
        {
            VertexArray.Bind(vao, Shader);
            GL.DrawArraysInstancedBaseInstance(PrimitiveType.Lines, 0, VertexCount, 1, objectId);
        }

        /// <summary>Binds the vertex array for a series of <see cref="DrawRange"/> calls.</summary>
        public void Bind() => VertexArray.Bind(vao, Shader);

        /// <summary>Draws a range of the uploaded vertices. Call <see cref="Bind"/> first.</summary>
        /// <param name="start">First vertex to draw.</param>
        /// <param name="count">Number of vertices to draw, two per segment.</param>
        /// <param name="objectId">Object id used as instancing base for picking.</param>
        public static void DrawRange(int start, int count, uint objectId = 0)
            => GL.DrawArraysInstancedBaseInstance(PrimitiveType.Lines, start, count, 1, objectId);

        /// <summary>Deletes the GL objects.</summary>
        public void Delete()
        {
            VertexArray.Delete(vao);
            vertexBuffer.Delete();
        }
    }
}
