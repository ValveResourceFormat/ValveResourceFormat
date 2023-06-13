namespace GUI.Types.ParticleRenderer.Initializers
{
    interface IParticleInitializer
    {
        Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState);
    }
}
