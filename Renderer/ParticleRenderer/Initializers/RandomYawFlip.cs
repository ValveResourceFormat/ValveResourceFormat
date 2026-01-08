namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class RandomYawFlip : ParticleFunctionInitializer
    {
        private readonly float percent;

        public RandomYawFlip(ParticleDefinitionParser parse) : base(parse)
        {
            percent = parse.Float("m_flPercent", 0.5f);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            if (Random.Shared.NextSingle() > percent)
            {
                particle.SetScalar(ParticleField.Yaw, particle.GetScalar(ParticleField.Yaw) + MathF.PI);
            }

            return particle;
        }
    }
}
