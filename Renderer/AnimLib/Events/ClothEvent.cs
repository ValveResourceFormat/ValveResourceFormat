using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class ClothEvent : Event
{
    public ClothEvent__Type Type { get; }
    public float Stiffness { get; }
    public float SpeedIn { get; }
    public float SpeedOut { get; }
    public float LengthSeconds { get; }
    public string VertexSetName { get; }
    public string EffectName { get; }

    public ClothEvent(KVObject data) : base(data)
    {
        Type = data.GetEnumValue<ClothEvent__Type>("m_type");
        Stiffness = data.GetFloatProperty("m_flStiffness");
        SpeedIn = data.GetFloatProperty("m_flSpeedIn");
        SpeedOut = data.GetFloatProperty("m_flSpeedOut");
        LengthSeconds = data.GetFloatProperty("m_flLengthSeconds");
        VertexSetName = data.GetProperty<string>("m_vertexSetName");
        EffectName = data.GetProperty<string>("m_effectName");
    }
}
