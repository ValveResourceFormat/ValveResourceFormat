namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles at positions taken from a user-defined list of control-point-relative
    /// offsets. Optionally spaces particles evenly along the path formed by the list, with support
    /// for closed-loop paths.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PointList">C_INIT_PointList</seealso>
    class PointList : ParticleFunctionInitializer
    {
        private class PointDefinition
        {
            public int ControlPointID;
            public bool UseLocalOffset;
            public Vector3 Offset = Vector3.Zero;
            public Vector3 GetPosition(ParticleSystemState particleSystem)
            {
                if (UseLocalOffset)
                {
                    // The offset is authored in the control point's frame.
                    return ControlPointTransformProvider.TransformPosition(particleSystem, ControlPointID, Offset);
                }

                var origin = particleSystem.GetControlPoint(ControlPointID).Position;
                return origin + Offset;
            }
        }

        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly List<PointDefinition> pointList = [];

        private readonly int numPointsOnPath = 20;
        private readonly bool usePath;
        private readonly bool closedLoop;

        public PointList(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            numPointsOnPath = parse.Int32("m_nNumPointsAlongPath", numPointsOnPath);
            usePath = parse.Boolean("m_bPlaceAlongPath", usePath);
            closedLoop = parse.Boolean("m_bClosedLoop", closedLoop);

            foreach (var point in parse.Array("m_pointList"))
            {
                var cp = point.Int32("m_nControlPoint", 0);
                var useLocalCoords = point.Boolean("m_bLocalCoords", false);
                var offset = point.Vector3("m_vOffset", Vector3.Zero);

                var newPoint = new PointDefinition
                {
                    ControlPointID = cp,
                    UseLocalOffset = useLocalCoords,
                    Offset = offset,
                };

                pointList.Add(newPoint);
            }
        }

        public override ulong WrittenFields => fieldOutput == ParticleField.Position
            ? FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious)
            : FieldMask(fieldOutput);

        /// <summary>
        /// Where the particle in collection slot <paramref name="index"/> goes, or null to leave it as it
        /// is. Along a path the slots cycle through <c>m_nNumPointsAlongPath</c> stops spread over the
        /// path's length, and a stop the path's length does not reach is not placed.
        /// </summary>
        private Vector3? GetParticlePosition(int index, ParticleSystemState particleSystem)
        {
            var pointCount = pointList.Count;

            if (pointCount == 0)
            {
                return null;
            }

            if (!usePath || pointCount == 1)
            {
                return pointList[index % pointCount].GetPosition(particleSystem);
            }

            if (numPointsOnPath == 0)
            {
                return null;
            }

            Span<Vector3> points = pointCount <= 64 ? stackalloc Vector3[pointCount] : new Vector3[pointCount];

            for (var i = 0; i < pointCount; i++)
            {
                points[i] = pointList[i].GetPosition(particleSystem);
            }

            var segmentCount = closedLoop ? pointCount : pointCount - 1;
            var totalLength = 0f;

            for (var segment = 1; segment <= segmentCount; segment++)
            {
                totalLength += Vector3.Distance(points[segment % pointCount], points[segment - 1]);
            }

            var target = (1f / (numPointsOnPath - 1)) * (index % numPointsOnPath) * totalLength;
            var reached = 0f;

            for (var segment = 1; segment <= segmentCount; segment++)
            {
                var start = points[segment - 1];
                var end = points[segment % pointCount];
                var segmentEnd = reached + Vector3.Distance(end, start);

                if (segmentEnd >= target)
                {
                    var blend = reached == segmentEnd
                        ? (target - segmentEnd >= 0f ? 1f : 0f)
                        : Math.Clamp((target - reached) / (segmentEnd - reached), 0f, 1f);

                    return ((end - start) * blend) + start;
                }

                reached = segmentEnd;
            }

            return null;
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (GetParticlePosition(particle.Index, particleSystemState) is not { } particlePosition)
            {
                return particle;
            }

            particle.SetVector(fieldOutput, particlePosition);

            // Placement along the path leaves the previous position as it was
            if (fieldOutput == ParticleField.Position && (!usePath || pointList.Count == 1))
            {
                particle.PositionPrevious = particlePosition;
            }

            return particle;
        }
    }
}
