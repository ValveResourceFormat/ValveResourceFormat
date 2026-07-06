namespace ValveResourceFormat.Renderer.Particles.Initializers
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
            public Vector3 GetPosition(ParticleSystemRenderState particleSystem)
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

        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly List<PointDefinition> pointList = [];

        private readonly int numPointsOnPath = 20;
        private readonly bool usePath;
        private readonly bool closedLoop;

        public PointList(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
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

        private int currentNumber;
        private Vector3 GetParticlePosition(ParticleSystemRenderState particleSystem)
        {
            if (pointList.Count == 0)
            {
                return Vector3.Zero;
            }

            if (usePath)
            {
                var pointCount = pointList.Count;

                // An open path interpolates across Count - 1 segments; a closed loop has one
                // extra segment wrapping back to the first point
                var numPathSegments = closedLoop ? pointCount : pointCount - 1;

                if (numPathSegments < 1)
                {
                    return pointList[0].GetPosition(particleSystem);
                }

                // An open path must place the last particle on the final point; a closed loop
                // wraps, so full completion is one step past the last particle
                var divisor = closedLoop ? numPointsOnPath : Math.Max(1, numPointsOnPath - 1);
                var pathCompletion = (currentNumber++ % numPointsOnPath) / (float)divisor;

                // Particles are spaced by arc length so unequal segments don't clump them
                Span<float> segmentLengths = numPathSegments <= 64 ? stackalloc float[numPathSegments] : new float[numPathSegments];
                var totalLength = 0f;

                for (var i = 0; i < numPathSegments; i++)
                {
                    var a = pointList[i].GetPosition(particleSystem);
                    var b = pointList[(i + 1) % pointCount].GetPosition(particleSystem);
                    segmentLengths[i] = Vector3.Distance(a, b);
                    totalLength += segmentLengths[i];
                }

                if (totalLength <= 0f)
                {
                    return pointList[0].GetPosition(particleSystem);
                }

                var targetLength = pathCompletion * totalLength;
                var segment = 0;

                while (segment < numPathSegments - 1 && targetLength > segmentLengths[segment])
                {
                    targetLength -= segmentLengths[segment];
                    segment++;
                }

                var pos1 = pointList[segment].GetPosition(particleSystem);
                var pos2 = pointList[(segment + 1) % pointCount].GetPosition(particleSystem);
                var blend = segmentLengths[segment] > 0f ? targetLength / segmentLengths[segment] : 0f;

                return Vector3.Lerp(pos1, pos2, blend);
            }
            else
            {
                var pointID = currentNumber++ % pointList.Count;

                return pointList[pointID].GetPosition(particleSystem);
            }

        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var particlePosition = GetParticlePosition(particleSystemState);

            particle.SetVector(FieldOutput, particlePosition);

            return particle;
        }
    }
}
