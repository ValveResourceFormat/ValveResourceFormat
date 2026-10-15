using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Drags the movement of particles that are inside the hitboxes, or bones, of the model a
    /// control point carries, or with <c>m_bOutside</c> outside them, keeping the same share of
    /// their step's motion per second however the steps fall. The tested point can be offset per
    /// particle, and <c>m_bBoundBox</c> tests the box around all of them instead.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ModelDampenMovement">C_OP_ModelDampenMovement</seealso>
    class ModelDampenMovement : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly bool boundBox;
        private readonly bool outside;
        private readonly bool useBones;
        private readonly string hitboxSetName = "default";
        private readonly IVectorProvider positionOffset = new LiteralVectorProvider(Vector3.Zero);
        private readonly float drag;

        public ModelDampenMovement(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = Math.Clamp(parse.Int32("m_nControlPointNumber", controlPoint), 0, ParticleSystemState.MaxControlPoint);
            boundBox = parse.Boolean("m_bBoundBox", boundBox);
            outside = parse.Boolean("m_bOutside", outside);
            useBones = parse.Boolean("m_bUseBones", useBones);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            positionOffset = parse.VectorProvider("m_vecPosOffset", positionOffset);
            drag = parse.Float("m_fDrag", drag);
            drag = drag < 0f ? 0f : MathF.Min(0.99999988f, drag);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (particles.PreviousFrameTime <= 0f
                || HitBoxCache.Refresh(particleSystemState, controlPoint, useBones ? string.Empty : hitboxSetName, useBones) is not { Count: > 0 } cache)
            {
                return;
            }

            var keep = frameTime / particles.PreviousFrameTime * MathF.Exp(MathF.Log(1f - drag) / 0.033333335f * frameTime);

            foreach (ref var particle in particles.Current)
            {
                var point = particle.Position + positionOffset.NextVector(ref particle, particleSystemState);

                if (cache.Contains(point, boundBox) != outside)
                {
                    particle.Position = ((particle.Position - particle.PositionPrevious) * keep) + particle.PositionPrevious;
                }
            }
        }
    }
}
