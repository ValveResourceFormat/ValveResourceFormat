using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
            => Path.Combine(TestContext.TestDirectory!, "Files", "Textures");

        public static IEnumerable<string> GetTextureFiles()
            => Directory.EnumerateFiles(TexturesDir, "*.vtex_c").Select(Path.GetFileName)!;

        [Test, MethodDataSource(nameof(GetTextureFiles))]
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
        public async Task SpriteSheetRectsCoverTheInclusiveTexelRange()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "DXT5_lava_drops_sheet.vtex_c"));

            var texture = (Texture?)resource.DataBlock;
            Debug.Assert(texture != null);

            var spriteSheet = texture.GetSpriteSheetData();
            Debug.Assert(spriteSheet != null);
            await Assert.That(spriteSheet.Sequences).Count().IsEqualTo(4);

            // This sheet is a 2x2 grid of 16x16 cells in a 32x32 texture, authored at 64x64.
            // The uncropped UVs of the first cell are 0.25 and 15.75 texels, which is texel 0 up to and including texel 15.
            var firstImage = spriteSheet.Sequences[0].Frames[0].Images[0];
            var lastImage = spriteSheet.Sequences[3].Frames[0].Images[0];

            using (Assert.Multiple())
            {
                await Assert.That(firstImage.GetUncroppedRect(texture.ActualWidth, texture.ActualHeight)).IsEqualTo(new SKRectI(0, 0, 16, 16));
                await Assert.That(firstImage.GetCroppedRect(texture.ActualWidth, texture.ActualHeight)).IsEqualTo(new SKRectI(0, 4, 16, 12));
                await Assert.That(lastImage.GetUncroppedRect(texture.ActualWidth, texture.ActualHeight)).IsEqualTo(new SKRectI(16, 16, 32, 32));
                await Assert.That(lastImage.GetCroppedRect(texture.ActualWidth, texture.ActualHeight)).IsEqualTo(new SKRectI(16, 18, 32, 30));
            }
        }

        [Test]
        public async Task SpriteSheetExtractsOneSpritePerSequence()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "DXT5_lava_drops_sheet.vtex_c"));

            var extract = new TextureExtract(resource);

            await Assert.That(extract.TryGetMksData(out var sprites, out var mks)).IsTrue();

            using (Assert.Multiple())
            {
                await Assert.That(sprites).Count().IsEqualTo(4);
                await Assert.That(sprites.Keys).All(rect => rect.Width == 16);
                await Assert.That(mks).Contains("frame DXT5_lava_drops_sheet_seq0.png 1");
            }
        }

        [Test]
        public async Task Undo_YCoCg_TransformsColorCorrectly()
        {
            // Pure matrix: neutral chroma (Co == Cg == 128) reconstructs to neutral grey. The sRGB
            // linearization of the inputs is the caller's job (ApplyTextureConversions), see issue #1127.
            var rgb = Common.Decode_YCoCg(new Vector4(128, 128, 8, 128) / 255f);

            using (Assert.Multiple())
            {
                await Assert.That(Common.ToClampedLdrColor(rgb.X)).IsEqualTo((byte)128);
                await Assert.That(Common.ToClampedLdrColor(rgb.Y)).IsEqualTo((byte)128);
                await Assert.That(Common.ToClampedLdrColor(rgb.Z)).IsEqualTo((byte)128);
            }
        }

        [Test]
        public async Task ApplyTextureConversions_YCoCg_WithColorSpaceSrgb_LinearizesInputsBeforeMatrix()
        {
            using var bitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            bitmap.SetPixel(0, 0, new SKColor(red: 128, green: 128, blue: 8, alpha: 128)); // Co, Cg, scale, Y

            Common.ApplyTextureConversions(bitmap, TextureCodec.YCoCg | TextureCodec.ColorSpaceSrgb);

            var result = bitmap.GetPixel(0, 0);
            using (Assert.Multiple())
            {
                await Assert.That(result.Red).IsEqualTo((byte)128);
                await Assert.That(result.Green).IsEqualTo((byte)60);
                await Assert.That(result.Blue).IsEqualTo((byte)255);
                await Assert.That(result.Alpha).IsEqualTo((byte)255);
            }
        }

        [Test]
        public async Task ApplyTextureConversions_YCoCg_AppliesRawMatrixWithoutSrgbFlag()
        {
            using var bitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            bitmap.SetPixel(0, 0, new SKColor(red: 128, green: 128, blue: 8, alpha: 128)); // Co, Cg, scale, Y

            Common.ApplyTextureConversions(bitmap, TextureCodec.YCoCg);

            var result = bitmap.GetPixel(0, 0);
            using (Assert.Multiple())
            {
                await Assert.That(result.Red).IsEqualTo((byte)128);
                await Assert.That(result.Green).IsEqualTo((byte)128);
                await Assert.That(result.Blue).IsEqualTo((byte)128);
                await Assert.That(result.Alpha).IsEqualTo((byte)255);
            }
        }

        [Test]
        public async Task Undo_NormalizeNormals_TransformsColorCorrectly()
        {
            var color = new Color { r = 128, g = 128, b = 0, a = 255 };

            Common.ReconstructNormals(ref color);

            using (Assert.Multiple())
            {
                await Assert.That(color.r).IsEqualTo((byte)128);
                await Assert.That(color.g).IsEqualTo((byte)128);
                await Assert.That(color.b).IsEqualTo((byte)255);
                await Assert.That(color.a).IsEqualTo((byte)255);
            }
        }

        [Test]
        public async Task ClampColor_ReturnsValueWhenInRange()
        {
            using (Assert.Multiple())
            {
                await Assert.That(Common.ClampColor(0)).IsZero();
                await Assert.That(Common.ClampColor(128)).IsEqualTo((byte)128);
                await Assert.That(Common.ClampColor(255)).IsEqualTo((byte)255);
            }
        }

        [Test]
        public async Task ClampColor_ClampsOutOfRangeValues()
        {
            using (Assert.Multiple())
            {
                await Assert.That(Common.ClampColor(-10)).IsZero();
                await Assert.That(Common.ClampColor(300)).IsEqualTo((byte)255);
            }
        }

        [Test]
        public async Task ClampHighRangeColor_ReturnsValueWhenInRange()
        {
            using (Assert.Multiple())
            {
                await Assert.That(Common.ClampHighRangeColor(0f)).IsZero();
                await Assert.That(Common.ClampHighRangeColor(0.5f)).IsEqualTo(0.5f);
                await Assert.That(Common.ClampHighRangeColor(1f)).IsEqualTo(1f);
            }
        }

        [Test]
        public async Task ClampHighRangeColor_ClampsOutOfRangeValues()
        {
            using (Assert.Multiple())
            {
                await Assert.That(Common.ClampHighRangeColor(-0.5f)).IsZero();
                await Assert.That(Common.ClampHighRangeColor(1.5f)).IsEqualTo(1f);
            }
        }

        [Test]
        public async Task ToClampedLdrColor_ConvertsFloatToByte()
        {
            using (Assert.Multiple())
            {
                await Assert.That(Common.ToClampedLdrColor(0f)).IsZero();
                await Assert.That(Common.ToClampedLdrColor(0.5f)).IsEqualTo((byte)128);
                await Assert.That(Common.ToClampedLdrColor(1f)).IsEqualTo((byte)255);
                await Assert.That(Common.ToClampedLdrColor(2f)).IsEqualTo((byte)255);
            }
        }

        [Test]
        public async Task SwapRB_SwapsRedAndBlueChannels()
        {
            var pixels = new byte[] {
                1, 2, 3, 4,
                5, 6, 7, 8
            };

            Common.SwapRB(pixels);

            using (Assert.Multiple())
            {
                await Assert.That(pixels[0]).IsEqualTo((byte)3);
                await Assert.That(pixels[1]).IsEqualTo((byte)2);
                await Assert.That(pixels[2]).IsEqualTo((byte)1);
                await Assert.That(pixels[3]).IsEqualTo((byte)4);

                await Assert.That(pixels[4]).IsEqualTo((byte)7);
                await Assert.That(pixels[5]).IsEqualTo((byte)6);
                await Assert.That(pixels[6]).IsEqualTo((byte)5);
                await Assert.That(pixels[7]).IsEqualTo((byte)8);
            }
        }

        [Test]
        public async Task SwapRB_HandlesLargeArrays()
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
                using (Assert.Multiple())
                {
                    await Assert.That(pixels[i * 4]).IsEqualTo((byte)((i >> 8) & 0xFF));
                    await Assert.That(pixels[i * 4 + 1]).IsZero();
                    await Assert.That(pixels[i * 4 + 2]).IsEqualTo((byte)(i & 0xFF));
                    await Assert.That(pixels[i * 4 + 3]).IsEqualTo((byte)255);
                }
            }
        }

        [Test]
        public async Task SwapRedAlpha_SwapsColorsSimdAndScalar()
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
                using (Assert.Multiple())
                {
                    await Assert.That(pixels[i * 4 + 0]).IsEqualTo((byte)2);
                    await Assert.That(pixels[i * 4 + 1]).IsEqualTo((byte)3);
                    await Assert.That(pixels[i * 4 + 2]).IsEqualTo(AlphaColor(i));
                    await Assert.That(pixels[i * 4 + 3]).IsEqualTo(RedColor(i));
                }
            }
        }
    }
}
