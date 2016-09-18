using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    [TestFixture]
    public class Test
    {
        // TODO: Add asserts for blocks/resources that were skipped

        [Test]
        public void ReadBlocks()
        {
            var resources = new Dictionary<string, Resource>();
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files");
            var files = Directory.GetFiles(path, "*.*_c");

            if (files.Length == 0)
            {
                Assert.Fail("There are no files to test.");
            }

            foreach (var file in files)
            {
                var resource = new Resource();
                resource.Read(file);

                resources.Add(Path.GetFileName(file), resource);

                Assert.AreNotEqual(ResourceType.Unknown, resource.ResourceType);

                // Verify extension
                var extension = Path.GetExtension(file);

                if (extension.EndsWith("_c", StringComparison.Ordinal))
                {
                    extension = extension.Substring(0, extension.Length - 2);
                }

                var type = typeof(ResourceType).GetMember(resource.ResourceType.ToString()).First();
                var attribute = "." + ((ExtensionAttribute)type.GetCustomAttributes(typeof(ExtensionAttribute), false).First()).Extension;

                Assert.AreEqual(extension, attribute, file);
            }

            VerifyResources(resources);
        }

        [Test]
        public void ReadBlocksWithMemoryStream()
        {
            var resources = new Dictionary<string, Resource>();
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files");
            var files = Directory.GetFiles(path, "*.*_c");

            if (files.Length == 0)
            {
                Assert.Fail("There are no files to test.");
            }

            foreach (var file in files)
            {
                var resource = new Resource();

                var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
                var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Seek(0, SeekOrigin.Begin);

                resource.Read(ms);

                resources.Add(Path.GetFileName(file), resource);
            }

            VerifyResources(resources);
        }

        private void VerifyResources(Dictionary<string, Resource> resources)
        {
            SoundWavCorrectlyExports(resources["beep.vsnd_c"]);

            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "ValidOutput");
            var files = Directory.GetFiles(path, "*.*txt", SearchOption.AllDirectories);
            var exceptions = new StringBuilder();

            foreach (var file in files)
            {
                var name = Path.GetFileName(Path.GetDirectoryName(file));

                if (!resources.ContainsKey(name))
                {
                    Assert.Fail("{0}: no such resource", name);

                    continue;
                }

                var resource = resources[name];
                var blockName = Path.GetFileNameWithoutExtension(file);

                BlockType blockType;
                Enum.TryParse(blockName, false, out blockType);

                if (!resource.Blocks.ContainsKey(blockType))
                {
                    Assert.Fail("{0}: no such block: {1}", name, blockType);

                    continue;
                }

                var actualOutput = resource.Blocks[blockType].ToString();
                var expectedOutput = File.ReadAllText(file);

                // We don't care about Valve's messy whitespace, so just strip it.
                actualOutput = Regex.Replace(actualOutput, @"\s+", string.Empty);
                expectedOutput = Regex.Replace(expectedOutput, @"\s+", string.Empty);

                try
                {
                    Assert.AreEqual(expectedOutput, actualOutput);
                }
                catch (AssertionException e)
                {
                    exceptions.AppendLine("File: " + file);
                    exceptions.AppendLine(e + Environment.NewLine);
                }
            }

            if (exceptions.Length > 0)
            {
                throw new AssertionException(exceptions.ToString());
            }
        }

        private void SoundWavCorrectlyExports(Resource resource)
        {
            Assert.AreEqual(ResourceType.Sound, resource.ResourceType);

            using (var sha1 = new SHA1CryptoServiceProvider())
            {
                var sound = ((Sound)resource.Blocks[BlockType.DATA]).GetSound();
                var actualHash = BitConverter.ToString(sha1.ComputeHash(sound)).Replace("-", "");

                Assert.AreEqual("59AC27F1A4395D8D02E4B3ADAA99023F243C8B41", actualHash);
            }
        }

        [Test]
        public void InvalidResourceThrows()
        {
            using (var resource = new Resource())
            {
                using (var ms = new MemoryStream(Enumerable.Repeat<byte>(1, 12).ToArray()))
                {
                    Assert.Throws<InvalidDataException>(() => resource.Read(ms));
                }
            }
        }

        [Test]
        public void PackageInResourceThrows()
        {
            var data = new byte[] { 0x34, 0x12, 0xAA, 0x55, 0x00, 0x00 };

            using (var resource = new Resource())
            {
                using (var ms = new MemoryStream(data))
                {
                    var ex = Assert.Throws<InvalidDataException>(() => resource.Read(ms));

                    Assert.That(ex.Message, Does.Contain("Use ValvePak"));
                }
            }
        }

        [Test]
        public void CompiledShaderInResourceThrows()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Shaders", "error_pcgl_40_ps.vcs");

            using (var resource = new Resource())
            {
                var ex = Assert.Throws<InvalidDataException>(() => resource.Read(path));

                Assert.That(ex.Message, Does.Contain("Use CompiledShader"));
            }
        }
    }
}
