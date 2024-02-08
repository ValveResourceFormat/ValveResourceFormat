namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public abstract class ChoreoTag
    {
        public string Name { get; private set; }
        public float Duration { get; private set; }

        protected ChoreoTag(string name, float duration)
        {
            Name = name;
            Duration = duration;
        }
    }
}
