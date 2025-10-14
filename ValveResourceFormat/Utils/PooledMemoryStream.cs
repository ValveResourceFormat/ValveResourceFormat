using System.Buffers;
using System.Diagnostics;
using System.IO;

namespace ValveResourceFormat.Utils;

/// <summary>
/// A memory stream that uses ArrayPool for buffer management.
/// </summary>
public sealed class PooledMemoryStream : MemoryStream
{
    /// <remarks>
    /// The buffer length is larger than requested. Use BufferSpan for correct size.
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
