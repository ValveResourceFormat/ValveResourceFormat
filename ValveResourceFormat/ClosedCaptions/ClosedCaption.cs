#nullable disable

namespace ValveResourceFormat.ClosedCaptions
{
    public class ClosedCaption
    {
        public uint Hash { get; set; }
        public uint HashText { get; set; }
        public int Blocknum { get; set; }
        public ushort Offset { get; set; }
        public ushort Length { get; set; }
        public string Text { get; set; }
    }
}
