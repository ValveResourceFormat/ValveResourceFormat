using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// A scratch buffer of <typeparamref name="T"/> elements backed by an array rented from the
    /// shared <see cref="byte"/> pool.
    /// </summary>
    /// <remarks>
    /// Renting bytes for every element type keeps all scratch buffers in the same pool buckets
    /// instead of one set of buckets per element type.
    /// Dispose (preferably with <see langword="using"/>) to return the array to the pool.
    /// </remarks>
    /// <typeparam name="T">Element type the buffer is written as.</typeparam>
    public readonly ref struct RentedBuffer<T> where T : unmanaged
    {
        /// <summary>
        /// Gets the requested elements. Exactly the requested length, unlike <see cref="ByteArray"/>.
        /// </summary>
        public Span<T> Span { get; }

        /// <summary>
        /// Gets the rented array, for APIs that take a <see cref="byte"/> array.
        /// The pool hands out whole buckets, so this is usually longer than requested.
        /// </summary>
        public byte[] ByteArray { get; }

        /// <summary>
        /// Rents a buffer of <paramref name="count"/> elements.
        /// </summary>
        /// <param name="count">Number of <typeparamref name="T"/> elements to fit.</param>
        public RentedBuffer(int count)
        {
            ByteArray = ArrayPool<byte>.Shared.Rent(count * Unsafe.SizeOf<T>());
            Span = MemoryMarshal.Cast<byte, T>(ByteArray.AsSpan())[..count];
        }

        /// <summary>
        /// Returns the rented array to the pool.
        /// </summary>
        public void Dispose() => ArrayPool<byte>.Shared.Return(ByteArray);
    }
}
