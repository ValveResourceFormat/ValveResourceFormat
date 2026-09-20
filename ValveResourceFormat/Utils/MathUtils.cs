using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// Shared scalar, vector and integer math helpers.
    /// </summary>
    public static class MathUtils
    {
        /// <summary>
        /// Remaps a value from input range to 0-1.
        /// </summary>
        /// <param name="x">Value to remap.</param>
        /// <param name="inputMin">Input range minimum.</param>
        /// <param name="inputMax">Input range maximum.</param>
        /// <returns>Value remapped to 0-1 range.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Remap(float x, float inputMin, float inputMax)
        {
            if (inputMin == inputMax) { return inputMax; }

            return (x - inputMin) / (inputMax - inputMin);
        }

        /// <summary>
        /// GLSL's smoothstep: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, and a
        /// Hermite ease between them. Edges may be given in either order, so a descending pair produces a
        /// falling curve.
        /// </summary>
        /// <param name="edge0">Value at which the result reaches 0.</param>
        /// <param name="edge1">Value at which the result reaches 1.</param>
        /// <param name="x">Value to interpolate.</param>
        /// <returns>The eased value in the 0-1 range.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            if (edge0 == edge1) { return x < edge0 ? 0f : 1f; }

            var t = Saturate((x - edge0) / (edge1 - edge0));
            return t * t * (3f - (2f * t));
        }

        /// <summary>
        /// Remaps a value from one range to another.
        /// </summary>
        /// <param name="x">Value to remap.</param>
        /// <param name="inputMin">Input range minimum.</param>
        /// <param name="inputMax">Input range maximum.</param>
        /// <param name="outputMin">Output range minimum.</param>
        /// <param name="outputMax">Output range maximum.</param>
        /// <returns>Value remapped to output range.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RemapRange(float x, float inputMin, float inputMax, float outputMin, float outputMax)
        {
            return float.Lerp(outputMin, outputMax, Remap(x, inputMin, inputMax));
        }

        /// <summary>
        /// Remaps a value from one range to another, holding the output at the range ends
        /// for inputs outside the input range. Mirrors Source's RemapValClamped.
        /// </summary>
        /// <param name="x">Value to remap.</param>
        /// <param name="inputMin">Input range minimum.</param>
        /// <param name="inputMax">Input range maximum.</param>
        /// <param name="outputMin">Output range minimum.</param>
        /// <param name="outputMax">Output range maximum.</param>
        /// <returns>Value remapped to the output range, clamped to it.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RemapValClamped(float x, float inputMin, float inputMax, float outputMin, float outputMax)
        {
            // Source treats a degenerate input range as a threshold
            if (inputMin == inputMax)
            {
                return x >= inputMax ? outputMax : outputMin;
            }

            return float.Lerp(outputMin, outputMax, Saturate(Remap(x, inputMin, inputMax)));
        }

        /// <summary>
        /// Scales <paramref name="value"/> to unit length, returning <see cref="Vector3.Zero"/> for a
        /// vector with no length rather than the NaN a plain divide would give.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 SafeNormalize(Vector3 value)
        {
            var lengthSquared = value.LengthSquared();

            return lengthSquared == 0f ? Vector3.Zero : value / MathF.Sqrt(lengthSquared);
        }

        /// <summary>
        /// Scales <paramref name="value"/> to unit length, returning <paramref name="fallback"/> when its
        /// squared length does not exceed <paramref name="minimumLengthSquared"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 SafeNormalize(Vector3 value, Vector3 fallback, float minimumLengthSquared = 0f)
        {
            var lengthSquared = value.LengthSquared();

            return lengthSquared > minimumLengthSquared ? value / MathF.Sqrt(lengthSquared) : fallback;
        }

        /// <summary>
        /// Removes from <paramref name="value"/> its component along <paramref name="unitNormal"/>, leaving
        /// the part that lies in the plane the normal describes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ProjectOntoPlane(Vector3 value, Vector3 unitNormal)
        {
            return value - (unitNormal * Vector3.Dot(value, unitNormal));
        }

        /// <summary>
        /// The cross product of a triangle's two edges leaving <paramref name="a"/>: its face normal, with
        /// a length of twice the triangle's area.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 TriangleCross(Vector3 a, Vector3 b, Vector3 c)
        {
            return Vector3.Cross(b - a, c - a);
        }

        /// <summary>
        /// The largest of a vector's three components.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MaxComponent(this Vector3 v)
        {
            return MathF.Max(v.X, MathF.Max(v.Y, v.Z));
        }

        /// <summary>
        /// The smallest of a vector's three components.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MinComponent(this Vector3 v)
        {
            return MathF.Min(v.X, MathF.Min(v.Y, v.Z));
        }

        /// <summary>
        /// Arc cosine that clamps its input to [-1, 1] first, so a cosine that drifted just outside the
        /// range through rounding gives an angle rather than NaN.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SafeAcos(float cosine)
        {
            return MathF.Acos(Math.Clamp(cosine, -1f, 1f));
        }

        /// <summary>
        /// The angle in radians between two unit vectors.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AngleBetween(Vector3 a, Vector3 b)
        {
            return SafeAcos(Vector3.Dot(a, b));
        }

        /// <summary>
        /// Moves <paramref name="value"/> toward <paramref name="target"/> by at most <paramref name="step"/>,
        /// without overshooting.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Approach(float value, float target, float step)
        {
            var delta = target - value;

            return delta > step ? value + step
                : delta < -step ? value - step
                : target;
        }

        /// <summary>
        /// The lerp factor that closes a fixed fraction of the distance to a target every
        /// <paramref name="timeConstant"/> seconds, no matter how <paramref name="deltaTime"/> is chopped up.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ExponentialSmoothing(float deltaTime, float timeConstant)
        {
            return 1f - MathF.Exp(-deltaTime / timeConstant);
        }

        /// <summary>
        /// Divides two positive integers, rounding the result up.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DivideRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }

        /// <summary>
        /// Rounds <paramref name="value"/> up to a multiple of <paramref name="alignment"/>, which must be
        /// a power of two.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AlignUp(int value, int alignment)
        {
            Debug.Assert(BitOperations.IsPow2(alignment));

            return (value + alignment - 1) & ~(alignment - 1);
        }

        /// <summary>
        /// The size of a mip level: <paramref name="size"/> halved <paramref name="level"/> times, never
        /// less than 1.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MipLevelSize(int size, int level)
        {
            return Math.Max(1, size >> level);
        }

        /// <summary>
        /// Clamps a value to [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Saturate(float x)
        {
            return Math.Clamp(x, 0.0f, 1.0f);
        }

        /// <summary>
        /// Clamps a value between two bounds given in either order, unlike <see cref="Math.Clamp(float, float, float)"/>
        /// which throws when <paramref name="min"/> is greater than <paramref name="max"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Clamp<T>(T value, T min, T max) where T : INumber<T>
        {
            if (min > max)
            {
                return T.Min(T.Max(value, max), min);
            }

            return T.Min(T.Max(value, min), max);
        }

        /// <summary>
        /// Converts a decibel value to a linear amplitude multiplier (0 dB is 1.0, -6 dB is roughly half).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DecibelsToLinear(float decibels)
        {
            return MathF.Pow(10f, decibels / 20f);
        }

        /// <summary>
        /// Maps a linear 0-1 value onto an exponential curve approximating perceptual loudness, so the
        /// middle of a volume control (or a linear distance falloff) does not sound louder than it should.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToPerceptualVolume(float linear)
        {
            return (float)((Math.Exp(linear) - 1) / (Math.E - 1));
        }

        /// <summary>
        /// Returns the fractional part of a value (x - floor(x)).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Fract(float x) => x - MathF.Floor(x);

        /// <summary>
        /// Wraps a value into the half-open range [<paramref name="lowBounds"/>, <paramref name="highBounds"/>).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Wrap(float x, float lowBounds, float highBounds)
        {
            var range = highBounds - lowBounds;
            return x - range * MathF.Floor((x - lowBounds) / range);
        }

        /// <summary>
        /// Linearly interpolates between two angles in radians, taking the shortest path around the circle.
        /// </summary>
        /// <param name="from">Start angle in radians.</param>
        /// <param name="to">End angle in radians.</param>
        /// <param name="amount">Interpolation weight (0.0 = from, 1.0 = to).</param>
        /// <returns>Interpolated angle in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LerpAngle(float from, float to, float amount)
        {
            var diff = (to - from) % MathF.Tau;
            var shortestPath = 2.0f * diff % MathF.Tau - diff;

            return from + shortestPath * amount;
        }

        /// <summary>
        /// Evaluates a cubic Bezier curve at <paramref name="t"/> in [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var u = 1.0f - t;
            var uu = u * u;
            var tt = t * t;
            return (uu * u * p0) + (3.0f * uu * t * p1) + (3.0f * u * tt * p2) + (tt * t * p3);
        }

        /// <summary>Number of bits held by one word of a <see cref="uint"/> bitfield.</summary>
        public const int BitsPerWord = 32;

        /// <summary>
        /// Gets whether bit <paramref name="index"/> of a packed bitfield is set.
        /// </summary>
        /// <param name="bits">The bitfield, 32 bits per word.</param>
        /// <param name="index">Index of the bit, counted across the whole field.</param>
        public static bool GetBit(ReadOnlySpan<uint> bits, int index)
            => (bits[index / BitsPerWord] & (1u << (index % BitsPerWord))) != 0;

        /// <summary>
        /// Sets bit <paramref name="index"/> of a packed bitfield.
        /// </summary>
        /// <param name="bits">The bitfield, 32 bits per word.</param>
        /// <param name="index">Index of the bit, counted across the whole field.</param>
        public static void SetBit(Span<uint> bits, int index)
            => bits[index / BitsPerWord] |= 1u << (index % BitsPerWord);

        /// <summary>
        /// Gets whether bit <paramref name="index"/> of a packed bitfield is set.
        /// </summary>
        /// <param name="bits">The bitfield, 8 bits per byte.</param>
        /// <param name="index">Index of the bit, counted across the whole field.</param>
        public static bool GetBit(ReadOnlySpan<byte> bits, int index)
            => (bits[index / 8] & (1 << (index % 8))) != 0;

        /// <summary>
        /// The length of one of a transform's basis vectors: how much it scales along that axis.
        /// <paramref name="axis"/> is 0 for X, 1 for Y, 2 for Z.
        /// </summary>
        public static float AxisScale(this Matrix4x4 m, int axis) => axis switch
        {
            0 => m.GetRow(0).AsVector3().Length(),
            1 => m.GetRow(1).AsVector3().Length(),
            _ => m.GetRow(2).AsVector3().Length(),
        };

        /// <summary>
        /// The largest per-axis scale baked into a transform.
        /// </summary>
        public static float MaxAxisScale(this Matrix4x4 m)
            => MathF.Max(m.AxisScale(0), MathF.Max(m.AxisScale(1), m.AxisScale(2)));
    }
}
