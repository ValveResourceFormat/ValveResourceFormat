namespace ValveResourceFormat.CompiledShader;

public class VfxShaderFileDXIL : VfxShaderFile
{
    public override string BlockName => "DXIL";
    public int Arg0 { get; } // always 3
    public int Arg1 { get; } // always 0xFFFF or 0xFFFE
    public int HeaderBytes { get; }

    public VfxShaderFileDXIL(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
    {
        if (Size > 0)
        {
            Arg0 = datareader.ReadInt16();
            Arg1 = (int)datareader.ReadUInt16();
            uint dxilDelim = datareader.ReadUInt16();
            if (dxilDelim != 0xFFFE)
            {
                throw new ShaderParserException($"Unexpected DXIL source id {dxilDelim:x08}");
            }

            HeaderBytes = (int)datareader.ReadUInt16() * 4; // size is given as a 4-byte count
            Bytecode = datareader.ReadBytes(Size - 8);
        }

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }
}
