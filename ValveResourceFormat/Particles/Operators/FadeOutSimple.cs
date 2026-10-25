namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha field out along a smoothstep over a proportional fade-out time at the end of the particle's
    /// life. It only ever lowers the field, never raising a value another operator has already brought down.
    /// </summary>
    /// <remarks>
    /// The fade-out time is a fraction of the particle's lifetime in the 0-1 range: a setting of
    /// 0.25 on a particle with a 4-second lifetime starts fading 3 seconds after emission and
    /// takes 1 second to fade out completely.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeOutSimple">C_OP_FadeOutSimple</seealso>
    class FadeOutSimple : ParticleFunctionOperator
    {
        private readonly float fadeOutTime = 0.25f;
        private readonly ParticleField fieldOutput = ParticleField.Alpha;

        public FadeOutSimple(ParticleDefinitionParser parse) : base(parse)
        {
            fadeOutTime = parse.Float("m_flFadeOutTime", fadeOutTime);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);

            parse.AddInitialRead(fieldOutput);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var fadeStart = 1f - fadeOutTime;

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
                var time = ages[i] / lifetimes[i];
                var fading = fadeStart < time;

                if (!fading)
                {
                    continue;
                }

                var faded = (1f - MathUtils.Smoothstep(fadeStart, 1f, time)) * initials[i];
                outputs[i] = MathF.Min(faded, outputs[i]);
            }
        }
    }
}
