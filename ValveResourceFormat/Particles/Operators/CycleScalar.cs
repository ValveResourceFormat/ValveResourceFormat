using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Cycles a scalar attribute between two values as a triangle wave: the value rises to
    /// <c>m_flEndValue</c> at half the cycle and returns to <c>m_flStartValue</c> at the end of it.
    /// With <c>m_bDoNotRepeatCycle</c> the wave becomes a ramp that reaches the end value after one
    /// cycle and holds there.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CycleScalar">C_OP_CycleScalar</seealso>
    class CycleScalar : ParticleFunctionOperator
    {
        private readonly ParticleField destField = ParticleField.Alpha;
        private readonly float startValue;
        private readonly float endValue = 1f;
        private readonly float cycleTime = 1f;
        private readonly bool doNotRepeatCycle;
        private readonly bool synchronizeParticles;
        private readonly int cpScale = -1;
        private readonly int cpFieldMin;
        private readonly int cpFieldMax;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public CycleScalar(ParticleDefinitionParser parse) : base(parse)
        {
            destField = parse.ParticleField("m_nDestField", destField);
            startValue = parse.Float("m_flStartValue", startValue);
            endValue = parse.Float("m_flEndValue", endValue);
            cycleTime = parse.Float("m_flCycleTime", cycleTime);
            doNotRepeatCycle = parse.Boolean("m_bDoNotRepeatCycle", doNotRepeatCycle);
            synchronizeParticles = parse.Boolean("m_bSynchronizeParticles", synchronizeParticles);
            cpScale = Math.Clamp(parse.Int32("m_nCPScale", cpScale), -1, 64);
            cpFieldMin = Math.Clamp(parse.Int32("m_nCPFieldMin", cpFieldMin), -1, 2);
            cpFieldMax = Math.Clamp(parse.Int32("m_nCPFieldMax", cpFieldMax), -1, 2);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddScale(cpScale, cpFieldMin);
            defaults.AddScale(cpScale, cpFieldMax);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var start = startValue;
            var end = endValue;

            if (cpScale >= 0)
            {
                var controlPoint = particleSystemState.GetControlPoint(cpScale).Position;

                if (cpFieldMin >= 0)
                {
                    start *= controlPoint.GetComponent(cpFieldMin);
                }

                if (cpFieldMax >= 0)
                {
                    end *= controlPoint.GetComponent(cpFieldMax);
                }
            }

            // A cycle that does not repeat ramps up over the whole cycle instead of half of it
            var rate = 1f / MathF.Max(ParticleMath.FloatEpsilon, cycleTime);
            var clamp = doNotRepeatCycle ? cycleTime : float.MaxValue;

            if (doNotRepeatCycle)
            {
                rate *= 0.5f;
            }

            foreach (var particle in particles.Current)
            {
                var time = synchronizeParticles ? particleSystemState.Age : particle.Age;
                var phase = MathF.Min(time, clamp) * rate;
                phase -= MathF.Truncate(phase);

                var triangle = 2f * MathF.Min(phase, 1f - phase);
                var value = MathUtils.Lerp(start, end, triangle);

                particle.SetScalar(destField, particle.ModifyScalarBySetMethod(particles, destField, value, setMethod));
            }
        }
    }
}
