using System;
using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{

    public struct EmphasisSample
    {
        public float Time { get; }
        public float Value { get; }
    }

    public struct PhonemeTag
    {
        public float StartTime { get; set; }
        public float EndTime { get; set; }
        public UInt16 PhonemeCode { get; set; }
    }

    public class Sentence
    {
        public bool ShouldVoiceDuck { get; set; }

        public PhonemeTag[] RunTimePhonemes { get; set; }

        public EmphasisSample[] EmphasisSamples { get; set; }
    }

    public class Sound : ResourceData
    {
        public enum AudioFileType
        {
            AAC = 0,
            WAV = 1,
            MP3 = 2,
        }

        public enum AudioFormatV4
        {
            PCM16 = 0,
            PCM8 = 1,
            MP3 = 2,
            ADPCM = 3,
        }

        // https://github.com/naudio/NAudio/blob/fb35ce8367f30b8bc5ea84e7d2529e172cf4c381/NAudio.Core/Wave/WaveFormats/WaveFormatEncoding.cs
        public enum WaveAudioFormat
        {
            Unknown = 0,
            PCM = 1,
            ADPCM = 2,
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
        public WaveAudioFormat AudioFormat { get; private set; }

        public uint SampleSize { get; private set; }

        public uint SampleCount { get; private set; }

        public int LoopStart { get; private set; }

        public int LoopEnd { get; private set; }

        public float Duration { get; private set; }

        public Sentence Sentence { get; private set; }

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
                var soundFormat = (AudioFormatV4)reader.ReadByte();
                Channels = reader.ReadByte();

                switch (soundFormat)
                {
                    case AudioFormatV4.PCM8:
                        SoundType = AudioFileType.WAV;
                        Bits = 8;
                        SampleSize = 1;
                        AudioFormat = WaveAudioFormat.PCM;
                        break;

                    case AudioFormatV4.PCM16:
                        SoundType = AudioFileType.WAV;
                        Bits = 16;
                        SampleSize = 2;
                        AudioFormat = WaveAudioFormat.PCM;
                        break;

                    case AudioFormatV4.MP3:
                        SoundType = AudioFileType.MP3;
                        break;

                    case AudioFormatV4.ADPCM:
                        SoundType = AudioFileType.WAV;
                        Bits = 4;
                        SampleSize = 1;
                        AudioFormat = WaveAudioFormat.ADPCM;
                        throw new NotImplementedException("ADPCM is currently not implemented correctly.");

                    default:
                        throw new UnexpectedMagicException("Unexpected audio type", (int)soundFormat, nameof(soundFormat));
                }
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
                AudioFormat = (WaveAudioFormat)ExtractSub(bitpackedSoundInfo, 12, 2);
                SampleRate = ExtractSub(bitpackedSoundInfo, 14, 17);
            }

            LoopStart = reader.ReadInt32();
            SampleCount = reader.ReadUInt32();
            Duration = reader.ReadSingle();

            var sentenceOffset = (long)reader.ReadUInt32();
            reader.BaseStream.Position += 4;

            if (sentenceOffset != 0)
            {
                sentenceOffset = reader.BaseStream.Position + sentenceOffset;
            }

            // Skipping over m_pHeader
            reader.BaseStream.Position += 4;

            StreamingDataSize = reader.ReadUInt32();

            if (resource.Version >= 1)
            {
                var d = reader.ReadUInt32();
                if (d != 0)
                {
                    throw new UnexpectedMagicException("Unexpected", d, nameof(d));
                }

                var e = reader.ReadUInt32();
                if (e != 0)
                {
                    throw new UnexpectedMagicException("Unexpected", e, nameof(e));
                }
            }

            // v2 and v3 are the same?
            if (resource.Version >= 2)
            {
                var f = reader.ReadUInt32();
                if (f != 0)
                {
                    throw new UnexpectedMagicException("Unexpected", f, nameof(f));
                }
            }

            if (resource.Version >= 4)
            {
                LoopEnd = reader.ReadInt32();
            }

            ReadPhonemeStream(reader, sentenceOffset);
        }

        private void ReadPhonemeStream(BinaryReader reader, long sentenceOffset)
        {
            if (sentenceOffset == 0)
            {
                return;
            }

            Reader.BaseStream.Position = sentenceOffset;

            var numPhonemeTags = reader.ReadInt32();

            var a = reader.ReadInt32(); // numEmphasisSamples ?
            var b = Reader.ReadInt32(); // Sentence.ShouldVoiceDuck ?

            // Skip sounds that have these
            if (a != 0 || b != 0)
            {
                return;
            }

            Sentence = new Sentence
            {
                RunTimePhonemes = new PhonemeTag[numPhonemeTags]
            };

            for (var i = 0; i < numPhonemeTags; i++)
            {
                var startTime = reader.ReadSingle();
                var endTime = reader.ReadSingle();
                var phonemeCode = reader.ReadUInt16();

                reader.BaseStream.Position += 2;

                var phonemeTag = new PhonemeTag
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    PhonemeCode = phonemeCode
                };

                Sentence.RunTimePhonemes[i] = phonemeTag;
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
            using var sound = GetSoundStream();
            return sound.ToArray();
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

                if (AudioFormat == WaveAudioFormat.ADPCM)
                {
                    byteRate = 1;
                    blockAlign = 4;
                }

                stream.Write(headerRiff, 0, headerRiff.Length);
                stream.Write(PackageInt(StreamingDataSize + 42, 4), 0, 4);

                stream.Write(formatWave, 0, formatWave.Length);
                stream.Write(formatTag, 0, formatTag.Length);
                stream.Write(PackageInt(16, 4), 0, 4); // Subchunk1Size

                stream.Write(PackageInt((uint)AudioFormat, 2), 0, 2);
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

            var loopStart = TimeSpan.FromSeconds(LoopStart);
            output.AppendLine($"LoopStart: ({loopStart}) {LoopStart}");

            var loopEnd = TimeSpan.FromSeconds(LoopEnd);
            output.AppendLine($"LoopEnd: ({loopEnd}) {LoopEnd}");

            var duration = TimeSpan.FromSeconds(Duration);
            output.AppendLine($"Duration: {duration} ({Duration})");

            output.AppendLine($"StreamingDataSize: {StreamingDataSize}");

            if (Sentence != null)
            {
                output.AppendLine($"Sentence[{Sentence.RunTimePhonemes.Length}]:");
                foreach (var phoneme in Sentence.RunTimePhonemes)
                {
                    output.AppendLine($"\tPhonemeTag(StartTime={phoneme.StartTime}, EndTime={phoneme.EndTime}, PhonemeCode={phoneme.PhonemeCode})");
                }
            }

            return output.ToString();
        }
    }
}
