namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Remaps a scalar particle field into a vector field by interpolating between a minimum and
    /// maximum output vector. With local coordinates a spatial output is expressed in a control
    /// point's frame, which is how muzzle flashes lay their sprites out along the barrel axis.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapScalarToVector">C_INIT_RemapScalarToVector</seealso>
    class RemapScalarToVector : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldInput = ParticleField.CreationTime;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;
        private readonly float startTime = -1f;
        private readonly float endTime = -1f;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly int controlPoint;
        private readonly bool localCoords = true;
        private readonly float remapBias = 0.5f;

        public RemapScalarToVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Vector3("m_vecOutputMin", outputMin);
            outputMax = parse.Vector3("m_vecOutputMax", outputMax);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            remapBias = parse.Float("m_flRemapBias", remapBias);
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            if (fieldOutput.FieldType() != "vector")
            {
                return particle;
            }

            // A negative time bound means the window is unset and the remap always runs.
            if (startTime >= 0f && endTime >= 0f && (particle.CreationTime < startTime || particle.CreationTime > endTime))
            {
                return particle;
            }

            var input = MathUtils.Saturate(MathUtils.Remap(particle.GetScalar(fieldInput), inputMin, inputMax));

            // Source's Bias(): 0.5 leaves the value untouched, lower pushes it down, higher pushes it up.
            if (remapBias != 0.5f && remapBias > 0f)
            {
                input = MathF.Pow(input, MathF.Log(remapBias) / MathF.Log(0.5f));
            }

            var value = Vector3.Lerp(outputMin, outputMax, input);

            if (localCoords)
            {
                // Positions are authored as offsets from the control point and directions as
                // rotations of it; colors carry no frame at all.
                value = fieldOutput switch
                {
                    ParticleField.Position or ParticleField.PositionPrevious or ParticleField.HitboxOffsetPosition
                        => ControlPointTransformProvider.TransformPosition(particleSystemState, controlPoint, value),
                    ParticleField.Normal or ParticleField.ScratchVector or ParticleField.ScratchVector2
                        => ControlPointTransformProvider.TransformDirection(particleSystemState, controlPoint, value),
                    _ => value,
                };
            }

            particle.SetVector(fieldOutput, particle.ModifyVectorBySetMethodAtSpawn(particles, fieldOutput, value, setMethod));

            return particle;
        }
    }
}
