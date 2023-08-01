namespace GUI.Types.ParticleRenderer.Initializers
{
    abstract class ParticleFunctionInitializer : ParticleFunction
    {
        public abstract Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState);
    }
}
