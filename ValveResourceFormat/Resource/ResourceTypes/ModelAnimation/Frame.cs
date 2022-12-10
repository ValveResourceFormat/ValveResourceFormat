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

        public void SetAttribute(string bone, string attribute, Vector3 data)
        {
            switch (attribute)
            {
                case "Position":
                    GetBone(bone).Position = data;
                    break;

                case "data":
                    //ignore
                    break;

#if DEBUG
                default:
                    Console.WriteLine($"Unknown frame attribute '{attribute}' encountered with Vector3 data");
                    break;
#endif
            }
        }

        public void SetAttribute(string bone, string attribute, Quaternion data)
        {
            switch (attribute)
            {
                case "Angle":
                    GetBone(bone).Angle = data;
                    break;

                case "data":
                    //ignore
                    break;

#if DEBUG
                default:
                    Console.WriteLine($"Unknown frame attribute '{attribute}' encountered with Quaternion data");
                    break;
#endif
            }
        }

        public void SetAttribute(string bone, string attribute, float data)
        {
            switch (attribute)
            {
                case "Scale":
                    GetBone(bone).Scale = data;
                    break;

                case "data":
                    //ignore
                    break;

#if DEBUG
                default:
                    Console.WriteLine($"Unknown frame attribute '{attribute}' encountered with float data");
                    break;
#endif
            }
        }

        private FrameBone GetBone(string name)
        {
            if (!Bones.TryGetValue(name, out var bone))
            {
                bone = new FrameBone(new Vector3(0, 0, 0), new Quaternion(0, 0, 0, 1), 1);

                Bones[name] = bone;
            }

            return bone;
        }
    }
}
