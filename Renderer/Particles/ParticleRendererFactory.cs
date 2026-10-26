using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.Particles.Renderers;

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
        "C_OP_RenderSprites" => new RenderSprites(definition, rendererContext, scene),
        "C_OP_RenderCables" => new RenderCables(definition, rendererContext, scene),
        "C_OP_RenderRopes" => new RenderRopes(definition, rendererContext, scene),
        "C_OP_RenderTrails" => new RenderTrails(definition, rendererContext, scene),
        "C_OP_RenderSound" => new RenderSound(definition),
        "C_OP_RenderStandardLight" => new RenderStandardLight(definition, scene),
        "C_OP_RenderOmni2Light" => new RenderOmni2Light(definition, scene),
        "C_OP_RenderModels" => new RenderModels(definition, rendererContext, scene),
        "C_OP_RenderProjected" => new RenderProjected(definition, scene),
        "C_OP_RenderBlobs" => new RenderBlobs(definition, rendererContext, scene),
        "C_OP_RenderDeferredLight" => RenderDeferredLight.GameHasDeferredLights(rendererContext)
            ? new RenderDeferredLight(definition, scene)
            : new RenderGameOnly(definition),
        "C_OP_RenderLightBeam" => new RenderLightBeam(definition, scene),
        "C_OP_RenderScreenVelocityRotate" => new RenderScreenVelocityRotate(definition),
        _ when IsGameOnly(className) => new RenderGameOnly(definition),
        _ => null,
    };

    /// <summary>
    /// Checks whether the given Source 2 class name is a supported renderer.
    /// </summary>
    public static bool IsSupported(string className) => className switch
    {
        "C_OP_RenderSprites" or "C_OP_RenderCables" or "C_OP_RenderRopes" or "C_OP_RenderTrails"
            or "C_OP_RenderSound" or "C_OP_RenderStandardLight" or "C_OP_RenderOmni2Light" or "C_OP_RenderModels"
            or "C_OP_RenderProjected" or "C_OP_RenderDeferredLight" or "C_OP_RenderLightBeam"
            or "C_OP_RenderBlobs" or "C_OP_RenderScreenVelocityRotate" => true,
        _ => IsGameOnly(className),
    };

    /// <summary>Renderers that draw nothing in the viewer, so they are created as no-ops rather than reported.</summary>
    private static bool IsGameOnly(string className) => className is "C_OP_ClientPhysics" or "C_OP_RenderGpuImplicit"
        or "C_OP_RenderScreenShake" or "C_OP_RenderStatusEffect" or "C_OP_RenderStatusEffectCitadel" or "C_OP_RenderText"
        or "C_OP_RenderTonemapController" or "C_OP_RenderTreeShake" or "C_OP_RenderVRHapticEvent";
}
