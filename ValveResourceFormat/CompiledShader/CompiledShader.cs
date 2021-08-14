using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ValveResourceFormat.ShaderParser;

#pragma warning disable CA1051 // Do not declare visible instance fields
namespace ValveResourceFormat
{
    public class CompiledShader : IDisposable
    {
        public const int MAGIC = 0x32736376; // "vcs2"

        private BinaryReader Reader;
        private ShaderDataReader datareader;

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
            var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Read(filename, fs);
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="filename">The filename <see cref="string"/>.</param>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(string filenamepath, Stream input)
        {
            Reader = new BinaryReader(input);
            datareader = new ShaderDataReader(Reader);
            ShaderFile shaderFile = new ShaderFile(filenamepath, datareader);
            shaderFile.PrintByteAnalysis();


            // shaderFile.GetDecompressedZFrame(0); // retrieves a decompressed zframe
        }
    }
}
