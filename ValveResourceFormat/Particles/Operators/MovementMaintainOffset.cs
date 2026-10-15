namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lines all particles up along an offset vector, centred on their average position: particle i
    /// sits at the centroid plus the offset times ((count - 1) / 2 - i), optionally times its radius.
    /// The previous positions are laid out the same way around their own centroid, so the line keeps
    /// the centroid's velocity and the particles lose their own.
    /// </summary>
    /// <remarks>
    /// With a control point the offset is in that control point's frame. Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MovementMaintainOffset">C_OP_MovementMaintainOffset</seealso>
    class MovementMaintainOffset : ParticleFunctionOperator
    {
        private readonly Vector3 offset;
        private readonly int controlPoint = -1;
        private readonly bool radiusScale;

        public MovementMaintainOffset(ParticleDefinitionParser parse) : base(parse)
        {
            offset = parse.Vector3("m_vecOffset", offset);
            controlPoint = parse.Int32("m_nCP", controlPoint);
            radiusScale = parse.Boolean("m_bRadiusScale", radiusScale);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var step = offset;

            if (controlPoint > -1)
            {
                var point = particleSystemState.GetControlPoint(controlPoint);
                step = (point.Forward * offset.X) + (point.Left * offset.Y) + (point.Up * offset.Z);
            }

            var current = particles.Current;
            var sum = Vector3.Zero;
            var sumPrevious = Vector3.Zero;

            foreach (ref var particle in current)
            {
                sum += particle.Position;
                sumPrevious += particle.PositionPrevious;
            }

            var inverseCount = 1f / current.Length;
            var mean = sum * inverseCount;
            var meanPrevious = sumPrevious * inverseCount;
            var placement = step * (current.Length - 1) * 0.5f;

            foreach (ref var particle in current)
            {
                var scaled = radiusScale ? placement * particle.Radius : placement;

                particle.Position = scaled + mean;
                particle.PositionPrevious = scaled + meanPrevious;

                placement -= step;
            }
        }
    }
}
