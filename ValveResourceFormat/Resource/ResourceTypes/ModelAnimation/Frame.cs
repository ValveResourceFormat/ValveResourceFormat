using System;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class Frame
    {
        public FrameBone[] Bones { get; }

        public Frame(Skeleton skeleton)
        {
            Bones = new FrameBone[skeleton.Bones.Length];
            Clear();
        }

        public void SetAttribute(int bone, AnimationChannelAttribute attribute, Vector3 data)
        {
            switch (attribute)
            {
                case AnimationChannelAttribute.Position:
                    Bones[bone].Position = data;
                    break;

#if DEBUG
                default:
                    Console.WriteLine($"Unknown frame attribute '{attribute}' encountered with Vector3 data");
                    break;
#endif
            }
        }

        public void SetAttribute(int bone, AnimationChannelAttribute attribute, Quaternion data)
        {
            switch (attribute)
            {
                case AnimationChannelAttribute.Angle:
                    Bones[bone].Angle = data;
                    break;

#if DEBUG
                default:
                    Console.WriteLine($"Unknown frame attribute '{attribute}' encountered with Quaternion data");
                    break;
#endif
            }
        }

        public void SetAttribute(int bone, AnimationChannelAttribute attribute, float data)
        {
            switch (attribute)
            {
                case AnimationChannelAttribute.Scale:
                    Bones[bone].Scale = data;
                    break;

#if DEBUG
                default:
                    Console.WriteLine($"Unknown frame attribute '{attribute}' encountered with float data");
                    break;
#endif
            }
        }

        public void Clear()
        {
            for (var i = 0; i < Bones.Length; i++)
            {
                Bones[i].Position  = new Vector3(0, 0, 0);
                Bones[i].Angle = new Quaternion(0, 0, 0, 1);
                Bones[i].Scale = 1;
            }
        }
    }
}
