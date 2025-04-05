using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public readonly struct AnimationActivity
    {
        public string Name { get; init; }
        public int Activity { get; init; }
        public int Flags { get; init; }
        public int Weight { get; init; }
        public AnimationActivity(KVObject data)
        {
            Name = data.GetStringProperty("m_name");
            Activity = data.GetInt32Property("m_nActivity");
            Flags = data.GetInt32Property("m_nFlags");
            Weight = data.GetInt32Property("m_nWeight");
        }
    }
}
