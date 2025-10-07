namespace ValveResourceFormat.Serialization.KeyValues;

/// <summary>
/// Represents a KeyValues3 identifier with a name and GUID.
/// </summary>
public readonly record struct KV3ID(string Name, Guid Id)
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{Name}:version{{{Id}}}";
    }
}
