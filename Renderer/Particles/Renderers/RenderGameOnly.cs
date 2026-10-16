using ValveResourceFormat.Particles;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// A renderer that draws nothing in the viewer. Most hand their whole effect to the game: screen and
    /// foliage shakes, status effect material values, VR haptics, client physics and text the game sets
    /// on the system. The GPU isosurface mesh is not drawn.
    /// </summary>
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
