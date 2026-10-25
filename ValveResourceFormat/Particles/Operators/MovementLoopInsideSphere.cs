namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Keeps particles inside a sphere around a control point: a particle that leaves it reappears on
    /// the opposite side, at 0.95 of its distance, keeping its velocity. When the control point itself
    /// moves further than the radius in one step, every particle is carried along with it instead.
    /// </summary>
    /// <remarks>
    /// The per-axis scale multiplies the offsets before they are measured, so a larger scale gives a
    /// smaller extent on that axis. <c>m_nDistSqrAttr</c> receives every particle's scaled squared
    /// distance. Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MovementLoopInsideSphere">C_OP_MovementLoopInsideSphere</seealso>
    class MovementLoopInsideSphere : ParticleFunctionOperator
    {
        private const float WrapDistance = 1.95f;

        private readonly int controlPoint;
        private readonly INumberProvider distance = new LiteralNumberProvider(1024f);
        private readonly IVectorProvider scale = new LiteralVectorProvider(Vector3.One);
        private readonly ParticleField distanceSquaredField = ParticleField.NoneDisabled;
        private readonly ulong storedFields;

        public MovementLoopInsideSphere(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nCP", controlPoint);
            distance = parse.CollectionNumberProvider("m_flDistance", distance);
            scale = parse.CollectionVectorProvider("m_vecScale", scale);
            distanceSquaredField = parse.ParticleField("m_nDistSqrAttr", distanceSquaredField);

            storedFields = ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

            if (distanceSquaredField != ParticleField.NoneDisabled)
            {
                storedFields |= distanceSquaredField.Mask();
            }
        }

        /// <inheritdoc/>
        public override ulong StoredFields => storedFields;

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var radius = distance.NextNumber(particleSystemState);
            var radiusSquared = radius * radius;
            var axisScale = scale.NextVector(particleSystemState);

            var point = particleSystemState.GetControlPoint(controlPoint);
            var center = point.Position;
            var move = center - point.PositionPrevious;

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);

            if ((axisScale * move).LengthSquared() > radiusSquared)
            {
                var ages = current.ReadAges();

                for (var i = 0; i < positions.Length; i++)
                {
                    var carried = move * (MathF.Min(ages[i], frameTime) / frameTime);

                    positions[i] += carried;
                    previousPositions[i] += carried;
                }

                return;
            }

            var writeDistanceSquared = distanceSquaredField != ParticleField.NoneDisabled && ParticleBuffer.IsScalar(distanceSquaredField);
            var distanceSquaredValues = writeDistanceSquared ? current.Floats(distanceSquaredField) : default;

            for (var i = 0; i < positions.Length; i++)
            {
                var offset = positions[i] - center;
                var distanceSquared = (axisScale * offset).LengthSquared();

                if (writeDistanceSquared)
                {
                    distanceSquaredValues[i] = distanceSquared;
                }

                if (radiusSquared < distanceSquared)
                {
                    positions[i] -= WrapDistance * offset;
                    previousPositions[i] -= WrapDistance * offset;
                }
            }
        }
    }
}
