using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class ParameterizedBlendNode__BlendRange
{
    public short InputIdx0 { get; }
    public short InputIdx1 { get; }
    public Range ParameterValueRange { get; }

    public ParameterizedBlendNode__BlendRange(KVObject data)
    {
        InputIdx0 = data.GetInt16Property("m_nInputIdx0");
        InputIdx1 = data.GetInt16Property("m_nInputIdx1");
        ParameterValueRange = new(data.GetProperty<KVObject>("m_parameterValueRange"));
    }

    /// <summary>Constructs a blend range directly (used for runtime-built parameterizations).</summary>
    public ParameterizedBlendNode__BlendRange(short inputIdx0, short inputIdx1, Range parameterValueRange)
    {
        InputIdx0 = inputIdx0;
        InputIdx1 = inputIdx1;
        ParameterValueRange = parameterValueRange;
    }
}
