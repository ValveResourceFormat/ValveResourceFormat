using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.Particles.Renderers;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles;

/// <summary>
/// Builds the renderers a particle system draws through. These are the only particle functions that
/// need a graphics backend, which is why they are created here rather than by the simulation.
/// </summary>
public static class ParticleRendererFactory
{
    /// <summary>
    /// Creates the renderer for a Source 2 renderer class name.
    /// </summary>
    /// <returns><see langword="null"/> when the class is not implemented.</returns>
    internal static ParticleFunctionRenderer? Create(string className, ParticleDefinitionParser definition, RendererContext rendererContext, Scene scene) => className switch
    {
        "C_OP_RenderSprites" => new RenderSprites(definition, rendererContext),
        "C_OP_RenderCables" => new RenderCables(definition, rendererContext, scene),
        "C_OP_RenderRopes" => new RenderRopes(definition, rendererContext),
        "C_OP_RenderTrails" => new RenderTrails(definition, rendererContext),
        "C_OP_RenderSound" => new RenderSound(definition),
        "C_OP_RenderStandardLight" => new RenderStandardLight(definition, rendererContext, scene),
        "C_OP_RenderOmni2Light" => new RenderOmni2Light(definition, rendererContext, scene),
        _ => null,
    };

    /// <summary>
    /// Checks whether the given Source 2 class name is a supported renderer.
    /// </summary>
    public static bool IsSupported(string className) => className switch
    {
        "C_OP_RenderSprites" or "C_OP_RenderCables" or "C_OP_RenderRopes" or "C_OP_RenderTrails"
            or "C_OP_RenderSound" or "C_OP_RenderStandardLight" or "C_OP_RenderOmni2Light" => true,
        _ => false,
    };
}
