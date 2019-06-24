namespace GUI.Types.ParticleRenderer.Initializers
{
    public interface IParticleInitializer
    {
        Particle Initialize(Particle particle, ParticleSystemRenderState particleSystemState);
    }
}
