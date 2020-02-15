using System;
using System.Collections.Generic;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class Frame
    {
        public Dictionary<string, FrameBone> Bones { get; set; }

        public Frame()
        {
            Bones = new Dictionary<string, FrameBone>();
        }

        public void SetAttribute(string bone, string attribute, object data)
        {
            switch (attribute)
            {
                case "Position":
                    InsertIfUnknown(bone);
                    Bones[bone].Position = (Vector3)data;
                    break;
                case "Angle":
                    InsertIfUnknown(bone);
                    Bones[bone].Angle = (Quaternion)data;
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

        private void InsertIfUnknown(string name)
        {
            if (!Bones.ContainsKey(name))
            {
                Bones[name] = new FrameBone(new Vector3(0, 0, 0), new Quaternion(0, 0, 0, 1));
            }
        }
    }
}
