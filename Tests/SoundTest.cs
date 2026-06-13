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
