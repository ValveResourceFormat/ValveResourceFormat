namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Uniformly scales a particle's radius, spawn position and velocity by a literal scale,
    /// optionally multiplied by the value carried on a scale control point.
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
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
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
                // The scale is carried in the control point's X component, driven game-side from the
                // effect's control point configuration. An undriven point reads zero, which would
                // collapse the effect entirely, so treat that as "no scale supplied".
                var controlPointScale = particleSystemState.GetControlPoint(scaleControlPoint).Position.X;

                if (controlPointScale == 0f)
                {
                    return particle;
                }

                finalScale *= controlPointScale;
            }

            if (finalScale == 1f)
            {
                return particle;
            }

            if (scaleRadius)
            {
                particle.Radius *= finalScale;
            }

            if (scalePosition)
            {
                var origin = particleSystemState.GetControlPoint(controlPoint).Position;
                particle.Position = origin + ((particle.Position - origin) * finalScale);
            }

            if (scaleVelocity)
            {
                particle.Velocity *= finalScale;
            }

            return particle;
        }
    }
}
