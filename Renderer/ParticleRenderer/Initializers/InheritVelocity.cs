using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Inherits velocity from a control point, optionally scaling the inherited value.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InheritVelocity">C_INIT_InheritVelocity</seealso>
    class InheritVelocity : ParticleFunctionInitializer
    {
        private readonly int controlPointNumber;
        private readonly float velocityScale = 1f;

        public InheritVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            velocityScale = parse.Float("m_flVelocityScale", velocityScale);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            // The inherited velocity comes from the control point's motion over the last frame; a move
            // of 100+ units is treated as a teleport and skipped.
            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);

            if (controlPoint.StepDelta.Length() >= 100f)
            {
                return particle;
            }

            particle.Velocity += controlPoint.Velocity * velocityScale;
            return particle;
        }
    }
}
