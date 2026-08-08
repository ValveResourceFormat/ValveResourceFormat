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
        public override void Read(int frameIndex, Frame outFrame, ReadOnlySpan<ElementRemap> remaps)
        {
            var offset = frameIndex * ElementCount;

            const int BaseElementSize = sizeof(float) * 3; // sizeof(Vector3)
            var data = Data.Span;
            var baseData = MemoryMarshal.Cast<byte, Vector3>(data.Slice(0, ElementCount * BaseElementSize));
            var deltaData = MemoryMarshal.Cast<byte, Half3>(data.Slice(ElementCount * BaseElementSize));
            //var numFrames = deltaData.Length / ElementCount;

            foreach (var remap in remaps)
            {
                var baseVector = baseData[remap.Source];
                var deltaVector = deltaData[offset + remap.Source];

                outFrame.SetAttribute(
                    remap.Dest,
                    ChannelAttribute,
                    baseVector + deltaVector
                );
            }
        }
    }
}
