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
        public static string ReadNullTermString(this BinaryReader stream, Encoding encoding)
        {
            if (encoding == Encoding.UTF8)
            {
                return ReadNullTermUtf8String(stream);
            }

            var characterSize = encoding.GetByteCount("e");

            using var ms = new MemoryStream();

            while (true)
            {
                var data = new byte[characterSize];

                int bytesRead;
                var totalRead = 0;
                while ((bytesRead = stream.Read(data, totalRead, characterSize - totalRead)) != 0)
                {
                    totalRead += bytesRead;
                }

                if (encoding.GetString(data, 0, characterSize) == "\0")
                {
                    break;
                }

                ms.Write(data, 0, data.Length);
            }

            return encoding.GetString(ms.ToArray());
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

        private static string ReadNullTermUtf8String(BinaryReader stream)
        {
            using var ms = new MemoryStream();

            while (true)
            {
                var b = stream.ReadByte();

                if (b == 0x00)
                {
                    break;
                }

                ms.WriteByte(b);
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
