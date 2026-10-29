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
        /// for inputs outside the input range.
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
            // A degenerate input range is treated as a threshold
            if (inputMin == inputMax)
            {
                return x >= inputMax ? outputMax : outputMin;
            }

            return float.Lerp(outputMin, outputMax, Saturate(Remap(x, inputMin, inputMax)));
        }

        /// <summary>
        /// Interpolates as <c>from + (to - from) * amount</c>. Unlike <see cref="float.Lerp(float, float, float)"/>,
        /// which weights the two ends, it gives <paramref name="from"/> exactly when both ends are equal, so a
        /// collapsed range keeps its value through a later truncation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float from, float to, float amount) => ((to - from) * amount) + from;

        /// <inheritdoc cref="Lerp(float, float, float)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Lerp(Vector3 from, Vector3 to, float amount) => ((to - from) * amount) + from;

        /// <inheritdoc cref="Lerp(float, float, float)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Lerp(Vector3 from, Vector3 to, Vector3 amount) => ((to - from) * amount) + from;

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
        /// A unit vector perpendicular to the unit vector <paramref name="direction"/>, picked by a fixed rule,
        /// so the same direction always gives the same perpendicular: +X for +Z and +Y, -Z for +X.
        /// </summary>
        public static Vector3 Perpendicular(Vector3 direction)
        {
            var guess = new Vector3(((1f - direction.Z) * direction.Y * direction.Y) + direction.Z, 0f, -direction.X);

            return SafeNormalize(ProjectOntoPlane(SafeNormalize(guess), direction));
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
        /// The rotation matrix with <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/> as its
        /// rows, which carries +X, +Y and +Z onto those axes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateBasis(Vector3 x, Vector3 y, Vector3 z) => new(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            0f, 0f, 0f, 1f);

        /// <summary>
        /// The largest of the given values, compared with <see cref="MathF.Max(float, float)"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float a, float b, float c)
        {
            return MathF.Max(MathF.Max(a, b), c);
        }

        /// <inheritdoc cref="Max(float, float, float)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float a, float b, float c, float d)
        {
            return MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
        }

        /// <summary>
        /// The smallest of the given values, compared with <see cref="MathF.Min(float, float)"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float a, float b, float c)
        {
            return MathF.Min(MathF.Min(a, b), c);
        }

        /// <inheritdoc cref="Min(float, float, float)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float a, float b, float c, float d)
        {
            return MathF.Min(MathF.Min(a, b), MathF.Min(c, d));
        }

        /// <summary>
        /// The largest of a vector's three components.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MaxComponent(this Vector3 v)
        {
            return Max(v.X, v.Y, v.Z);
        }

        /// <summary>
        /// The smallest of a vector's three components.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MinComponent(this Vector3 v)
        {
            return Min(v.X, v.Y, v.Z);
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
        /// The angle in radians between two rotations, the shorter way round.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AngleBetween(Quaternion a, Quaternion b)
        {
            // An arc cosine of the dot product cannot resolve angles below about 0.03 degrees in single
            // precision, as their cosine rounds to one; the sine of the difference rotation can.
            var difference = Quaternion.Conjugate(a) * b;
            var sine = new Vector3(difference.X, difference.Y, difference.Z).Length();
            return 2f * MathF.Atan2(sine, MathF.Abs(difference.W));
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

        /// <summary>The BT.709-1 weights of the red, green and blue channels in a color's luminance, as the game shaders use them.</summary>
        public static readonly Vector3 LuminanceWeights = new(0.2125f, 0.7154f, 0.0721f);

        /// <summary>The Rec. 709 weights of the red, green and blue channels in a color's luminance, as the engine uses them for cubemap normalization.</summary>
        public static readonly Vector3 Rec709LuminanceWeights = new(0.2126f, 0.7152f, 0.0722f);

        /// <summary>
        /// BT.709-1 luminance of a linear RGB color.
        /// </summary>
        public static float Luminance(Vector3 color) => Vector3.Dot(color, LuminanceWeights);

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
            => Max(m.AxisScale(0), m.AxisScale(1), m.AxisScale(2));

        /// <summary>
        /// Wraps <paramref name="index"/> into [0, <paramref name="count"/>), so a negative index counts
        /// back from the end.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WrapIndex(int index, int count)
        {
            return ((index % count) + count) % count;
        }

        /// <summary>
        /// Scales <paramref name="value"/> down to a non-negative <paramref name="maxLength"/> when it is
        /// longer, and returns it unchanged otherwise.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ClampLength(Vector3 value, float maxLength)
        {
            var lengthSquared = value.LengthSquared();

            return lengthSquared > maxLength * maxLength ? value * (maxLength / MathF.Sqrt(lengthSquared)) : value;
        }

        /// <summary>
        /// The area of the triangle with corners <paramref name="a"/>, <paramref name="b"/> and <paramref name="c"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float TriangleArea(Vector3 a, Vector3 b, Vector3 c)
        {
            return TriangleCross(a, b, c).Length() * 0.5f;
        }

        /// <summary>
        /// Diagonalizes a symmetric 3x3 matrix with Jacobi rotations accumulated in a quaternion. Each
        /// iteration rotates away the largest off-diagonal entry, for at most 24 iterations.
        /// </summary>
        /// <param name="matrix">The symmetric matrix in its upper-left 3x3 block.</param>
        /// <param name="eigenvalues">The diagonal of the rotated matrix, unsorted.</param>
        /// <returns>
        /// The rotation whose matrix (<see cref="Matrix4x4.CreateFromQuaternion"/>) has the eigenvectors as its
        /// rows, in the order of <paramref name="eigenvalues"/>.
        /// </returns>
        public static Quaternion DiagonalizeSymmetric(Matrix4x4 matrix, out Vector3 eigenvalues)
        {
            const int MaxIterations = 24;

            var rotation = Quaternion.Identity;
            eigenvalues = new Vector3(matrix.M11, matrix.M22, matrix.M33);

            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                var frame = Matrix4x4.CreateFromQuaternion(rotation);
                var diagonal = frame * matrix * Matrix4x4.Transpose(frame);

                eigenvalues = new Vector3(diagonal.M11, diagonal.M22, diagonal.M33);

                // Entry k is the one coupling the two axes other than k
                var offDiagonal = new Vector3(diagonal.M23, diagonal.M13, diagonal.M12);
                var magnitude = Vector3.Abs(offDiagonal);

                int pivot;

                if (magnitude.X > magnitude.Y && magnitude.X > magnitude.Z)
                {
                    pivot = 0;
                }
                else if (magnitude.Y > magnitude.Z)
                {
                    pivot = 1;
                }
                else
                {
                    pivot = 2;
                }

                var pivotValue = offDiagonal[pivot];

                if (pivotValue == 0f)
                {
                    break;
                }

                var theta = (eigenvalues[(pivot + 2) % 3] - eigenvalues[(pivot + 1) % 3]) / (2f * pivotValue);
                var sign = theta > 0f ? 1f : -1f;
                theta *= sign;

                // Past 1e6 the square root is theta to float precision, and squaring theta could overflow
                var root = theta < 1e6f ? MathF.Sqrt((theta * theta) + 1f) : theta;
                var tangent = sign / (theta + root);
                var cosine = 1f / MathF.Sqrt((tangent * tangent) + 1f);

                if (cosine == 1f)
                {
                    break;
                }

                var axis = Vector3.Zero;
                axis[pivot] = -sign * MathF.Sqrt((1f - cosine) * 0.5f);

                var w = MathF.Sqrt(1f - (axis[pivot] * axis[pivot]));

                if (w == 1f)
                {
                    break;
                }

                rotation = Quaternion.Normalize(rotation * new Quaternion(axis, w));
            }

            return rotation;
        }

        /// <summary>
        /// The radical inverse of a non-negative <paramref name="index"/> in base <paramref name="radix"/>: its
        /// digits mirrored about the radix point, giving a value in [0, 1). Successive indices in bases 2, 3 and
        /// 5 give the coordinates of a Halton sequence.
        /// </summary>
        public static float RadicalInverse(int index, int radix)
        {
            var inverseRadix = 1f / radix;
            var digitScale = inverseRadix;
            var result = 0f;

            while (index > 0)
            {
                result += index % radix * digitScale;
                index /= radix;
                digitScale *= inverseRadix;
            }

            return result;
        }
    }
}
