using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ClothEvent : Event
{
    public ClothEvent.ClothEventType Type { get; }
    public float Stiffness { get; } = 1f;
    public float SpeedIn { get; } = 10f;
    public float SpeedOut { get; } = 10f;
    public float LengthSeconds { get; } = 1f;
    public string VertexSetName { get; }
    public string EffectName { get; }
}
