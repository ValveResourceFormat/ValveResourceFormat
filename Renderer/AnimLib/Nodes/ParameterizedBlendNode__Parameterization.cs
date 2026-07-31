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

    /// <summary>Constructs a parameterization directly (used for runtime-built parameterizations).</summary>
    public ParameterizedBlendNode__Parameterization(ParameterizedBlendNode__BlendRange[] blendRanges, Range parameterRange)
    {
        BlendRanges = blendRanges;
        ParameterRange = parameterRange;
    }

    /// <summary>
    /// Creates a parameterization for a set of values, each corresponding to a source node (in source order).
    /// Ported from Esoterica's <c>ParameterizedBlendNode::Parameterization::CreateParameterization</c>.
    /// </summary>
    public static ParameterizedBlendNode__Parameterization CreateParameterization(float[] values)
    {
        var numSources = values.Length;

        // Sort index/value pairs by value (ties broken by index)
        var pairs = new (short Index, float Value)[numSources];
        for (short i = 0; i < numSources; i++)
        {
            pairs[i] = (i, values[i]);
        }

        Array.Sort(pairs, (a, b) => a.Value == b.Value ? a.Index.CompareTo(b.Index) : a.Value.CompareTo(b.Value));

        var blendRanges = new ParameterizedBlendNode__BlendRange[numSources - 1];
        for (var i = 0; i < numSources - 1; i++)
        {
            blendRanges[i] = new ParameterizedBlendNode__BlendRange(
                pairs[i].Index,
                pairs[i + 1].Index,
                new Range(pairs[i].Value, pairs[i + 1].Value));
        }

        return new ParameterizedBlendNode__Parameterization(blendRanges, new Range(pairs[0].Value, pairs[numSources - 1].Value));
    }
}
