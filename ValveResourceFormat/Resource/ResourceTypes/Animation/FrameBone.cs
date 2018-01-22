using System;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.Animation
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
