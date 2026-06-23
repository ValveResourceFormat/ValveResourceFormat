using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatSpringNode : FloatValueNode
{
    public float StartValue { get; }
    public float Hertz { get; }
    public float DampingRatio { get; }
    public short InputValueNodeIdx { get; }
    public bool UseStartValue { get; }

    public FloatSpringNode(KVObject data) : base(data)
    {
        StartValue = data.GetFloatProperty("m_flStartValue");
        Hertz = data.GetFloatProperty("m_flHertz");
        DampingRatio = data.GetFloatProperty("m_flDampingRatio");
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        UseStartValue = data.GetProperty<bool>("m_bUseStartValue");
    }
}
