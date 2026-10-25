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
            ? ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask()
            : fieldOutput.Mask();

        /// <summary>
        /// Where along the path the particle in collection slot <paramref name="index"/> goes, or null to
        /// leave it as it is. The slots cycle through <c>m_nNumPointsAlongPath</c> stops spread over the
        /// path's length, and a stop the path's length does not reach is not placed.
        /// </summary>
        private Vector3? GetPathPosition(int index, ReadOnlySpan<Vector3> points, int segmentCount, float totalLength)
        {
            var target = (1f / (numPointsOnPath - 1)) * (index % numPointsOnPath) * totalLength;
            var reached = 0f;

            for (var segment = 1; segment <= segmentCount; segment++)
            {
                var start = points[segment - 1];
                var end = points[segment % points.Length];
                var segmentEnd = reached + Vector3.Distance(end, start);

                if (segmentEnd >= target)
                {
                    return MathUtils.Lerp(start, end, MathUtils.RemapValClamped(target, reached, segmentEnd, 0f, 1f));
                }

                reached = segmentEnd;
            }

            return null;
        }

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (!ParticleBuffer.IsVector(fieldOutput))
            {
                return;
            }

            var outputs = run.Vectors(fieldOutput);
            var pointCount = pointList.Count;

            if (pointCount == 0)
            {
                return;
            }

            if (!usePath || pointCount == 1)
            {
                var writesPrevious = fieldOutput == ParticleField.Position;
                var previousPositions = writesPrevious ? run.Vectors(ParticleField.PositionPrevious) : default;

                for (var i = 0; i < outputs.Length; i++)
                {
                    var position = pointList[(run.Start + i) % pointCount].GetPosition(particleSystemState);
                    outputs[i] = position;

                    if (writesPrevious)
                    {
                        previousPositions[i] = position;
                    }
                }

                return;
            }

            if (numPointsOnPath == 0)
            {
                return;
            }

            var points = ParticleScratch.Vectors(pointCount);

            for (var i = 0; i < pointCount; i++)
            {
                points[i] = pointList[i].GetPosition(particleSystemState);
            }

            var segmentCount = closedLoop ? pointCount : pointCount - 1;
            var totalLength = 0f;

            for (var segment = 1; segment <= segmentCount; segment++)
            {
                totalLength += Vector3.Distance(points[segment % pointCount], points[segment - 1]);
            }

            // Placement along the path leaves the previous position as it was, deliberately not
            // writing the previous position of the particle whose index is the path segment number
            for (var i = 0; i < outputs.Length; i++)
            {
                if (GetPathPosition(run.Start + i, points, segmentCount, totalLength) is { } position)
                {
                    outputs[i] = position;
                }
            }
        }
    }
}
