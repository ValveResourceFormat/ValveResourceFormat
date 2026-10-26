namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha field in along a smoothstep over a proportional fade-in time at the start of the particle's life.
    /// </summary>
    /// <remarks>
    /// The fade-in time is a fraction of the particle's lifetime in the 0-1 range: a setting of
    /// 0.5 on a particle with a 4-second lifetime takes 2 seconds to fade in completely.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeInSimple">C_OP_FadeInSimple</seealso>
    class FadeInSimple : ParticleFunctionOperator
    {
        private readonly float fadeInTime = 0.25f;
        private readonly ParticleField fieldOutput = ParticleField.Alpha;

        public FadeInSimple(ParticleDefinitionParser parse) : base(parse)
        {
            fadeInTime = parse.Float("m_flFadeInTime", fadeInTime);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);

            parse.AddInitialRead(fieldOutput);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var ages = current.ReadAges();

            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var outputs = current.Floats(fieldOutput);
            var initials = particles.Initial.ReadFloats(fieldOutput);

            for (var i = 0; i < outputs.Length; i++)
            {
                var age = ages[i];
                var fadeInLength = fadeInTime * lifetimes[i];
                var fading = MathF.Max(0f, age - frameTime) < fadeInLength;

                if (!fading)
                {
                    continue;
                }

                // A fading particle has a positive fade length, so the smoothstep's edges cannot meet here
                outputs[i] = MathUtils.Smoothstep(0f, fadeInLength, age) * initials[i];
            }
        }
    }
}
