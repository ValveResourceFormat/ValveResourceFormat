namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Remaps the index of the sequence the object a control point is bound to plays into an
    /// attribute of new particles, and can store its playback rate in another. Particles are left
    /// alone when the control point is bound to no object.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitialSequenceFromModel">C_INIT_InitialSequenceFromModel</seealso>
    class InitialSequenceFromModel : ParticleFunctionInitializer
    {
        private readonly int controlPoint;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly ParticleField fieldOutputAnim = ParticleField.NoneDisabled;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public InitialSequenceFromModel(ParticleDefinitionParser parse) : base(parse)
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
        }

        public override ulong WrittenFields
            => fieldOutput.Mask() | (fieldOutputAnim != ParticleField.NoneDisabled ? fieldOutputAnim.Mask() : 0);

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
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

            if (ParticleBuffer.IsScalar(fieldOutput))
            {
                var outputs = run.Floats(fieldOutput);
                var integerOutputs = fieldOutput.IsIntegerAttribute() ? run.Integers(fieldOutput) : default;

                for (var i = 0; i < outputs.Length; i++)
                {
                    var current = outputs[i];
                    var output = ParticleFieldExtensions.ApplySetMethod(value, current, current, particleSystemState.CurrentStep, setMethod);

                    if (integerOutputs.IsEmpty)
                    {
                        outputs[i] = output;
                    }
                    else
                    {
                        integerOutputs[i] = (int)output;
                    }
                }
            }

            if (fieldOutputAnim != ParticleField.NoneDisabled && ParticleBuffer.IsScalar(fieldOutputAnim))
            {
                run.Floats(fieldOutputAnim).Fill(rate);
            }
        }
    }
}
