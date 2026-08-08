using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    [TestFixture]
    public class SoundTest
    {
        private const string BeepSoundWaveHash = "C33363C025C1B250760D28AE58D2691C6898FDCD224A3DA31ED096173E991B2F";

        [Test]
        public void TestSound()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "beep.vsnd_c");
            using var resource = new Resource();
            resource.Read(file);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resource.ResourceType, Is.EqualTo(ResourceType.Sound));
                Assert.That(resource.DataBlock, Is.InstanceOf<Sound>());
            }

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            using var hash = SHA256.Create();
            using var sound = soundData.GetSoundStream();
            var actualHash = Convert.ToHexString(hash.ComputeHash(sound));

            Assert.That(actualHash, Is.EqualTo(BeepSoundWaveHash));
        }

        [Test]
        public void TestSoundPhonemesWithEmphasis()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "primal_anger_03.vsnd_c");
            using var resource = new Resource();
            resource.Read(file);

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            var sentence = soundData.Sentence;
            Assert.That(sentence, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sentence.ShouldVoiceDuck, Is.False);
                Assert.That(sentence.RunTimePhonemes, Has.Length.EqualTo(8));
                Assert.That(sentence.EmphasisSamples, Has.Length.EqualTo(4));

                Assert.That(sentence.RunTimePhonemes[0].StartTime, Is.EqualTo(0.058f));
                Assert.That(sentence.RunTimePhonemes[0].EndTime, Is.EqualTo(0.272f));
                Assert.That(sentence.RunTimePhonemes[0].PhonemeCode, Is.EqualTo(633));
                Assert.That(sentence.RunTimePhonemes[7].StartTime, Is.EqualTo(1.672f));
                Assert.That(sentence.RunTimePhonemes[7].EndTime, Is.EqualTo(1.788f));
                Assert.That(sentence.RunTimePhonemes[7].PhonemeCode, Is.EqualTo(633));

                Assert.That(sentence.EmphasisSamples[0].Time, Is.EqualTo(0.35f));
                Assert.That(sentence.EmphasisSamples[0].Value, Is.EqualTo(1f));
                Assert.That(sentence.EmphasisSamples[3].Time, Is.EqualTo(1.2f));
                Assert.That(sentence.EmphasisSamples[3].Value, Is.EqualTo(0.983333f));
            }
        }

        [Test]
        public void TestSentenceExport()
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

            Assert.That(sentence.ToValveSentence(), Is.EqualTo(expected));
        }

        [Test]
        public void TestSentenceExportWithEmphasis()
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

            Assert.That(sentence.ToValveSentence(), Is.EqualTo(expected));
        }

        [Test]
        public void TestSoundNoFileName()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "beep.vsnd_c");
            using var fs = File.OpenRead(file);
            using var resource = new Resource();
            resource.Read(fs, verifyFileSize: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resource.ResourceType, Is.EqualTo(ResourceType.Sound));
                Assert.That(resource.DataBlock, Is.InstanceOf<Sound>());
            }

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            using var hash = SHA256.Create();
            using var sound = soundData.GetSoundStream();
            var actualHash = Convert.ToHexString(hash.ComputeHash(sound));

            Assert.That(actualHash, Is.EqualTo(BeepSoundWaveHash));
        }

        [Test]
        public void TestSoundNoFileNameVerifySize()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "beep.vsnd_c");
            using var fs = File.OpenRead(file);
            using var resource = new Resource();
            resource.Read(fs);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resource.ResourceType, Is.EqualTo(ResourceType.Sound));
                Assert.That(resource.DataBlock, Is.InstanceOf<Sound>());
            }

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            using var hash = SHA256.Create();
            using var sound = soundData.GetSoundStream();
            var actualHash = Convert.ToHexString(hash.ComputeHash(sound));

            Assert.That(actualHash, Is.EqualTo(BeepSoundWaveHash));
        }
    }
}
