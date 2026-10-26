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
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            boundBox = parse.Boolean("m_bBoundBox", boundBox);
            outside = parse.Boolean("m_bOutside", outside);
            useBones = parse.Boolean("m_bUseBones", useBones);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            positionOffset = parse.VectorProvider("m_vecPosOffset", positionOffset);
            drag = Math.Clamp(parse.Float("m_fDrag", drag), 0f, float.BitDecrement(1f));
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (particleSystemState.PreviousStep <= 0f
                || HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones) is not { Count: > 0, Object: not null } cache)
            {
                return;
            }

            var keep = frameTime / particleSystemState.PreviousStep * MathF.Pow(1f - drag, frameTime * 30f);

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.ReadVectors(ParticleField.PositionPrevious);

            var offsets = positionOffset.Fill(current, particleSystemState);

            // Every particle is tested every step; a batch whose query fails does not hold back the particles after it
            for (var i = 0; i < positions.Length; i++)
            {
                var point = positions[i] + offsets[i];

                if (cache.Contains(point, boundBox) != outside)
                {
                    positions[i] = MathUtils.Lerp(previousPositions[i], positions[i], keep);
                }
            }
        }
    }
}
