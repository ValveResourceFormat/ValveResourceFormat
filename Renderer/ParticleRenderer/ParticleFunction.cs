using Microsoft.Extensions.Logging;

namespace ValveResourceFormat.Renderer.Particles
{
    abstract class ParticleFunction
    {
        //INumberProvider OpStrength; // operator strength
        //ParticleEndCapMode OpEndCapState; // operator end cap state
        public readonly float OpStartFadeInTime; // operator start fadein
        public readonly float OpEndFadeInTime; // operator end fadein
        public readonly float OpStartFadeOutTime; // operator start fadeout
        public readonly float OpEndFadeOutTime; // operator end fadeout
        public readonly float OpFadeOscillatePeriod; // operator fade oscillate
        //bool NormalizeToStopTime; // normalize fade times to endcap
        //float OpTimeOffsetMin; // operator fade time offset min
        //float OpTimeOffsetMax; // operator fade time offset max
        //int OpTimeOffsetSeed; // operator fade time offset seed
        //int OpTimeScaleSeed; // operator fade time scale seed
        //float OpTimeScaleMin; // operator fade time scale min
        //float OpTimeScaleMax; // operator fade time scale max

        readonly bool StrengthFastPath;
        protected readonly ILogger Logger;

        public ParticleFunction(ParticleDefinitionParser parse)
        {
            Logger = parse.Logger;
            OpStartFadeInTime = parse.Float("m_flOpStartFadeInTime");
            OpEndFadeInTime = parse.Float("m_flOpEndFadeInTime");
            OpStartFadeOutTime = parse.Float("m_flOpStartFadeOutTime");
            OpEndFadeOutTime = parse.Float("m_flOpEndFadeOutTime");
            OpFadeOscillatePeriod = parse.Float("m_flOpFadeOscillatePeriod");

            StrengthFastPath =
                OpStartFadeInTime == 0f &&
                OpEndFadeInTime == 0f &&
                OpStartFadeOutTime == 0f &&
                OpEndFadeOutTime == 0f;
            //OpTimeOffsetMin == 0f &&
            //OpTimeOffsetMax == 0f &&
            //OpTimeScaleMin == 1f &&
            //OpTimeScaleMax == 1f &&
            //OpStrengthMaxScale == 1f &&
            //OpStrengthMinScale == 1f &&
            //OpEndCapState == ParticleEndCapMode.PARTICLE_ENDCAP_ALWAYS_ON);
        }

        public float GetOperatorRunStrength(ParticleSystemRenderState systemState) // CheckIfOperatorShouldRun
        {
            if (StrengthFastPath)
            {
                return 1f;
            }

            /* TODO
            if (OpEndCapState != -1)
            {
                if (systemState.InEndCap != (OpEndCapState == 1))
                    return false;
            }
            */

            var time = systemState.Age;

            /* TODO
            if (OpTimeOffsetSeed) // allow per-instance-of-particle-system random phase control for operator strength.
            {
                float flOffset = RandomFloat(OpTimeOffsetSeed, OpTimeOffsetMin, OpTimeOffsetMax);
                time += flOffset;
                time = MathF.Max(0f, time);
            }

            if (OpTimeScaleSeed && time > OpStartFadeInTime)
            {
                float timeScalar = 1.0 / MathF.Max(0.0001f, RandomFloat(OpTimeScaleSeed, OpTimeScaleMin, OpTimeScaleMax));
                time = OpStartFadeInTime + timeScalar * (time - OpStartFadeInTime);
            }
            */

            if (OpFadeOscillatePeriod > 0.0)
            {
                time *= 1f / OpFadeOscillatePeriod;
                time %= 1f;
            }

            var strength = MathUtils.FadeInOut(OpStartFadeInTime, OpEndFadeInTime, OpStartFadeOutTime, OpEndFadeOutTime, time);

            return strength;
        }
    }
}
