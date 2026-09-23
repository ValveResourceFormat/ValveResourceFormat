using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace ValveResourceFormat.Utils
{
    internal static class StreamHelpers
    {
        /// <summary>
        /// Reads a null terminated string.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="encoding">Encoding.</param>
        /// <param name="bufferLengthHint">Initial buffer length used when reading the string.</param>
        /// <returns>String.</returns>
        public static string ReadNullTermString(this BinaryReader stream, Encoding encoding, int bufferLengthHint = 32)
        {
            if (encoding == Encoding.UTF8)
            {
                return ReadNullTermUtf8String(stream);
            }

            var characterSize = encoding.GetByteCount("e");
            Span<byte> data = stackalloc byte[characterSize];

            using var ms = new MemoryStream(capacity: bufferLengthHint);

            while (true)
            {
                data.Clear();
                stream.Read(data);

                if (encoding.GetString(data) == "\0")
                {
                    break;
                }

                ms.Write(data);
            }

            ms.TryGetBuffer(out var buffer);

            return encoding.GetString(buffer);
        }

        /// <summary>
        /// Reads a string located at a signed 32-bit relative offset read from the stream.
        /// </summary>
        /// <returns>String.</returns>
        /// <param name="stream">Stream.</param>
        /// <param name="encoding">Encoding.</param>
        public static string ReadOffsetString(this BinaryReader stream, Encoding encoding)
        {
            var currentOffset = stream.BaseStream.Position;
            var offset = stream.ReadInt32();

            if (offset == 0)
            {
                return string.Empty;
            }

            stream.BaseStream.Position = currentOffset + offset;

            var str = ReadNullTermString(stream, encoding);

            stream.BaseStream.Position = currentOffset + 4;

            return str;
        }

        [SkipLocalsInit]
        private static string ReadNullTermUtf8String(BinaryReader stream)
        {
            // Most strings fit on the stack, the pool is only rented from for the ones that do not
            Span<byte> buffer = stackalloc byte[256];
            byte[]? rented = null;

            try
            {
                var position = 0;

                do
                {
                    var b = stream.ReadByte();

                    if (b == 0x00)
                    {
                        break;
                    }

                    if (position == buffer.Length)
                    {
                        var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        buffer.CopyTo(newBuffer);

                        if (rented != null)
                        {
                            ArrayPool<byte>.Shared.Return(rented);
                        }

                        buffer = rented = newBuffer;
                    }

                    buffer[position++] = b;
                }
                while (true);

                return Encoding.UTF8.GetString(buffer[..position]);
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }
    }
}
