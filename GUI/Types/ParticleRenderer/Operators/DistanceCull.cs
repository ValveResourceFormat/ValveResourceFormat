using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    // Cull when crossing sphere
    class DistanceCull : IParticleOperator
    {
        private readonly int cp;
        private readonly float distance;
        private readonly Vector3 pointOffset = Vector3.Zero;
        private readonly bool cullInside;
        public DistanceCull(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nControlPoint"))
            {
                cp = keyValues.GetInt32Property("m_nControlPoint");
            }

            if (keyValues.ContainsKey("m_vecPointOffset"))
            {
                pointOffset = keyValues.GetArray<double>("m_vecPointOffset").ToVector3();
            }

            if (keyValues.ContainsKey("m_flDistance"))
            {
                distance = keyValues.GetFloatProperty("m_flDistance");
            }

            if (keyValues.ContainsKey("m_bCullInside"))
            {
                cullInside = keyValues.GetProperty<bool>("m_bCullInside");
            }
        }
        private bool CulledBySphere(Vector3 position, ParticleSystemRenderState particleSystemState)
        {
            var sphereOrigin = particleSystemState.GetControlPoint(cp).Position + pointOffset;

            var distanceFromEdge = Vector3.Distance(sphereOrigin, position) - distance;

            return cullInside
                ? distanceFromEdge < 0
                : distanceFromEdge > 0;
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                if (CulledBySphere(particle.Position, particleSystemState))
                {
                    particle.Kill();
                }
            }
        }
    }
}
