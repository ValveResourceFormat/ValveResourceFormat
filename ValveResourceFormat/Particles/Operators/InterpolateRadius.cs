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
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (endTime <= startTime)
            {
                return;
            }

            foreach (ref var particle in particles.Current)
            {
                var time = particle.NormalizedAge;

                if (time >= startTime && time < endTime)
                {
                    var startScale = this.startScale.NextNumber(ref particle, particleSystemState);
                    var endScale = this.endScale.NextNumber(ref particle, particleSystemState);

                    var timeScale = MathUtils.Remap(time, startTime, endTime);

                    if (easeInAndOut)
                    {
                        timeScale = timeScale * timeScale * (3 - 2 * timeScale); // smoothstep
                    }
                    else if (bias != 0.5f)
                    {
                        timeScale = ParticleMath.Bias(timeScale, bias);
                    }

                    var radiusScale = float.Lerp(startScale, endScale, timeScale);

                    particle.Radius = particle.GetInitialScalar(particles, ParticleField.Radius) * radiusScale;
                }
            }
        }
    }
}
