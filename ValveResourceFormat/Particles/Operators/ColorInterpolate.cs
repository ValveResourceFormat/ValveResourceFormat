using System.Numerics.Tensors;

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
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var colors = current.Vectors(fieldOutput);
            var initialColors = particles.Initial.ReadVectors(fieldOutput);

            // Each age is replaced by its fade fraction, which particles without a lifetime never use
            var fractions = current.ReadAges();
            TensorPrimitives.Divide(fractions, lifetimes, fractions);
            TensorPrimitives.Subtract(fractions, fadeStartTime, fractions);
            TensorPrimitives.Divide(fractions, fadeEndTime - fadeStartTime, fractions);
            TensorPrimitives.Clamp(fractions, 0f, 1f, fractions);

            if (easeInOut)
            {
                var easing = ParticleScratch.Floats(fractions.Length);
                TensorPrimitives.Multiply(fractions, -2f, easing);
                TensorPrimitives.Add(easing, 3f, easing);
                TensorPrimitives.Multiply(easing, fractions, easing);
                TensorPrimitives.Multiply(easing, fractions, fractions);
            }

            for (var i = 0; i < colors.Length; i++)
            {
                var initialColor = initialColors[i];
                var color = colors[i];

                if (lifetimes[i] > 0f)
                {
                    color = MathUtils.Lerp(initialColor, colorFade, fractions[i]);
                }

                colors[i] = BlendByStrength(initialColor, color, strength);
            }
        }

        /// <summary>
        /// How far into the fade from <paramref name="start"/> to <paramref name="end"/> a normalized age
        /// is, eased along a smoothstep when asked to.
        /// </summary>
        internal static float FadeFraction(float normalizedAge, float start, float end, bool easeInOut)
            => easeInOut
                ? MathUtils.Smoothstep(start, end, normalizedAge)
                : MathUtils.Saturate(MathUtils.Remap(normalizedAge, start, end));
    }
}
