using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a data channel in an animation, naming the bones or flex controllers it drives.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/CAnimDataChannelDesc">CAnimDataChannelDesc</seealso>
    public class AnimationDataChannel
    {
        /// <summary>
        /// Gets the attribute type of this channel.
        /// </summary>
        public AnimationChannelAttribute Attribute { get; }

        private readonly string[] elementNameArray;
        private readonly long[] elementIndexArray;

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationDataChannel"/> class.
        /// </summary>
        public AnimationDataChannel(KVObject dataChannel)
        {
            elementNameArray = dataChannel.GetArray<string>("m_szElementNameArray");
            elementIndexArray = dataChannel.GetIntegerArray("m_nElementIndexArray");

            var channelAttribute = dataChannel.GetStringProperty("m_szVariableName");
            Attribute = channelAttribute switch
            {
                "Position" => AnimationChannelAttribute.Position,
                "Angle" => AnimationChannelAttribute.Angle,
                "Scale" => AnimationChannelAttribute.Scale,
                "data" => AnimationChannelAttribute.Data,
                _ => AnimationChannelAttribute.Unknown,
            };
        }

        /// <summary>
        /// Resolves this channel's element names against a target skeleton, producing a table that maps
        /// each bone (or flex controller, for <see cref="AnimationChannelAttribute.Data"/> channels) to the
        /// element index driving it, or -1 when this channel carries nothing for it.
        /// </summary>
        public int[] CreateRemapTable(Skeleton skeleton, FlexController[] flexControllers)
        {
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

            return remapTable;
        }
    }
}
