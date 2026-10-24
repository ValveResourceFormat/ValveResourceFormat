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
            => FieldMask(fieldOutput) | (fieldOutputAnim != ParticleField.NoneDisabled ? FieldMask(fieldOutputAnim) : 0);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (particleSystemState.GetControlPoint(controlPoint).ModelObject is not { } modelObject)
            {
                return particle;
            }

            var sequence = modelObject.SequenceIndex;
            var rate = modelObject.SequenceRate;

            if (sequence < 0)
            {
                rate = fieldOutputAnim != ParticleField.NoneDisabled ? 1f : 0f;
            }

            var value = MathUtils.RemapValClamped(sequence, inputMin, inputMax, outputMin, outputMax);
            var output = particle.ModifyScalarBySetMethodAtSpawn(particles, fieldOutput, value, setMethod);
            particle.SetScalar(fieldOutput, fieldOutput.IsIntegerAttribute() ? MathF.Truncate(output) : output);

            if (fieldOutputAnim != ParticleField.NoneDisabled)
            {
                particle.SetScalar(fieldOutputAnim, rate);
            }

            return particle;
        }
    }
}
