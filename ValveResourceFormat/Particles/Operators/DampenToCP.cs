namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Scales down the motion of particles inside a radius around a control point, damping them to a
    /// standstill at the control point itself.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DampenToCP">C_OP_DampenToCP</seealso>
    class DampenToCP : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly float range = 100f;
        private readonly float scale = 1f;

        public DampenToCP(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            range = parse.Float("m_flRange", range);
            scale = parse.Float("m_flScale", scale);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (range <= 0f)
            {
                return;
            }

            var controlPointPosition = particleSystemState.GetControlPoint(controlPoint).Position;

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.ReadVectors(ParticleField.PositionPrevious);

            for (var i = 0; i < positions.Length; i++)
            {
                var position = positions[i];
                var distance = Vector3.Distance(position, controlPointPosition);

                if (distance > range)
                {
                    continue;
                }

                var dampening = MathF.Pow(distance / range, scale);

                // Motion lives in the Verlet position pair, so shortening the step damps the particle
                var previousPosition = previousPositions[i];
                var dampened = previousPosition + ((position - previousPosition) * dampening);

                positions[i] = Vector3.Lerp(position, dampened, strength);
            }
        }
    }
}
