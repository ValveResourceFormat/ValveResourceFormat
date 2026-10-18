using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Keeps every particle within a distance band around a point that travels along a path with the
    /// particle's age, reaching the end after <c>m_flTravelTime</c> (scaled per particle by
    /// <c>m_nFieldScale</c>), or at the parameter <c>m_nManualTField</c> holds. The maximum distance
    /// runs from <c>m_flMaxDistance0</c> at the start through <c>m_flMaxDistanceMid</c> to
    /// <c>m_flMaxDistance1</c> at the end. Every maximum is raised to at least the minimum, so the
    /// -1 defaults of the middle and end only mean "same as before" with a negative minimum. Only
    /// <see cref="Particle.Position"/> is written.
    /// </summary>
    /// <remarks>
    /// Particles are handled in blocks of four. When any of a block exceeds the largest of the three
    /// distances, the block is clamped with the maximum distances the last block that did not left
    /// behind, rather than with each particle's own.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ConstrainDistanceToPath">C_OP_ConstrainDistanceToPath</seealso>
    class ConstrainDistanceToPath : ParticleFunctionConstraint
    {
        private const int BlockSize = 4;

        private readonly float minDistance;
        private readonly float maxDistanceStart = 100f;
        private readonly float maxDistanceMid = -1f;
        private readonly float maxDistanceEnd = -1f;
        private readonly ParticlePathParameters pathParams;
        private readonly float travelTime = 10f;
        private readonly ParticleField fieldScale = ParticleField.NoneDisabled;
        private readonly ParticleField manualTField = ParticleField.NoneDisabled;

        public ConstrainDistanceToPath(ParticleDefinitionParser parse) : base(parse)
        {
            minDistance = parse.Float("m_fMinDistance", minDistance);
            maxDistanceStart = MathF.Max(minDistance, parse.Float("m_flMaxDistance0", maxDistanceStart));
            maxDistanceMid = MathF.Max(minDistance, parse.Float("m_flMaxDistanceMid", maxDistanceMid));
            maxDistanceEnd = MathF.Max(minDistance, parse.Float("m_flMaxDistance1", maxDistanceEnd));
            pathParams = new ParticlePathParameters(parse, "m_PathParameters").WithClampedControlPoints();
            travelTime = parse.Float("m_flTravelTime", travelTime);
            fieldScale = parse.ParticleField("m_nFieldScale", fieldScale);
            manualTField = parse.ParticleField("m_nManualTField", manualTField);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var time = particleSystemState.Age;
            var (start, mid, end) = ParticlePath.CalculatePathValues(particleSystemState, pathParams, time);

            var distanceMid = maxDistanceMid < 0f ? maxDistanceStart : maxDistanceMid;
            var distanceEnd = maxDistanceEnd < 0f ? distanceMid : maxDistanceEnd;
            var uniform = (distanceMid == maxDistanceStart || maxDistanceMid < 0f) && (distanceEnd == maxDistanceStart || maxDistanceEnd < 0f);
            var largest = MathUtils.Max(maxDistanceStart, distanceMid, distanceEnd);
            var minSquared = minDistance * minDistance;

            Span<float> limit = [largest, largest, largest, largest];
            Span<float> pathT = stackalloc float[BlockSize];
            Span<Vector3> target = stackalloc Vector3[BlockSize];
            Span<float> distanceSquared = stackalloc float[BlockSize];

            var current = particles.Current;
            var moved = false;

            for (var blockStart = 0; blockStart < current.Length; blockStart += BlockSize)
            {
                var lanes = Math.Min(BlockSize, current.Length - blockStart);
                var anyBeyondLargest = false;

                for (var lane = 0; lane < lanes; lane++)
                {
                    ref var particle = ref current[blockStart + lane];
                    var t = PathParameter(ref particle, time);
                    var point = ParticlePath.Evaluate(start, mid, end, t);

                    pathT[lane] = t;
                    target[lane] = point;
                    distanceSquared[lane] = Vector3.DistanceSquared(particle.Position, point);
                    anyBeyondLargest |= largest * largest < distanceSquared[lane];
                }

                if (!uniform && !anyBeyondLargest)
                {
                    for (var lane = 0; lane < lanes; lane++)
                    {
                        var t = pathT[lane];
                        limit[lane] = MathUtils.Lerp(MathUtils.Lerp(maxDistanceStart, distanceMid, t), MathUtils.Lerp(distanceMid, distanceEnd, t), t);
                    }
                }

                for (var lane = 0; lane < lanes; lane++)
                {
                    var lengthSquared = distanceSquared[lane];
                    var over = uniform || anyBeyondLargest
                        ? largest * largest < lengthSquared
                        : limit[lane] * limit[lane] < lengthSquared;
                    var under = lengthSquared < minSquared;

                    if (!over && !under)
                    {
                        continue;
                    }

                    ref var particle = ref current[blockStart + lane];
                    var direction = lengthSquared > 0f ? (particle.Position - target[lane]) / MathF.Sqrt(lengthSquared) : Vector3.Zero;

                    particle.Position = target[lane] + (direction * (under ? minDistance : limit[lane]));
                    moved = true;
                }
            }

            return moved;
        }

        private float PathParameter(ref Particle particle, float time)
        {
            if (manualTField != ParticleField.NoneDisabled)
            {
                return particle.GetScalar(manualTField);
            }

            var scale = fieldScale != ParticleField.NoneDisabled ? particle.GetScalar(fieldScale) : 1f;

            return MathF.Min(1f, (time - particle.CreationTime) / MathF.Max(travelTime * scale, ParticleMath.FloatEpsilon));
        }
    }
}
