namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets the particle radius to a random value between a minimum and maximum, with an optional
    /// random exponent to bias the distribution. Corresponds to <c>C_INIT_RandomRadius</c>.
    /// </summary>
    class RandomRadius : ParticleFunctionInitializer
    {
        private readonly float radiusMin = 1;
        private readonly float radiusMax = 1;
        private readonly float radiusRandomExponent = 1;

        public RandomRadius(ParticleDefinitionParser parse) : base(parse)
        {
            radiusMin = parse.Float("m_flRadiusMin", radiusMin);
            radiusMax = parse.Float("m_flRadiusMax", radiusMax);
            radiusRandomExponent = parse.Float("m_flRadiusRandExponent", radiusRandomExponent);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.Radius = ParticleCollection.RandomWithExponentBetween(particle.ParticleID, radiusRandomExponent, radiusMin, radiusMax);

            return particle;
        }
    }
}
