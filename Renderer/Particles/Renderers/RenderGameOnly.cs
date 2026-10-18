using ValveResourceFormat.Particles;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// A renderer that draws nothing in the viewer. Most hand their whole effect to the game: screen and
    /// foliage shakes, status effect material values, VR haptics, client physics and text the game sets
    /// on the system. The GPU isosurface mesh is not drawn.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ClientPhysics">C_OP_ClientPhysics</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderGpuImplicit">C_OP_RenderGpuImplicit</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderScreenShake">C_OP_RenderScreenShake</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderStatusEffect">C_OP_RenderStatusEffect</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderStatusEffectCitadel">C_OP_RenderStatusEffectCitadel</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderText">C_OP_RenderText</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderTreeShake">C_OP_RenderTreeShake</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderVRHapticEvent">C_OP_RenderVRHapticEvent</seealso>
    internal sealed class RenderGameOnly(ParticleDefinitionParser parse) : ParticleFunctionRenderer(parse)
    {
        /// <inheritdoc/>
        public override bool DrawsInPasses => false;

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
        }
    }
}
