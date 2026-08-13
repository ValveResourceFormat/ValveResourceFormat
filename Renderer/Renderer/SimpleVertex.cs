using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Simple vertex with position and color for debug rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public record struct SimpleVertex(
        [field: VertexAttribute(VertexSlot.Position)] Vector3 Position,
        [field: VertexAttribute(VertexSlot.Color)] Color32 Color)
    {
        /// <summary>Vertex layout, for creating VAOs.</summary>
        public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<SimpleVertex>();
    }

    /// <summary>
    /// Simple vertex with position, color, and normal for debug rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public record struct SimpleVertexNormal(
        [field: VertexAttribute(VertexSlot.Position)] Vector3 Position,
        [field: VertexAttribute(VertexSlot.Color)] Color32 Color,
        [field: VertexAttribute(VertexSlot.Normal)] Vector3 Normal)
    {
        /// <summary>Vertex layout, for creating VAOs.</summary>
        public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<SimpleVertexNormal>();

        /// <summary>Initializes a <see cref="SimpleVertexNormal"/> with a zero normal.</summary>
        /// <param name="Position">Vertex position.</param>
        /// <param name="Color">Vertex color.</param>
        public SimpleVertexNormal(Vector3 Position, Color32 Color) : this(Position, Color, Vector3.Zero) { }
    }
}
