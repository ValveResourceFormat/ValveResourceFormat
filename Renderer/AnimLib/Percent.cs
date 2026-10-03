using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A percentage in the 0 to 1 range.</summary>
public readonly struct Percent
{
    /// <summary>The percentage as a 0 to 1 fraction.</summary>
    public float Value { get; }

    /// <summary>Creates a percentage from a 0 to 1 fraction.</summary>
    public Percent(float value)
    {
        Value = value;
    }

    /// <summary>Reads the percentage from resource data.</summary>
    public Percent(KVObject data)
    {
        Value = data.GetFloatProperty("m_flValue");
    }
}
