using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Utils
{
    /// <summary>
    /// One vertex of a metaball surface.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MetaballVertex(Vector3 position, Vector3 normal, Vector4 tangent, Vector2 uv)
    {
        [VertexAttribute(VertexSlot.Position)] public readonly Vector3 Position = position;
        [VertexAttribute(VertexSlot.Normal)] public readonly Vector3 Normal = normal;
        [VertexAttribute(VertexSlot.Tangent)] public readonly Vector4 TangentU_SignV = tangent;
        [VertexAttribute(VertexSlot.TexCoord)] public readonly Vector2 UV = uv;

        /// <summary>The layout of this vertex, for creating vertex array objects.</summary>
        public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<MetaballVertex>();
    }
}
