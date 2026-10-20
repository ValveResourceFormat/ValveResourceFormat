namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Base class for spin operators that rotate particles at a constant rate.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CGeneralSpin">CGeneralSpin</seealso>
    abstract class CGeneralSpin : ParticleFunctionOperator
    {
        protected readonly int spinRateDegrees;
        protected readonly int spinRateMinDegrees;
        protected readonly float spinRateStopTime;

        protected CGeneralSpin(ParticleDefinitionParser parse) : base(parse)
        {
            spinRateDegrees = parse.Int32("m_nSpinRateDegrees", spinRateDegrees);
            spinRateMinDegrees = parse.Int32("m_nSpinRateMinDegrees", spinRateMinDegrees);
            spinRateStopTime = parse.Float("m_fSpinRateStopTime", spinRateStopTime);
        }

        /// <summary>
        /// Advances <paramref name="field"/> of every particle by this step's spin. The rate is in degrees
        /// per second, and the converted rate is scaled by an extra 2*pi (57 deg/s spins roughly one full
        /// turn per second). Strength scales the rate, and a zero result spins nothing. With a stop time,
        /// the step decays linearly to 0 over that fraction of the particle's lifetime; without one, a
        /// step longer than a turn wraps. The minimum rate, which strength does not scale, floors the
        /// step either way. The angle itself is kept within one turn either side of 0, except while the
        /// step is one the drawing layer blends back from, so the blend never crosses a wrap.
        /// </summary>
        protected void Rotate(ParticleCollection particles, ParticleField field, float frameTime, float strength, ParticleSystemState systemState)
        {
            var rate = strength * float.DegreesToRadians(spinRateDegrees);

            if (rate == 0f)
            {
                return;
            }

            var wrap = systemState.Data?.TryGetRenderBlend(out _) != true;

            var step = MathF.Abs(rate * MathF.Tau) * frameTime;

            if (spinRateStopTime == 0f)
            {
                step %= MathF.Tau;
            }

            if (rate < 0f)
            {
                step = -step;
            }

            var minimumStep = float.DegreesToRadians(spinRateMinDegrees) * MathF.Tau * frameTime;

            foreach (ref var particle in particles.Current)
            {
                var stopAge = particle.Lifetime * spinRateStopTime;
                var remaining = spinRateStopTime == 0f ? 1f : stopAge != 0f ? MathF.Max(0f, 1f - (particle.Age / stopAge)) : 0f;
                var delta = remaining * step;

                delta = rate >= 0f
                    ? (delta <= minimumStep ? minimumStep : delta)
                    : (minimumStep <= delta ? minimumStep : delta);

                var angle = particle.GetScalar(field) + delta;

                if (wrap)
                {
                    if (angle >= MathF.Tau)
                    {
                        angle -= MathF.Tau;
                    }

                    if (angle <= -MathF.Tau)
                    {
                        angle += MathF.Tau;
                    }
                }

                particle.SetScalar(field, angle);
            }
        }
    }

    /// <summary>
    /// Continuously rotates a particle's roll angle at a constant spin rate (in degrees per second)
    /// until the particle exceeds the spin stop time.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Spin">C_OP_Spin</seealso>
    class Spin : CGeneralSpin
    {
        public Spin(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            Rotate(particles, ParticleField.Roll, frameTime, strength, particleSystemState);
        }
    }

    /// <summary>
    /// Continuously rotates a particle's yaw angle at a constant spin rate (in degrees per second)
    /// until the particle exceeds the spin stop time.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SpinYaw">C_OP_SpinYaw</seealso>
    class SpinYaw : CGeneralSpin
    {
        public SpinYaw(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            Rotate(particles, ParticleField.Yaw, frameTime, strength, particleSystemState);
        }
    }
}
