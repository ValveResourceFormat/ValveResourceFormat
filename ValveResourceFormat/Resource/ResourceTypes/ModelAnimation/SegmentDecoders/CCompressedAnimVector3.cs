using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes compressed Vector3 animation data using half-precision floats.
    /// </summary>
    public class CCompressedAnimVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * ElementCount;
            var halfVectorData = MemoryMarshal.Cast<byte, Half3>(Data);

            for (var i = 0; i < RemapTable.Length; i++)
            {
                var elementIndex = WantedElements[i];

                outFrame.SetAttribute(
                    RemapTable[i],
                    ChannelAttribute,
                    halfVectorData[offset + elementIndex]
                );
            }
        }
    }
}
