using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ConstTargetNode : TargetValueNode
{
    public Target Value { get; }

    public ConstTargetNode(KVObject data) : base(data)
    {
        Value = new(data.GetProperty<KVObject>("m_value"));
    }
}
