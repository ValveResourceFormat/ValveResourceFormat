using System;
using System.Linq;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    public class CCompressedStaticQuaternion : AnimationSegmentDecoder
    {
        public Quaternion[] Data { get; }

        public CCompressedStaticQuaternion(ArraySegment<byte> data, int[] wantedElements, int[] remapTable,
            AnimationChannelAttribute channelAttribute) : base(remapTable, channelAttribute)
        {
            Data = wantedElements.Select(i =>
            {
                return SegmentHelpers.ReadQuaternion(data.Slice(i * 6));
            }).ToArray();
        }

        public override void Read(int frameIndex, Frame outFrame)
        {
            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, Data[i]);
            }
        }
    }
}
