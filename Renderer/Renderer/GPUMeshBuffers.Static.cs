using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
namespace ValveResourceFormat.Renderer;

public partial class GPUMeshBufferCache
{
    private QuadIndexBuffer? quadIndices;

    /// <summary>Gets the shared quad index buffer used for rendering quad-based geometry as triangle pairs.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public QuadIndexBuffer QuadIndices
    {
        get
        {
            quadIndices ??= new QuadIndexBuffer(RendererContext.Device, 65532);

            return quadIndices;
        }
    }

    private int emptyVAO = -1;

    /// <summary>Gets a lazily created empty vertex array object with no attributes, used for attributeless draws.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int EmptyVAO
    {
        get
        {
            if (emptyVAO == -1)
            {
                emptyVAO = RendererContext.Device.CreateVertexArray(nameof(EmptyVAO));
            }

            return emptyVAO;
        }
    }

    private int vectorOneVertexBuffer = -1;

    /// <summary>Gets a lazily created vertex buffer containing a single <c>(1, 1, 1, 1)</c> float4, used as a default color attribute.</summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int VectorOneVertexBuffer
    {
        get
        {
            if (vectorOneVertexBuffer == -1)
            {
                vectorOneVertexBuffer = RendererContext.Device.CreateBuffer(nameof(VectorOneVertexBuffer));
                GL.NamedBufferData(vectorOneVertexBuffer, 4 * sizeof(float), [1f, 1f, 1f, 1f], BufferUsageHint.StaticDraw);
            }

            return vectorOneVertexBuffer;
        }
    }
}
