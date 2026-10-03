using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A percentage in the 0 to 1 range.</summary>
[KV3Transfer]
public readonly partial struct Percent
{
    /// <summary>The percentage as a 0 to 1 fraction.</summary>
    public float Value { get; }

    /// <summary>Creates a percentage from a 0 to 1 fraction.</summary>
    public Percent(float value)
    {
        Value = value;
    }
}
