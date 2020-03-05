using System;
using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class Sound : ResourceData
    {
        public enum AudioFileType
        {
            AAC = 0,
            WAV = 1,
            MP3 = 2,
            Unknown_This_Is_Actually_One_In_New_Format = 3,
        }

        /// <summary>
        /// Gets the audio file type.
        /// </summary>
        /// <value>The file type.</value>
        public AudioFileType SoundType { get; private set; }

        /// <summary>
        /// Gets the samples per second.
        /// </summary>
        /// <value>The sample rate.</value>
        public uint SampleRate { get; private set; }

        /// <summary>
        /// Gets the bit size.
        /// </summary>
        /// <value>The bit size.</value>
        public uint Bits { get; private set; }

        /// <summary>
        /// Gets the number of channels. 1 for mono, 2 for stereo.
        /// </summary>
        /// <value>The number of channels.</value>
        public uint Channels { get; private set; }

        /// <summary>
        /// Gets the bitstream encoding format.
        /// </summary>
        /// <value>The audio format.</value>
        public uint AudioFormat { get; private set; }

        public uint SampleSize { get; private set; }

        public uint SampleCount { get; private set; }

        public int LoopStart { get; private set; }

        public float Duration { get; private set; }

        public uint StreamingDataSize { get; private set; }

        private BinaryReader Reader;

        public override void Read(BinaryReader reader, Resource resource)
        {
            Reader = reader;
            reader.BaseStream.Position = Offset;

            if (resource.Version > 4)
            {
                throw new InvalidDataException($"Invalid vsnd version '{resource.Version}'");
            }

            if (resource.Version >= 4)
            {
                SampleRate = reader.ReadUInt16();

                // TODO: Is any of this actually correct?
                var bitpackedSoundInfo = reader.ReadUInt16();
                SoundType = GetTypeFromNewFormat(ExtractSub(bitpackedSoundInfo, 0, 2));
                // unknown = ExtractSub(bitpackedSoundInfo, 2, 2);
                Bits = ExtractSub(bitpackedSoundInfo, 4, 7);

                SampleSize = Bits / 8;
                Channels = 1;
                AudioFormat = 1;
            }
            else
            {
                var bitpackedSoundInfo = reader.ReadUInt32();
                var type = ExtractSub(bitpackedSoundInfo, 0, 2);

                if (type > 2)
                {
                    throw new InvalidDataException($"Unknown sound type in old vsnd version: {type}");
                }

                SoundType = (AudioFileType)type;
                Bits = ExtractSub(bitpackedSoundInfo, 2, 5);
                Channels = ExtractSub(bitpackedSoundInfo, 7, 2);
                SampleSize = ExtractSub(bitpackedSoundInfo, 9, 3);
                AudioFormat = ExtractSub(bitpackedSoundInfo, 12, 2);
                SampleRate = ExtractSub(bitpackedSoundInfo, 14, 17);
            }

            LoopStart = reader.ReadInt32();
            SampleCount = reader.ReadUInt32();
            Duration = reader.ReadSingle();
            reader.BaseStream.Position += 12;
            StreamingDataSize = reader.ReadUInt32();
        }

        private static AudioFileType GetTypeFromNewFormat(uint type)
        {
            switch (type)
            {
                case 0:
                    return AudioFileType.WAV;
                case 1:
                    return AudioFileType.Unknown_This_Is_Actually_One_In_New_Format;
                case 2:
                    return AudioFileType.MP3;
                default:
                    throw new InvalidDataException($"Unknown sound type: {type}");
            }
        }

        private static uint ExtractSub(uint l, byte offset, byte nrBits)
        {
            var rightShifted = l >> offset;
            var mask = (1 << nrBits) - 1;
            return (uint)(rightShifted & mask);
        }

        /// <summary>
        /// Returns a fully playable sound data.
        /// In case of WAV files, header is automatically generated as Valve removes it when compiling.
        /// </summary>
        /// <returns>Byte array containing sound data.</returns>
        public byte[] GetSound()
        {
            using (var sound = GetSoundStream())
            {
                return sound.ToArray();
            }
        }

        /// <summary>
        /// Returns a fully playable sound data.
        /// In case of WAV files, header is automatically generated as Valve removes it when compiling.
        /// </summary>
        /// <returns>Memory stream containing sound data.</returns>
        public MemoryStream GetSoundStream()
        {
            Reader.BaseStream.Position = Offset + Size;

            var stream = new MemoryStream();

            if (SoundType == AudioFileType.WAV)
            {
                // http://soundfile.sapp.org/doc/WaveFormat/
                // http://www.codeproject.com/Articles/129173/Writing-a-Proper-Wave-File
                var headerRiff = new byte[] { 0x52, 0x49, 0x46, 0x46 };
                var formatWave = new byte[] { 0x57, 0x41, 0x56, 0x45 };
                var formatTag = new byte[] { 0x66, 0x6d, 0x74, 0x20 };
                var subChunkId = new byte[] { 0x64, 0x61, 0x74, 0x61 };

                var byteRate = SampleRate * Channels * (Bits / 8);
                var blockAlign = Channels * (Bits / 8);

                stream.Write(headerRiff, 0, headerRiff.Length);
                stream.Write(PackageInt(StreamingDataSize + 42, 4), 0, 4);

                stream.Write(formatWave, 0, formatWave.Length);
                stream.Write(formatTag, 0, formatTag.Length);
                stream.Write(PackageInt(16, 4), 0, 4); // Subchunk1Size

                stream.Write(PackageInt(AudioFormat, 2), 0, 2);
                stream.Write(PackageInt(Channels, 2), 0, 2);
                stream.Write(PackageInt(SampleRate, 4), 0, 4);
                stream.Write(PackageInt(byteRate, 4), 0, 4);
                stream.Write(PackageInt(blockAlign, 2), 0, 2);
                stream.Write(PackageInt(Bits, 2), 0, 2);
                //stream.Write(PackageInt(0,2), 0, 2); // Extra param size
                stream.Write(subChunkId, 0, subChunkId.Length);
                stream.Write(PackageInt(StreamingDataSize, 4), 0, 4);
            }

            Reader.BaseStream.CopyTo(stream, (int)StreamingDataSize);

            // Flush and reset position so that consumers can read it
            stream.Flush();
            stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        private static byte[] PackageInt(uint source, int length)
        {
            var retVal = new byte[length];
            retVal[0] = (byte)(source & 0xFF);
            retVal[1] = (byte)((source >> 8) & 0xFF);

            if (length == 4)
            {
                retVal[2] = (byte)((source >> 0x10) & 0xFF);
                retVal[3] = (byte)((source >> 0x18) & 0xFF);
            }

            return retVal;
        }

        public override string ToString()
        {
            var output = new StringBuilder();

            output.AppendLine($"SoundType: {SoundType}");
            output.AppendLine($"Sample Rate: {SampleRate}");
            output.AppendLine($"Bits: {Bits}");
            output.AppendLine($"SampleSize: {SampleSize}");
            output.AppendLine($"SampleCount: {SampleCount}");
            output.AppendLine($"Format: {AudioFormat}");
            output.AppendLine($"Channels: {Channels}");
            output.AppendLine($"LoopStart: {LoopStart}");

            var duration = TimeSpan.FromSeconds(Duration);
            output.AppendLine($"Duration: {duration} ({Duration})");

            return output.ToString();
        }
    }
}
