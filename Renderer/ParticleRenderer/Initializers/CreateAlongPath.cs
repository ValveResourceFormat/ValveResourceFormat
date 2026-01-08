using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class CreateAlongPath : ParticleFunctionInitializer
    {
        private readonly int StartControlPointNumber;
        private readonly int EndControlPointNumber = 1;

        private readonly float MaxDistance;
        private readonly bool UseRandomCPs;

        private readonly float MidPoint;

        private readonly Vector3 StartPointOffset = Vector3.Zero;
        private readonly Vector3 MidPointOffset = Vector3.Zero;
        private readonly Vector3 EndOffset = Vector3.Zero;
        public CreateAlongPath(ParticleDefinitionParser parse) : base(parse)
        {
            UseRandomCPs = parse.Boolean("m_bUseRandomCPs", UseRandomCPs);
            MaxDistance = parse.Float("m_flMaxDistance", MaxDistance);

            // The functionality of this initializer relies on path params existing
            parse = new ParticleDefinitionParser(parse.Data.GetSubCollection("m_PathParams"));

            StartControlPointNumber = parse.Int32("m_nStartControlPointNumber", StartControlPointNumber);
            EndControlPointNumber = parse.Int32("m_nEndControlPointNumber", EndControlPointNumber);
            MidPoint = parse.Float("m_flMidPoint", MidPoint);
            StartPointOffset = parse.Vector3("m_vStartPointOffset", StartPointOffset);
            MidPointOffset = parse.Vector3("m_vMidPointOffset", MidPointOffset);
            EndOffset = parse.Vector3("m_vEndOffset", EndOffset);
        }

        private Vector3 GetParticlePosition(ParticleSystemRenderState particleSystem)
        {
            var progress = Random.Shared.NextSingle();
            if (UseRandomCPs)
            {
                Vector3 cpPos0;
                Vector3 cpPos1;
                for (var cp = StartControlPointNumber; cp <= EndControlPointNumber - 1; cp++)
                {
                    cpPos0 = particleSystem.GetControlPoint(cp).Position;
                    cpPos1 = particleSystem.GetControlPoint(cp + 1).Position;

                    var startProgression = MathUtils.Remap(cp, StartControlPointNumber, EndControlPointNumber);
                    var endProgression = MathUtils.Remap(cp + 1, StartControlPointNumber, EndControlPointNumber);

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
                return InterpolatePositions(progress, particleSystem.GetControlPoint(StartControlPointNumber).Position, particleSystem.GetControlPoint(EndControlPointNumber).Position);
            }

            throw new NotImplementedException($"Invalid path progression {progress}");
        }

        private Vector3 InterpolatePositions(float relativeProgression, Vector3 position0, Vector3 position1)
        {
            // todo: bulge and midpoint offset. Plus, curve!!
            return Vector3.Lerp(position0 + StartPointOffset, position1 + EndOffset, relativeProgression);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var particlePosition = GetParticlePosition(particleSystemState);

            particle.SetVector(ParticleField.Position, particlePosition);

            return particle;
        }
    }
}
