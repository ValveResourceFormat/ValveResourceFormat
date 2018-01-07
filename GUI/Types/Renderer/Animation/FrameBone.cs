using System;
using OpenTK;

namespace GUI.Types.Renderer.Animation
{
    internal class FrameBone
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
