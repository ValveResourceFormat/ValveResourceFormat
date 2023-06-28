using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class PointList : IParticleInitializer
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

        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly List<PointDefinition> pointList = new();

        private readonly int numPointsOnPath = 20;
        private readonly bool usePath;
        private readonly bool closedLoop;

        public PointList(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                fieldOutput = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_nNumPointsAlongPath"))
            {
                numPointsOnPath = keyValues.GetInt32Property("m_nNumPointsAlongPath");
            }

            if (keyValues.ContainsKey("m_bPlaceAlongPath"))
            {
                usePath = keyValues.GetProperty<bool>("m_bPlaceAlongPath");
            }

            if (keyValues.ContainsKey("m_bClosedLoop"))
            {
                closedLoop = keyValues.GetProperty<bool>("m_bClosedLoop");
            }

            if (keyValues.ContainsKey("m_pointList"))
            {
                var points = keyValues.GetArray("m_pointList");
                foreach (var point in points)
                {
                    var cp = 0;
                    var useLocalCoords = false;
                    var offset = Vector3.Zero;

                    if (point.ContainsKey("m_nControlPoint"))
                    {
                        cp = point.GetInt32Property("m_nControlPoint");
                    }

                    if (point.ContainsKey("m_bLocalCoords"))
                    {
                        useLocalCoords = point.GetProperty<bool>("m_bLocalCoords");
                    }

                    if (point.ContainsKey("m_vOffset"))
                    {
                        offset = point.GetArray<double>("m_vOffset").ToVector3();
                    }

                    var newPoint = new PointDefinition
                    {
                        ControlPointID = cp,
                        UseLocalOffset = useLocalCoords,
                        Offset = offset,
                    };

                    pointList.Add(newPoint);
                }
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

                return MathUtils.Lerp(relativeBlend, pos1, pos2);
            }
            else
            {
                var pointID = currentNumber++ % pointList.Count;

                return pointList[pointID].GetPosition(particleSystem);
            }

        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var particlePosition = GetParticlePosition(particleSystemState);

            particle.SetInitialVector(fieldOutput, particlePosition);

            return particle;
        }
    }
}
