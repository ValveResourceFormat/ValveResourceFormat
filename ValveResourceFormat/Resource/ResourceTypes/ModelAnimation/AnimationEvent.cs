using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public struct AnimationEvent
    {
        public string Name { get; init; }
        public int Frame { get; init; }
        public float Cycle { get; init; }
        public KVObject EventData { get; init; }
        public string Options { get; init; }
        public AnimationEvent(KVObject data)
        {
            Name = data.GetStringProperty("m_sEventName");
            Frame = data.GetInt32Property("m_nFrame");
            Cycle = data.GetFloatProperty("m_flCycle");
            EventData = data.GetSubCollection("m_EventData");
            Options = data.GetStringProperty("m_sOptions");
        }
    }
}
