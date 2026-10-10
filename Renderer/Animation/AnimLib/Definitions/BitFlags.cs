using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
readonly partial struct BitFlags
{
    [KVProperty("m_flags")]
    public uint Flags { get; }
}
