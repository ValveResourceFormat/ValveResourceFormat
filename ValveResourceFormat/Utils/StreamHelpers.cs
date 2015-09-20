using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat
{
    internal static class StreamHelpers
    {
        public static string ReadNullTermString(this BinaryReader stream, Encoding encoding)
        {
            int characterSize = encoding.GetByteCount("e");

            using (var ms = new MemoryStream())
            {

                while (true)
                {
                    var data = new byte[characterSize];
                    stream.Read(data, 0, characterSize);

                    if (encoding.GetString(data, 0, characterSize) == "\0")
                    {
                        break;
                    }

                    ms.Write(data, 0, data.Length);
                }

                return encoding.GetString(ms.ToArray());
            }
        }
    }
}
