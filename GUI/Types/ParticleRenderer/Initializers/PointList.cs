using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class PointList : ParticleFunctionInitializer
    {
        private class PointDefinition
        {
            public int ControlPointID;
            public bool UseLocalOffset;
            public Vector3 Offset = Vector3.Zero;
            public Vector3 GetPosition(ParticleSystemRenderState particleSystem)
            {
                var origin = particleSystem.GetControlPoint(ControlPointID).Position;
                return origin + Offset; // temp: doesn't account for local offset (whatever that does).
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
            if (usePath)
            {
                var relativeParticleNumber = currentNumber++ % numPointsOnPath;

                var numPathNodes = closedLoop
                    ? pointList.Count + 1
                    : pointList.Count;

                // Percentage of path completed / 100
                var pathCompletion = relativeParticleNumber / (float)numPointsOnPath;

                // Shortcut so we don't index out of bounds
                if (pathCompletion == 1.0f)
                {
                    return pointList[numPathNodes - 1].GetPosition(particleSystem);
                }

                // Get the ID first of the two points we're interpolating between
                var firstPointID = (int)MathF.Floor(pathCompletion * numPathNodes);

                var pos1 = pointList[firstPointID].GetPosition(particleSystem);

                Vector3 pos2;
                // If we need to loop around and sample the first point, just take the first point so we don't index out of bounds
                if (closedLoop && firstPointID == pointList.Count - 1)
                {
                    pos2 = pointList[0].GetPosition(particleSystem);
                }
                else
                {
                    pos2 = pointList[firstPointID + 1].GetPosition(particleSystem);
                }

                // I think this is right?
                var point1Percent = (float)firstPointID / numPathNodes;
                var point2Percent = (float)(firstPointID + 1) / numPathNodes;

                var relativeBlend = MathUtils.Remap(pathCompletion, point1Percent, point2Percent);

                return Vector3.Lerp(pos1, pos2, relativeBlend);
            }
            else
            {
                var pointID = currentNumber++ % pointList.Count;

                return pointList[pointID].GetPosition(particleSystem);
            }

        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var particlePosition = GetParticlePosition(particleSystemState);

            particle.SetVector(FieldOutput, particlePosition);

            return particle;
        }
    }
}
