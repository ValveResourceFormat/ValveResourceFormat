namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps a control point's position into a vector particle field, component by component: each
    /// component maps its own input range onto its own output range and clamps at the ends, so one
    /// control point can carry three unrelated parameters.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapCPtoVector">C_OP_RemapCPtoVector</seealso>
    class RemapCPtoVector : ParticleFunctionOperator
    {
        private readonly int cpInput;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        // -1 leaves the output range in world space.
        private readonly int localSpaceCp = -1;
        private readonly Vector3 inputMin = Vector3.Zero;
        private readonly Vector3 inputMax = Vector3.Zero;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.Zero;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool offset;
        private readonly bool accelerate;

        // The output field cannot change, so the operator settles at construction whether it does anything.
        private readonly bool outputsVector;
        private readonly bool clampsToUnit;
        private readonly ControlPointRemapTiming timing;

        public RemapCPtoVector(ParticleDefinitionParser parse) : base(parse)
        {
            cpInput = parse.Int32("m_nCPInput", cpInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            localSpaceCp = parse.Int32("m_nLocalSpaceCP", localSpaceCp);
            inputMin = parse.Vector3("m_vInputMin", inputMin);
            inputMax = parse.Vector3("m_vInputMax", inputMax);
            outputMin = parse.Vector3("m_vOutputMin", outputMin);
            outputMax = parse.Vector3("m_vOutputMax", outputMax);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            offset = parse.Boolean("m_bOffset", offset);
            accelerate = parse.Boolean("m_bAccelerate", accelerate);

            outputsVector = fieldOutput.FieldType() == "vector";
            clampsToUnit = fieldOutput is ParticleField.Color or ParticleField.GlowRgb;
            timing = new ControlPointRemapTiming(parse);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddField(cpInput, fieldOutput);
        }

        public override void Reset()
        {
            timing.Reset();
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!outputsVector)
            {
                return;
            }

            var input = particleSystemState.GetControlPoint(cpInput).Position;
            var outputMin = this.outputMin;
            var outputMax = this.outputMax;

            if (localSpaceCp != -1)
            {
                outputMin = ControlPointTransformProvider.TransformDirection(particleSystemState, localSpaceCp, outputMin);
                outputMax = ControlPointTransformProvider.TransformDirection(particleSystemState, localSpaceCp, outputMax);
            }

            var interpolation = timing.NextInterpolation(input, particleSystemState.Age);

            var remapped = new Vector3(
                MathUtils.RemapValClamped(input.X, inputMin.X, inputMax.X, outputMin.X, outputMax.X),
                MathUtils.RemapValClamped(input.Y, inputMin.Y, inputMax.Y, outputMin.Y, outputMax.Y),
                MathUtils.RemapValClamped(input.Z, inputMin.Z, inputMax.Z, outputMin.Z, outputMax.Z));

            foreach (var particle in particles.Current)
            {
                if (!timing.Covers(particle.CreationTime))
                {
                    continue;
                }

                var current = particle.GetVector(fieldOutput);
                var value = setMethod switch
                {
                    ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE => remapped * particle.GetInitialVector(particles, fieldOutput),
                    ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE => remapped + particle.GetInitialVector(particles, fieldOutput),
                    ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE => remapped * current.X,
                    ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE => remapped + current,
                    ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE => (remapped * frameTime) + particle.GetInitialVector(particles, fieldOutput),
                    _ => remapped,
                };

                value = MathUtils.Lerp(current, value, interpolation);

                if (clampsToUnit)
                {
                    value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
                }

                if (fieldOutput is not (ParticleField.Position or ParticleField.PositionPrevious))
                {
                    particle.SetVector(fieldOutput, value);
                    continue;
                }

                if (accelerate && !offset)
                {
                    particle.SetVector(fieldOutput, value * frameTime);
                    continue;
                }

                var previous = particle.PositionPrevious;

                if (offset)
                {
                    value += current;
                    previous += value;
                }
                else
                {
                    previous = value;
                }

                particle.SetVector(fieldOutput, accelerate ? (value * frameTime) + value : value);
                particle.PositionPrevious = previous;
            }
        }
    }
}
