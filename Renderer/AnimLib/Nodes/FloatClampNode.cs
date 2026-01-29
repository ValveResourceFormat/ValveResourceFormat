using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatClampNode : FloatValueNode
{
    public short InputValueNodeIdx { get; }
    public Range ClampRange { get; }

    public FloatClampNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        ClampRange = new(data.GetProperty<KVObject>("m_clampRange"));
    }
}
