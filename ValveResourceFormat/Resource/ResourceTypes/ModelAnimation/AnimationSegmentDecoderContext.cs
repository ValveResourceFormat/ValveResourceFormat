using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class AnimationSegmentDecoderContext
    {
        public ArraySegment<byte> Data { get; }
        public int[] Elements { get; }
        public int[] WantedElements { get; set; }
        public int[] RemapTable { get; set; }
        //public int elementCount { get; set; }
        //public AnimationChannelAttribute ChannelAttribute { get; set; }
        public AnimationDataChannel Channel { get; set; }

        public AnimationSegmentDecoderContext(ArraySegment<byte> data, int[] elements)
        {
            Data = data;
            Elements = elements;
        }
    }
}
