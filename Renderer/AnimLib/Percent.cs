using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class Percent
{
    public float Value { get; }

    public Percent(KVObject data)
    {
        Value = data.GetFloatProperty("m_flValue");
    }
}
