using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Simple vertex with position and color for debug rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public record struct SimpleVertex(
        [field: VertexAttribute(VertexAttributeSlot.Position)] Vector3 Position,
        [field: VertexAttribute(VertexAttributeSlot.Color)] Color32 Color)
    {
        /// <summary>Size of a <see cref="SimpleVertex"/> in bytes.</summary>
        public static readonly int SizeInBytes = Marshal.SizeOf<SimpleVertex>();

        /// <summary>Vertex layout, for creating VAOs.</summary>
        public static readonly VertexFormat Format = VertexFormat.FromStruct<SimpleVertex>();
    }

    /// <summary>
    /// Simple vertex with position, color, and normal for debug rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public record struct SimpleVertexNormal(
        [field: VertexAttribute(VertexAttributeSlot.Position)] Vector3 Position,
        [field: VertexAttribute(VertexAttributeSlot.Color)] Color32 Color,
        [field: VertexAttribute(VertexAttributeSlot.Normal)] Vector3 Normal)
    {
        /// <summary>Size of a <see cref="SimpleVertexNormal"/> in bytes.</summary>
        public static readonly int SizeInBytes = Marshal.SizeOf<SimpleVertexNormal>();

        /// <summary>Vertex layout, for creating VAOs.</summary>
        public static readonly VertexFormat Format = VertexFormat.FromStruct<SimpleVertexNormal>();

        /// <summary>Initializes a <see cref="SimpleVertexNormal"/> with a zero normal.</summary>
        /// <param name="Position">Vertex position.</param>
        /// <param name="Color">Vertex color.</param>
        public SimpleVertexNormal(Vector3 Position, Color32 Color) : this(Position, Color, Vector3.Zero) { }
    }
}
