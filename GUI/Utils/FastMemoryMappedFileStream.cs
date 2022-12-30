using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GUI.Utils
{
    public class FastMemoryMappedFileStream : UnmanagedMemoryStream
    {
        private SafeBuffer _buffer;
        private unsafe byte* _ptr;

        public unsafe FastMemoryMappedFileStream(SafeBuffer buffer, long offset, long length, FileAccess access)
        {
            Initialize(buffer, offset, length, access);

            _buffer = buffer;
            _buffer.AcquirePointer(ref _ptr);
            _ptr += offset;
        }

        public unsafe Span<byte> GetSpan(int count)
        {
            var end = _ptr + Position + count;
            if (end > _ptr + Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"The requested span is outside the bounds of the stream (end would be {(IntPtr) (end - _ptr)} but length is {Length})");
            }

            var ret = new Span<byte>(_ptr + Position, count);

            Position += count;

            return ret;
        }

        public override int Read(Span<byte> buffer)
        {
            GetSpan(buffer.Length).CopyTo(buffer);
            return buffer.Length;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _buffer.ReleasePointer();
            _buffer.Dispose();
        }
    }
}
