using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;

namespace Tests
{
    public class TextureTests
    {
        private static string TexturesDir
            => Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Textures");

        private static IEnumerable<string> GetTextureFiles()
            => Directory.EnumerateFiles(TexturesDir, "*.vtex_c").Select(Path.GetFileName)!;

        [Test, TestCaseSource(nameof(GetTextureFiles))]
        public void ExportTexture(string fileName)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, fileName));

            var texture = (Texture?)resource.DataBlock;
            Debug.Assert(texture != null);

            using var _ = texture.GenerateBitmap();

            if (texture.IsHighDynamicRange)
            {
                using var __ = texture.GenerateBitmap(decodeFlags: TextureCodec.ForceLDR);
            }
        }

        [Test]
        public void SpriteSheetRectsCoverTheInclusiveTexelRange()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "DXT5_lava_drops_sheet.vtex_c"));

            var texture = (Texture?)resource.DataBlock;
            Debug.Assert(texture != null);

            var spriteSheet = texture.GetSpriteSheetData();
            Debug.Assert(spriteSheet != null);

            Assert.That(spriteSheet.Sequences, Has.Length.EqualTo(4));

            // This sheet is a 2x2 grid of 16x16 cells in a 32x32 texture, authored at 64x64.
            // The uncropped UVs of the first cell are 0.25 and 15.75 texels, which is texel 0 up to and including texel 15.
            var firstImage = spriteSheet.Sequences[0].Frames[0].Images[0];
            var lastImage = spriteSheet.Sequences[3].Frames[0].Images[0];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstImage.GetUncroppedRect(texture.ActualWidth, texture.ActualHeight), Is.EqualTo(new SKRectI(0, 0, 16, 16)));
                Assert.That(firstImage.GetCroppedRect(texture.ActualWidth, texture.ActualHeight), Is.EqualTo(new SKRectI(0, 4, 16, 12)));
                Assert.That(lastImage.GetUncroppedRect(texture.ActualWidth, texture.ActualHeight), Is.EqualTo(new SKRectI(16, 16, 32, 32)));
                Assert.That(lastImage.GetCroppedRect(texture.ActualWidth, texture.ActualHeight), Is.EqualTo(new SKRectI(16, 18, 32, 30)));
            }
        }

        [Test]
        public void SpriteSheetExtractsOneSpritePerSequence()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "DXT5_lava_drops_sheet.vtex_c"));

            var extract = new TextureExtract(resource);

            Assert.That(extract.TryGetMksData(out var sprites, out var mks), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sprites, Has.Count.EqualTo(4));
                Assert.That(sprites.Keys, Is.All.Matches<SKRectI>(rect => rect.Width == 16));
                Assert.That(mks, Does.Contain("frame DXT5_lava_drops_sheet_seq0.png 1"));
            }
        }

        [Test]
        public void Undo_YCoCg_TransformsColorCorrectly()
        {
            // Pure matrix: neutral chroma (Co == Cg == 128) reconstructs to neutral grey. The sRGB
            // linearization of the inputs is the caller's job (ApplyTextureConversions), see issue #1127.
            var rgb = Common.Decode_YCoCg(new Vector4(128, 128, 8, 128) / 255f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Common.ToClampedLdrColor(rgb.X), Is.EqualTo(128));
                Assert.That(Common.ToClampedLdrColor(rgb.Y), Is.EqualTo(128));
                Assert.That(Common.ToClampedLdrColor(rgb.Z), Is.EqualTo(128));
            }
        }

        [Test]
        public void ApplyTextureConversions_YCoCg_WithColorSpaceSrgb_LinearizesInputsBeforeMatrix()
        {
            using var bitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            bitmap.SetPixel(0, 0, new SKColor(red: 128, green: 128, blue: 8, alpha: 128)); // Co, Cg, scale, Y

            Common.ApplyTextureConversions(bitmap, TextureCodec.YCoCg | TextureCodec.ColorSpaceSrgb);

            var result = bitmap.GetPixel(0, 0);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Red, Is.EqualTo(128));
                Assert.That(result.Green, Is.EqualTo(60));
                Assert.That(result.Blue, Is.EqualTo(255));
                Assert.That(result.Alpha, Is.EqualTo(255));
            }
        }

        [Test]
        public void ApplyTextureConversions_YCoCg_AppliesRawMatrixWithoutSrgbFlag()
        {
            using var bitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            bitmap.SetPixel(0, 0, new SKColor(red: 128, green: 128, blue: 8, alpha: 128)); // Co, Cg, scale, Y

            Common.ApplyTextureConversions(bitmap, TextureCodec.YCoCg);

            var result = bitmap.GetPixel(0, 0);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Red, Is.EqualTo(128));
                Assert.That(result.Green, Is.EqualTo(128));
                Assert.That(result.Blue, Is.EqualTo(128));
                Assert.That(result.Alpha, Is.EqualTo(255));
            }
        }

        [Test]
        public void Undo_NormalizeNormals_TransformsColorCorrectly()
        {
            var color = new Color { r = 128, g = 128, b = 0, a = 255 };

            Common.ReconstructNormals(ref color);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(color.r, Is.EqualTo(128));
                Assert.That(color.g, Is.EqualTo(128));
                Assert.That(color.b, Is.EqualTo(255));
                Assert.That(color.a, Is.EqualTo(255));
            }
        }

        [Test]
        public void ClampColor_ReturnsValueWhenInRange()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Common.ClampColor(0), Is.Zero);
                Assert.That(Common.ClampColor(128), Is.EqualTo(128));
                Assert.That(Common.ClampColor(255), Is.EqualTo(255));
            }
        }

        [Test]
        public void ClampColor_ClampsOutOfRangeValues()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Common.ClampColor(-10), Is.Zero);
                Assert.That(Common.ClampColor(300), Is.EqualTo(255));
            }
        }

        [Test]
        public void ClampHighRangeColor_ReturnsValueWhenInRange()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Common.ClampHighRangeColor(0f), Is.Zero);
                Assert.That(Common.ClampHighRangeColor(0.5f), Is.EqualTo(0.5f));
                Assert.That(Common.ClampHighRangeColor(1f), Is.EqualTo(1f));
            }
        }

        [Test]
        public void ClampHighRangeColor_ClampsOutOfRangeValues()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Common.ClampHighRangeColor(-0.5f), Is.Zero);
                Assert.That(Common.ClampHighRangeColor(1.5f), Is.EqualTo(1f));
            }
        }

        [Test]
        public void ToClampedLdrColor_ConvertsFloatToByte()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Common.ToClampedLdrColor(0f), Is.Zero);
                Assert.That(Common.ToClampedLdrColor(0.5f), Is.EqualTo(128));
                Assert.That(Common.ToClampedLdrColor(1f), Is.EqualTo(255));
                Assert.That(Common.ToClampedLdrColor(2f), Is.EqualTo(255));
            }
        }

        [Test]
        public void SwapRB_SwapsRedAndBlueChannels()
        {
            var pixels = new byte[] {
                1, 2, 3, 4,
                5, 6, 7, 8
            };

            Common.SwapRB(pixels);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(pixels[0], Is.EqualTo(3));
                Assert.That(pixels[1], Is.EqualTo(2));
                Assert.That(pixels[2], Is.EqualTo(1));
                Assert.That(pixels[3], Is.EqualTo(4));

                Assert.That(pixels[4], Is.EqualTo(7));
                Assert.That(pixels[5], Is.EqualTo(6));
                Assert.That(pixels[6], Is.EqualTo(5));
                Assert.That(pixels[7], Is.EqualTo(8));
            }
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
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(pixels[i * 4], Is.EqualTo((byte)((i >> 8) & 0xFF)));
                    Assert.That(pixels[i * 4 + 1], Is.Zero);
                    Assert.That(pixels[i * 4 + 2], Is.EqualTo((byte)(i & 0xFF)));
                    Assert.That(pixels[i * 4 + 3], Is.EqualTo(255));
                }
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
