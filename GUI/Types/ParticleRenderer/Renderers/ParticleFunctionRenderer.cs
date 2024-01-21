namespace GUI.Types.ParticleRenderer.Renderers
{
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
