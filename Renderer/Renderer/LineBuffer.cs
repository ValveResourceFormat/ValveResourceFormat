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

        /// <summary>Creates the GL objects and binds the shader's layout.</summary>
        /// <param name="rendererContext">Renderer context for loading the shader.</param>
        /// <param name="label">Debug label of the GL objects.</param>
        /// <param name="shaderName">Shader the lines are drawn with, taking a position and color per vertex.</param>
        public LineBuffer(RendererContext rendererContext, string label, string shaderName = "default")
        {
            Shader = rendererContext.ShaderLoader.LoadShader(shaderName);

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
        public void Draw(uint objectId = 0) => Draw(0, VertexCount, objectId);

        /// <summary>Draws a range of the uploaded vertices, with the object id as instancing base for picking.</summary>
        /// <param name="first">Index of the first vertex to draw.</param>
        /// <param name="count">Number of vertices to draw, two per segment or three per triangle.</param>
        /// <param name="objectId">Object id used as instancing base for picking.</param>
        /// <param name="primitive">What the vertices make up.</param>
        public void Draw(int first, int count, uint objectId = 0, PrimitiveType primitive = PrimitiveType.Lines)
        {
            VertexArray.Bind(vao, Shader);
            GL.DrawArraysInstancedBaseInstance(primitive, first, count, 1, objectId);
        }

        /// <summary>Deletes the GL objects.</summary>
        public void Delete()
        {
            VertexArray.Delete(vao);
            vertexBuffer.Delete();
        }
    }
}
