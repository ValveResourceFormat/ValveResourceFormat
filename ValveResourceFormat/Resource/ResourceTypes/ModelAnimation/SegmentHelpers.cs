using System.Globalization;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    [StructLayout(LayoutKind.Sequential, Size = 6)]
    struct Half3(Half x, Half y, Half z)
    {
        public Half X { get; set; } = x;
        public Half Y { get; set; } = y;
        public Half Z { get; set; } = z;

        public static implicit operator Half3(Vector3 v) => new((Half)v.X, (Half)v.Y, (Half)v.Z);
        public static implicit operator Vector3(Half3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the vector components formatted as "&lt;X Y Z&gt;" with 3 decimal places.
        /// </remarks>
        public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"<{X:0.000} {Y:0.000} {Z:0.000}>");
    }

    internal class SegmentHelpers
    {
        public const int CompressedQuaternionSize = 6;

        private const int ValueMask = 0x3FFF;
        private const int OffsetFlag = 0x4000;
        private const int SignFlag = 0x8000;

        // Equals MathF.Sin(MathF.PI / 4.0f) / 16384.0f
        private const float Scale = 0.70710677f / 16384.0f;

        /// <summary>
        /// Read and decode encoded quaternion.
        /// </summary>
        /// <param name="bytes">The byte stream.</param>
        /// <returns>The decoded rotation.</returns>
        public static Quaternion ReadQuaternion(ReadOnlySpan<byte> bytes)
        {
            // Each component is a little endian word: 14 bits of value, a flag in bit 14 and a sign in bit 15
            var words = MemoryMarshal.Cast<byte, ushort>(bytes);
            var word1 = words[0];
            var word2 = words[1];
            var word3 = words[2];

            var i1 = word1 & ValueMask;
            var i2 = word2 & ValueMask;
            var i3 = word3 & ValueMask;

            var x = (word1 & OffsetFlag) == 0 ? Scale * (i1 - 16384) : Scale * i1;
            var y = (word2 & OffsetFlag) == 0 ? Scale * (i2 - 16384) : Scale * i2;
            var z = (word3 & OffsetFlag) == 0 ? Scale * (i3 - 16384) : Scale * i3;

            // The omitted component is the largest, so valid data never goes negative here; truncated data can
            var w = MathF.Sqrt(MathF.Max(0f, 1 - (x * x) - (y * y) - (z * z)));

            // Apply sign 3
            if ((word3 & SignFlag) != 0)
            {
                w *= -1;
            }

            // Apply sign 1 and 2
            if ((word1 & SignFlag) != 0)
            {
                return (word2 & SignFlag) != 0 ? new Quaternion(y, z, w, x) : new Quaternion(z, w, x, y);
            }

            return (word2 & SignFlag) != 0 ? new Quaternion(w, x, y, z) : new Quaternion(x, y, z, w);
        }
    }
}
