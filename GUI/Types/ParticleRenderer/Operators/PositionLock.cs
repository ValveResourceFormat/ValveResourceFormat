using System;
using System.Numerics;
using System.Collections.Generic;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    /// <summary>
    /// This module is mostly used to stick effects onto moving components.
    /// </summary>
    class PositionLock : IParticleOperator
    {
        private readonly float startTimeMin = 1;
        private readonly float startTimeMax = 1;
        private readonly float startTimeExp = 1;
        private readonly float endTimeMin = 1;
        private readonly float endTimeMax = 1;
        private readonly float endTimeExp = 1;

        private readonly float fadeDist;
        private readonly float instantJumpThreshold = 512;
        private readonly float prevPosScale = 1;
        private readonly int cp;

        public PositionLock(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nControlPointNumber"))
            {
                cp = keyValues.GetInt32Property("m_nControlPointNumber");
            }

            if (keyValues.ContainsKey("m_flStartTime_min"))
            {
                startTimeMin = keyValues.GetFloatProperty("m_flStartTime_min");
            }

            if (keyValues.ContainsKey("m_flStartTime_max"))
            {
                startTimeMax = keyValues.GetFloatProperty("m_flStartTime_max");
            }

            if (keyValues.ContainsKey("m_flStartTime_exp"))
            {
                startTimeExp = keyValues.GetFloatProperty("m_flStartTime_exp");
            }

            if (keyValues.ContainsKey("m_flEndTime_min"))
            {
                endTimeMin = keyValues.GetFloatProperty("m_flEndTime_min");
            }

            if (keyValues.ContainsKey("m_flEndTime_max"))
            {
                endTimeMax = keyValues.GetFloatProperty("m_flEndTime_max");
            }

            if (keyValues.ContainsKey("m_flEndTime_exp"))
            {
                endTimeExp = keyValues.GetFloatProperty("m_flEndTime_exp");
            }

            if (keyValues.ContainsKey("m_flRange"))
            {
                fadeDist = keyValues.GetFloatProperty("m_flRange");
            }

            if (keyValues.ContainsKey("m_flJumpThreshold"))
            {
                instantJumpThreshold = keyValues.GetFloatProperty("m_flJumpThreshold");
            }

            if (keyValues.ContainsKey("m_flPrevPosScale"))
            {
                prevPosScale = keyValues.GetInt32Property("m_flPrevPosScale");
            }
        }

        private readonly Dictionary<int, float> startTimes = new();
        private readonly Dictionary<int, float> endTimes = new();

        private Vector3 prevFramePos = new Vector3(float.MaxValue);

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var cpPos = particleSystemState.GetControlPoint(cp).Position;

            if (prevFramePos.X != float.MaxValue)
            {
                prevFramePos = cpPos;
            }

            foreach (ref var particle in particles)
            {
                var weight = fadeDist == 0.0f
                    ? 1
                    : 1 - MathUtils.Saturate(Vector3.Distance(cpPos, particle.Position) / fadeDist);

                // Generate new random if one doesn't exist yet
                if (!startTimes.ContainsKey(particle.ParticleCount))
                {
                    startTimes[particle.ParticleCount] = MathUtils.RandomWithExponentBetween(startTimeExp, startTimeMin, startTimeMax);
                    endTimes[particle.ParticleCount] = MathUtils.RandomWithExponentBetween(endTimeExp, endTimeMin, endTimeMax);
                }

                var delta = cpPos - prevFramePos * prevPosScale;
                var newPos = MathUtils.Lerp(weight, cpPos, cpPos + delta);

                // If it jumps more than instantJumpThreshold, ignore time fade
                if (Vector3.Distance(cpPos, prevFramePos) > instantJumpThreshold)
                {
                    particle.Position = newPos;
                }

                var startTime = startTimes[particle.ParticleCount];
                var endTime = endTimes[particle.ParticleCount];

                if (particle.Age < startTime || particle.Age > endTime)
                {
                    continue;
                }

                particle.Position = newPos;
            }
            prevFramePos = cpPos;
        }
    }
}
