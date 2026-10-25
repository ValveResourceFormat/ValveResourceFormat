using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ValveResourceFormat.Utils
{
    internal static class StreamHelpers
    {
        public static Guid ReadGuid(this BinaryReader reader)
        {
            Span<byte> bytes = stackalloc byte[16];
            reader.ReadExactly(bytes);

            return new Guid(bytes);
        }

        /// <summary>
        /// Reads an array of little endian values stored back to back.
        /// </summary>
        public static T[] ReadArray<T>(this BinaryReader reader, long count) where T : unmanaged
        {
            var array = new T[count];
            reader.ReadExactly(MemoryMarshal.AsBytes(array.AsSpan()));

            return array;
        }

        /// <summary>
        /// Reads a null terminated string.
        /// </summary>
        /// <param name="stream">The reader to read from. It is left positioned after the null terminator.</param>
        /// <param name="encoding">The encoding of the string, which decides the size of the terminator.</param>
        /// <param name="bufferLengthHint">Initial buffer length used when reading the string.</param>
        /// <returns>The decoded string without the terminator.</returns>
        public static string ReadNullTermString(this BinaryReader stream, Encoding encoding, int bufferLengthHint = 32)
        {
            if (encoding.IsSingleByte || encoding is UTF8Encoding)
            {
                return ReadSingleByteNullTermString(stream, encoding);
            }

            var characterSize = encoding.GetByteCount("e");
            Span<byte> data = stackalloc byte[characterSize];

            using var ms = new MemoryStream(capacity: bufferLengthHint);

            while (true)
            {
                data.Clear();
                stream.BaseStream.ReadAtLeast(data, data.Length, throwOnEndOfStream: false);

                if (!data.ContainsAnyExcept((byte)0))
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
        /// <returns>The string, or an empty string when the offset is zero.</returns>
        /// <param name="stream">The reader positioned at the offset. It is left positioned after the offset.</param>
        /// <param name="encoding">The encoding of the string.</param>
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

        /// <summary>
        /// Reads a row major 3x4 matrix, three rotation rows each followed by a translation component.
        /// </summary>
        public static Matrix4x4 ReadMatrix3x4(this BinaryReader stream)
        {
            return Matrix4x4.Transpose(new Matrix4x4(
                stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(),
                stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(),
                stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(),
                0f, 0f, 0f, 1f
            ));
        }

        [SkipLocalsInit]
        private static string ReadSingleByteNullTermString(BinaryReader reader, Encoding encoding)
        {
            var stream = reader.BaseStream;
            var start = stream.CanSeek ? stream.Position : -1;

            // Most strings fit on the stack, the pool is only rented from for the ones that do not
            Span<byte> buffer = stackalloc byte[256];
            byte[]? rented = null;

            try
            {
                var length = 0;
                var terminated = false;

                while (true)
                {
                    if (length == buffer.Length)
                    {
                        var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        buffer.CopyTo(newBuffer);

                        if (rented != null)
                        {
                            ArrayPool<byte>.Shared.Return(rented);
                        }

                        buffer = rented = newBuffer;
                    }

                    var chunk = start >= 0 ? buffer[length..] : buffer.Slice(length, 1);
                    var read = stream.Read(chunk);

                    if (read == 0)
                    {
                        break;
                    }

                    var terminator = chunk[..read].IndexOf((byte)0);

                    if (terminator >= 0)
                    {
                        length += terminator;
                        terminated = true;
                        break;
                    }

                    length += read;
                }

                if (start >= 0)
                {
                    stream.Position = start + length + (terminated ? 1 : 0);
                }

                return encoding.GetString(buffer[..length]);
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
