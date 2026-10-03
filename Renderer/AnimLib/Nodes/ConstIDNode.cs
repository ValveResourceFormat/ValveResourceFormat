using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ConstIDNode : IDValueNode
{
    public GlobalSymbol Value { get; }

    public ConstIDNode(KVObject data) : base(data)
    {
        Value = data.GetProperty<string>("m_value");
    }
}
