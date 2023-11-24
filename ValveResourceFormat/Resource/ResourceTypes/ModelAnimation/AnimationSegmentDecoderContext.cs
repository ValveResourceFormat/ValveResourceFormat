using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public struct AnimationSegmentDecoderContext
    {
        public ArraySegment<byte> Data { get; set; }
        public int[] Elements { get; set; }
        public int[] WantedElements { get; set; }
        public int[] RemapTable { get; set; }
        public AnimationDataChannel Channel { get; set; }
    }
}
