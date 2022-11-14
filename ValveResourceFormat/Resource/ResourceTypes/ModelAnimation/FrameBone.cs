using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class FrameBone
    {
        public Vector3 Position { get; set; }
        public Quaternion Angle { get; set; }

        public FrameBone(Vector3 pos, Quaternion a)
        {
            Position = pos;
            Angle = a;
        }
    }
}
