namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Interpolates a particle's color toward a per-particle randomly chosen target color (between a min and max fade color) over a normalized age range.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ColorInterpolateRandom">C_OP_ColorInterpolateRandom</seealso>
    class ColorInterpolateRandom : ParticleFunctionOperator
    {
        /// <summary>Table offsets separating this operator's red, green and blue draws.</summary>
        private const int RedOffset = 3;
        private const int GreenOffset = 7;
        private const int BlueOffset = 9;

        private readonly Vector3 colorFadeMin = Vector3.One;
        private readonly Vector3 colorFadeMax = Vector3.One;
        private readonly float fadeStartTime;
        private readonly float fadeEndTime = 1f;
        private readonly ParticleField fieldOutput = ParticleField.Color;
        private readonly bool easeInOut = true;

        public ColorInterpolateRandom(ParticleDefinitionParser parse) : base(parse)
        {
            colorFadeMin = parse.Color24("m_ColorFadeMin", colorFadeMin);
            colorFadeMax = parse.Color24("m_ColorFadeMax", colorFadeMax);
            fadeStartTime = parse.Float("m_flFadeStartTime", fadeStartTime);
            fadeEndTime = parse.Float("m_flFadeEndTime", fadeEndTime);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            easeInOut = parse.Boolean("m_bEaseInOut", easeInOut);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (fadeEndTime == fadeStartTime)
            {
                return;
            }

            foreach (ref var particle in particles.Current)
            {
                // Eased fades draw each channel separately, uneased fades scale all three by one draw
                var newColor = easeInOut
                    ? new Vector3(
                        particleSystemState.Random.ForParticleBetween(particle.ParticleId, RedOffset, colorFadeMin.X, colorFadeMax.X),
                        particleSystemState.Random.ForParticleBetween(particle.ParticleId, GreenOffset, colorFadeMin.Y, colorFadeMax.Y),
                        particleSystemState.Random.ForParticleBetween(particle.ParticleId, BlueOffset, colorFadeMin.Z, colorFadeMax.Z))
                    : particleSystemState.Random.ForParticleBetween(particle.ParticleId, RedOffset, colorFadeMin, colorFadeMax);

                var t = MathUtils.Saturate(MathUtils.Remap(particle.NormalizedAge, fadeStartTime, fadeEndTime));

                if (easeInOut)
                {
                    t = t * t * (3 - 2 * t);
                }

                particle.SetVector(fieldOutput, Vector3.Lerp(particle.GetInitialVector(particles, fieldOutput), newColor, t));
            }
        }
    }
}
