using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Simple vertex with position and color for debug rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public record struct SimpleVertex(Vector3 Position, Color32 Color)
    {
        /// <summary>Size of a <see cref="SimpleVertex"/> in bytes.</summary>
        public static readonly int SizeInBytes = Marshal.SizeOf<SimpleVertex>();

        /// <summary>Input layout describing the vertex attributes, for creating VAOs through <see cref="GPUMeshBufferCache"/>.</summary>
        public static readonly VBIB.RenderInputLayoutField[] InputLayout =
        [
            new("POSITION", DXGI_FORMAT.R32G32B32_FLOAT, offset: 0),
            new("COLOR", DXGI_FORMAT.R8G8B8A8_UNORM, offset: 12),
        ];
    }

    /// <summary>
    /// Simple vertex with position, color, and normal for debug rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public record struct SimpleVertexNormal(Vector3 Position, Color32 Color, Vector3 Normal)
    {
        /// <summary>Size of a <see cref="SimpleVertexNormal"/> in bytes.</summary>
        public static readonly int SizeInBytes = Marshal.SizeOf<SimpleVertexNormal>();

        /// <summary>Input layout describing the vertex attributes, for creating VAOs through <see cref="GPUMeshBufferCache"/>.</summary>
        public static readonly VBIB.RenderInputLayoutField[] InputLayout =
        [
            new("POSITION", DXGI_FORMAT.R32G32B32_FLOAT, offset: 0),
            new("COLOR", DXGI_FORMAT.R8G8B8A8_UNORM, offset: 12),
            new("NORMAL", DXGI_FORMAT.R32G32B32_FLOAT, offset: 16),
        ];

        /// <summary>Initializes a <see cref="SimpleVertexNormal"/> with a zero normal.</summary>
        /// <param name="Position">Vertex position.</param>
        /// <param name="Color">Vertex color.</param>
        public SimpleVertexNormal(Vector3 Position, Color32 Color) : this(Position, Color, Vector3.Zero) { }
    }
}
