using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class AnimationDataChannel
    {
        public string[] BoneNames { get; }
        public string ChannelAttribute { get; }

        public AnimationDataChannel(IKeyValueCollection dataChannel, int channelElements)
        {
            var boneNames = dataChannel.GetArray<string>("m_szElementNameArray");
            var elementIndexArray = dataChannel.GetIntegerArray("m_nElementIndexArray");
            BoneNames = new string[channelElements];
            for (var i = 0; i < elementIndexArray.Length; i++)
            {
                BoneNames[elementIndexArray[i]] = boneNames[i];
            }

            ChannelAttribute = dataChannel.GetProperty<string>("m_szVariableName");
        }
    }
}
