using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Scales a particle's radius by interpolating between a start scale and end scale over a
    /// specified time window of the particle's normalized lifetime, with an optional bias applied
    /// to the interpolation curve.
    /// </summary>
    /// <remarks>
    /// "Radius Scale" in the particle editor. Multiple instances can be used in one effect as
    /// long as their time windows don't overlap.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_InterpolateRadius">C_OP_InterpolateRadius</seealso>
    class InterpolateRadius : ParticleFunctionOperator
    {
        private readonly float startTime;
        private readonly float endTime = 1;
        private readonly INumberProvider startScale = new LiteralNumberProvider(1);
        private readonly INumberProvider endScale = new LiteralNumberProvider(1);
        private readonly bool easeInAndOut;

        /// <summary>The bias curve's parameter. 0.5 is the identity, and an authored 0 means 0.5.</summary>
        private readonly float bias = 0.5f;

        public InterpolateRadius(ParticleDefinitionParser parse) : base(parse)
        {
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            startScale = parse.NumberProvider("m_flStartScale", startScale);
            endScale = parse.NumberProvider("m_flEndScale", endScale);
            easeInAndOut = parse.Boolean("m_bEaseInAndOut", easeInAndOut);

            var authoredBias = parse.Float("m_flBias", bias);
            bias = authoredBias == 0f ? 0.5f : authoredBias;

            parse.AddInitialRead(ParticleField.Radius);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Radius.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (endTime <= startTime)
            {
                return;
            }

            var current = particles.Current;
            var radii = current.Floats(ParticleField.Radius);
            var initialRadii = particles.Initial.ReadFloats(ParticleField.Radius);
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var ages = current.ReadAges();

            var startScales = startScale.Fill(current, particleSystemState);
            var endScales = endScale.Fill(current, particleSystemState);

            for (var i = 0; i < radii.Length; i++)
            {
                var lifetime = lifetimes[i];
                var time = ParticleMath.NormalizedAge(ages[i], lifetime);
                var frameFraction = frameTime / lifetime;

                // A window shorter than a frame would otherwise fall between two updates and never
                // be entered, so the window's far end carries a frame's worth of slack
                var shortWindow = lifetime * (endTime - startTime) <= frameTime;
                var windowEnd = shortWindow ? startTime + frameFraction : endTime;

                var scaling = lifetime > 0f && time >= startTime && time <= frameFraction + windowEnd;

                if (!scaling)
                {
                    continue;
                }

                var timeScale = (time - startTime) / (endTime - startTime);

                if (easeInAndOut)
                {
                    // The smoothstep curve without clamping, which the slack past the window's end runs beyond
                    timeScale = timeScale * timeScale * (3f - (2f * timeScale));
                }
                else if (bias != 0.5f)
                {
                    timeScale = ParticleMath.Bias(timeScale, bias);
                }

                var start = startScales[i];
                var end = endScales[i];
                var radiusScale = MathUtils.Lerp(start, end, timeScale);

                // Clamped between the two scales in whichever order they come
                radiusScale = Math.Clamp(radiusScale, MathF.Min(start, end), MathF.Max(start, end));

                radii[i] = initialRadii[i] * radiusScale;
            }
        }
    }
}
