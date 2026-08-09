namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets the initial roll speed of a particle by adding a configurable base speed to a random
    /// offset sampled between a minimum and maximum degree-per-second range, with an optional
    /// exponent bias and random direction flip.
    /// </summary>
    /// <remarks>
    /// <c>m_nFieldOutput</c> is parsed but the engine always writes the roll speed.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomRotationSpeed">C_INIT_RandomRotationSpeed</seealso>
    class RandomRotationSpeed : ParticleFunctionInitializer
    {
        private readonly bool randomlyFlipDirection = true;
        private readonly float degrees;
        private readonly float degreesMin;
        private readonly float degreesMax = 360f;
        private readonly float randomExponent = 1f;

        public RandomRotationSpeed(ParticleDefinitionParser parse) : base(parse)
        {
            _ = parse.ParticleField("m_nFieldOutput", ParticleField.Roll);
            randomlyFlipDirection = parse.Boolean("m_bRandomlyFlipDirection", randomlyFlipDirection);
            degrees = parse.Float("m_flDegrees", degrees);
            degreesMin = parse.Float("m_flDegreesMin", degreesMin);
            degreesMax = parse.Float("m_flDegreesMax", degreesMax);
            randomExponent = parse.Float("m_flRotationRandExponent", randomExponent);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var value = float.DegreesToRadians(degrees + particleSystemState.Random.NextWithExponentBetween(randomExponent, degreesMin, degreesMax));

            if (randomlyFlipDirection && particleSystemState.Random.Next() < 0.5f)
            {
                value *= -1;
            }

            particle.SetScalar(ParticleField.RollSpeed, value);

            return particle;
        }
    }
}
