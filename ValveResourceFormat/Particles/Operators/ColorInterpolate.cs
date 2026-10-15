namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Interpolates a particle's color from its initial value toward a target fade color over a normalized age range.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ColorInterpolate">C_OP_ColorInterpolate</seealso>
    class ColorInterpolate : ParticleFunctionOperator
    {
        private readonly Vector3 colorFade = Vector3.One;
        private readonly float fadeStartTime;
        private readonly float fadeEndTime = 1f;
        private readonly ParticleField fieldOutput = ParticleField.Color;
        private readonly bool easeInOut = true;

        public ColorInterpolate(ParticleDefinitionParser parse) : base(parse)
        {
            colorFade = parse.Color24("m_ColorFade", colorFade);
            fadeStartTime = parse.Float("m_flFadeStartTime", fadeStartTime);
            fadeEndTime = parse.Float("m_flFadeEndTime", fadeEndTime);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            easeInOut = parse.Boolean("m_bEaseInOut", easeInOut);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (fadeEndTime == fadeStartTime)
            {
                return;
            }

            var inverseFadeLength = 1f / (fadeEndTime - fadeStartTime);

            foreach (ref var particle in particles.Current)
            {
                var initialColor = particle.GetInitialVector(particles, fieldOutput);
                var color = particle.GetVector(fieldOutput);

                if (particle.Lifetime > 0f)
                {
                    var t = MathUtils.Saturate(((particle.Age / particle.Lifetime) - fadeStartTime) * inverseFadeLength);

                    if (easeInOut)
                    {
                        t = MathUtils.Smoothstep(0f, 1f, t);
                    }

                    color = Vector3.Lerp(initialColor, colorFade, t);
                }

                particle.SetVector(fieldOutput, BlendByStrength(initialColor, color, strength));
            }
        }
    }
}
