namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Flags for shader variables, describing which binding state the variable affects.
/// Stored in bits 8-15 of <see cref="VfxVariableDescription.ContextStateAffectedByVariable"/>;
/// the rasterizer state bit sits below this range in bit 7.
/// </summary>
[Flags]
public enum VariableFlags : byte
{
    /// <summary>Depth stencil state.</summary>
    DepthStencilState = 1 << 0,
    /// <summary>Blend state.</summary>
    BlendState = 1 << 1,
    /// <summary>Texture binding. Texture variables carry this together with <see cref="Sampler"/>.</summary>
    Texture = 1 << 3,
    /// <summary>Sampler binding.</summary>
    Sampler = 1 << 4,
    /// <summary>Constant data.</summary>
    Constant = 1 << 5,
    /// <summary>UAV binding.</summary>
    Uav = 1 << 6,
    /// <summary>External descriptor set binding.</summary>
    Bindless = 1 << 7,
}
