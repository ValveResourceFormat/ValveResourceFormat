namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Flags indicating additional shader files present.
/// </summary>
[Flags]
public enum VcsAdditionalFileFlags
{
#pragma warning disable CS1591
    None = 0,
    HasPixelShaderRenderState = 0x01,
    HasRaytracing = 0x02,
    HasMeshShader = 0x04,
#pragma warning restore CS1591
}
