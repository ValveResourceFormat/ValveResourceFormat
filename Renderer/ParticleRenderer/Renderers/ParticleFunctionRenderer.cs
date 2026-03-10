namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Base class for all particle renderers. Renderers are responsible for drawing the visual
    /// representation of a particle collection each frame.
    /// </summary>
    abstract class ParticleFunctionRenderer : ParticleFunction
    {
        protected ParticleFunctionRenderer(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public abstract void Render(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Matrix4x4 modelViewMatrix);
        public abstract void SetRenderMode(string renderMode);
        public abstract IEnumerable<string> GetSupportedRenderModes();
        public abstract void SetWireframe(bool wireframe);
    }
}
