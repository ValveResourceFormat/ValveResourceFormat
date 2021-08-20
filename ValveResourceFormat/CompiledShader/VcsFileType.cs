namespace ValveResourceFormat.ShaderParser
{
    public enum VcsFileType
    {
        Undetermined,
        Any,
        Features,                   // features.vcs
        VertexShader,               // vs.vcs
        PixelShader,                // ps.vcs
        GeometryShader,             // gs.vcs
        // TODO - ComputeShader needs implementation
        ComputeShader,              // cs.vcs
        PotentialShadowReciever,    // psrs.vcs
    };
}
