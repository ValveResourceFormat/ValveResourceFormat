using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// A scratch buffer of <typeparamref name="T"/> elements backed by an array rented from the
    /// shared <see cref="float"/> pool, for data that is written as one type but consumed as floats
    /// or by exact-length span.
    /// </summary>
    /// <remarks>
    /// Renting floats for every element type keeps all scratch buffers in the same pool buckets
    /// instead of one set of buckets per element type.
    /// Dispose (preferably with <see langword="using"/>) to return the array to the pool.
    /// </remarks>
    /// <typeparam name="T">Element type the buffer is written as.</typeparam>
    public readonly ref struct RentedFloatBuffer<T> where T : unmanaged
    {
        /// <summary>
        /// Gets the requested elements. Exactly the requested length, unlike <see cref="FloatArray"/>.
        /// </summary>
        public Span<T> Span { get; }

        /// <summary>
        /// Gets the rented array, for APIs that take a <see cref="float"/> array.
        /// The pool hands out whole buckets, so this is usually longer than requested.
        /// </summary>
        public float[] FloatArray { get; }

        /// <summary>
        /// Rents a buffer of <paramref name="count"/> elements.
        /// </summary>
        /// <param name="count">Number of <typeparamref name="T"/> elements to fit.</param>
        public RentedFloatBuffer(int count)
        {
            var byteCount = count * Unsafe.SizeOf<T>();

            // Bytes to floats, rounded up, because the pool only hands out whole floats: adding
            // sizeof(float) - 1 first turns the truncating integer division into a ceiling one,
            // so a T that is not a multiple of 4 bytes still gets its last element covered.
            var floatCount = (byteCount + sizeof(float) - 1) / sizeof(float);

            FloatArray = ArrayPool<float>.Shared.Rent(floatCount);
            Span = MemoryMarshal.Cast<float, T>(FloatArray.AsSpan())[..count];
        }

        /// <summary>
        /// Returns the rented array to the pool.
        /// </summary>
        public void Dispose() => ArrayPool<float>.Shared.Return(FloatArray);
    }
}
