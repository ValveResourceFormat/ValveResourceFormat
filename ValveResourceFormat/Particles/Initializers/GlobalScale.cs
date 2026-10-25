using System.Numerics.Tensors;

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
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            scaleRadius = parse.Boolean("m_bScaleRadius", scaleRadius);
            scalePosition = parse.Boolean("m_bScalePosition", scalePosition);
            scaleVelocity = parse.Boolean("m_bScaleVelocity", scaleVelocity);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddScale(scaleControlPoint);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.Radius.Mask() | ParticleField.HitboxOffsetPosition.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
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
                return;
            }

            if (scaleRadius)
            {
                var radii = run.Floats(ParticleField.Radius);
                TensorPrimitives.Multiply(radii, finalScale, radii);
            }

            var origin = particleSystemState.GetControlPoint(controlPoint).Position;
            var positions = run.Vectors(ParticleField.Position);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);

            for (var i = 0; i < positions.Length; i++)
            {
                var position = positions[i];
                var previous = previousPositions[i];
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

                positions[i] = position;
                previousPositions[i] = previous;
            }
        }
    }
}
