namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Rebuilds a vector attribute of every particle each step from an offset stored in another,
    /// placed in a control point's current frame, so particles stay rigidly attached to it. The
    /// offset can be scaled by a component of another control point's position.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MovementRigidAttachToCP">C_OP_MovementRigidAttachToCP</seealso>
    class MovementRigidAttachToCP : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly int scaleControlPoint = -1;
        private readonly int scaleComponent;
        private readonly ParticleField fieldInput = ParticleField.PositionPrevious;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly bool offsetLocal = true;

        public MovementRigidAttachToCP(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = Math.Clamp(parse.Int32("m_nControlPointNumber", controlPoint), 0, ParticleSystemState.MaxControlPoint);
            scaleControlPoint = Math.Clamp(parse.Int32("m_nScaleControlPoint", scaleControlPoint), -1, ParticleSystemState.MaxControlPoint);
            scaleComponent = parse.Int32("m_nScaleCPField", scaleComponent);
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            offsetLocal = parse.Boolean("m_bOffsetLocal", offsetLocal);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var point = particleSystemState.GetControlPoint(controlPoint);
            var scale = 1f;

            if (scaleControlPoint >= 0)
            {
                scale = scaleComponent <= 2 ? particleSystemState.GetControlPoint(scaleControlPoint).Position.GetComponent(scaleComponent) : 0f;
            }

            var frame = point.GetRotationMatrix();
            frame.Translation = point.Position;

            foreach (ref var particle in particles.Current)
            {
                var offset = particle.GetVector(fieldInput) * scale;
                particle.SetVector(fieldOutput, offsetLocal ? Vector3.Transform(offset, frame) : offset + point.Position);
            }
        }
    }
}
