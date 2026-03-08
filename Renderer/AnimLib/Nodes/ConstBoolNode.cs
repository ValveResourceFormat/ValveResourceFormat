using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ConstBoolNode : BoolValueNode
{
    public bool Value { get; }

    public ConstBoolNode(KVObject data) : base(data)
    {
        Value = data.GetProperty<bool>("m_bValue");
    }
}
