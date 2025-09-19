using System.Buffers;
using System.Diagnostics;
using System.IO;

namespace ValveResourceFormat.Utils;

public sealed class PooledMemoryStream : MemoryStream
{
    /// <remarks>
    /// The buffer length is larger than requested. Use BufferSpan for correct size.
    /// </remarks>
    private readonly byte[] Buffer;
    public Span<byte> BufferSpan => MemoryExtensions.AsSpan(Buffer)[..(int)Length];

    public PooledMemoryStream(int length)
        : base(ArrayPool<byte>.Shared.Rent(length), 0, length, true, true)
    {
        Buffer = GetBuffer();
        Debug.Assert(Length == length);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            ArrayPool<byte>.Shared.Return(Buffer);
        }
    }
}
