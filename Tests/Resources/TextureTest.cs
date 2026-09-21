using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;

namespace Tests.Resources
{
    public class TextureTest
    {
        private static string TexturesDir => TestFixtures.Path("Textures");

        public static IEnumerable<string> GetTextureFiles() => TestFixtures.FilesIn("Textures", "*.vtex_c");

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

            if (texture.IsRawAnyImage)
            {
                return;
            }

            for (var mipLevel = 1u; mipLevel < texture.NumMipLevels; mipLevel++)
            {
                using var ___ = texture.GenerateBitmap(mipLevel: mipLevel);
            }
        }

        [Test, MethodDataSource(nameof(GetTextureFiles))]
        public async Task InPlaceLz4MipReadMatchesScratchRead(string fileName)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, fileName));

            var texture = (Texture?)resource.DataBlock;
            Debug.Assert(texture != null);

            if (texture.IsRawAnyImage)
            {
                return; // png/jpeg blobs have no mip chain to read
            }

            // todo: IsActuallyCompressedMips gate

            for (var mipLevel = 0u; mipLevel < texture.NumMipLevels; mipLevel++)
            {
                var size = texture.CalculateBufferSizeForMipLevel(mipLevel);
                var expected = new byte[size];
                texture.ReadTextureMipLevel(expected, mipLevel);

                // Oversized like a pooled rent would be, so the margin math is exercised realistically
                var inPlace = new byte[texture.CalculateInPlaceDecompressionBufferSize(mipLevel) + 3];
                texture.ReadTextureMipLevelInPlace(inPlace, mipLevel);

                var matches = inPlace.AsSpan(0, size).SequenceEqual(expected);
                await Assert.That(matches).IsTrue();
            }
        }

        [Test]
        public async Task R11EacDecodesToGreyscale()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "R11_EAC_gradient.vtex_c"));

            var texture = (Texture?)resource.DataBlock;
            Debug.Assert(texture != null);

            using var bitmap = texture.GenerateBitmap();

            using (Assert.Multiple())
            {
                await Assert.That(texture.Format).IsEqualTo(VTexFormat.R11_EAC);
                await Assert.That(texture.NumMipLevels).IsEqualTo((byte)5);

                await Assert.That(bitmap.GetPixel(0, 0)).IsEqualTo(new SKColor(0, 0, 0));
                await Assert.That(bitmap.GetPixel(63, 63)).IsEqualTo(new SKColor(255, 255, 255));
                await Assert.That(bitmap.GetPixel(44, 22)).IsEqualTo(new SKColor(242, 242, 242));
                await Assert.That(bitmap.GetPixel(32, 50)).IsEqualTo(new SKColor(13, 13, 13));
            }
        }

        [Test]
        public async Task RG11EacDecodesRedAndGreenIndependently()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "RG11_EAC_gradient.vtex_c"));

            var texture = (Texture?)resource.DataBlock;
            Debug.Assert(texture != null);

            using var bitmap = texture.GenerateBitmap();

            using (Assert.Multiple())
            {
                await Assert.That(texture.Format).IsEqualTo(VTexFormat.RG11_EAC);

                await Assert.That(bitmap.GetPixel(0, 0)).IsEqualTo(new SKColor(0, 255, 0));
                await Assert.That(bitmap.GetPixel(63, 63)).IsEqualTo(new SKColor(255, 0, 0));
                await Assert.That(bitmap.GetPixel(44, 22)).IsEqualTo(new SKColor(242, 77, 0));
                await Assert.That(bitmap.GetPixel(32, 50)).IsEqualTo(new SKColor(13, 5, 0));
            }
        }

        [Test]
        public async Task EacDecodesMipsSmallerThanOneBlock()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "RG11_EAC_nonaligned.vtex_c"));

            var texture = (Texture?)resource.DataBlock;
            Debug.Assert(texture != null);

            using var smallest = texture.GenerateBitmap(mipLevel: 5);

            using (Assert.Multiple())
            {
                await Assert.That(texture.Width).IsEqualTo((ushort)36);
                await Assert.That(texture.Height).IsEqualTo((ushort)20);
                await Assert.That(smallest.Width).IsEqualTo(1);
                await Assert.That(smallest.Height).IsEqualTo(1);
                await Assert.That(smallest.GetPixel(0, 0)).IsEqualTo(new SKColor(111, 97, 0));
            }
        }

        private static byte[] BuildEacBlock(int baseCodeword, int multiplier, int table, ReadOnlySpan<int> indices)
        {
            var block = new byte[8];
            block[0] = (byte)baseCodeword;
            block[1] = (byte)(multiplier << 4 | table);

            ulong bits = 0;

            for (var i = 0; i < 16; i++)
            {
                bits = bits << 3 | (uint)indices[i];
            }

            for (var i = 0; i < 6; i++)
            {
                block[2 + i] = (byte)(bits >> (40 - i * 8));
            }

            return block;
        }

        [Test]
        public async Task R11EacMatchesEtc2AlphaDecodeWhenMultiplierIsSet()
        {
            int[] indices = [0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0];
            int[] baseCodewords = [0, 37, 128, 200, 255];

            using var red = new SKBitmap(4, 4, Texture.DefaultBitmapColorType, SKAlphaType.Unpremul);
            using var alpha = new SKBitmap(4, 4, Texture.DefaultBitmapColorType, SKAlphaType.Unpremul);

            var etc2Block = new byte[16];
            var mismatches = 0;

            foreach (var baseCodeword in baseCodewords)
            {
                for (var multiplier = 1; multiplier < 16; multiplier++)
                {
                    for (var table = 0; table < 16; table++)
                    {
                        var block = BuildEacBlock(baseCodeword, multiplier, table, indices);
                        block.CopyTo(etc2Block, 0);

                        new DecodeR11EAC(4, 4).Decode(red, block);
                        new DecodeETC2EAC(4, 4).Decode(alpha, etc2Block);

                        for (var y = 0; y < 4; y++)
                        {
                            for (var x = 0; x < 4; x++)
                            {
                                if (red.GetPixel(x, y).Red != alpha.GetPixel(x, y).Alpha)
                                {
                                    mismatches++;
                                }
                            }
                        }
                    }
                }
            }

            await Assert.That(mismatches).IsZero();
        }

        [Test]
        public async Task R11EacStepsByOneWhenMultiplierIsZero()
        {
            int[] indices = [0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3, 4, 5, 6, 7];
            var block = BuildEacBlock(100, 0, 0, indices);

            using var bitmap = new SKBitmap(4, 4, Texture.DefaultBitmapColorType, SKAlphaType.Unpremul);
            new DecodeR11EAC(4, 4).Decode(bitmap, block);

            using (Assert.Multiple())
            {
                await Assert.That(bitmap.GetPixel(0, 0).Red).IsEqualTo((byte)100);
                await Assert.That(bitmap.GetPixel(0, 3).Red).IsEqualTo((byte)98);
                await Assert.That(bitmap.GetPixel(1, 0).Red).IsEqualTo((byte)100);
                await Assert.That(bitmap.GetPixel(1, 3).Red).IsEqualTo((byte)102);
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
                await Assert.That(sprites.Keys).All(sprite => sprite.Image.Width == 16 && sprite.Image.Height == 16);
                await Assert.That(sprites.Keys).All(sprite => !sprite.Cropped && sprite.Pixels != sprite.Image);
                await Assert.That(mks).Contains("frame DXT5_lava_drops_sheet_seq0.png 1");
                await Assert.That(mks).Contains("LOOP");
                await Assert.That(mks.Split('\n').Any(line => line.StartsWith("name ", StringComparison.Ordinal))).IsFalse();
                await Assert.That(mks).DoesNotContain("alphacrop");
            }
        }

        [Test]
        public async Task SpriteSheetMksKeepsAlphaCroppedSequences()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "sheet_alphacrop.vtex_c"));

            var extract = new TextureExtract(resource);

            await Assert.That(extract.TryGetMksData(out var sprites, out var mks)).IsTrue();

            var lines = mks.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var cropped = sprites.Keys.Single(sprite => sprite.Cropped);

            using (Assert.Multiple())
            {
                await Assert.That(lines).Contains("sequence 0 alphacrop");
                await Assert.That(lines).Contains("name cropped_seq");
                await Assert.That(lines).Contains("sequence 1");
                await Assert.That(lines).Contains("name whole_seq");
                await Assert.That(cropped.Image.Width).IsGreaterThan(cropped.Pixels.Width);
                await Assert.That(cropped.Image.Height).IsGreaterThan(cropped.Pixels.Height);
            }
        }

        [Test]
        public async Task SpriteSheetMksKeepsTimingAndDecalParams()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "sheet_clamp_decal.vtex_c"));

            var extract = new TextureExtract(resource);

            await Assert.That(extract.TryGetMksData(out _, out var mks)).IsTrue();

            var lines = mks.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var held = Array.IndexOf(lines, "sequence 0");
            var looped = Array.IndexOf(lines, "sequence 1");

            using (Assert.Multiple())
            {
                await Assert.That(lines[held + 2]).IsEqualTo("clamp-extendlastframe");
                await Assert.That(lines).Contains("decalScale 0.2");
                await Assert.That(lines).Contains("animationScale 5");
                await Assert.That(lines[held + 6]).EndsWith(" 2");
                await Assert.That(lines[looped + 2]).IsEqualTo("LOOP");
                await Assert.That(lines[looped + 3]).EndsWith(" 0");
            }
        }

        [Test]
        public async Task SpriteSheetMksKeepsSequenceNames()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TexturesDir, "sheet_named_sequences.vtex_c"));

            var extract = new TextureExtract(resource);

            await Assert.That(extract.TryGetMksData(out _, out var mks)).IsTrue();

            var lines = mks.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            using (Assert.Multiple())
            {
                await Assert.That(lines).Contains("name embers_loop");
                await Assert.That(lines).Contains("name embers_burst");
                await Assert.That(lines).Contains("name 2");
                await Assert.That(lines[Array.IndexOf(lines, "sequence 0") + 1]).IsEqualTo("name embers_loop");
                await Assert.That(lines[Array.IndexOf(lines, "sequence 0") + 2]).IsEqualTo("LOOP");
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
