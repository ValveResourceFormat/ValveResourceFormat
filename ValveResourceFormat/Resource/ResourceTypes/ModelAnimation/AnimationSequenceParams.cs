using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public struct AnimationSequenceParams
    {
        public float FadeInTime { get; init; }
        public float FadeOutTime { get; init; }
        public AnimationSequenceParams(KVObject data)
        {
            FadeInTime = data.GetFloatProperty("m_flFadeInTime");
            FadeOutTime = data.GetFloatProperty("m_flFadeOutTime");
        }
    }
}
