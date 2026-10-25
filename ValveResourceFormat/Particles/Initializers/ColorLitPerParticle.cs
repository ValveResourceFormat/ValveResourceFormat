using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the particle colour to a random value between two colours, tinted by the light arriving at
    /// the particle's own position. The light, quantised to a byte colour, is clamped between the tint colours and scaled by the
    /// light amplification before it is blended in by the tint percentage, which is 0 by default and
    /// leaves the random colour as is.
    /// </summary>
    /// <remarks>
    /// The position is declared as written for the first-writer rule but only read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_ColorLitPerParticle">C_INIT_ColorLitPerParticle</seealso>
    class ColorLitPerParticle : ParticleFunctionInitializer
    {
        private readonly Vector3 colorMin = Vector3.One;
        private readonly Vector3 colorMax = Vector3.One;
        private readonly Vector3 tintMin = Vector3.Zero;
        private readonly Vector3 tintMax = Vector3.One;
        private readonly float tintPercentage;
        private readonly ParticleColorBlendMode tintBlendMode = ParticleColorBlendMode.PARTICLEBLEND_DEFAULT;
        private readonly float lightAmplification = 1f;

        public ColorLitPerParticle(ParticleDefinitionParser parse) : base(parse)
        {
            colorMin = parse.Color24("m_ColorMin", colorMin);
            colorMax = parse.Color24("m_ColorMax", colorMax);
            tintMin = parse.Color24("m_TintMin", tintMin);
            tintMax = parse.Color24("m_TintMax", tintMax);
            tintPercentage = parse.Float("m_flTintPerc", tintPercentage);
            tintBlendMode = parse.Enum("m_nTintBlendMode", tintBlendMode);
            lightAmplification = parse.Float("m_flLightAmplification", lightAmplification);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.Color.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var positions = run.ReadVectors(ParticleField.Position);
            var colors = run.Vectors(ParticleField.Color);

            for (var i = 0; i < run.Length; i++)
            {
                var light = particleSystemState.Lighting.SampleAmbientLight(positions[i]);
                var tint = ParticleMath.LightTint(light, tintMin, tintMax, lightAmplification);

                var color = particleSystemState.Random.NextBetween(colorMin, colorMax);
                var tinted = tintBlendMode.Blend(color, tint);

                colors[i] = Vector3.Min(MathUtils.Lerp(color, tinted, tintPercentage), Vector3.One);
            }
        }
    }
}
