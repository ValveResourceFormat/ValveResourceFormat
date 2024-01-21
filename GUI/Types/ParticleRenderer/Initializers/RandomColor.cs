namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomColor : ParticleFunctionInitializer
    {
        private readonly Vector3 colorMin = Vector3.One;
        private readonly Vector3 colorMax = Vector3.One;

        public RandomColor(ParticleDefinitionParser parse) : base(parse)
        {
            colorMin = parse.Color24("m_ColorMin", colorMin);
            colorMax = parse.Color24("m_ColorMax", colorMax);
            // lots of stuff with Tinting in hlvr.
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.Color = ParticleCollection.RandomBetween(particle.ParticleID, colorMin, colorMax);

            return particle;
        }
    }
}
