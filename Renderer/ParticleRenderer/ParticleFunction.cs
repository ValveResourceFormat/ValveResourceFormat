using System.Runtime.CompilerServices;
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

            var strength = FadeInOut(OpStartFadeInTime, OpEndFadeInTime, OpStartFadeOutTime, OpEndFadeOutTime, time);

            return strength;
        }

        private static float FadeInOut(float fadeInStart, float fadeInEnd, float fadeOutStart, float fadeOutEnd, float time)
        {
            if (fadeInStart > time) // started yet?
            {
                return 0f;
            }

            if (fadeOutEnd > 0f && fadeOutEnd < time) // timed out?
            {
                return 0f;
            }

            // handle out of order cases
            fadeInEnd = MathF.Max(fadeInEnd, fadeInStart);
            fadeOutStart = MathF.Max(fadeOutStart, fadeInEnd);
            fadeOutEnd = MathF.Max(fadeOutEnd, fadeOutStart);

            var strength = 1f;

            if (fadeInEnd > time && fadeInEnd > fadeInStart)
            {
                strength = MathF.Min(strength, FLerp(0, 1, fadeInStart, fadeInEnd, time));
            }

            if (time > fadeOutStart && fadeOutEnd > fadeOutStart)
            {
                strength = MathF.Min(strength, FLerp(0f, 1f, fadeOutEnd, fadeOutStart, time));
            }

            return strength;
        }

        // 5-argument floating point linear interpolation.
        // FLerp(f1,f2,i1,i2,x)=
        //    f1 at x=i1
        //    f2 at x=i2
        //   smooth lerp between f1 and f2 at x>i1 and x<i2
        //   extrapolation for x<i1 or x>i2
        //
        //   If you know a function f(x)'s value (f1) at position i1, and its value (f2) at position i2,
        //   the function can be linearly interpolated with FLerp(f1,f2,i1,i2,x)
        //    i2=i1 will cause a divide by zero.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FLerp(float f1, float f2, float i1, float i2, float x)
        {
            return f1 + (f2 - f1) * (x - i1) / (i2 - i1);
        }
    }
}
