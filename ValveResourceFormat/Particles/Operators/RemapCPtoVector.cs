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

            outputsVector = ParticleBuffer.IsVector(fieldOutput);
            clampsToUnit = fieldOutput is ParticleField.Color or ParticleField.GlowRgb;
            timing = new ControlPointRemapTiming(parse);

            if (setMethod.ReadsInitialValue() || setMethod == ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE)
            {
                parse.AddInitialRead(fieldOutput);
            }
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddField(cpInput, fieldOutput);
        }

        public override void Reset()
        {
            timing.Reset();
        }

        /// <inheritdoc/>
        public override ulong StoredFields
            => fieldOutput.Mask() | (fieldOutput == ParticleField.Position ? ParticleField.PositionPrevious.Mask() : 0);

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

            var live = particles.Current;
            var creationTimes = live.ReadFloats(ParticleField.CreationTime);
            var output = live.Vectors(fieldOutput);
            var writesPosition = fieldOutput is ParticleField.Position or ParticleField.PositionPrevious;
            var previousPositions = writesPosition ? live.Vectors(ParticleField.PositionPrevious) : default;
            var initial = particles.Initial.ReadVectors(fieldOutput);

            for (var i = 0; i < output.Length; i++)
            {
                if (!timing.Covers(creationTimes[i]))
                {
                    continue;
                }

                var current = output[i];

                // Scaling the current value scales every axis by its X, and ramping adds to the initial value
                var value = setMethod switch
                {
                    ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE => remapped * initial[i],
                    ParticleSetMethod.PARTICLE_SET_ADD_TO_INITIAL_VALUE => remapped + initial[i],
                    ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE => remapped * current.X,
                    ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE => remapped + current,
                    ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE => (remapped * frameTime) + initial[i],
                    _ => remapped,
                };

                value = MathUtils.Lerp(current, value, interpolation);

                if (clampsToUnit)
                {
                    value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
                }

                if (!writesPosition)
                {
                    output[i] = value;
                    continue;
                }

                if (accelerate && !offset)
                {
                    output[i] = value * frameTime;
                    continue;
                }

                var previous = previousPositions[i];

                if (offset)
                {
                    value += current;
                    previous += value;
                }
                else
                {
                    previous = value;
                }

                output[i] = accelerate ? (value * frameTime) + value : value;
                previousPositions[i] = previous;
            }
        }
    }
}
