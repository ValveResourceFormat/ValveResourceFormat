namespace ValveResourceFormat.Serialization.KeyValues;

public readonly record struct KV3ID(string Name, Guid Id)
{
    public override string ToString()
    {
        return $"{Name}:version{{{Id}}}";
    }
}
