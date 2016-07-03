using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat
{
    public class ValveFont
    {
        private const string MAGIC = "VFONT1";
        private const byte MAGIC_TRICK = 167;

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public byte[] Read(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Read(fs);
            }
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public byte[] Read(Stream input)
        {
            using (var reader = new BinaryReader(input))
            {
                // Magic is at the end
                reader.BaseStream.Seek(-MAGIC.Length, SeekOrigin.End);

                if (Encoding.ASCII.GetString(reader.ReadBytes(MAGIC.Length)) != MAGIC)
                {
                    throw new InvalidDataException("Given file is not a vfont, version 1.");
                }

                return Decode(reader);
            }
        }

        private byte[] Decode(BinaryReader reader)
        {
            reader.BaseStream.Seek(-1 - MAGIC.Length, SeekOrigin.End);

            // How many magic bytes there are
            var bytes = reader.ReadByte();
            var output = new byte[reader.BaseStream.Length - MAGIC.Length - bytes];
            int magic = MAGIC_TRICK;

            // Read the magic bytes
            reader.BaseStream.Seek(-bytes, SeekOrigin.Current);

            bytes--;

            for (var i = 0; i < bytes; i++)
            {
                magic ^= (reader.ReadByte() + MAGIC_TRICK) % 256;
            }

            // Decode the rest
            reader.BaseStream.Seek(0, SeekOrigin.Begin);

            for (var i = 0; i < output.Length; i++)
            {
                var currentByte = reader.ReadByte();

                output[i] = (byte)(currentByte ^ magic);

                magic = (currentByte + MAGIC_TRICK) % 256;
            }

            return output;
        }
    }
}
