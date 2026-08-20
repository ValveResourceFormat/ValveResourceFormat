using System.Threading.Tasks;
using ValveResourceFormat.Renderer;

namespace Tests.Renderer
{
    public class FramebufferTest
    {
        /// <summary>
        /// The default framebuffer is resized on every window resize. Its attachments come from the
        /// window rather than from the device, so the resize must not try to create any.
        /// </summary>
        [Test]
        public async Task DefaultFramebufferResizeCreatesNoAttachments()
        {
            var framebuffer = Framebuffer.GLDefaultFramebuffer;

            await Assert.That(framebuffer.Resize(320, 240)).IsTrue();

            using (Assert.Multiple())
            {
                await Assert.That(framebuffer.Width).IsEqualTo(320);
                await Assert.That(framebuffer.Height).IsEqualTo(240);
                await Assert.That(framebuffer.Color).IsNull();
                await Assert.That(framebuffer.Depth).IsNull();
            }

            // A second resize to the same size is a no-op, a different one runs the path again.
            await Assert.That(framebuffer.Resize(320, 240)).IsFalse();
            await Assert.That(framebuffer.Resize(640, 480)).IsTrue();
        }
    }
}
