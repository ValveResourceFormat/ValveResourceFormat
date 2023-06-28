using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class CreateAlongPath : IParticleInitializer
    {
        private readonly int startCP;
        private readonly int endCP = 1;

        private readonly float maximumDistance;
        private readonly bool useRandom;

        private readonly float midpointPosition;

        private readonly Vector3 startOffset = Vector3.Zero;
        private readonly Vector3 midpointOffset = Vector3.Zero;
        private readonly Vector3 endOffset = Vector3.Zero;
        public CreateAlongPath(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_bUseRandomCPs"))
            {
                useRandom = keyValues.GetProperty<bool>("m_bUseRandomCPs");
            }

            if (keyValues.ContainsKey("m_flMaxDistance"))
            {
                maximumDistance = keyValues.GetFloatProperty("m_flMaxDistance");
            }


            // No branch because the functionality of this initializer relies on path params existing
            var pathParams = keyValues.GetSubCollection("m_PathParams");

            if (pathParams.ContainsKey("m_nStartControlPointNumber"))
            {
                startCP = pathParams.GetInt32Property("m_nStartControlPointNumber");
            }

            if (pathParams.ContainsKey("m_nEndControlPointNumber"))
            {
                endCP = pathParams.GetInt32Property("m_nEndControlPointNumber");
            }

            if (pathParams.ContainsKey("m_flMidPoint"))
            {
                midpointPosition = pathParams.GetFloatProperty("m_flMidPoint");
            }

            if (pathParams.ContainsKey("m_vStartPointOffset"))
            {
                startOffset = pathParams.GetArray<double>("m_vStartPointOffset").ToVector3();
            }

            if (pathParams.ContainsKey("m_vMidPointOffset"))
            {
                midpointOffset = pathParams.GetArray<double>("m_vMidPointOffset").ToVector3();
            }

            if (pathParams.ContainsKey("m_vEndOffset"))
            {
                endOffset = pathParams.GetArray<double>("m_vEndOffset").ToVector3();
            }
        }

        private Vector3 GetParticlePosition(ParticleSystemRenderState particleSystem)
        {
            var progress = Random.Shared.NextSingle();
            if (useRandom)
            {
                Vector3 cpPos0;
                Vector3 cpPos1;
                for (var cp = startCP; cp <= endCP - 1; cp++)
                {
                    cpPos0 = particleSystem.GetControlPoint(cp).Position;
                    cpPos1 = particleSystem.GetControlPoint(cp + 1).Position;

                    var startProgression = MathUtils.Remap(cp, startCP, endCP);
                    var endProgression = MathUtils.Remap(cp + 1, startCP, endCP);

                    if (progress < startProgression || progress > endProgression)
                    {
                        continue;
                    }

                    var localProgress = MathUtils.Remap(progress, startProgression, endProgression);
                    return InterpolatePositions(localProgress, cpPos0, cpPos1);
                }
            }
            else
            {
                return InterpolatePositions(progress, particleSystem.GetControlPoint(startCP).Position, particleSystem.GetControlPoint(endCP).Position);
            }

            throw new NotImplementedException($"Invalid path progression {progress}");
        }

        private Vector3 InterpolatePositions(float relativeProgression, Vector3 position0, Vector3 position1)
        {
            // todo: bulge and midpoint offset. Plus, curve!!
            return MathUtils.Lerp(relativeProgression, position0 + startOffset, position1 + endOffset);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var particlePosition = GetParticlePosition(particleSystemState);

            particle.SetInitialVector(ParticleField.Position, particlePosition);

            return particle;
        }
    }
}
