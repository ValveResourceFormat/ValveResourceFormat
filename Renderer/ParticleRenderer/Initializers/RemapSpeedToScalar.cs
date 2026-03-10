namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Remaps the speed of a particle (or its control point) from an input range to a scalar output field.
    /// When per-particle mode is enabled, the particle's own speed is used; otherwise the control point speed drives the output.
    /// Corresponds to <c>C_INIT_RemapSpeedToScalar</c>.
    /// </summary>
    class RemapSpeedToScalar : ParticleFunctionInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 10;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        private readonly bool perParticle;

        public RemapSpeedToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            perParticle = parse.Boolean("m_bPerParticle", perParticle);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            if (!perParticle)
            {
                // I think it depends on the speed of the control point, which we don't track.
                return particle;
            }
            var particleCount = Math.Clamp(particle.ParticleID, inputMin, inputMax);

            var output = MathUtils.RemapRange(particleCount, inputMin, inputMax, outputMin, outputMax);

            particle.SetScalar(FieldOutput, output);

            return particle;
        }
    }
}
