namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps one component of a control point's position from an input range onto an output range
    /// and writes it into a scalar particle field, so a control point can drive a field directly.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapCPtoScalar">C_OP_RemapCPtoScalar</seealso>
    class RemapCPtoScalar : ParticleFunctionOperator
    {
        private readonly int cpInput;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly int field;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly float startTime = -1f;
        private readonly float endTime = -1f;
        private readonly float interpRate;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        private bool interpolating;
        private float lastInput;
        private float interpolationStart;

        public RemapCPtoScalar(ParticleDefinitionParser parse) : base(parse)
        {
            cpInput = parse.Int32("m_nCPInput", cpInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            field = Math.Clamp(parse.Int32("m_nField", field), 0, 2);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            interpRate = parse.Float("m_flInterpRate", interpRate);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        public override void Reset()
        {
            interpolating = false;
            interpolationStart = 0f;
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var outputMin = this.outputMin;
            var outputMax = this.outputMax;

            if (fieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }

            var input = particleSystemState.GetControlPoint(cpInput).Position;
            var component = field switch
            {
                0 => input.X,
                1 => input.Y,
                _ => input.Z,
            };

            var interpolation = interpRate > 0f ? NextInterpolation(component, particleSystemState.Age) : 1f;
            var remapped = MathUtils.RemapValClamped(component, inputMin, inputMax, outputMin, outputMax);

            foreach (ref var particle in particles.Current)
            {
                // The window is measured against each particle's creation time, and a bound of -1 leaves it open
                if (startTime != -1f && endTime != -1f && !(startTime <= particle.CreationTime && particle.CreationTime < endTime))
                {
                    continue;
                }

                var current = particle.GetScalar(fieldOutput);
                var target = particle.ModifyScalarBySetMethod(particles, fieldOutput, remapped, setMethod);

                particle.SetScalar(fieldOutput, ((target - current) * interpolation) + current);
            }
        }

        /// <summary>
        /// How far the field eases toward the remapped value this step. The first step snaps to it. After
        /// that the clock restarts at 0 whenever the input changes or <c>m_flInterpRate</c> seconds have
        /// passed since the last restart, and climbs to 1 over that interval.
        /// </summary>
        private float NextInterpolation(float input, float time)
        {
            if (!interpolating)
            {
                interpolating = true;
                lastInput = -input;
                return 1f;
            }

            var end = interpolationStart + interpRate;

            if (lastInput != input || time > end)
            {
                interpolationStart = time;
                lastInput = input;
            }

            return MathUtils.RemapValClamped(time, interpolationStart, end, 0f, 1f);
        }
    }
}
