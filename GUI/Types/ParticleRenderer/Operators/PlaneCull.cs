using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class PlaneCull : IParticleOperator
    {
        private readonly int cp;
        private readonly float planeOffset;
        private readonly Vector3 planeNormal = new(0, 0, 1);
        private readonly bool localSpace;

        public PlaneCull(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nPlaneControlPoint"))
            {
                cp = keyValues.GetInt32Property("m_nPlaneControlPoint");
            }

            if (keyValues.ContainsKey("m_vecPlaneDirection"))
            {
                planeNormal = Vector3.Normalize(keyValues.GetArray<double>("m_vecPlaneDirection").ToVector3());
            }

            if (keyValues.ContainsKey("m_flPlaneOffset"))
            {
                planeOffset = keyValues.GetFloatProperty("m_flPlaneOffset");
            }

            // currently does nothing
            if (keyValues.ContainsKey("m_bLocalSpace"))
            {
                localSpace = keyValues.GetProperty<bool>("m_bLocalSpace");
            }
        }
        private bool CulledByPlane(Vector3 position, ParticleSystemRenderState particleSystemState)
        {
            var pointOnPlane = particleSystemState.GetControlPoint(cp).Position;

            // Offset in normal direction by planeOffset
            pointOnPlane -= (planeNormal * planeOffset);

            var sign = Vector3.Dot(planeNormal, position - pointOnPlane);
            return sign < 0;
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                if (CulledByPlane(particles[i].Position, particleSystemState))
                {
                    particles[i].Kill();
                }
            }
        }
    }
}
