using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Gives new particles that start inside the hitboxes, or bones, of the model a control point
    /// carries, or with <c>m_bCullOutside</c> outside them, a life of -1. <c>m_bBoundBox</c> tests
    /// the box around all of them instead.
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

        public override ulong WrittenFields => FieldMask(ParticleField.LifeDuration);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones) is { Count: > 0 } cache
                && (cache.Object == null || cache.Contains(particle.Position, boundBox) != cullOutside))
            {
                particle.Lifetime = -1f;
            }

            return particle;
        }
    }
}
