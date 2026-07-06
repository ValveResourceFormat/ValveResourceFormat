using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places particles at positions interpolated along a path defined by a sequence of control
    /// points. Supports optional random CP pair selection and a configurable midpoint bulge.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateAlongPath">C_INIT_CreateAlongPath</seealso>
    class CreateAlongPath : ParticleFunctionInitializer
    {
        private readonly int StartControlPointNumber;
        private readonly int EndControlPointNumber = 1;

        private readonly float MaxDistance;
        private readonly bool UseRandomCPs; // randomly select sequential CP pairs between start and end points

        private readonly float MidPoint;

        private readonly Vector3 StartPointOffset = Vector3.Zero;
        private readonly Vector3 MidPointOffset = Vector3.Zero;
        private readonly Vector3 EndOffset = Vector3.Zero; // Offset from control point for path end
        public CreateAlongPath(ParticleDefinitionParser parse) : base(parse)
        {
            UseRandomCPs = parse.Boolean("m_bUseRandomCPs", UseRandomCPs);
            // Modern schema names it m_fMaxDistance; older content uses m_flMaxDistance.
            MaxDistance = parse.Float("m_fMaxDistance", parse.Float("m_flMaxDistance", MaxDistance));

            // The functionality of this initializer relies on path params existing
            parse = new ParticleDefinitionParser(parse.Data.GetSubCollection("m_PathParams"), parse.Logger);

            StartControlPointNumber = parse.Int32("m_nStartControlPointNumber", StartControlPointNumber);
            EndControlPointNumber = parse.Int32("m_nEndControlPointNumber", EndControlPointNumber);
            MidPoint = parse.Float("m_flMidPoint", MidPoint);
            StartPointOffset = parse.Vector3("m_vStartPointOffset", StartPointOffset);
            MidPointOffset = parse.Vector3("m_vMidPointOffset", MidPointOffset);
            EndOffset = parse.Vector3("m_vEndOffset", EndOffset);
        }

        private Vector3 GetParticlePosition(ParticleSystemRenderState particleSystem, int particleId)
        {
            var startCp = StartControlPointNumber;
            var endCp = EndControlPointNumber;

            if (UseRandomCPs)
            {
                endCp = startCp + 1 + (int)(Random.Shared.NextSingle() * (endCp - startCp));
                startCp = endCp - 1;
            }

            var progress = Random.Shared.NextSingle();
            var position = InterpolatePositions(progress, particleSystem.GetControlPoint(startCp).Position, particleSystem.GetControlPoint(endCp).Position);

            position += ParticleCollection.RandomBetweenPerComponent(particleId, new Vector3(-MaxDistance), new Vector3(MaxDistance));

            return position;
        }

        private Vector3 InterpolatePositions(float relativeProgression, Vector3 position0, Vector3 position1)
        {
            // todo: bulge and midpoint offset. Plus, curve!!
            return Vector3.Lerp(position0 + StartPointOffset, position1 + EndOffset, relativeProgression);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var particlePosition = GetParticlePosition(particleSystemState, particle.ParticleID);

            particle.SetVector(ParticleField.Position, particlePosition);

            return particle;
        }
    }
}
