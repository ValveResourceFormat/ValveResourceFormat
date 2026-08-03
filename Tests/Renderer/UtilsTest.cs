using System.Numerics;
using NUnit.Framework;
using ValveResourceFormat.Renderer.Utils;

namespace Tests.Renderer
{
    public class UtilsTest
    {
        [Test]
        public void Color32Test()
        {
            var fromFloats = new Color32(1f, 0.5f, 0f, 1f);
            var fromBytes = new Color32((byte)255, (byte)128, (byte)0, (byte)255);

            Assert.That(fromFloats, Is.EqualTo(fromBytes));

            // Float sRGB conversions of 1.0 commonly produce the value just below 1.0
            // (e.g. pow(1, 1/2.4) * 1.055f - 0.055f == 0.99999994f); it must still pack to 255.
            var nearOne = 0.99999994f;
            var white = Color32.FromVector4(new Vector4(nearOne, nearOne, nearOne, 1f));

            Assert.That(white.PackedValue, Is.EqualTo(0xFFFFFFFFu));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(new Color32(0f, 0f, 0f, 0f).PackedValue, Is.EqualTo(0u));
                Assert.That(new Color32(1f, 1f, 1f, 1f).PackedValue, Is.EqualTo(0xFFFFFFFFu));

                // 0.5 * 255 = 127.5, rounds up to 128
                Assert.That(new Color32(0.5f, 0.5f, 0.5f, 0.5f).R, Is.EqualTo(128));

                // Just below and above a midpoint
                Assert.That(new Color32(127.4f / 255f, 0f, 0f, 1f).R, Is.EqualTo(127));
                Assert.That(new Color32(127.6f / 255f, 0f, 0f, 1f).R, Is.EqualTo(128));
            }

        }
    }
}
