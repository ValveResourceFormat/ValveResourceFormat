using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ParameterizedBlendNode__BlendRange
{
    public short InputIdx0 { get; } = -1;
    public short InputIdx1 { get; } = -1;
    public Range ParameterValueRange { get; }

    /// <summary>Constructs a blend range directly (used for runtime-built parameterizations).</summary>
    public ParameterizedBlendNode__BlendRange(short inputIdx0, short inputIdx1, Range parameterValueRange)
    {
        InputIdx0 = inputIdx0;
        InputIdx1 = inputIdx1;
        ParameterValueRange = parameterValueRange;
    }
}
