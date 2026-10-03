using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ConstVectorNode : VectorValueNode
{
    public Vector3 Value { get; }
}
