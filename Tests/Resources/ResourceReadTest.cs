using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.Utils;

namespace Tests.Resources
{
    [ExecutionPriority(TUnit.Core.Enums.Priority.High)]
    public class ResourceReadTest
    {
        // TODO: Add asserts for blocks/resources that were skipped
        [Test]
        [MethodDataSource(typeof(TestFixtures), nameof(TestFixtures.CompiledFiles))]
        public async Task ReadBlocks(string relativePath)
        {
            using var resource = TestFixtures.Load(relativePath);

            await ResourceVerify.Parsed(resource, TestFixtures.Path(relativePath));
        }

        [Test]
        [MethodDataSource(typeof(TestFixtures), nameof(TestFixtures.TopLevelCompiledFiles))]
        public async Task ReadBlocksWithMemoryStream(string relativePath)
        {
            var file = TestFixtures.Path(relativePath);
            using var resource = new Resource
            {
                FileName = file,
            };

            await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var ms = new MemoryStream();
            await fs.CopyToAsync(ms);
            ms.Seek(0, SeekOrigin.Begin);

            resource.Read(ms);

            await ResourceVerify.DataBlock(resource, file);
        }

        [Test]
        [MethodDataSource(typeof(TestFixtures), nameof(TestFixtures.TopLevelCompiledFiles))]
        public async Task ReadBlocksNoFileName(string relativePath)
        {
            var file = TestFixtures.Path(relativePath);
            using var resource = new Resource();
            await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            resource.Read(fs);

            await ResourceVerify.DataBlock(resource, file);
        }

        [Test]
        public void InvalidResourceThrows()
        {
            using var resource = new Resource();
            using var ms = new MemoryStream(Enumerable.Repeat<byte>(1, 12).ToArray());

            Assert.ThrowsExactly<UnexpectedMagicException>(() => resource.Read(ms));
        }

        [Test]
        public async Task PackageInResourceThrows()
        {
            var data = new byte[] { 0x34, 0x12, 0xAA, 0x55, 0x00, 0x00 };

            using var resource = new Resource();
            using var ms = new MemoryStream(data);

            var ex = Assert.ThrowsExactly<InvalidDataException>(() => resource.Read(ms));

            await Assert.That(ex!.Message).Contains("Use ValvePak");
        }
    }
}
