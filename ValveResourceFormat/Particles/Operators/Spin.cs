using System.Numerics.Tensors;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Base class for spin operators that rotate particles at a constant rate.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CGeneralSpin">CGeneralSpin</seealso>
    abstract class CGeneralSpin : ParticleFunctionOperator
    {
        private readonly int spinRateDegrees;
        private readonly int spinRateMinDegrees;
        private readonly float spinRateStopTime;

        protected CGeneralSpin(ParticleDefinitionParser parse) : base(parse)
        {
            spinRateDegrees = parse.Int32("m_nSpinRateDegrees", spinRateDegrees);
            spinRateMinDegrees = parse.Int32("m_nSpinRateMinDegrees", spinRateMinDegrees);
            spinRateStopTime = parse.Float("m_fSpinRateStopTime", spinRateStopTime);
        }

        /// <summary>The angle this spin turns.</summary>
        protected abstract ParticleField SpinField { get; }

        /// <inheritdoc/>
        public override ulong StoredFields => spinRateDegrees == 0 ? 0 : SpinField.Mask();

        /// <summary>
        /// Advances the spin angle of every particle by this step's spin. The rate is in degrees
        /// per second, and the converted rate is scaled by an extra 2*pi (57 deg/s spins roughly one full
        /// turn per second). Strength scales the rate, and a zero result spins nothing. With a stop time,
        /// the step decays linearly to 0 over that fraction of the particle's lifetime; without one, a
        /// step longer than a turn wraps. The minimum rate, which strength does not scale, floors the
        /// step either way. The angle itself is kept within one turn either side of 0, except while the
        /// step is one the drawing layer blends back from, so the blend never crosses a wrap.
        /// </summary>
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var rate = strength * float.DegreesToRadians(spinRateDegrees);

            if (rate == 0f)
            {
                return;
            }

            var wrap = particleSystemState.Data?.TryGetRenderBlend(out _) != true;

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

            var current = particles.Current;
            var angles = current.Floats(SpinField);

            if (spinRateStopTime == 0f)
            {
                var delta = rate >= 0f ? MathF.Max(step, minimumStep) : MathF.Min(step, minimumStep);
                TensorPrimitives.Add(angles, delta, angles);
            }
            else
            {
                var stopAges = ParticleScratch.Floats(angles.Length);
                var deltas = current.ReadAges();

                // The step decays linearly from the full step at birth to 0 at the stop age
                TensorPrimitives.Multiply(current.ReadFloats(ParticleField.LifeDuration), spinRateStopTime, stopAges);
                TensorPrimitives.Divide(deltas, stopAges, deltas);
                TensorPrimitives.Subtract(1f, deltas, deltas);
                TensorPrimitives.MaxNumber(deltas, 0f, deltas);

                // A particle with no lifetime to stop over does not spin
                for (var i = 0; i < deltas.Length; i++)
                {
                    if (stopAges[i] == 0f)
                    {
                        deltas[i] = 0f;
                    }
                }

                TensorPrimitives.Multiply(deltas, step, deltas);

                if (rate >= 0f)
                {
                    TensorPrimitives.Max(deltas, minimumStep, deltas);
                }
                else
                {
                    TensorPrimitives.Min(deltas, minimumStep, deltas);
                }

                TensorPrimitives.Add(angles, deltas, angles);
            }

            if (wrap)
            {
                TensorPrimitives.Remainder(angles, MathF.Tau, angles);
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

        protected override ParticleField SpinField => ParticleField.Roll;
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

        protected override ParticleField SpinField => ParticleField.Yaw;
    }
}
