using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

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

    public class Sound : Block
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

        public override BlockType Type => BlockType.DATA;

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

        public byte[] Header { get; private set; } = []; // Mostly for wave files
        public uint StreamingDataSize { get; private set; }

        private BinaryReader Reader => Resource.Reader;

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            if (Resource.Version > 4)
            {
                throw new InvalidDataException($"Invalid vsnd version '{Resource.Version}'");
            }

            if (Resource.Version >= 4)
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

            var sentenceOffset = reader.ReadUInt32();
            var b = reader.ReadUInt32(); // size?

            if (sentenceOffset != 0)
            {
                sentenceOffset = (uint)(reader.BaseStream.Position + sentenceOffset);
            }

            var headerSize = reader.ReadInt32();
            StreamingDataSize = reader.ReadUInt32();

            // this is likely to be m_nSeekTable
            if (Resource.Version >= 1)
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
            // this is likely to be CAudioMorphData
            if (Resource.Version >= 2)
            {
                var f = reader.ReadUInt32();
                if (f != 0)
                {
                    throw new UnexpectedMagicException("Unexpected", f, nameof(f));
                }
            }

            if (Resource.Version >= 4)
            {
                LoopEnd = reader.ReadInt32();
            }

            if (headerSize > 0)
            {
                Debug.Assert(AudioFormat == WaveAudioFormat.ADPCM);

                Header = Reader.ReadBytes(headerSize);
            }

            ReadPhonemeStream(reader, sentenceOffset);
        }

        public bool ConstructFromCtrl()
        {
            Offset = Resource.FileSize;

            var obj = (BinaryKV3)Resource.GetBlockByType(BlockType.CTRL);
            var soundClass = obj.Data.GetStringProperty("_class");

            if (soundClass != "CVoiceContainerDefault")
            {
                Console.Error.WriteLine($"Unsupported sound file: {soundClass}");
                return false;
            }

            var sound = obj.Data.GetSubCollection("m_vSound");

            switch (sound.GetStringProperty("m_nFormat"))
            {
                case "MP3": SetSoundFormatBits(AudioFormatV4.MP3); break;
                case "PCM8": SetSoundFormatBits(AudioFormatV4.PCM8); break;
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

            return true;
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
                    Bits = 16;
                    SampleSize = 1;
                    AudioFormat = WaveAudioFormat.ADPCM;

                    break;

                default:
                    throw new UnexpectedMagicException("Unexpected audio type", (int)soundFormat, nameof(soundFormat));
            }
        }

        private void ReadPhonemeStream(BinaryReader reader, uint sentenceOffset)
        {
            if (sentenceOffset == 0)
            {
                return;
            }

            reader.BaseStream.Position = sentenceOffset;

            var numPhonemeTags = reader.ReadInt32();

            var a = reader.ReadInt32(); // numEmphasisSamples ?
            var b = reader.ReadInt32(); // Sentence.ShouldVoiceDuck ?

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
            if (StreamingDataSize == 0)
            {
                return [];
            }

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
            if (StreamingDataSize == 0)
            {
                return new MemoryStream();
            }

            const int WaveHeaderSizeWithoutFmt = 28;
            var waveHeaderSize = WaveHeaderSizeWithoutFmt + (AudioFormat == WaveAudioFormat.ADPCM ? Header.Length : 16);
            var totalSize = (int)StreamingDataSize + (SoundType == AudioFileType.WAV ? waveHeaderSize : 0);

            var stream = new MemoryStream(capacity: totalSize);

            if (SoundType == AudioFileType.WAV)
            {
                // http://soundfile.sapp.org/doc/WaveFormat/
                // http://www.codeproject.com/Articles/129173/Writing-a-Proper-Wave-File
                // https://github.com/microsoft/DirectXTK/wiki/Wave-Formats

                stream.Write("RIFF"u8);
                stream.Write(MemoryMarshal.AsBytes([totalSize - 8]));
                stream.Write("WAVE"u8);
                stream.Write("fmt "u8);

                if (AudioFormat == WaveAudioFormat.ADPCM)
                {
                    stream.Write(MemoryMarshal.AsBytes([Header.Length]));
                    stream.Write(Header); // Quite likely to be ADPCMWAVEFORMAT
                }
                else
                {
                    var byteRate = SampleRate * Channels * (Bits / 8);
                    var blockAlign = Channels * (Bits / 8);

                    stream.Write(MemoryMarshal.AsBytes([16])); // size of PCMWAVEFORMAT

                    // PCMWAVEFORMAT
                    stream.Write(MemoryMarshal.AsBytes([(ushort)AudioFormat, (ushort)Channels]));
                    stream.Write(MemoryMarshal.AsBytes([SampleRate, byteRate]));
                    stream.Write(MemoryMarshal.AsBytes([(ushort)blockAlign, (ushort)Bits]));
                }

                stream.Write("data"u8);
                stream.Write(MemoryMarshal.AsBytes([StreamingDataSize]));

                Debug.Assert(stream.Length == waveHeaderSize);
            }

            Reader.BaseStream.Position = Offset + Size;
            Reader.BaseStream.CopyTo(stream);
            Debug.Assert(stream.Length == totalSize);

            // Flush and reset position so that consumers can read it
            stream.Flush();
            stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine($"SoundType: {SoundType}");
            writer.WriteLine($"Sample Rate: {SampleRate}");
            writer.WriteLine($"Bits: {Bits}");
            writer.WriteLine($"SampleSize: {SampleSize}");
            writer.WriteLine($"SampleCount: {SampleCount}");
            writer.WriteLine($"Format: {AudioFormat}");
            writer.WriteLine($"Channels: {Channels}");

            var loopStart = TimeSpan.FromSeconds(LoopStart);
            writer.WriteLine($"LoopStart: ({loopStart}) {LoopStart}");

            var loopEnd = TimeSpan.FromSeconds(LoopEnd);
            writer.WriteLine($"LoopEnd: ({loopEnd}) {LoopEnd}");

            var duration = TimeSpan.FromSeconds(Duration);
            writer.WriteLine($"Duration: {duration} ({Duration})");

            writer.WriteLine($"StreamingDataSize: {StreamingDataSize}");

            if (Sentence != null)
            {
                writer.WriteLine($"Sentence[{Sentence.RunTimePhonemes.Length}]:");
                writer.Indent++;
                foreach (var phoneme in Sentence.RunTimePhonemes)
                {
                    writer.WriteLine($"PhonemeTag(StartTime={phoneme.StartTime}, EndTime={phoneme.EndTime}, PhonemeCode={phoneme.PhonemeCode})");
                }
                writer.Indent--;
            }
        }
    }
}
