namespace ValveResourceFormat.CompiledShader;

public class GlslSource : GpuSource
{
    public override string BlockName => "GLSL";
    public int Arg0 { get; } // always 3
    // offset2, if present, always observes offset2 == offset + 8
    // offset2 can also be interpreted as the source-size
    public int SizeText { get; } = -1;

    public GlslSource(ShaderDataReader datareader, int sourceId)
        : base(datareader, sourceId)
    {
        if (Size > 0)
        {
            Arg0 = DataReader.ReadInt32();
            SizeText = DataReader.ReadInt32();
            Sourcebytes = DataReader.ReadBytes(SizeText - 1); // -1 because the sourcebytes are null-term
            DataReader.BaseStream.Position += 1;
        }

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }
}
