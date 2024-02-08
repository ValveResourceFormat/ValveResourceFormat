namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoTag
    {
        public string Name { get; private set; }
        public float Fraction { get; private set; }

        public ChoreoTag(string name, float fraction)
        {
            Name = name;
            Fraction = fraction;
        }
    }
}
