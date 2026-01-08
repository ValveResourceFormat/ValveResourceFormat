namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    abstract class ParticleFunctionInitializer : ParticleFunction
    {
        protected ParticleFunctionInitializer(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public abstract Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState);
    }
}
