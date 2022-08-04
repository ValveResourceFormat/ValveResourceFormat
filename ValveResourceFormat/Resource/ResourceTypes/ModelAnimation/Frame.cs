using System;
using System.Collections.Generic;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class Frame
    {
        public Dictionary<string, FrameBone> Bones { get; }

        public Frame()
        {
            Bones = new Dictionary<string, FrameBone>();
        }

        public void SetAttribute(string bone, string attribute, object data)
        {
            switch (attribute)
            {
                case "Position":
                    GetBone(bone).Position = (Vector3)data;
                    break;

                case "Angle":
                    GetBone(bone).Angle = (Quaternion)data;
                    break;

                case "data":
                    //ignore
                    break;
#if DEBUG
                default:
                    Console.WriteLine($"Unknown frame attribute '{attribute}' encountered");
                    break;
#endif
            }
        }

        private FrameBone GetBone(string name)
        {
            if (!Bones.TryGetValue(name, out var bone))
            {
                bone = new FrameBone(new Vector3(0, 0, 0), new Quaternion(0, 0, 0, 1));

                Bones[name] = bone;
            }

            return bone;
        }
    }
}
