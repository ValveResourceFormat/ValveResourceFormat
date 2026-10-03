using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class RootMotionEvent : Event
{
    public float BlendTimeSeconds { get; } = 0.1f;
}
