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

        public PositionLock(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nControlPointNumber", cp);

            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);

            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);

            startTimeExp = parse.Float("m_flStartTime_exp", startTimeExp);

            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);

            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);

            endTimeExp = parse.Float("m_flEndTime_exp", endTimeExp);

            fadeDist = parse.Float("m_flRange", fadeDist);

            instantJumpThreshold = parse.Float("m_flJumpThreshold", instantJumpThreshold);

            prevPosScale = parse.Float("m_flPrevPosScale", prevPosScale);
        }


        private Vector3 prevFramePos = new(float.MaxValue);

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

                var delta = cpPos - prevFramePos * prevPosScale;
                var newPos = MathUtils.Lerp(weight, cpPos, cpPos + delta);

                // If it jumps more than instantJumpThreshold, ignore time fade
                if (Vector3.Distance(cpPos, prevFramePos) > instantJumpThreshold)
                {
                    particle.Position = newPos;
                }

                // TODO: Consistent rng
                var startTime = MathUtils.RandomWithExponentBetween(startTimeExp, startTimeMin, startTimeMax);
                var endTime = MathUtils.RandomWithExponentBetween(endTimeExp, endTimeMin, endTimeMax);

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
