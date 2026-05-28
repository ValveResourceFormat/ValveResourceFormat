using NAudio.Wave;
using System.Buffers;

namespace GUI.Types.Audio
{
    class LoopWaveStream : WaveStream
    {
        int loopStart;
        int loopEnd;
        WaveStream _stream;
        public LoopWaveStream(WaveStream stream, int loopStart, int loopEnd)
        {
            _stream = stream;
            this.loopStart = loopStart;
            this.loopEnd = loopEnd;
        }

        public override WaveFormat WaveFormat => _stream.WaveFormat;

        public override long Length => _stream.Length;

        public override long Position
        {
            get
            {
                return _stream.Position;
            }
            set
            {
                _stream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            /*int initialCount = count;
            if (Position + count >= loopEnd)
            {
                count = loopEnd - (int)Position;
            }

            var read = _stream.Read(buffer, offset, count);

            while (read < initialCount)
            {
                count = initialCount - read;
                offset += read;

                _stream.Seek(loopStart, System.IO.SeekOrigin.Begin);
                read += _stream.Read(buffer, offset, count);
            }

            return read;*/
            var read = 0;

            while (read < count)
            {
                //TODO: don't go over loopEnd when reading
                read += _stream.Read(buffer, offset + read, count - read);
                if (_stream.Position >= loopEnd)
                {
                    _stream.Position = loopStart;
                }
            }
            return read;
        }
    }
}
