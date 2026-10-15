using System.Linq;

namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Keeps every particle within a distance band around a point that travels along a path of
    /// control points with the particle's age, scaled by <c>m_flTimeScale</c>. Each point is a
    /// control point plus an offset, in the control point's frame with local coordinates, and the
    /// path runs straight from one point to the next over each point's <c>m_flTimeDuration</c>. A
    /// looped path returns to its first point and wraps the age; on an open path, particles younger
    /// or older than the path are left alone. Only the first 32 points are used, and only
    /// <see cref="Particle.Position"/> is written.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ConstrainDistanceToUserSpecifiedPath">C_OP_ConstrainDistanceToUserSpecifiedPath</seealso>
    class ConstrainDistanceToUserSpecifiedPath : ParticleFunctionConstraint
    {
        private const int MaxPoints = 32;

        private readonly float minDistance;
        private readonly float maxDistance = 100f;
        private readonly float timeScale = 1f;
        private readonly bool loopedPath;
        private readonly PathPoint[] points;

        private readonly Vector3[] pointPositions = new Vector3[MaxPoints];
        private readonly float[] pointTimes = new float[MaxPoints];

        private readonly record struct PathPoint(int ControlPoint, bool LocalCoords, Vector3 Offset, float Duration);

        public ConstrainDistanceToUserSpecifiedPath(ParticleDefinitionParser parse) : base(parse)
        {
            minDistance = parse.Float("m_fMinDistance", minDistance);
            maxDistance = parse.Float("m_flMaxDistance", maxDistance);
            timeScale = parse.Float("m_flTimeScale", timeScale);
            loopedPath = parse.Boolean("m_bLoopedPath", loopedPath);
            points = [.. parse.Array("m_pointList").Select(static point => new PathPoint(
                point.Int32("m_nControlPoint"),
                point.Boolean("m_bLocalCoords"),
                point.Vector3("m_vOffset"),
                point.Float("m_flTimeDuration")))];
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (points.Length < 2)
            {
                return false;
            }

            var count = 0;
            var total = 0f;

            foreach (var point in points.AsSpan(0, Math.Min(points.Length, MaxPoints)))
            {
                var controlPoint = particleSystemState.GetControlPoint(point.ControlPoint);
                var offset = point.LocalCoords ? Vector3.TransformNormal(point.Offset, controlPoint.GetRotationMatrix()) : point.Offset;

                pointPositions[count] = controlPoint.Position + offset;
                pointTimes[count] = total;
                total += point.Duration;
                count++;
            }

            if (count < MaxPoints && loopedPath)
            {
                pointPositions[count] = pointPositions[0];
                pointTimes[count] = total;
                count++;
            }

            var low = minDistance;
            var high = MathF.Max(low, maxDistance);
            var moved = false;

            foreach (ref var particle in particles.Current)
            {
                var t = (particleSystemState.Age - particle.CreationTime) * timeScale;

                if (loopedPath)
                {
                    t %= total;
                }

                if (!TryGetPathPosition(t, count, out var target))
                {
                    continue;
                }

                var delta = particle.Position - target;
                var distance = delta.Length();

                if ((distance >= low && distance <= high) || distance == 0f)
                {
                    continue;
                }

                particle.Position = target + (delta * (Math.Clamp(distance, low, high) / distance));
                moved = true;
            }

            return moved;
        }

        /// <summary>
        /// The point on the path at time <paramref name="t"/>; at a time two segments share, the later
        /// segment's start.
        /// </summary>
        private bool TryGetPathPosition(float t, int count, out Vector3 position)
        {
            position = Vector3.Zero;
            var matched = false;
            var previous = 0f;

            for (var k = 1; k < count; k++)
            {
                if (previous <= t && t <= pointTimes[k])
                {
                    var span = pointTimes[k] - previous;
                    var fraction = span != 0f ? (t - previous) / span : 1f;
                    position = Vector3.Lerp(pointPositions[k - 1], pointPositions[k], fraction);
                    matched = true;
                }

                previous = pointTimes[k];
            }

            return matched;
        }
    }
}
