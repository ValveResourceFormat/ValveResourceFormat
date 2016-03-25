using System;
using System.IO;

namespace ValveResourceFormat
{
    public class CompiledShader : IDisposable
    {
        public const int MAGIC = 0x32736376; // "vcs2"

        private BinaryReader Reader;

        /// <summary>
        /// Releases binary reader.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && Reader != null)
            {
                Reader.Dispose();
                Reader = null;
            }
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);

            Read(fs);
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(Stream input)
        {
            Reader = new BinaryReader(input);

            var test = Reader.ReadUInt32();
            Console.WriteLine(test);
            Console.WriteLine(MAGIC);

            if (test != MAGIC)
            {
                throw new InvalidDataException("Given file is not a vcs2.");
            }
        }
    }
}
