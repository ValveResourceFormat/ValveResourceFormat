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
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly ControlPointRemapTiming timing;

        public RemapCPtoScalar(ParticleDefinitionParser parse) : base(parse)
        {
            cpInput = parse.Int32("m_nCPInput", cpInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            field = Math.Clamp(parse.Int32("m_nField", field), 0, 2);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            timing = new ControlPointRemapTiming(parse);

            AddInitialReadOf(parse, setMethod, fieldOutput);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddField(cpInput, field, fieldOutput);
        }

        public override void Reset()
        {
            timing.Reset();
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var outputMin = this.outputMin;
            var outputMax = this.outputMax;

            if (fieldOutput.IsAlpha())
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }

            var input = particleSystemState.GetControlPoint(cpInput).Position;
            var component = input.GetComponent(field);

            var interpolation = timing.NextInterpolation(new Vector3(component, 0f, 0f), particleSystemState.Age);
            var remapped = MathUtils.RemapValClamped(component, inputMin, inputMax, outputMin, outputMax);

            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var creationTimes = current.ReadFloats(ParticleField.CreationTime);
            var output = current.Floats(fieldOutput);
            var initial = particles.Initial.ReadFloats(fieldOutput);
            var isInteger = fieldOutput.IsIntegerAttribute();

            for (var i = 0; i < output.Length; i++)
            {
                if (!timing.Covers(creationTimes[i]))
                {
                    continue;
                }

                var currentValue = output[i];
                var target = ParticleFieldExtensions.ApplySetMethod(remapped, initial[i], currentValue, particleSystemState.CurrentStep, setMethod);
                var value = MathUtils.Lerp(currentValue, target, interpolation);

                output[i] = isInteger ? BitConverter.Int32BitsToSingle((int)value) : value;
            }
        }
    }
}
