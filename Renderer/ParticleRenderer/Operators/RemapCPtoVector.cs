namespace ValveResourceFormat.Renderer.Particles.Operators
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
        // -1 means the control point is read in world space.
        private readonly int localSpaceCp = -1;
        private readonly Vector3 inputMin = Vector3.Zero;
        private readonly Vector3 inputMax = Vector3.One;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;
        private readonly float startTime = -1f;
        private readonly float endTime = -1f;
        private readonly float interpRate;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool offset;
        private readonly bool accelerate;

        // The output field cannot change, so the operator settles at construction whether it does anything.
        private readonly bool outputsVector;

        public RemapCPtoVector(ParticleDefinitionParser parse) : base(parse)
        {
            cpInput = parse.Int32("m_nCPInput", cpInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            localSpaceCp = parse.Int32("m_nLocalSpaceCP", localSpaceCp);
            inputMin = parse.Vector3("m_vInputMin", inputMin);
            inputMax = parse.Vector3("m_vInputMax", inputMax);
            outputMin = parse.Vector3("m_vOutputMin", outputMin);
            outputMax = parse.Vector3("m_vOutputMax", outputMax);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            interpRate = parse.Float("m_flInterpRate", interpRate);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            offset = parse.Boolean("m_bOffset", offset);
            accelerate = parse.Boolean("m_bAccelerate", accelerate);

            outputsVector = fieldOutput.FieldType() == "vector";
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (!outputsVector)
            {
                return;
            }

            // The window is measured against the system's own age, and a negative bound leaves it unset.
            if (startTime >= 0f && endTime >= 0f
                && (particleSystemState.Age < startTime || particleSystemState.Age > endTime))
            {
                return;
            }

            var input = particleSystemState.GetControlPoint(cpInput).Position;

            if (localSpaceCp >= 0)
            {
                input = ControlPointTransformProvider.TransformDirection(particleSystemState, localSpaceCp, input);
            }

            var remapped = new Vector3(
                MathUtils.RemapValClamped(input.X, inputMin.X, inputMax.X, outputMin.X, outputMax.X),
                MathUtils.RemapValClamped(input.Y, inputMin.Y, inputMax.Y, outputMin.Y, outputMax.Y),
                MathUtils.RemapValClamped(input.Z, inputMin.Z, inputMax.Z, outputMin.Z, outputMax.Z));

            // An interpolation scale eases the field toward the remapped value over time; zero snaps to it.
            var interpolation = interpRate > 0f ? MathUtils.Saturate(interpRate * frameTime) : 1f;

            foreach (ref var particle in particles.Current)
            {
                var value = remapped;

                // Offset and accelerate both read the remap as a displacement rather than a value in its
                // own right: one displaces the spawn value once, the other integrates over time.
                if (offset)
                {
                    value += particle.GetInitialVector(particles, fieldOutput);
                }
                else if (accelerate)
                {
                    value = particle.GetVector(fieldOutput) + (value * frameTime);
                }

                var target = particle.ModifyVectorBySetMethod(particles, fieldOutput, value, setMethod);

                particle.SetVector(fieldOutput, Vector3.Lerp(particle.GetVector(fieldOutput), target, interpolation * strength));
            }
        }
    }
}
