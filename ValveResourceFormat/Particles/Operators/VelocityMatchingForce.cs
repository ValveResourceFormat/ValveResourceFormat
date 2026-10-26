using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Steers particle velocities toward each other, like a flock. With a negative
    /// <c>m_flNeighborDistance</c> every particle turns by <c>m_flDirScale</c> toward the average
    /// direction and changes speed by <c>m_flSpdScale</c> toward the average speed of the previous
    /// step; otherwise each pair closer than the neighbour distance moves toward their shared
    /// velocity, weighted by how close they are and, through <c>m_flFacingStrength</c>, by how much
    /// they already move together.
    /// </summary>
    /// <remarks>
    /// The averages lag one step behind, so the first step only measures them. <c>m_nCPBroadcast</c>
    /// receives the average speed in all three components and faces the average direction. The facing
    /// weight tests the dot product of the raw velocities, so with the default facing strength of 1 a
    /// pair gets full weight once that product reaches 1 and none below it. With <c>m_bUseAABB</c> only
    /// the pairs whose boxes, grown by each radius plus the neighbour distance, overlap are matched, in
    /// the order <see cref="ParticleObjectTree"/> finds them.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_VelocityMatchingForce">C_OP_VelocityMatchingForce</seealso>
    class VelocityMatchingForce : ParticleFunctionOperator
    {
        private readonly float dirScale = 0.25f;
        private readonly float spdScale = 0.25f;
        private readonly float neighborDistance = -1f;
        private readonly float facingStrength = 1f;
        private readonly int cpBroadcast = -1;
        private readonly bool useAABB;
        private readonly ParticleObjectTree objectTree = new();

        private Vector3 averageDirection;
        private float averageSpeed;

        public VelocityMatchingForce(ParticleDefinitionParser parse) : base(parse)
        {
            dirScale = parse.Float("m_flDirScale", dirScale);
            spdScale = parse.Float("m_flSpdScale", spdScale);
            neighborDistance = parse.Float("m_flNeighborDistance", neighborDistance);
            facingStrength = parse.Float("m_flFacingStrength", facingStrength);
            cpBroadcast = parse.Int32("m_nCPBroadcast", cpBroadcast);
            useAABB = parse.Boolean("m_bUseAABB", useAABB);
        }

        public override void Reset()
        {
            averageDirection = Vector3.Zero;
            averageSpeed = 0f;
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (neighborDistance < 0f)
            {
                MatchAverage(particles, frameTime, particleSystemState);
                return;
            }

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.ReadVectors(ParticleField.PositionPrevious);
            var distanceSquared = neighborDistance * neighborDistance;

            if (useAABB)
            {
                objectTree.Build(positions, current.ReadFloats(ParticleField.Radius), neighborDistance, particleSystemState.Data?.WorldBoundingBox);
                var slotToParticle = objectTree.SlotToParticle;

                // Slots are mapped back to particles. The game passes the slot numbers on as particle
                // indices, which matches unrelated particles; that is not reproduced
                foreach (var (first, second) in objectTree.Pairs)
                {
                    MatchPair(positions, previousPositions, slotToParticle[first], slotToParticle[second], frameTime, strength, distanceSquared);
                }

                return;
            }

            for (var i = 0; i < positions.Length - 1; i++)
            {
                for (var j = i + 1; j < positions.Length; j++)
                {
                    MatchPair(positions, previousPositions, i, j, frameTime, strength, distanceSquared);
                }
            }
        }

        private void MatchAverage(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState)
        {
            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.ReadVectors(ParticleField.PositionPrevious);
            var velocitySum = Vector3.Zero;
            var speedSum = 0f;

            for (var i = 0; i < positions.Length; i++)
            {
                var velocity = GetVelocity(positions[i], previousPositions[i], frameTime);
                var speed = velocity.Length();
                velocitySum += velocity;
                speedSum += speed;

                if (averageDirection == Vector3.Zero)
                {
                    continue;
                }

                var direction = ParticleMath.NormalizeWithEpsilon(velocity);
                direction = ParticleMath.NormalizeWithEpsilon(MathUtils.Lerp(direction, averageDirection, dirScale));
                var newSpeed = MathUtils.Lerp(speed, averageSpeed, spdScale);

                positions[i] = (direction * newSpeed * frameTime) + previousPositions[i];
            }

            averageDirection = ParticleMath.NormalizeWithEpsilon(velocitySum);
            averageSpeed = speedSum / particles.Count;

            if (cpBroadcast == -1)
            {
                return;
            }

            particleSystemState.SetControlPointValue(cpBroadcast, new Vector3(averageSpeed));

            var (left, up) = ParticleMath.VectorVectors(averageDirection);
            particleSystemState.SetControlPointOrientation(cpBroadcast, averageDirection, left, up);
        }

        private static Vector3 GetVelocity(Vector3 position, Vector3 previousPosition, float frameTime)
            => frameTime > 0f ? (position - previousPosition) / frameTime : Vector3.Zero;

        private void MatchPair(Span<Vector3> positions, ReadOnlySpan<Vector3> previousPositions, int first, int second, float frameTime, float strength, float distanceSquared)
        {
            var separation = (positions[second] - positions[first]).LengthSquared();

            if (separation <= ParticleMath.FloatEpsilon || separation >= distanceSquared)
            {
                return;
            }

            var closeness = (1f - MathUtils.Saturate(separation / distanceSquared)) * strength;
            var weight = closeness * closeness;

            var firstVelocity = GetVelocity(positions[first], previousPositions[first], frameTime);
            var secondVelocity = GetVelocity(positions[second], previousPositions[second], frameTime);
            var firstSpeed = firstVelocity.Length();
            var secondSpeed = secondVelocity.Length();
            var averagePairSpeed = (secondSpeed + firstSpeed) * 0.5f;

            if (facingStrength != 0f)
            {
                var facing = MathF.Max(0f, Vector3.Dot(secondVelocity, firstVelocity));
                weight *= MathUtils.RemapValClamped(facing, facingStrength, 1f, 0f, 1f);
            }

            var shared = MathUtils.SafeNormalize((firstVelocity + secondVelocity) * 0.5f);
            var turn = weight * dirScale;
            var speedBlend = MathUtils.Saturate(weight * spdScale);

            var newFirstVelocity = MathUtils.Lerp(MathUtils.SafeNormalize(firstVelocity), shared, turn) * MathUtils.Lerp(firstSpeed, averagePairSpeed, speedBlend);
            var newSecondVelocity = MathUtils.Lerp(MathUtils.SafeNormalize(secondVelocity), shared, turn) * MathUtils.Lerp(secondSpeed, averagePairSpeed, speedBlend);

            positions[first] = (newFirstVelocity * frameTime) + previousPositions[first];
            positions[second] = (newSecondVelocity * frameTime) + previousPositions[second];
        }
    }
}
