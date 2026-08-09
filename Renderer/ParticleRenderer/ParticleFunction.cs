using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ValveResourceFormat.Renderer.Particles
{
    abstract class ParticleFunction
    {
        private readonly INumberProvider opStrengthInput = new LiteralNumberProvider(1f);
        private readonly ParticleEndCapMode opEndCapState = ParticleEndCapMode.PARTICLE_ENDCAP_ALWAYS_ON;
        public readonly float OpStartFadeInTime; // operator start fadein
        public readonly float OpEndFadeInTime; // operator end fadein
        public readonly float OpStartFadeOutTime; // operator start fadeout
        public readonly float OpEndFadeOutTime; // operator end fadeout
        public readonly float OpFadeOscillatePeriod; // operator fade oscillate
        //bool NormalizeToStopTime; // normalize fade times to endcap
        private readonly float opTimeOffsetMin;
        private readonly float opTimeOffsetMax;
        //int OpTimeOffsetSeed; // operator fade time offset seed
        //int OpTimeScaleSeed; // operator fade time scale seed
        private readonly float opTimeScaleMin = 1f;
        private readonly float opTimeScaleMax = 1f;

        /// <summary>Every field feeding the fade curve is at its default, so the curve is always 1.</summary>
        readonly bool FadeCurveIsUnity;

        /// <summary>The curve is unity and the strength is a literal 1, so the whole evaluation folds away.</summary>
        readonly bool StrengthFastPath;
        protected readonly ILogger Logger;

        public ParticleFunction(ParticleDefinitionParser parse)
        {
            Logger = parse.Logger;
            opStrengthInput = parse.NumberProvider("m_flOpStrength", opStrengthInput);
            opEndCapState = parse.Enum("m_nOpEndCapState", opEndCapState);
            OpStartFadeInTime = parse.Float("m_flOpStartFadeInTime");
            OpEndFadeInTime = parse.Float("m_flOpEndFadeInTime");
            OpStartFadeOutTime = parse.Float("m_flOpStartFadeOutTime");
            OpEndFadeOutTime = parse.Float("m_flOpEndFadeOutTime");
            OpFadeOscillatePeriod = parse.Float("m_flOpFadeOscillatePeriod");
            opTimeOffsetMin = parse.Float("m_flOpTimeOffsetMin", opTimeOffsetMin);
            opTimeOffsetMax = parse.Float("m_flOpTimeOffsetMax", opTimeOffsetMax);
            opTimeScaleMin = parse.Float("m_flOpTimeScaleMin", opTimeScaleMin);
            opTimeScaleMax = parse.Float("m_flOpTimeScaleMax", opTimeScaleMax);

            FadeCurveIsUnity =
                OpStartFadeInTime == 0f &&
                OpEndFadeInTime == 0f &&
                OpStartFadeOutTime == 0f &&
                OpEndFadeOutTime == 0f &&
                opTimeOffsetMin == 0f &&
                opTimeOffsetMax == 0f &&
                opTimeScaleMin == 1f &&
                opTimeScaleMax == 1f;

            StrengthFastPath = FadeCurveIsUnity
                && opEndCapState == ParticleEndCapMode.PARTICLE_ENDCAP_ALWAYS_ON
                && opStrengthInput is LiteralNumberProvider { Value: 1f };
        }

        /// <summary>
        /// Whether the authored <c>m_nOpEndCapState</c> lets the function run in the phase the system is
        /// in. Walks with no operator strength to evaluate, such as the initializers, test this alone.
        /// </summary>
        public bool RunsInCurrentPhase(ParticleSystemRenderState systemState)
            => opEndCapState == ParticleEndCapMode.PARTICLE_ENDCAP_ALWAYS_ON
                || systemState.InEndCap == (opEndCapState == ParticleEndCapMode.PARTICLE_ENDCAP_ENDCAP_ON);

        public float GetOperatorRunStrength(ParticleSystemRenderState systemState) // CheckIfOperatorShouldRun
        {
            if (StrengthFastPath)
            {
                return 1f;
            }

            if (!RunsInCurrentPhase(systemState))
            {
                return 0f;
            }

            var opStrength = opStrengthInput.NextNumber(systemState);

            if (FadeCurveIsUnity || opStrength <= 0f)
            {
                return opStrength;
            }

            var time = systemState.Age;

            /* TODO
            if (OpTimeOffsetSeed) // allow per-instance-of-particle-system random phase control for operator strength.
            {
                float flOffset = RandomFloat(OpTimeOffsetSeed, opTimeOffsetMin, opTimeOffsetMax);
                time += flOffset;
                time = MathF.Max(0f, time);
            }

            if (OpTimeScaleSeed && time > OpStartFadeInTime)
            {
                float timeScalar = 1.0 / MathF.Max(0.0001f, RandomFloat(OpTimeScaleSeed, opTimeScaleMin, opTimeScaleMax));
                time = OpStartFadeInTime + timeScalar * (time - OpStartFadeInTime);
            }
            */

            if (OpFadeOscillatePeriod > 0.0)
            {
                time *= 1f / OpFadeOscillatePeriod;
                time %= 1f;
            }

            var strength = FadeInOut(OpStartFadeInTime, OpEndFadeInTime, OpStartFadeOutTime, OpEndFadeOutTime, time);

            return MathF.Max(0f, opStrength) * strength;
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
