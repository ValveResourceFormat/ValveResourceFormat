using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class AnimationDataChannel
    {
        public int[] RemapTable { get; } // Bone ID => Element Index
        public Dictionary<int, string> IndexToName { get; } = new();
        public AnimationChannelAttribute Attribute { get; }

        public AnimationDataChannel(Skeleton skeleton, IKeyValueCollection dataChannel, int channelElements)
        {
            RemapTable = Enumerable.Range(0, skeleton.Bones.Length).Select(_ => -1).ToArray();

            var elementNameArray = dataChannel.GetArray<string>("m_szElementNameArray");
            var elementIndexArray = dataChannel.GetIntegerArray("m_nElementIndexArray");

            for (var i = 0; i < elementIndexArray.Length; i++)
            {
                var elementName = elementNameArray[i];
                var elementIndex = (int)elementIndexArray[i];
                var boneID = Array.FindIndex(skeleton.Bones, bone => bone.Name == elementName);
                if (boneID != -1)
                {
                    RemapTable[boneID] = elementIndex;
                }

                IndexToName[elementIndex] = elementName;
            }

            var channelAttribute = dataChannel.GetProperty<string>("m_szVariableName");
            Attribute = channelAttribute switch
            {
                "Position" => AnimationChannelAttribute.Position,
                "Angle" => AnimationChannelAttribute.Angle,
                "Scale" => AnimationChannelAttribute.Scale,
                "data" => AnimationChannelAttribute.Data,
                _ => AnimationChannelAttribute.Unknown,
            };
        }
    }
}
