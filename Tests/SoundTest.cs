using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class SoundTest
    {
        private const string BeepSoundWaveHash = "C33363C025C1B250760D28AE58D2691C6898FDCD224A3DA31ED096173E991B2F";

        [Test]
        public async Task TestSound()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "beep.vsnd_c");
            using var resource = new Resource();
            resource.Read(file);

            using (Assert.Multiple())
            {
                await Assert.That(resource.ResourceType).IsEqualTo(ResourceType.Sound);
                await Assert.That(resource.DataBlock).IsAssignableTo<Sound>();
            }

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            using var hash = SHA256.Create();
            using var sound = soundData.GetSoundStream();
            var actualHash = Convert.ToHexString(await hash.ComputeHashAsync(sound));

            await Assert.That(actualHash).IsEqualTo(BeepSoundWaveHash);
        }

        [Test]
        public async Task TestSoundPhonemesWithEmphasis()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "primal_anger_03.vsnd_c");
            using var resource = new Resource();
            resource.Read(file);

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            var sentence = soundData.Sentence;
            await Assert.That(sentence).IsNotNull();

            using (Assert.Multiple())
            {
                await Assert.That(sentence.ShouldVoiceDuck).IsFalse();
                await Assert.That(sentence.RunTimePhonemes).Count().IsEqualTo(8);
                await Assert.That(sentence.EmphasisSamples).Count().IsEqualTo(4);

                await Assert.That(sentence.RunTimePhonemes[0].StartTime).IsEqualTo(0.058f);
                await Assert.That(sentence.RunTimePhonemes[0].EndTime).IsEqualTo(0.272f);
                await Assert.That(sentence.RunTimePhonemes[0].PhonemeCode).IsEqualTo((ushort)633);
                await Assert.That(sentence.RunTimePhonemes[7].StartTime).IsEqualTo(1.672f);
                await Assert.That(sentence.RunTimePhonemes[7].EndTime).IsEqualTo(1.788f);
                await Assert.That(sentence.RunTimePhonemes[7].PhonemeCode).IsEqualTo((ushort)633);

                await Assert.That(sentence.EmphasisSamples[0].Time).IsEqualTo(0.35f);
                await Assert.That(sentence.EmphasisSamples[0].Value).IsEqualTo(1f);
                await Assert.That(sentence.EmphasisSamples[3].Time).IsEqualTo(1.2f);
                await Assert.That(sentence.EmphasisSamples[3].Value).IsEqualTo(0.983333f);
            }
        }

        [Test]
        public async Task TestSentenceExport()
        {
            var sentence = new Sentence
            {
                RunTimePhonemes =
                [
                    new PhonemeTag { StartTime = 0f, EndTime = 0.048f, PhonemeCode = 240 },
                    new PhonemeTag { StartTime = 0.048f, EndTime = 0.1f, PhonemeCode = 115 },
                ]
            };

            var expected = string.Join('\n',
                "VERSION 1.0",
                "PLAINTEXT",
                "{",
                "}",
                "WORDS",
                "{",
                "\tWORD ðs 0.000 0.100",
                "\t{",
                "\t\t240 ð 0.000 0.048 1",
                "\t\t115 s 0.048 0.100 1",
                "\t}",
                "}",
                "EMPHASIS",
                "{",
                "}",
                "OPTIONS",
                "{",
                "\tvoice_duck 0",
                "}",
                "");

            await Assert.That(sentence.ToValveSentence()).IsEqualTo(expected);
        }

        [Test]
        public async Task TestSentenceExportWithEmphasis()
        {
            var sentence = new Sentence
            {
                ShouldVoiceDuck = true,
                RunTimePhonemes =
                [
                    new PhonemeTag { StartTime = 0f, EndTime = 0.048f, PhonemeCode = 240 },
                ],
                EmphasisSamples =
                [
                    new EmphasisSample { Time = 0.1f, Value = 0.75f },
                    new EmphasisSample { Time = 0.5f, Value = 0.25f },
                ]
            };

            var expected = string.Join('\n',
                "VERSION 1.0",
                "PLAINTEXT",
                "{",
                "}",
                "WORDS",
                "{",
                "\tWORD ð 0.000 0.048",
                "\t{",
                "\t\t240 ð 0.000 0.048 1",
                "\t}",
                "}",
                "EMPHASIS",
                "{",
                "\t0.100000 0.750000",
                "\t0.500000 0.250000",
                "}",
                "OPTIONS",
                "{",
                "\tvoice_duck 1",
                "}",
                "");

            await Assert.That(sentence.ToValveSentence()).IsEqualTo(expected);
        }

        [Test]
        public async Task TestSoundNoFileName()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "beep.vsnd_c");
            using var fs = File.OpenRead(file);
            using var resource = new Resource();
            resource.Read(fs, verifyFileSize: false);

            using (Assert.Multiple())
            {
                await Assert.That(resource.ResourceType).IsEqualTo(ResourceType.Sound);
                await Assert.That(resource.DataBlock).IsAssignableTo<Sound>();
            }

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            using var hash = SHA256.Create();
            using var sound = soundData.GetSoundStream();
            var actualHash = Convert.ToHexString(await hash.ComputeHashAsync(sound));

            await Assert.That(actualHash).IsEqualTo(BeepSoundWaveHash);
        }

        [Test]
        public async Task TestSoundNoFileNameVerifySize()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "beep.vsnd_c");
            using var fs = File.OpenRead(file);
            using var resource = new Resource();
            resource.Read(fs);

            using (Assert.Multiple())
            {
                await Assert.That(resource.ResourceType).IsEqualTo(ResourceType.Sound);
                await Assert.That(resource.DataBlock).IsAssignableTo<Sound>();
            }

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            using var hash = SHA256.Create();
            using var sound = soundData.GetSoundStream();
            var actualHash = Convert.ToHexString(await hash.ComputeHashAsync(sound));

            await Assert.That(actualHash).IsEqualTo(BeepSoundWaveHash);
        }
    }
}
