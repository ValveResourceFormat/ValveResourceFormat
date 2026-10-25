namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Interpolates color toward a per-particle random fade color between min and max over a normalized age range.
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

            parse.AddInitialRead(fieldOutput);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (fadeEndTime == fadeStartTime)
            {
                return;
            }

            if (!ParticleBuffer.IsVector(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var ages = current.ReadAges();

            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var particleIds = current.ReadIntegers(ParticleField.ParticleId);
            var colors = current.Vectors(fieldOutput);
            var initialColors = particles.Initial.ReadVectors(fieldOutput);

            for (var i = 0; i < colors.Length; i++)
            {
                var lifetime = lifetimes[i];

                if (lifetime <= 0f)
                {
                    continue;
                }

                // Eased fades draw each channel separately, uneased fades scale all three by one draw
                var particleId = particleIds[i];
                var newColor = easeInOut
                    ? new Vector3(
                        particleSystemState.Random.ForParticleBetween(particleId, RedOffset, colorFadeMin.X, colorFadeMax.X),
                        particleSystemState.Random.ForParticleBetween(particleId, GreenOffset, colorFadeMin.Y, colorFadeMax.Y),
                        particleSystemState.Random.ForParticleBetween(particleId, BlueOffset, colorFadeMin.Z, colorFadeMax.Z))
                    : particleSystemState.Random.ForParticleBetween(particleId, RedOffset, colorFadeMin, colorFadeMax);

                var t = ColorInterpolate.FadeFraction(ages[i] / lifetime, fadeStartTime, fadeEndTime, easeInOut);

                colors[i] = MathUtils.Lerp(initialColors[i], newColor, t);
            }
        }
    }
}
