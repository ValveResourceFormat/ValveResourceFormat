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

        public MovementLoopInsideSphere(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nCP", controlPoint);
            distance = parse.CollectionNumberProvider("m_flDistance", distance);
            scale = parse.CollectionVectorProvider("m_vecScale", scale);
            distanceSquaredField = parse.ParticleField("m_nDistSqrAttr", distanceSquaredField);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var radius = distance.NextNumber(particleSystemState);
            var radiusSquared = radius * radius;
            var axisScale = scale.NextVector(particleSystemState);

            var point = particleSystemState.GetControlPoint(controlPoint);
            var center = point.Position;
            var move = center - point.PositionPrevious;

            if ((axisScale * move).LengthSquared() > radiusSquared)
            {
                foreach (ref var particle in particles.Current)
                {
                    var carried = move * (MathF.Min(particle.Age, frameTime) / frameTime);

                    particle.Position += carried;
                    particle.PositionPrevious += carried;
                }

                return;
            }

            foreach (ref var particle in particles.Current)
            {
                var offset = particle.Position - center;
                var distanceSquared = (axisScale * offset).LengthSquared();

                if (distanceSquaredField != ParticleField.NoneDisabled)
                {
                    particle.SetScalar(distanceSquaredField, distanceSquared);
                }

                if (radiusSquared < distanceSquared)
                {
                    particle.Position -= WrapDistance * offset;
                    particle.PositionPrevious -= WrapDistance * offset;
                }
            }
        }
    }
}
