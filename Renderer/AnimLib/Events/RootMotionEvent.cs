using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class RootMotionEvent : Event
{
    public float BlendTimeSeconds { get; }

    public RootMotionEvent(KVObject data) : base(data)
    {
        BlendTimeSeconds = data.GetFloatProperty("m_flBlendTimeSeconds");
    }
}
