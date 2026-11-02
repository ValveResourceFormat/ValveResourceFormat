using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders
{
    /// <summary>
    /// Decodes static Vector3 data using half-precision floats that don't change per frame.
    /// </summary>
    internal class CCompressedStaticVector3 : AnimationSegmentDecoder
    {
        private Vector3[] Values = [];

        /// <inheritdoc/>
        protected override void OnInitialized()
        {
            var halfVectorData = MemoryMarshal.Cast<byte, Half3>(Data);

            Values = new Vector3[WantedElements.Length];

            for (var i = 0; i < Values.Length; i++)
            {
                Values[i] = halfVectorData[WantedElements[i]];
            }
        }

        /// <inheritdoc/>
        public override void Read(int frameIndex, Frame outFrame)
        {
            for (var i = 0; i < RemapTable.Length; i++)
            {
                outFrame.SetAttribute(RemapTable[i], ChannelAttribute, Values[i]);
            }
        }
    }
}
