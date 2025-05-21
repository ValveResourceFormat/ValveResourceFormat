using System.Buffers;
using System.IO;
using System.Text;

namespace ValveResourceFormat
{
    internal static class StreamHelpers
    {
        /// <summary>
        /// Reads a null terminated string.
        /// </summary>
        /// <returns>String.</returns>
        /// <param name="stream">Stream.</param>
        /// <param name="encoding">Encoding.</param>
        public static string ReadNullTermString(this BinaryReader stream, Encoding encoding, int bufferLengthHint = 32)
        {
            if (encoding == Encoding.UTF8)
            {
                return ReadNullTermUtf8String(stream, bufferLengthHint);
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
        /// Reads a string at a given uint offset.
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

        private static string ReadNullTermUtf8String(BinaryReader stream, int bufferLengthHint)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(bufferLengthHint);

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

                    if (position >= buffer.Length)
                    {
                        var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = newBuffer;
                    }

                    buffer[position++] = b;
                }
                while (true);

                return Encoding.UTF8.GetString(buffer[..position]);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
