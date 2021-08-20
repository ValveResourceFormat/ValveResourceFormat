namespace ValveResourceFormat.ShaderParser
{
    public enum VcsFileType
    {
        Features,                   // features.vcs
        VertexShader,               // vs.vcs
        PixelShader,                // ps.vcs
        GeometryShader,             // gs.vcs
        HullShader,                 // hs.vcs
        DomainShader,               // ds.vcs
        // todo - ComputeShader needs implementation
        // (HullShader, DomainShader and RaytracingShader also need implementation, but examples of these are limited)
        ComputeShader,              // cs.vcs
        PixelShaderRenderState,     // psrs.vcs
        RaytracingShader,           // rtx.vcs
    };
}
