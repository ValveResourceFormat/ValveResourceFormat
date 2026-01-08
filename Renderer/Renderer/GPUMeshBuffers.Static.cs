using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
namespace ValveResourceFormat.Renderer;

public partial class GPUMeshBufferCache
{
    private QuadIndexBuffer? quadIndices;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public QuadIndexBuffer QuadIndices
    {
        get
        {
            quadIndices ??= new QuadIndexBuffer(65532);

            return quadIndices;
        }
    }

    private int emptyVAO = -1;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int EmptyVAO
    {
        get
        {
            if (emptyVAO == -1)
            {
                GL.CreateVertexArrays(1, out emptyVAO);
            }

            return emptyVAO;
        }
    }

    private int vectorOneVertexBuffer = -1;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int VectorOneVertexBuffer
    {
        get
        {
            if (vectorOneVertexBuffer == -1)
            {
                GL.CreateBuffers(1, out vectorOneVertexBuffer);
                GL.NamedBufferData(vectorOneVertexBuffer, 4 * sizeof(float), [1f, 1f, 1f, 1f], BufferUsageHint.StaticDraw);
            }

            return vectorOneVertexBuffer;
        }
    }
}
