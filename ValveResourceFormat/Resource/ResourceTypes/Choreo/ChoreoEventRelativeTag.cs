namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoEventRelativeTag
    {
        public string Name { get; init; }
        public string SoundName { get; init; }
        public ChoreoEventRelativeTag(string name, string soundName)
        {
            Name = name;
            SoundName = soundName;
        }
    }
}
