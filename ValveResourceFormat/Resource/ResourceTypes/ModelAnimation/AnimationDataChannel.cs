using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a data channel in an animation, mapping bones or flex controllers to animation elements.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/CAnimDataChannelDesc">CAnimDataChannelDesc</seealso>
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
        /// <param name="skeleton">The skeleton bone channels bind against.</param>
        /// <param name="flexControllers">The model's flex controllers, which morph channels bind against.</param>
        /// <param name="userNames">
        /// The decode key's own <c>m_userArray</c> names, which user channels bind against. Unlike the
        /// skeleton and flex controllers, this list is local to one decode key, not shared model-wide.
        /// </param>
        /// <param name="dataChannel">The <c>CAnimDataChannelDesc</c> key values.</param>
        public AnimationDataChannel(Skeleton skeleton, FlexController[] flexControllers, string[] userNames, KVObject dataChannel)
        {
            var elementNameArray = dataChannel.GetArray<string>("m_szElementNameArray");
            var elementIndexArray = dataChannel.GetIntegerArray("m_nElementIndexArray");

            var channelClass = dataChannel.GetStringProperty("m_szChannelClass");
            var channelAttribute = dataChannel.GetStringProperty("m_szVariableName");

            // m_szChannelClass disambiguates MorphChannel from UserChannel, which both use the variable
            // name "data". Fall back to the old name-only classification for decode keys that predate it.
            Attribute = channelClass switch
            {
                "BoneChannel" => BoneAttributeFromVariableName(channelAttribute),
                "MorphChannel" => AnimationChannelAttribute.Data,
                "UserChannel" => AnimationChannelAttribute.User,
                _ => channelAttribute switch
                {
                    "Position" => AnimationChannelAttribute.Position,
                    "Angle" => AnimationChannelAttribute.Angle,
                    "Scale" => AnimationChannelAttribute.Scale,
                    "data" => AnimationChannelAttribute.Data,
                    _ => AnimationChannelAttribute.Unknown,
                },
            };

            var domain = Attribute switch
            {
                AnimationChannelAttribute.Data => flexControllers.Length,
                AnimationChannelAttribute.User => userNames.Length,
                _ => skeleton.Bones.Length,
            };

            var remapTable = new int[domain];
            Array.Fill(remapTable, -1);

            for (var i = 0; i < elementIndexArray.Length; i++)
            {
                var elementName = elementNameArray![i];
                var elementIndex = (int)elementIndexArray[i];

                var id = Attribute switch
                {
                    AnimationChannelAttribute.Data
                        => Array.FindIndex(flexControllers, ctrl => ctrl.Name.Equals(elementName, StringComparison.OrdinalIgnoreCase)),
                    AnimationChannelAttribute.User
                        => Array.FindIndex(userNames, name => name.Equals(elementName, StringComparison.OrdinalIgnoreCase)),
                    _ => Array.FindIndex(skeleton.Bones, bone => bone.Name.Equals(elementName, StringComparison.OrdinalIgnoreCase)),
                };

                if (id != -1)
                {
                    remapTable[id] = elementIndex;
                }
            }

            RemapTable = remapTable;
        }

        private static AnimationChannelAttribute BoneAttributeFromVariableName(string channelAttribute) => channelAttribute switch
        {
            "Position" => AnimationChannelAttribute.Position,
            "Angle" => AnimationChannelAttribute.Angle,
            "Scale" => AnimationChannelAttribute.Scale,
            _ => AnimationChannelAttribute.Unknown,
        };
    }
}
