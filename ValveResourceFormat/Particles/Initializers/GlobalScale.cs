namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Uniformly scales a particle's radius, spawn position and velocity by a literal scale,
    /// optionally multiplied by the X component of a scale control point. A scale control point
    /// reading 0 is taken as unset and ignored, since games always set it and previews never do.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_GlobalScale">C_INIT_GlobalScale</seealso>
    class GlobalScale : ParticleFunctionInitializer
    {
        private readonly float scale = 1f;
        private readonly int scaleControlPoint = -1;
        private readonly int controlPoint;
        private readonly bool scaleRadius = true;
        private readonly bool scalePosition = true;
        private readonly bool scaleVelocity = true;

        public GlobalScale(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.Float("m_flScale", scale);
            scaleControlPoint = parse.Int32("m_nScaleControlPointNumber", scaleControlPoint);
            controlPoint = Math.Clamp(parse.Int32("m_nControlPointNumber", controlPoint), 0, ParticleSystemState.MaxControlPoint);
            scaleRadius = parse.Boolean("m_bScaleRadius", scaleRadius);
            scalePosition = parse.Boolean("m_bScalePosition", scalePosition);
            scaleVelocity = parse.Boolean("m_bScaleVelocity", scaleVelocity);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious) | FieldMask(ParticleField.Radius) | FieldMask(ParticleField.HitboxOffsetPosition);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var finalScale = scale;

            if (scaleControlPoint >= 0)
            {
                var controlPointScale = particleSystemState.GetControlPoint(scaleControlPoint).Position.X;

                if (controlPointScale != 0f)
                {
                    finalScale *= controlPointScale;
                }
            }

            if (finalScale == 1f)
            {
                return particle;
            }

            if (scaleRadius)
            {
                particle.Radius *= finalScale;
            }

            var origin = particleSystemState.GetControlPoint(controlPoint).Position;
            var position = particle.Position;
            var previous = particle.PositionPrevious;
            var velocity = position - previous;

            if (scalePosition)
            {
                position = origin + ((position - origin) * finalScale);
            }

            if (scaleVelocity)
            {
                previous = scalePosition
                    ? origin + ((previous - origin) * finalScale)
                    : position - (velocity * finalScale);
            }
            else if (scalePosition)
            {
                previous = position - velocity;
            }

            particle.Position = position;
            particle.PositionPrevious = previous;

            return particle;
        }
    }
}
