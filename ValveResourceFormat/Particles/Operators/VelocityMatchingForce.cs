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
    /// pair gets full weight once that product reaches 1 and none below it.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_VelocityMatchingForce">C_OP_VelocityMatchingForce</seealso>
    class VelocityMatchingForce : ParticleFunctionOperator
    {
        private const float LengthEpsilon = 1.1920929e-7f;

        private readonly float dirScale = 0.25f;
        private readonly float spdScale = 0.25f;
        private readonly float neighborDistance = -1f;
        private readonly float facingStrength = 1f;
        private readonly int cpBroadcast = -1;

        private Vector3 averageDirection;
        private float averageSpeed;

        public VelocityMatchingForce(ParticleDefinitionParser parse) : base(parse)
        {
            dirScale = parse.Float("m_flDirScale", dirScale);
            spdScale = parse.Float("m_flSpdScale", spdScale);
            neighborDistance = parse.Float("m_flNeighborDistance", neighborDistance);
            facingStrength = parse.Float("m_flFacingStrength", facingStrength);
            cpBroadcast = parse.Int32("m_nCPBroadcast", cpBroadcast);
        }

        public override void Reset()
        {
            averageDirection = Vector3.Zero;
            averageSpeed = 0f;
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (neighborDistance < 0f)
            {
                MatchAverage(particles, frameTime, particleSystemState);
                return;
            }

            var current = particles.Current;
            var distanceSquared = neighborDistance * neighborDistance;

            for (var i = 0; i < current.Length - 1; i++)
            {
                for (var j = i + 1; j < current.Length; j++)
                {
                    MatchPair(ref current[i], ref current[j], frameTime, strength, distanceSquared);
                }
            }
        }

        private void MatchAverage(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState)
        {
            var velocitySum = Vector3.Zero;
            var speedSum = 0f;

            foreach (ref var particle in particles.Current)
            {
                var velocity = (particle.Position - particle.PositionPrevious) / frameTime;
                var speed = velocity.Length();
                velocitySum += velocity;
                speedSum += speed;

                if (averageDirection == Vector3.Zero)
                {
                    continue;
                }

                var direction = NormalizeWithEpsilon(velocity);
                direction = NormalizeWithEpsilon(((averageDirection - direction) * dirScale) + direction);
                var newSpeed = ((averageSpeed - speed) * spdScale) + speed;

                particle.Position = (direction * newSpeed * frameTime) + particle.PositionPrevious;
            }

            averageDirection = NormalizeWithEpsilon(velocitySum);
            averageSpeed = speedSum / particles.Count;

            if (cpBroadcast == -1)
            {
                return;
            }

            particleSystemState.SetControlPointValue(cpBroadcast, new Vector3(averageSpeed));

            var (left, up) = ParticleMath.VectorVectors(averageDirection);
            particleSystemState.SetControlPointOrientation(cpBroadcast, averageDirection, left, up);
        }

        private void MatchPair(ref Particle first, ref Particle second, float frameTime, float strength, float distanceSquared)
        {
            var separation = (second.Position - first.Position).LengthSquared();

            if (separation <= LengthEpsilon || separation >= distanceSquared)
            {
                return;
            }

            var closeness = (1f - MathUtils.Saturate(separation / distanceSquared)) * strength;
            var weight = closeness * closeness;

            var firstVelocity = (first.Position - first.PositionPrevious) / frameTime;
            var secondVelocity = (second.Position - second.PositionPrevious) / frameTime;
            var firstSpeed = firstVelocity.Length();
            var secondSpeed = secondVelocity.Length();
            var averagePairSpeed = (secondSpeed + firstSpeed) * 0.5f;

            if (facingStrength != 0f)
            {
                var facing = MathF.Max(0f, Vector3.Dot(secondVelocity, firstVelocity));
                weight *= facingStrength == 1f
                    ? (facing - 1f < 0f ? 0f : 1f)
                    : MathUtils.Saturate((facing - facingStrength) / (1f - facingStrength));
            }

            var shared = MathUtils.SafeNormalize(((secondVelocity - firstVelocity) * 0.5f) + firstVelocity);
            var turn = weight * dirScale;
            var speedBlend = MathUtils.Saturate(weight * spdScale);

            Steer(ref first, firstVelocity, firstSpeed);
            Steer(ref second, secondVelocity, secondSpeed);

            void Steer(ref Particle particle, Vector3 velocity, float speed)
            {
                var direction = MathUtils.SafeNormalize(velocity);
                var newVelocity = (((shared - direction) * turn) + direction) * ((speedBlend * (averagePairSpeed - speed)) + speed);
                particle.Position = (newVelocity * frameTime) + particle.PositionPrevious;
            }
        }

        private static Vector3 NormalizeWithEpsilon(Vector3 value)
            => value / MathF.Sqrt(value.LengthSquared() + LengthEpsilon);
    }
}
