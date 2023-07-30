using System;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class Spin : IParticleOperator
    {
        private readonly float spinRate;
        private readonly float spinRateMin; // don't actually know if this is used or not. I don't think it is?
        private readonly float spinStopTime;
        public Spin(ParticleDefinitionParser parse)
        {
            spinRate = parse.Float("m_nSpinRateDegrees", spinRate);
            spinRateMin = parse.Float("m_nSpinRateMinDegrees", spinRateMin);
            spinStopTime = parse.Float("m_fSpinRateStopTime", spinStopTime);
        }

        // Does not require SpinUpdate 
        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Age < spinStopTime)
                {
                    particle.SetScalar(ParticleField.Roll, particle.Rotation.Z + spinRate * frameTime);
                }
            }
        }
    }

    class SpinYaw : IParticleOperator
    {
        private float spinRate;
        private float spinRateMin; // don't actually know if this is used or not. I don't think it is?
        private float spinStopTime;
        public SpinYaw(ParticleDefinitionParser parse)
        {
            spinRate = parse.Float("m_nSpinRateDegrees", spinRate);
            spinRateMin = parse.Float("m_nSpinRateMinDegrees", spinRateMin);
            spinStopTime = parse.Float("m_fSpinRateStopTime", spinStopTime);
        }

        // Does not require SpinUpdate
        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Age < spinStopTime)
                {
                    particle.SetScalar(ParticleField.Yaw, particle.Rotation.X + spinRate * frameTime);
                }
            }
        }
    }
}
