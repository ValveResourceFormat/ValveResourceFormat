namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Remaps how much of each new particle is visible on screen to a scalar attribute. Every point
    /// counts as fully visible, so the attribute gets the remap of 1, which is the output maximum with
    /// the default input range. An alpha output has both ends of the output range clamped to [0, 1].
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapInitialVisibilityScalar">C_INIT_RemapInitialVisibilityScalar</seealso>
    class RemapInitialVisibilityScalar : ParticleFunctionInitializer
    {
        private const float Visibility = 1f;

        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        public RemapInitialVisibilityScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);

            if (fieldOutput.IsAlpha())
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            particle.SetScalar(fieldOutput, MathUtils.RemapValClamped(Visibility, inputMin, inputMax, outputMin, outputMax));

            return particle;
        }
    }
}
