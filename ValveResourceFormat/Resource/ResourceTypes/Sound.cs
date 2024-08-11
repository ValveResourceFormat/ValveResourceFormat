using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{

    public readonly struct EmphasisSample
    {
        public float Time { get; }
        public float Value { get; }
    }

    public readonly struct PhonemeTag
    {
        public float StartTime { get; init; }
        public float EndTime { get; init; }
        public ushort PhonemeCode { get; init; }
    }

    public class Sentence
    {
        public bool ShouldVoiceDuck { get; init; }

        public PhonemeTag[] RunTimePhonemes { get; init; }

        public EmphasisSample[] EmphasisSamples { get; init; }
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

                SetSoundFormatBits(soundFormat);
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

        public void ConstructFromCtrl(BinaryReader reader, Resource resource)
        {
            Reader = reader;
            Offset = resource.FileSize;

            var obj = (BinaryKV3)resource.GetBlockByType(BlockType.CTRL);
            var soundClass = obj.Data.GetStringProperty("_class");

            if (soundClass != "CVoiceContainerDefault")
            {
                throw new InvalidDataException($"Unsupported sound file: {soundClass}");
            }

            var sound = obj.Data.GetSubCollection("m_vSound");

            switch (sound.GetStringProperty("m_nFormat"))
            {
                case "MP3": SetSoundFormatBits(AudioFormatV4.MP3); break;
                case "PCM16": SetSoundFormatBits(AudioFormatV4.PCM16); break;

                default:
                    throw new UnexpectedMagicException("Unexpected audio format", sound.GetStringProperty("m_nFormat"), "m_nFormat");
            }

            SampleRate = sound.GetUInt32Property("m_nRate");
            SampleCount = sound.GetUInt32Property("m_nSampleCount");
            Channels = sound.GetByteProperty("m_nChannels");
            LoopStart = sound.GetInt32Property("m_nLoopStart");
            LoopEnd = sound.GetInt32Property("m_nLoopEnd");
            Duration = sound.GetFloatProperty("m_flDuration");
            StreamingDataSize = sound.GetUInt32Property("m_nStreamingSize");

            // TODO: m_Sentences
        }

        private void SetSoundFormatBits(AudioFormatV4 soundFormat)
        {
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

            const int WaveHeaderSize = 44;
            var totalSize = (int)StreamingDataSize + (SoundType == AudioFileType.WAV ? WaveHeaderSize : 0);

            var stream = new MemoryStream(capacity: totalSize);

            if (SoundType == AudioFileType.WAV)
            {
                // http://soundfile.sapp.org/doc/WaveFormat/
                // http://www.codeproject.com/Articles/129173/Writing-a-Proper-Wave-File

                var byteRate = SampleRate * Channels * (Bits / 8);
                var blockAlign = Channels * (Bits / 8);

                if (AudioFormat == WaveAudioFormat.ADPCM)
                {
                    byteRate = 1;
                    blockAlign = 4;
                }

                stream.Write("RIFF"u8);
                stream.Write(MemoryMarshal.AsBytes([StreamingDataSize + 42]));
                stream.Write("WAVE"u8);
                stream.Write("fmt "u8);
                stream.Write(MemoryMarshal.AsBytes([16]));
                stream.Write(MemoryMarshal.AsBytes([(ushort)AudioFormat, (ushort)Channels]));
                stream.Write(MemoryMarshal.AsBytes([SampleRate, byteRate]));
                stream.Write(MemoryMarshal.AsBytes([(ushort)blockAlign, (ushort)Bits]));
                stream.Write("data"u8);
                stream.Write(MemoryMarshal.AsBytes([StreamingDataSize]));

                Debug.Assert(stream.Length == WaveHeaderSize);
            }

            Reader.BaseStream.CopyTo(stream, (int)StreamingDataSize);
            Debug.Assert(stream.Length == totalSize);

            // Flush and reset position so that consumers can read it
            stream.Flush();
            stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        public override string ToString()
        {
            var output = new StringBuilder();

            output.AppendLine(CultureInfo.InvariantCulture, $"SoundType: {SoundType}");
            output.AppendLine(CultureInfo.InvariantCulture, $"Sample Rate: {SampleRate}");
            output.AppendLine(CultureInfo.InvariantCulture, $"Bits: {Bits}");
            output.AppendLine(CultureInfo.InvariantCulture, $"SampleSize: {SampleSize}");
            output.AppendLine(CultureInfo.InvariantCulture, $"SampleCount: {SampleCount}");
            output.AppendLine(CultureInfo.InvariantCulture, $"Format: {AudioFormat}");
            output.AppendLine(CultureInfo.InvariantCulture, $"Channels: {Channels}");

            var loopStart = TimeSpan.FromSeconds(LoopStart);
            output.AppendLine(CultureInfo.InvariantCulture, $"LoopStart: ({loopStart}) {LoopStart}");

            var loopEnd = TimeSpan.FromSeconds(LoopEnd);
            output.AppendLine(CultureInfo.InvariantCulture, $"LoopEnd: ({loopEnd}) {LoopEnd}");

            var duration = TimeSpan.FromSeconds(Duration);
            output.AppendLine(CultureInfo.InvariantCulture, $"Duration: {duration} ({Duration})");

            output.AppendLine(CultureInfo.InvariantCulture, $"StreamingDataSize: {StreamingDataSize}");

            if (Sentence != null)
            {
                output.AppendLine(CultureInfo.InvariantCulture, $"Sentence[{Sentence.RunTimePhonemes.Length}]:");
                foreach (var phoneme in Sentence.RunTimePhonemes)
                {
                    output.AppendLine(CultureInfo.InvariantCulture, $"\tPhonemeTag(StartTime={phoneme.StartTime}, EndTime={phoneme.EndTime}, PhonemeCode={phoneme.PhonemeCode})");
                }
            }

            return output.ToString();
        }
    }
}
