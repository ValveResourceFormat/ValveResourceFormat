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
