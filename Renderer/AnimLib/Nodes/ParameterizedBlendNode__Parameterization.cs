using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class ParameterizedBlendNode__Parameterization
{
    public ParameterizedBlendNode__BlendRange[] BlendRanges { get; }
    public Range ParameterRange { get; }

    public ParameterizedBlendNode__Parameterization(KVObject data)
    {
        BlendRanges = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_blendRanges"), kv => new ParameterizedBlendNode__BlendRange(kv))];
        ParameterRange = new(data.GetProperty<KVObject>("m_parameterRange"));
    }
}
