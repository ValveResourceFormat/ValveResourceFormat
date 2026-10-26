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

        public override ulong WrittenFields => fieldOutput.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (!ParticleBuffer.IsVector(fieldOutput))
            {
                return;
            }

            var point = particleSystemState.GetControlPoint(controlPoint);
            var inputs = run.ReadVectors(fieldInput);
            var outputs = run.Vectors(fieldOutput);

            if (!localSpace)
            {
                var position = point.Position;

                for (var i = 0; i < outputs.Length; i++)
                {
                    outputs[i] = inputs[i] + position;
                }

                return;
            }

            var frame = point.GetTransform();

            if (!Matrix4x4.Invert(frame, out var toLocal))
            {
                return;
            }

            for (var i = 0; i < outputs.Length; i++)
            {
                outputs[i] = Vector3.Transform(inputs[i], toLocal);
            }
        }
    }
}
