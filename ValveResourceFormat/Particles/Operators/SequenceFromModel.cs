namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps the index of the sequence the object a control point is bound to plays into an
    /// attribute of every particle each step, and can store its playback rate in another. Nothing
    /// changes while the control point is bound to no object.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SequenceFromModel">C_OP_SequenceFromModel</seealso>
    class SequenceFromModel : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly ParticleField fieldOutputAnim = ParticleField.NoneDisabled;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public SequenceFromModel(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            fieldOutputAnim = parse.ParticleField("m_nFieldOutputAnim", fieldOutputAnim);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            setMethod = parse.Enum("m_nSetMethod", setMethod);

            if (fieldOutput.IsAlpha())
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }

            AddInitialReadOf(parse, setMethod, fieldOutput);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask() | fieldOutputAnim.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (particleSystemState.GetControlPoint(controlPoint).ModelObject is not { } modelObject)
            {
                return;
            }

            var sequence = modelObject.SequenceIndex;
            var rate = modelObject.SequenceRate;

            if (sequence < 0)
            {
                rate = fieldOutputAnim != ParticleField.NoneDisabled ? 1f : 0f;
            }

            var value = MathUtils.RemapValClamped(sequence, inputMin, inputMax, outputMin, outputMax);

            var current = particles.Current;

            if (ParticleBuffer.IsScalar(fieldOutput))
            {
                var output = current.Floats(fieldOutput);
                var isInteger = fieldOutput.IsIntegerAttribute();
                var integerOutput = isInteger ? current.Integers(fieldOutput) : default;
                var initial = particles.Initial.ReadFloats(fieldOutput);
                var deltaTime = particleSystemState.CurrentStep;

                for (var i = 0; i < output.Length; i++)
                {
                    var result = ParticleFieldExtensions.ApplySetMethod(value, initial[i], output[i], deltaTime, setMethod);

                    if (isInteger)
                    {
                        integerOutput[i] = (int)result;
                    }
                    else
                    {
                        output[i] = result;
                    }
                }
            }

            if (ParticleBuffer.IsScalar(fieldOutputAnim))
            {
                current.Floats(fieldOutputAnim).Fill(rate);
            }
        }
    }
}
