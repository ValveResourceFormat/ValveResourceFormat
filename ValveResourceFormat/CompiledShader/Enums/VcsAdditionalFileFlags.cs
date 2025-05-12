namespace ValveResourceFormat.CompiledShader;

[Flags]
public enum VcsAdditionalFileFlags
{
    None = 0,
    HasPixelShaderRenderState = 0x01,
    HasRaytracing = 0x02,
    HasMeshShader = 0x04,
}
