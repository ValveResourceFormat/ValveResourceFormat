using System.Numerics.Tensors;

namespace ValveResourceFormat.Utils;

/// <summary>
/// Vectorized conversions that <see cref="TensorPrimitives"/> runs one element at a time.
/// </summary>
internal static class TensorPrimitivesExtensions
{
    extension(TensorPrimitives)
    {
        /// <summary>Converts each element exactly, as a <c>(float)</c> cast does.</summary>
        public static void ConvertToSingle(ReadOnlySpan<short> source, Span<float> destination)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

            var i = 0;

            for (; i <= source.Length - Vector<short>.Count; i += Vector<short>.Count)
            {
                Vector.Widen(new Vector<short>(source[i..]), out var low, out var high);
                Vector.ConvertToSingle(low).CopyTo(destination[i..]);
                Vector.ConvertToSingle(high).CopyTo(destination[(i + Vector<int>.Count)..]);
            }

            for (; i < source.Length; i++)
            {
                destination[i] = source[i];
            }
        }

        /// <inheritdoc cref="ConvertToSingle(ReadOnlySpan{short}, Span{float})"/>
        public static void ConvertToSingle(ReadOnlySpan<ushort> source, Span<float> destination)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

            var i = 0;

            for (; i <= source.Length - Vector<ushort>.Count; i += Vector<ushort>.Count)
            {
                // Widened values fit in int, which converts natively unlike uint
                Vector.Widen(new Vector<ushort>(source[i..]), out var low, out var high);
                Vector.ConvertToSingle(Vector.AsVectorInt32(low)).CopyTo(destination[i..]);
                Vector.ConvertToSingle(Vector.AsVectorInt32(high)).CopyTo(destination[(i + Vector<int>.Count)..]);
            }

            for (; i < source.Length; i++)
            {
                destination[i] = source[i];
            }
        }

        /// <summary>Sign extends each element.</summary>
        public static void ConvertToInt32(ReadOnlySpan<sbyte> source, Span<int> destination)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

            var i = 0;

            for (; i <= source.Length - Vector<sbyte>.Count; i += Vector<sbyte>.Count)
            {
                Vector.Widen(new Vector<sbyte>(source[i..]), out var low, out var high);
                Vector.Widen(low, out var first, out var second);
                Vector.Widen(high, out var third, out var fourth);
                first.CopyTo(destination[i..]);
                second.CopyTo(destination[(i + Vector<int>.Count)..]);
                third.CopyTo(destination[(i + (2 * Vector<int>.Count))..]);
                fourth.CopyTo(destination[(i + (3 * Vector<int>.Count))..]);
            }

            for (; i < source.Length; i++)
            {
                destination[i] = source[i];
            }
        }

        /// <summary>Keeps the low 16 bits of each element, as a <c>(ushort)</c> cast does.</summary>
        public static void ConvertToUInt16(ReadOnlySpan<int> source, Span<ushort> destination)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

            var i = 0;

            for (; i <= source.Length - Vector<ushort>.Count; i += Vector<ushort>.Count)
            {
                var low = Vector.AsVectorUInt32(new Vector<int>(source[i..]));
                var high = Vector.AsVectorUInt32(new Vector<int>(source[(i + Vector<int>.Count)..]));
                Vector.Narrow(low, high).CopyTo(destination[i..]);
            }

            for (; i < source.Length; i++)
            {
                destination[i] = (ushort)source[i];
            }
        }

        /// <summary>
        /// Saturates each element to [0, 1] and scales it to a rounded byte, as
        /// <c>(byte)(MathUtils.Saturate(x) * 255 + 0.5f)</c> does. NaN becomes 0.
        /// </summary>
        public static void ConvertToClampedUnorm8(ReadOnlySpan<float> source, Span<byte> destination)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

            var i = 0;

            for (; i <= source.Length - Vector<byte>.Count; i += Vector<byte>.Count)
            {
                var first = ToUnorm8Lanes(new Vector<float>(source[i..]));
                var second = ToUnorm8Lanes(new Vector<float>(source[(i + Vector<float>.Count)..]));
                var third = ToUnorm8Lanes(new Vector<float>(source[(i + (2 * Vector<float>.Count))..]));
                var fourth = ToUnorm8Lanes(new Vector<float>(source[(i + (3 * Vector<float>.Count))..]));
                Vector.Narrow(Vector.Narrow(first, second), Vector.Narrow(third, fourth)).CopyTo(destination[i..]);
            }

            for (; i < source.Length; i++)
            {
                destination[i] = (byte)((MathUtils.Saturate(source[i]) * 255) + 0.5f);
            }
        }
    }

    private static Vector<uint> ToUnorm8Lanes(Vector<float> value)
    {
        // Vector conversion of NaN is not defined to give 0 like the scalar cast
        value = Vector.ConditionalSelect(Vector.Equals(value, value), value, Vector<float>.Zero);
        value = Vector.Min(Vector.Max(value, Vector<float>.Zero), Vector<float>.One);

        return Vector.AsVectorUInt32(Vector.ConvertToInt32((value * 255f) + new Vector<float>(0.5f)));
    }
}
