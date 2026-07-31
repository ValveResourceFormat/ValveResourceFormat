using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatRemapNode : FloatValueNode
{
    public short InputValueNodeIdx { get; }
    public FloatRemapNode__RemapRange InputRange { get; }
    public FloatRemapNode__RemapRange OutputRange { get; }

    public FloatRemapNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        InputRange = new(data.GetProperty<KVObject>("m_inputRange"));
        OutputRange = new(data.GetProperty<KVObject>("m_outputRange"));
    }
}
