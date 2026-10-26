using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Gives new particles that start inside the hitboxes, or bones, of the model a control point
    /// carries, or with <c>m_bCullOutside</c> outside them, a life of -1. <c>m_bBoundBox</c> tests
    /// the box around all of them instead. When the system falls back to the render bounds for a
    /// model without hitboxes, every new particle gets a life of -1.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_ModelCull">C_INIT_ModelCull</seealso>
    class ModelCull : ParticleFunctionInitializer
    {
        private readonly int controlPoint;
        private readonly bool boundBox;
        private readonly bool cullOutside;
        private readonly bool useBones;
        private readonly string hitboxSetName = "default";

        public ModelCull(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            boundBox = parse.Boolean("m_bBoundBox", boundBox);
            cullOutside = parse.Boolean("m_bCullOutside", cullOutside);
            useBones = parse.Boolean("m_bUseBones", useBones);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
        }

        public override ulong WrittenFields => ParticleField.LifeDuration.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones) is not { Count: > 0 } cache)
            {
                return;
            }

            var lifetimes = run.Floats(ParticleField.LifeDuration);

            // The render bounds box is not tested against, so every particle is culled
            if (cache.IsRenderBoundsFallback)
            {
                lifetimes.Fill(-1f);
                return;
            }

            var positions = run.ReadVectors(ParticleField.Position);

            // The outer box should be the entity's collision box rather than the box around every hitbox
            for (var i = 0; i < lifetimes.Length; i++)
            {
                if (cache.Contains(positions[i], boundBox) != cullOutside)
                {
                    lifetimes[i] = -1f;
                }
            }
        }
    }
}
