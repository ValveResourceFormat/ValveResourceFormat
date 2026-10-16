using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;

namespace Tests.Resources
{
    public class ResourceLifetimeTest
    {
        private const string Fixture = "empty_data.vjs_c";

        [Test]
        public async Task ResourceDisposesStreamWhenLeaveOpenFalse()
        {
            var testData = await File.ReadAllBytesAsync(TestFixtures.Path(Fixture));
            var resource = new Resource();
            using var testStream = new TestableMemoryStream(testData);

            resource.Read(testStream, leaveOpen: false);
            using (Assert.Multiple())
            {
                await Assert.That(testStream.IsDisposed).IsFalse();
                await Assert.That(resource.Reader).IsNotNull();
            }
            resource.Dispose();
            using (Assert.Multiple())
            {
                await Assert.That(testStream.IsDisposed).IsTrue();
                await Assert.That(resource.Reader).IsNull();
            }
        }

        [Test]
        public async Task ResourceDoesNotDisposeStreamWhenLeaveOpenTrue()
        {
            var testData = await File.ReadAllBytesAsync(TestFixtures.Path(Fixture));
            var resource = new Resource();
            using var testStream = new TestableMemoryStream(testData);

            resource.Read(testStream, leaveOpen: true);
            await Assert.That(resource.Reader).IsNotNull();
            resource.Dispose();
            using (Assert.Multiple())
            {
                await Assert.That(testStream.IsDisposed).IsFalse();
                await Assert.That(resource.Reader).IsNull();
            }
            await testStream.DisposeAsync();
            await Assert.That(testStream.IsDisposed).IsTrue();
        }

        [Test]
        public async Task ResourceDisposesFileStreamFromFilename()
        {
            var resource = new Resource();
            resource.Read(TestFixtures.Path(Fixture));
            await Assert.That(resource.Reader).IsNotNull();
            resource.Dispose();
            await Assert.That(resource.Reader).IsNull();
        }

        private sealed class TestableMemoryStream(byte[] buffer) : MemoryStream(buffer)
        {
            public bool IsDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
