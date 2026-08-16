using System.Numerics;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Utils;

namespace Tests.Renderer
{
    public class UtilsTest
    {
        [Test]
        public async Task Color32Test()
        {
            var fromFloats = new Color32(1f, 0.5f, 0f, 1f);
            var fromBytes = new Color32((byte)255, (byte)128, (byte)0, (byte)255);

            await Assert.That(fromFloats).IsEqualTo(fromBytes);

            // Float sRGB conversions of 1.0 commonly produce the value just below 1.0
            // (e.g. pow(1, 1/2.4) * 1.055f - 0.055f == 0.99999994f); it must still pack to 255.
            var nearOne = 0.99999994f;
            var white = Color32.FromVector4(new Vector4(nearOne, nearOne, nearOne, 1f));

            await Assert.That(white.PackedValue).IsEqualTo(0xFFFFFFFFu);

            using (Assert.Multiple())
            {
                await Assert.That(new Color32(0f, 0f, 0f, 0f).PackedValue).IsEqualTo(0u);
                await Assert.That(new Color32(1f, 1f, 1f, 1f).PackedValue).IsEqualTo(0xFFFFFFFFu);

                // 0.5 * 255 = 127.5, rounds up to 128
                await Assert.That(new Color32(0.5f, 0.5f, 0.5f, 0.5f).R).IsEqualTo((byte)128);

                // Just below and above a midpoint
                await Assert.That(new Color32(127.4f / 255f, 0f, 0f, 1f).R).IsEqualTo((byte)127);
                await Assert.That(new Color32(127.6f / 255f, 0f, 0f, 1f).R).IsEqualTo((byte)128);
            }

        }
    }
}
