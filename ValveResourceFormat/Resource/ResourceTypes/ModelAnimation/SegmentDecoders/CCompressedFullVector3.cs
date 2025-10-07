using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes full-precision Vector3 animation data.
    /// </summary>
    public class CCompressedFullVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * ElementCount;
            var vectorData = MemoryMarshal.Cast<byte, Vector3>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, vectorData[offset + WantedElements[i]]);
            }
        }
    }
}
