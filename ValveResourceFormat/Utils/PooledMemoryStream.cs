using System.Buffers;
using System.Diagnostics;
using System.IO;

namespace ValveResourceFormat.Utils;

/// <summary>
/// A memory stream that uses ArrayPool for buffer management.
/// </summary>
internal sealed class PooledMemoryStream : MemoryStream
{
    /// <remarks>
    /// The buffer length may be larger than requested. Use <see cref="BufferSpan"/> for correct size.
    /// </remarks>
    private readonly byte[] Buffer;

    /// <summary>
    /// Gets the buffer as a span with the correct size.
    /// </summary>
    public Span<byte> BufferSpan => MemoryExtensions.AsSpan(Buffer)[..(int)Length];

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledMemoryStream"/> class.
    /// </summary>
    /// <param name="length">The length of the stream.</param>
    public PooledMemoryStream(int length)
        : base(ArrayPool<byte>.Shared.Rent(length), 0, length, true, true)
    {
        Buffer = GetBuffer();
        Debug.Assert(Length == length);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Copies straight from the pooled buffer, the base implementation of a derived type goes through a temporary array.
    /// </remarks>
    public override int Read(Span<byte> destination)
    {
        var position = (int)Position;
        var remaining = (int)Length - position;

        if (remaining <= 0)
        {
            return 0;
        }

        var count = Math.Min(remaining, destination.Length);
        MemoryExtensions.AsSpan(Buffer, position, count).CopyTo(destination);
        Position = position + count;

        return count;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the pooled buffer to the ArrayPool when disposing.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            ArrayPool<byte>.Shared.Return(Buffer);
        }
    }
}
