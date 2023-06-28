using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class Spin : IParticleOperator
    {
        private readonly float spinRate;
        private readonly float spinRateMin; // don't actually know if this is used or not. I don't think it is?
        private readonly float spinStopTime;
        public Spin(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nSpinRateDegrees"))
            {
                spinRate = keyValues.GetFloatProperty("m_nSpinRateDegrees");
            }

            if (keyValues.ContainsKey("m_nSpinRateMinDegrees"))
            {
                spinRateMin = keyValues.GetFloatProperty("m_nSpinRateMinDegrees");
            }

            if (keyValues.ContainsKey("m_fSpinRateStopTime"))
            {
                spinStopTime = keyValues.GetFloatProperty("m_fSpinRateStopTime");
            }
        }

        // Does not require SpinUpdate 
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                if (particles[i].Age < spinStopTime)
                {
                    particles[i].SetScalar(ParticleField.Roll, particles[i].Rotation.Z + spinRate * frameTime);
                }
            }
        }
    }

    class SpinYaw : IParticleOperator
    {
        private float spinRate;
        private float spinRateMin; // don't actually know if this is used or not. I don't think it is?
        private float spinStopTime;
        public SpinYaw(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nSpinRateDegrees"))
            {
                spinRate = keyValues.GetFloatProperty("m_nSpinRateDegrees");
            }

            if (keyValues.ContainsKey("m_nSpinRateMinDegrees"))
            {
                spinRateMin = keyValues.GetFloatProperty("m_nSpinRateMinDegrees");
            }

            if (keyValues.ContainsKey("m_fSpinRateStopTime"))
            {
                spinStopTime = keyValues.GetFloatProperty("m_fSpinRateStopTime");
            }
        }

        // Does not require SpinUpdate
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (int i = 0; i < particles.Length; ++i)
            {
                if (particles[i].Age < spinStopTime)
                {
                    particles[i].SetScalar(ParticleField.Yaw, particles[i].Rotation.X + spinRate * frameTime);
                }
            }
        }
    }
}
