namespace ValveResourceFormat.Renderer.Particles.Operators
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
        private readonly ParticleField FieldOutput = ParticleField.Color;
        private readonly bool easeInOut = true;

        public ColorInterpolate(ParticleDefinitionParser parse) : base(parse)
        {
            colorFade = parse.Color24("m_ColorFade", colorFade);
            fadeStartTime = parse.Float("m_flFadeStartTime", fadeStartTime);
            fadeEndTime = parse.Float("m_flFadeEndTime", fadeEndTime);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            easeInOut = parse.Boolean("m_bEaseInOut", easeInOut);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var time = particle.NormalizedAge;

                if (time >= fadeStartTime && time <= fadeEndTime)
                {
                    var t = MathUtils.Remap(time, fadeStartTime, fadeEndTime);
                    if (easeInOut)
                    {
                        // Smoothstep easing
                        t = t * t * (3 - 2 * t);
                    }

                    // Interpolate from constant color to fade color
                    particle.SetVector(FieldOutput, Vector3.Lerp(particle.GetInitialVector(particles, ParticleField.Color), colorFade, t));
                }
            }
        }
    }
}
