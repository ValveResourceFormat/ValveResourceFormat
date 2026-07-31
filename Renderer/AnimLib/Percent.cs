using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class Percent
{
    public float Value { get; }


    public Percent(float value)
    {
        Value = value;
    }

    public Percent(KVObject data)
    {
        Value = data.GetFloatProperty("m_flValue");
    }
}
