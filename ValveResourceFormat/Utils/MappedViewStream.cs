using System.IO;
using System.IO.MemoryMappedFiles;

namespace ValveResourceFormat.Utils;

/// <summary>
/// A read only stream over a memory mapped view whose span reads copy straight out of the view.
/// </summary>
internal sealed class MappedViewStream : Stream
{
    private readonly MemoryMappedViewStream View;
    private readonly UnmanagedMemoryStream Inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="MappedViewStream"/> class that takes ownership of <paramref name="view"/>.
    /// </summary>
    public MappedViewStream(MemoryMappedViewStream view)
    {
        View = view;
        Inner = new UnmanagedMemoryStream(view.SafeMemoryMappedViewHandle, view.PointerOffset, view.Length, FileAccess.Read);
    }

    /// <inheritdoc/>
    public override bool CanRead => Inner.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => Inner.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => Inner.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => Inner.Position;
        set => Inner.Position = value;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer) => Inner.Read(buffer);

    /// <inheritdoc/>
    public override int ReadByte() => Inner.ReadByte();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Inner.Dispose();
            View.Dispose();
        }

        base.Dispose(disposing);
    }
}
