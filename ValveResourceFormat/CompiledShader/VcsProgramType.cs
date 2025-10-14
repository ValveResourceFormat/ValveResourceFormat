namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Shader program types.
    /// </summary>
    public enum VcsProgramType
    {
#pragma warning disable CS1591
        Features,                   // features.vcs
        VertexShader,               // vs.vcs
        PixelShader,                // ps.vcs
        GeometryShader,             // gs.vcs
        HullShader,                 // hs.vcs
        DomainShader,               // ds.vcs
        ComputeShader,              // cs.vcs
        PixelShaderRenderState,     // psrs.vcs
        RaytracingShader,           // rtx.vcs
        MeshShader,                 // ms.vcs
        Undetermined,
#pragma warning restore CS1591
    };
}
