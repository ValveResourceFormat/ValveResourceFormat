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
        private readonly INumberProvider bias = new LiteralNumberProvider(0.5f);


        public InterpolateRadius(ParticleDefinitionParser parse) : base(parse)
        {
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            startScale = parse.NumberProvider("m_flStartScale", startScale);
            endScale = parse.NumberProvider("m_flEndScale", endScale);
            easeInAndOut = parse.Boolean("m_bEaseInAndOut", easeInAndOut);
            bias = parse.NumberProvider("m_flBias", bias);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            // Lifted out of the loop: all three are literal on most operators, and the systems here carry
            // a few dozen particles each, so an interface call per particle is most of what this costs
            var startScaleInput = this.startScale.Hoisted();
            var endScaleInput = this.endScale.Hoisted();
            var biasInput = bias.Hoisted();

            foreach (ref var particle in particles.Current)
            {
                var time = particle.NormalizedAge;

                if (time >= startTime && time <= endTime)
                {
                    var startScale = startScaleInput.Next(ref particle, particleSystemState);
                    var endScale = endScaleInput.Next(ref particle, particleSystemState);

                    var timeScale = MathUtils.Remap(time, startTime, endTime);

                    if (easeInAndOut)
                    {
                        timeScale = timeScale * timeScale * (3 - 2 * timeScale); // smoothstep
                    }

                    // An unbiased operator raises the time scale to the first power, and Pow(x, 1) is x
                    // for every x this can produce. Skipping it drops a transcendental per particle from
                    // the default case, which the hoisted bias is what makes visible.
                    var exponent = 1.0f - biasInput.Next(ref particle, particleSystemState);

                    if (exponent != 1.0f)
                    {
                        timeScale = MathF.Pow(timeScale, exponent);
                    }
                    var radiusScale = float.Lerp(startScale, endScale, timeScale);

                    particle.Radius = particle.GetInitialScalar(particles, ParticleField.Radius) * radiusScale;
                }
            }
        }
    }
}
