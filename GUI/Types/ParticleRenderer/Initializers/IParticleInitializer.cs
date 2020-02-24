namespace GUI.Types.ParticleRenderer.Initializers
{
    public interface IParticleInitializer
    {
        Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState);
    }
}
