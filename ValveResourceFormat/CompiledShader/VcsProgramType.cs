namespace ValveResourceFormat.CompiledShader
{
    public enum VcsProgramType
    {
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
    };
}
