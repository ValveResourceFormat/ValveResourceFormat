using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes delta-compressed Vector3 animation data with a base value and half-precision deltas.
    /// </summary>
    public class CCompressedDeltaVector3 : AnimationSegmentDecoder
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Reads a base Vector3 and adds a half-precision delta to produce the final value.
        /// </remarks>
        public override void Read(int frameIndex, Frame outFrame)
        {
            var offset = frameIndex * ElementCount;

            const int BaseElementSize = sizeof(float) * 3; // sizeof(Vector3)
            var baseData = MemoryMarshal.Cast<byte, Vector3>(Data.AsSpan(0, ElementCount * BaseElementSize));
            var deltaData = MemoryMarshal.Cast<byte, Half3>(Data.AsSpan(ElementCount * BaseElementSize));
            //var numFrames = deltaData.Length / ElementCount;

            for (var i = 0; i < RemapTable.Length; i++)
            {
                var elementIndex = WantedElements[i];

                var baseVector = baseData[elementIndex];
                var deltaVector = deltaData[offset + elementIndex];

                outFrame.SetAttribute(
                    RemapTable[i],
                    ChannelAttribute,
                    baseVector + deltaVector
                );
            }
        }
    }
}
