using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatRemapNode
{
    [KV3Transfer]
    internal partial class RemapRange
    {
        public float Begin { get; }
        public float End { get; }
    }
}
