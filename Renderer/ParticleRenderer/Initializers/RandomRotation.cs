namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets the initial rotation of a particle by adding a configurable base angle to a random
    /// offset sampled between a minimum and maximum degree range, with an optional exponent bias
    /// and random direction flip. Corresponds to <c>C_INIT_RandomRotation</c>.
    /// </summary>
    class RandomRotation : ParticleFunctionInitializer
    {
        private readonly float degreesMin;
        private readonly float degreesMax = 360f;
        private readonly float degreesOffset;
        private readonly float randomExponent = 1.0f;
        private readonly ParticleField FieldOutput = ParticleField.Roll;
        private readonly bool randomlyFlipDirection;

        public RandomRotation(ParticleDefinitionParser parse) : base(parse)
        {
            degreesMin = parse.Float("m_flDegreesMin", degreesMin);
            degreesMax = parse.Float("m_flDegreesMax", degreesMax);
            degreesOffset = parse.Float("m_flDegrees", degreesOffset);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            randomlyFlipDirection = parse.Boolean("m_bRandomlyFlipDirection", randomlyFlipDirection);
            randomExponent = parse.Float("m_flRotationRandExponent", randomExponent);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var degrees = degreesOffset + ParticleCollection.RandomWithExponentBetween(particle.ParticleID, randomExponent, degreesMin, degreesMax);
            if (randomlyFlipDirection && Random.Shared.NextSingle() > 0.5f)
            {
                degrees *= -1;
            }

            particle.SetScalar(FieldOutput, float.DegreesToRadians(degrees));

            return particle;
        }
    }
}
