using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;

namespace Tests
{
    public class TextureTests
    {
        private static string[] GetTextureFiles()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Textures");
            return Directory.GetFiles(path, "*.vtex_c");
        }

        [Test, TestCaseSource(nameof(GetTextureFiles))]
        public void ExportTexture(string file)
        {
            using var resource = new Resource();
            resource.Read(file);

            var texture = (Texture?)resource.DataBlock;
            Debug.Assert(texture != null);

            using var _ = texture.GenerateBitmap();

            if (texture.IsHighDynamicRange)
            {
                using var __ = texture.GenerateBitmap(decodeFlags: TextureCodec.ForceLDR);
            }
        }

        [Test]
        public void Undo_YCoCg_TransformsColorCorrectly()
        {
            var color = new Color { r = 128, g = 128, b = 8, a = 128 };

            Common.Decode_YCoCg(ref color);

            Assert.That(color.r, Is.EqualTo(128));
            Assert.That(color.g, Is.EqualTo(128));
            Assert.That(color.b, Is.EqualTo(128));
            Assert.That(color.a, Is.EqualTo(255));
        }

        [Test]
        public void Undo_NormalizeNormals_TransformsColorCorrectly()
        {
            var color = new Color { r = 128, g = 128, b = 0, a = 255 };

            Common.ReconstructNormals(ref color);

            Assert.That(color.r, Is.EqualTo(128));
            Assert.That(color.g, Is.EqualTo(128));
            Assert.That(color.b, Is.EqualTo(255));
            Assert.That(color.a, Is.EqualTo(255));
        }

        [Test]
        public void ClampColor_ReturnsValueWhenInRange()
        {
            Assert.That(Common.ClampColor(0), Is.Zero);
            Assert.That(Common.ClampColor(128), Is.EqualTo(128));
            Assert.That(Common.ClampColor(255), Is.EqualTo(255));
        }

        [Test]
        public void ClampColor_ClampsOutOfRangeValues()
        {
            Assert.That(Common.ClampColor(-10), Is.Zero);
            Assert.That(Common.ClampColor(300), Is.EqualTo(255));
        }

        [Test]
        public void ClampHighRangeColor_ReturnsValueWhenInRange()
        {
            Assert.That(Common.ClampHighRangeColor(0f), Is.Zero);
            Assert.That(Common.ClampHighRangeColor(0.5f), Is.EqualTo(0.5f));
            Assert.That(Common.ClampHighRangeColor(1f), Is.EqualTo(1f));
        }

        [Test]
        public void ClampHighRangeColor_ClampsOutOfRangeValues()
        {
            Assert.That(Common.ClampHighRangeColor(-0.5f), Is.Zero);
            Assert.That(Common.ClampHighRangeColor(1.5f), Is.EqualTo(1f));
        }

        [Test]
        public void ToClampedLdrColor_ConvertsFloatToByte()
        {
            Assert.That(Common.ToClampedLdrColor(0f), Is.Zero);
            Assert.That(Common.ToClampedLdrColor(0.5f), Is.EqualTo(128));
            Assert.That(Common.ToClampedLdrColor(1f), Is.EqualTo(255));
            Assert.That(Common.ToClampedLdrColor(2f), Is.EqualTo(255));
        }

        [Test]
        public void SwapRB_SwapsRedAndBlueChannels()
        {
            var pixels = new byte[] {
                1, 2, 3, 4,
                5, 6, 7, 8
            };

            Common.SwapRB(pixels);

            Assert.That(pixels[0], Is.EqualTo(3));
            Assert.That(pixels[1], Is.EqualTo(2));
            Assert.That(pixels[2], Is.EqualTo(1));
            Assert.That(pixels[3], Is.EqualTo(4));

            Assert.That(pixels[4], Is.EqualTo(7));
            Assert.That(pixels[5], Is.EqualTo(6));
            Assert.That(pixels[6], Is.EqualTo(5));
            Assert.That(pixels[7], Is.EqualTo(8));
        }

        [Test]
        public void SwapRB_HandlesLargeArrays()
        {
            const int pixelCount = 1101;
            var pixels = new byte[pixelCount * 4];

            for (var i = 0; i < pixelCount; i++)
            {
                pixels[i * 4] = (byte)(i & 0xFF);
                pixels[i * 4 + 1] = 0;
                pixels[i * 4 + 2] = (byte)((i >> 8) & 0xFF);
                pixels[i * 4 + 3] = 255;
            }

            Common.SwapRB(pixels);

            for (var i = 0; i < pixelCount; i++)
            {
                Assert.That(pixels[i * 4], Is.EqualTo((byte)((i >> 8) & 0xFF)));
                Assert.That(pixels[i * 4 + 1], Is.Zero);
                Assert.That(pixels[i * 4 + 2], Is.EqualTo((byte)(i & 0xFF)));
                Assert.That(pixels[i * 4 + 3], Is.EqualTo(255));
            }
        }

        [Test]
        public void SwapRedAlpha_SwapsColorsSimdAndScalar()
        {
            const int pixelCount = 1101;
            var pixels = new byte[pixelCount * 4];

            static byte RedColor(int i) => (byte)(i & 0xFF);
            static byte AlphaColor(int i) => (byte)(i & 0x0F);

            for (var i = 0; i < pixelCount; i++)
            {
                pixels[i * 4 + 0] = 2;
                pixels[i * 4 + 1] = 3;
                pixels[i * 4 + 2] = RedColor(i);
                pixels[i * 4 + 3] = AlphaColor(i);
            }

            Common.SwapRA(pixels);

            for (var i = 0; i < pixelCount; i++)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(pixels[i * 4 + 0], Is.EqualTo(2));
                    Assert.That(pixels[i * 4 + 1], Is.EqualTo(3));
                    Assert.That(pixels[i * 4 + 2], Is.EqualTo(AlphaColor(i)));
                    Assert.That(pixels[i * 4 + 3], Is.EqualTo(RedColor(i)));
                }
            }
        }
    }
}
