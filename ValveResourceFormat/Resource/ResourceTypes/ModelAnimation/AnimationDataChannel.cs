using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a data channel in an animation, mapping bones or flex controllers to animation elements.
    /// </summary>
    public class AnimationDataChannel
    {
        /// <summary>
        /// Gets the remap table that maps bone or flex controller IDs to element indices.
        /// </summary>
        public int[] RemapTable { get; }

        /// <summary>
        /// Gets the attribute type of this channel.
        /// </summary>
        public AnimationChannelAttribute Attribute { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationDataChannel"/> class.
        /// </summary>
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

            var remapTable = new int[remapLength];
            Array.Fill(remapTable, -1);

            for (var i = 0; i < elementIndexArray.Length; i++)
            {
                var elementName = elementNameArray![i];
                var elementIndex = (int)elementIndexArray[i];

                int id;
                if (Attribute == AnimationChannelAttribute.Data)
                {
                    id = Array.FindIndex(flexControllers, ctrl => ctrl.Name.Equals(elementName, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    id = Array.FindIndex(skeleton.Bones, bone => bone.Name.Equals(elementName, StringComparison.OrdinalIgnoreCase));
                }

                if (id != -1)
                {
                    remapTable[id] = elementIndex;
                }
            }

            RemapTable = remapTable;
        }
    }
}
