namespace ValveResourceFormat.CompiledShader;

public class VfxShaderFileTempKv3 : VfxShaderFile
{
    public override string BlockName => "KV3";

    public VfxShaderFileTempKv3(Guid hash, VfxStaticComboData parent) : base(parent)
    {
        HashMD5 = hash;
    }

    public override string GetDecompiledFile()
    {
        throw new InvalidOperationException();
    }
}
