namespace ValveResourceFormat.Renderer.Particles.Initializers
{
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
