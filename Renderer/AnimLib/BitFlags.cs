using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class BitFlags
{
    public uint Flags { get; }

    public BitFlags(KVObject data)
    {
        Flags = data.GetUInt32Property("m_flags");
    }
}
