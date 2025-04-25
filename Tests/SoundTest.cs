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
        [Test]
        public void TestSound()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "beep.vsnd_c");
            using var resource = new Resource();
            resource.Read(file);

            Assert.That(resource.ResourceType, Is.EqualTo(ResourceType.Sound));
            Assert.That(resource.DataBlock, Is.InstanceOf<Sound>());

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            using var hash = SHA256.Create();
            using var sound = soundData.GetSoundStream();
            var actualHash = Convert.ToHexString(hash.ComputeHash(sound));

            Assert.That(actualHash, Is.EqualTo("1F8BF83F3E827A3C02C6AE6B6BD23BBEBD4E18C4F877D092CF0C5B800DAAB2B7"));
        }

        [Test]
        public void TestSoundNoFileName()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "beep.vsnd_c");
            using var fs = File.OpenRead(file);
            using var resource = new Resource();
            resource.Read(fs, verifyFileSize: false);

            Assert.That(resource.ResourceType, Is.EqualTo(ResourceType.Sound));
            Assert.That(resource.DataBlock, Is.InstanceOf<Sound>());

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            using var hash = SHA256.Create();
            using var sound = soundData.GetSoundStream();
            var actualHash = Convert.ToHexString(hash.ComputeHash(sound));

            Assert.That(actualHash, Is.EqualTo("1F8BF83F3E827A3C02C6AE6B6BD23BBEBD4E18C4F877D092CF0C5B800DAAB2B7"));
        }

        [Test]
        public void TestSoundNoFileNameVerifySize()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "beep.vsnd_c");
            using var fs = File.OpenRead(file);
            using var resource = new Resource();
            resource.Read(fs);

            Assert.That(resource.ResourceType, Is.EqualTo(ResourceType.Sound));
            Assert.That(resource.DataBlock, Is.InstanceOf<Sound>());

            var soundData = (Sound?)resource.DataBlock;
            Debug.Assert(soundData != null);

            using var hash = SHA256.Create();
            using var sound = soundData.GetSoundStream();
            var actualHash = Convert.ToHexString(hash.ComputeHash(sound));

            Assert.That(actualHash, Is.EqualTo("1F8BF83F3E827A3C02C6AE6B6BD23BBEBD4E18C4F877D092CF0C5B800DAAB2B7"));
        }
    }
}
