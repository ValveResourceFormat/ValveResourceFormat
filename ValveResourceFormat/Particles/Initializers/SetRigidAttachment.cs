namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Stores a vector attribute of new particles in another attribute, carried into a control
    /// point's frame, for <see cref="Operators.MovementRigidAttachToCP"/> to rebuild it from. Outside
    /// local space the control point's position is added to the vector instead.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SetRigidAttachment">C_INIT_SetRigidAttachment</seealso>
    class SetRigidAttachment : ParticleFunctionInitializer
    {
        private readonly int controlPoint;
        private readonly ParticleField fieldInput = ParticleField.Position;
        private readonly ParticleField fieldOutput = ParticleField.PositionPrevious;
        private readonly bool localSpace = true;

        public SetRigidAttachment(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            localSpace = parse.Boolean("m_bLocalSpace", localSpace);
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var point = particleSystemState.GetControlPoint(controlPoint);
            var input = particle.GetVector(fieldInput);

            if (!localSpace)
            {
                particle.SetVector(fieldOutput, input + point.Position);
                return particle;
            }

            var frame = point.GetTransform();

            if (Matrix4x4.Invert(frame, out var toLocal))
            {
                particle.SetVector(fieldOutput, Vector3.Transform(input, toLocal));
            }

            return particle;
        }
    }
}
