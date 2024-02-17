using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class AnimationDataChannel
    {
        public int[] RemapTable { get; } // Bone ID => Element Index
        public AnimationChannelAttribute Attribute { get; }

        public AnimationDataChannel(Skeleton skeleton, FlexController[] flexControllers, KVObject dataChannel)
        {
            var elementNameArray = dataChannel.GetArray<string>("m_szElementNameArray");
            var elementIndexArray = dataChannel.GetIntegerArray("m_nElementIndexArray");

            var channelAttribute = dataChannel.GetProperty<string>("m_szVariableName");
            Attribute = channelAttribute switch
            {
                "Position" => AnimationChannelAttribute.Position,
                "Angle" => AnimationChannelAttribute.Angle,
                "Scale" => AnimationChannelAttribute.Scale,
                "data" => AnimationChannelAttribute.Data,
                _ => AnimationChannelAttribute.Unknown,
            };

            int remapLength;
            if (Attribute == AnimationChannelAttribute.Data)
            {
                remapLength = flexControllers.Length;
            }
            else
            {
                remapLength = skeleton.Bones.Length;
            }
            RemapTable = Enumerable.Range(0, remapLength).Select(_ => -1).ToArray();

            for (var i = 0; i < elementIndexArray.Length; i++)
            {
                var elementName = elementNameArray[i];
                var elementIndex = (int)elementIndexArray[i];

                int id;
                if (Attribute == AnimationChannelAttribute.Data)
                {
                    id = Array.FindIndex(flexControllers, contr => contr.Name == elementName);
                }
                else
                {
                    id = Array.FindIndex(skeleton.Bones, bone => bone.Name == elementName);
                }

                if (id != -1)
                {
                    RemapTable[id] = elementIndex;
                }
            }
        }
    }
}
