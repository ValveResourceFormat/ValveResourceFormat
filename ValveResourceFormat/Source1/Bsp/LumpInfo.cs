namespace ValveResourceFormat.Source1.Bsp
{
    internal readonly struct LumpInfo
    {
        public LumpType Type { get; init; }
        public int Offset { get; init; }
        public int Length { get; init; }
        public int Version { get; init; }
        public int FourCC { get; init; }

        public override string ToString() => $"{{ Type: {Type}, Length: {Length:N0}, Version: {Version} }}";
    }
}
