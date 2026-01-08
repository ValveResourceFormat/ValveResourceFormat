using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Renderer
{
    public static class MathUtils
    {
        /// <summary>
        /// Remap to 0-1
        /// </summary>
        /// <param name="x"></param>
        /// <param name="inputMin"></param>
        /// <param name="inputMax"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Remap(float x, float inputMin, float inputMax)
        {
            if (inputMin == inputMax) { return inputMax; }

            return (x - inputMin) / (inputMax - inputMin);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RemapRange(float x, float inputMin, float inputMax, float outputMin, float outputMax)
        {
            return float.Lerp(outputMin, outputMax, Remap(x, inputMin, inputMax));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MinMaxFixUp<T>(ref T min, ref T max) where T : INumber<T>
        {
            if (min > max)
            {
                (min, max) = (max, min);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Saturate(float x)
        {
            return Math.Clamp(x, 0.0f, 1.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Fract(float x) => x % 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Wrap(float x, float lowBounds, float highBounds)
        {
            return ((x - lowBounds) % highBounds) + lowBounds;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToRadians(float deg)
        {
            return deg * (MathF.PI / 180.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToDegrees(float rad)
        {
            return rad * (180.0f / MathF.PI);
        }

        public static float FadeInOut(float fadeInStart, float fadeInEnd, float fadeOutStart, float fadeOutEnd, float time)
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
    }
}
