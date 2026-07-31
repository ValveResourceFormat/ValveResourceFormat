using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class SpeedScaleBaseNode : PassthroughNode
{
    public short InputValueNodeIdx { get; }
    public float DefaultInputValue { get; }

    public SpeedScaleBaseNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        DefaultInputValue = data.GetFloatProperty("m_flDefaultInputValue");
    }
}
