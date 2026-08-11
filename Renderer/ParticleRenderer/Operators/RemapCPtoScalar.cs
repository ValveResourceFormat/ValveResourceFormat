namespace ValveResourceFormat.Renderer.Particles.Operators
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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            // The window is measured against the system's own age, and a negative bound leaves it unset.
            if (startTime >= 0f && endTime >= 0f
                && (particleSystemState.Age < startTime || particleSystemState.Age > endTime))
            {
                return;
            }

            var outputMin = this.outputMin;
            var outputMax = this.outputMax;

            if (fieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = Math.Clamp(outputMin, 0f, 1f);
                outputMax = Math.Clamp(outputMax, 0f, 1f);
            }

            var input = particleSystemState.GetControlPoint(cpInput).Position;
            var component = field switch
            {
                0 => input.X,
                1 => input.Y,
                _ => input.Z,
            };

            var remapped = MathUtils.RemapValClamped(component, inputMin, inputMax, outputMin, outputMax);

            var interpolation = interpRate > 0f ? MathUtils.Saturate(interpRate * frameTime) : 1f;

            foreach (ref var particle in particles.Current)
            {
                var target = particle.ModifyScalarBySetMethod(particles, fieldOutput, remapped, setMethod);

                particle.SetScalar(fieldOutput, float.Lerp(particle.GetScalar(fieldOutput), target, interpolation * strength));
            }
        }
    }
}
