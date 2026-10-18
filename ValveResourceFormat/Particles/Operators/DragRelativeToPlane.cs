namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Damps particle velocity by a drag that is strongest at a plane and fades linearly to nothing
    /// over a falloff distance, by moving the previous position toward the current one.
    /// </summary>
    /// <remarks>
    /// The signed distance is the negated dot product of the position with the plane normal, minus the
    /// Z of the control point position, so the plane does not pass through the control point. Without
    /// <c>m_bDirectional</c> both sides fade; with it the drag is full on the side the normal points
    /// to. The normal is not normalized. A drag of 1 or more stops the particle.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DragRelativeToPlane">C_OP_DragRelativeToPlane</seealso>
    class DragRelativeToPlane : ParticleFunctionOperator
    {
        private readonly INumberProvider dragAtPlane = new LiteralNumberProvider(0f);
        private readonly INumberProvider falloff = new LiteralNumberProvider(1f);
        private readonly bool directional;
        private readonly IVectorProvider planeNormal = new LiteralVectorProvider(Vector3.UnitZ);
        private readonly int controlPoint;

        public DragRelativeToPlane(ParticleDefinitionParser parse) : base(parse)
        {
            dragAtPlane = parse.CollectionNumberProvider("m_flDragAtPlane", dragAtPlane);
            falloff = parse.CollectionNumberProvider("m_flFalloff", falloff);
            directional = parse.Boolean("m_bDirectional", directional);
            planeNormal = parse.CollectionVectorProvider("m_vecPlaneNormal", planeNormal);
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var normal = planeNormal.NextVector(particleSystemState);
            var drag = dragAtPlane.NextNumber(particleSystemState) * strength;
            var falloffDistance = falloff.NextNumber(particleSystemState);
            var falloffRange = falloffDistance == 0f ? 1f : falloffDistance;
            var planeOffset = particleSystemState.GetControlPoint(controlPoint).Position.Z;

            foreach (ref var particle in particles.Current)
            {
                var distance = -Vector3.Dot(normal, particle.Position) - planeOffset;

                if (!directional)
                {
                    distance = MathF.Abs(distance);
                }

                var amount = (1f - MathUtils.Saturate(distance / falloffRange)) * drag;

                if (amount > 0f)
                {
                    particle.PositionPrevious = MathUtils.Lerp(particle.PositionPrevious, particle.Position, amount);
                }
            }
        }
    }
}
