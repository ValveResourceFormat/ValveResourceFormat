using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Animation;

[KV3Transfer]
public readonly partial struct BitFlags
{
    [KVProperty("m_flags")]
    public uint Flags { get; }
}
