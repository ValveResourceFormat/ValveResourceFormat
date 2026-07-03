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
                var relativeParticleNumber = currentNumber++ % numPointsOnPath;

                // An open path interpolates across Count - 1 segments; a closed loop has one
                // extra segment wrapping back to the first point
                var numPathSegments = closedLoop
                    ? pointList.Count
                    : pointList.Count - 1;

                // Percentage of path completed
                var pathCompletion = relativeParticleNumber / (float)numPointsOnPath;

                var scaledCompletion = pathCompletion * numPathSegments;

                // Get the ID of the first of the two points we're interpolating between
                var firstPointID = (int)MathF.Floor(scaledCompletion);

                var pos1 = pointList[firstPointID].GetPosition(particleSystem);
                var pos2 = pointList[(firstPointID + 1) % pointList.Count].GetPosition(particleSystem);

                var relativeBlend = scaledCompletion - firstPointID;

                return Vector3.Lerp(pos1, pos2, relativeBlend);
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
