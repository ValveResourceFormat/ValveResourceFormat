using System;
using System.Collections.Generic;
using System.Text;

namespace ValveResourceFormat.ClosedCaptions
{
    public class ClosedCaption
    {
        public uint hash { get; set; }
        public int blocknum { get; set; }
        public ushort offset { get; set; }
        public ushort length { get; set; }
        public string text { get; set; }
    }
}
