using System;
using System.Numerics;

namespace GUI.Utils
{
    static class MathUtils
    {
        /// <summary>
        /// Remap to 0-1
        /// </summary>
        /// <param name="x"></param>
        /// <param name="inputMin"></param>
        /// <param name="inputMax"></param>
        /// <returns></returns>
        public static float Remap(float x, float inputMin, float inputMax)
        {
            if (inputMin == inputMax) { return inputMax; }

            return (x - inputMin) / (inputMax - inputMin);
        }

        public static float Lerp(float x, float min, float max)
        {
            return min + x * (max - min);
        }
        public static Vector2 Lerp(float x, Vector2 min, Vector2 max)
        {
            return min + new Vector2(x) * (max - min);
        }
        public static Vector3 Lerp(Vector3 vector, Vector3 min, Vector3 max)
        {
            return min + vector * (max - min);
        }
        public static Vector3 Lerp(Vector3 vector, float min, float max)
        {
            return new Vector3(min) + vector * (max - min);
        }
        public static Vector3 Lerp(float x, Vector3 min, Vector3 max)
        {
            return min + x * (max - min);
        }
        public static float RemapRange(float x, float inputMin, float inputMax, float outputMin, float outputMax)
        {
            return Lerp(Remap(x, inputMin, inputMax), outputMin, outputMax);
        }

        public static float RandomBetween(float min, float max)
        {
            return Lerp(Random.Shared.NextSingle(), min, max);
        }
        public static Vector3 RandomBetween(Vector3 min, Vector3 max)
        {
            return Lerp(Random.Shared.NextSingle(), min, max);
        }
        public static Vector3 RandomBetweenPerComponent(Vector3 min, Vector3 max)
        {
            return new Vector3(
                RandomBetween(min.X, max.X),
                RandomBetween(min.Y, max.Y),
                RandomBetween(min.Z, max.Z));
        }
        public static float RandomWithExponentBetween(float exponent, float min, float max)
        {
            return Lerp(MathF.Pow(Random.Shared.NextSingle(), exponent), min, max);
        }

        public static void MinMaxFixUp<T>(ref T min, ref T max) where T : INumber<T>
        {
            if (min > max)
            {
                (min, max) = (max, min);
            }
        }

        public static float Saturate(float x)
        {
            return Math.Clamp(x, 0.0f, 1.0f);
        }

        public static float Fract(float x)
            => x % 1f;

        public static float Wrap(float x, float lowBounds, float highBounds)
        {
            return ((x - lowBounds) % highBounds) + lowBounds;
        }

        public static float ToRadians(float deg)
        {
            return deg * (MathF.PI / 180.0f);
        }
        public static float ToDegrees(float rad)
        {
            return rad * (180.0f / MathF.PI);
        }
    }
}
