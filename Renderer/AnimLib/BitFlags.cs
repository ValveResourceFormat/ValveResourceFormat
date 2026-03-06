using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

readonly struct BitFlags
{
    public uint Flags { get; }

    public BitFlags(KVObject data)
    {
        Flags = data.GetUInt32Property("m_flags");
    }

    public bool IsFlagSet(uint flag)
    {
        return (Flags & flag) != 0;
    }
}
