using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills particles inside the hitboxes, or bones, of the model a control point carries, or with
    /// <c>m_bCullOutside</c> outside them. <c>m_bBoundBox</c> tests the box around all of them
    /// instead.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ModelCull">C_OP_ModelCull</seealso>
    class ModelCull : ParticleFunctionOperator
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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones) is not { Count: > 0, Object: not null } cache)
            {
                return;
            }

            var raiseCullEvents = particleSystemState.ListensFor(ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED_ON_CULL);

            foreach (ref var particle in particles.Current)
            {
                if (cache.Contains(particle.Position, boundBox) == cullOutside)
                {
                    continue;
                }

                particle.Kill();

                if (raiseCullEvents)
                {
                    particles.RaiseEvent(particle.Index, ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED_ON_CULL);
                }
            }
        }
    }
}
