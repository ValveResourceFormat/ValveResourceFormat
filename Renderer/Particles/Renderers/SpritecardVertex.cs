using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// One corner of a spritecard quad, shared by every renderer that draws through the spritecard
    /// shader. Each texture layer past the first carries its own coordinate pair, because a layer
    /// resolves its sheet frame against its own sequence and lands wherever its own transform puts it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SpritecardVertex
    {
        [VertexAttribute(VertexSlot.Position)] public Vector3 Position;
        [VertexAttribute(VertexSlot.Color)] public Vector4 Color;
        [VertexAttribute(VertexSlot.TexCoord)] public Vector2 UV;
        [VertexAttribute(VertexSlot.TexCoord1)] public Vector2 UVNextFrame;
        [VertexAttribute("vFrameBlend")] public float FrameBlend;
        [VertexAttribute("vLayerUv0")] public Vector4 LayerUv0;
        [VertexAttribute("vLayerUv1")] public Vector4 LayerUv1;
        [VertexAttribute("vLayerUv2")] public Vector4 LayerUv2;
        [VertexAttribute("vLayerUv3")] public Vector4 LayerUv3;

        /// <summary>The layout of this vertex, for creating vertex array objects.</summary>
        public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<SpritecardVertex>();

        /// <summary>Sets the coordinates of the layer at <paramref name="layer"/> places past the first.</summary>
        public void SetLayerUv(int layer, Vector4 uvs)
        {
            switch (layer)
            {
                case 0: LayerUv0 = uvs; break;
                case 1: LayerUv1 = uvs; break;
                case 2: LayerUv2 = uvs; break;
                case 3: LayerUv3 = uvs; break;
                default: throw new ArgumentOutOfRangeException(nameof(layer));
            }
        }
    }
}
