using System.IO;
using NUnit.Framework;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ChannelMapping = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace Tests
{
    public class MaterialExtractTest
    {
        private static Material GetMockMaterial(bool translucent)
        {
            var mockMaterial = new Material()
            {
                ShaderName = "vr_complex.vfx",
            };

            mockMaterial.IntParams["F_TRANSLUCENT"] = translucent ? 1 : 0;
            return mockMaterial;
        }

        [Test]
        public void TextureInputsForFeatureState([Values] bool translucent)
        {
            var vr_complex_expected_inputs = new[] {
                (ChannelMapping.RGB, "TextureColor"),
                (ChannelMapping.A, translucent ? "TextureTranslucency" : "TextureMetalness")
            };

            var result = new BasicShaderDataProvider().GetInputsForTexture("g_tColor", GetMockMaterial(translucent));
            Assert.That(result, Is.EquivalentTo(vr_complex_expected_inputs));
        }

        [Test]
        public void TextureInputPaths([Values] bool translucent)
        {
            var vr_complex_expected_inputs = new[] {
                new MaterialExtract.UnpackInfo()
                {
                    TextureType = "TextureColor",
                    FileName = "test_color.png",
                    Channel = ChannelMapping.RGB
                },
                new MaterialExtract.UnpackInfo()
                {
                    TextureType = translucent ? "TextureTranslucency" : "TextureMetalness",
                    FileName = translucent ? "test_65b7aff5_trans.png" : "test_65b7aff5_metal.png",
                    Channel = ChannelMapping.A
                }
            };

            var result = new MaterialExtract(GetMockMaterial(translucent), null, null, new BasicShaderDataProvider())
                .GetTextureUnpackInfos("g_tColor", "test_color_jpg_65b7aff5.vtex", null, false, false);
            Assert.That(result, Is.EquivalentTo(vr_complex_expected_inputs));
        }

        [Test]
        public void TestVmatExtract()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "point_worldtext_default.vmat_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var materialExtract = new MaterialExtract(resource);

            Assert.That(materialExtract.ToValveMaterial(), Is.Not.Empty);
        }

        private static IEnumerable<TestCaseData> PngImageChannelsSource()
        {
            var c1234 = new SKColor(1, 2, 3, 4);
            yield return new TestCaseData(c1234, ChannelMapping.R, new SKColor(1, 1, 1));
            yield return new TestCaseData(c1234, ChannelMapping.G, new SKColor(2, 2, 2));
            yield return new TestCaseData(c1234, ChannelMapping.B, new SKColor(3, 3, 3));
            yield return new TestCaseData(c1234, ChannelMapping.A, new SKColor(4, 4, 4, 255));

            yield return new TestCaseData(c1234, ChannelMapping.RG, new SKColor(1, 2, 3, 255));
            yield return new TestCaseData(c1234, ChannelMapping.RGB, new SKColor(1, 2, 3, 255));

            yield return new TestCaseData(c1234, ChannelMapping.AG, new SKColor(4, 2, 0));

            yield return new TestCaseData(c1234, ChannelMapping.RGBA, c1234);

            yield return new TestCaseData(c1234, ChannelMapping.NULL, SKColors.Black);

            yield return new TestCaseData(
                new SKColor(1, 2, 3, 4),
                ChannelMapping.FromChannels(1, 2, 0), // GBR
                new SKColor(2, 3, 1)
            );

            yield return new TestCaseData(
                new SKColor(1, 2, 3, 4),
                ChannelMapping.FromChannels(1, 2, 0, 3), // GBRA
                new SKColor(2, 3, 1, 4)
            );

            var alpha0 = new SKColor(1, 2, 3, 0);
            yield return new TestCaseData(alpha0, ChannelMapping.RGBA, alpha0);
            yield return new TestCaseData(alpha0, ChannelMapping.R, new SKColor(1, 1, 1));
            yield return new TestCaseData(alpha0, ChannelMapping.G, new SKColor(2, 2, 2));
            yield return new TestCaseData(alpha0, ChannelMapping.B, new SKColor(3, 3, 3));
            yield return new TestCaseData(alpha0, ChannelMapping.A, new SKColor(0, 0, 0, 255));
        }

        [Test, TestCaseSource(nameof(PngImageChannelsSource))]
        public void TestPngImageChannels(SKColor colorIn, ChannelMapping channels, SKColor colorOut)
        {
            using var img = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            // Using img.SetPixel(0, 0, colorIn); does not work here because when alpha is 0, it sets all channels to 0
            using var pixelmap = img.PeekPixels();
            var pixels = pixelmap.GetPixelSpan<SKColor>();
            pixels[0] = colorIn;

            Assert.That(img.GetPixel(0, 0), Is.EqualTo(colorIn), "Failed on setup");

            var png = TextureExtract.ToPngImageChannels(img, channels);
            using var result = SKBitmap.Decode(png, img.Info);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Width, Is.EqualTo(1));
                Assert.That(result.Height, Is.EqualTo(1));
                Assert.That(result.ColorType, Is.EqualTo(SKColorType.Bgra8888));
                Assert.That(result.AlphaType, Is.EqualTo(SKAlphaType.Unpremul));

                Assert.That(result.GetPixel(0, 0), Is.EqualTo(colorOut));
            }
        }
    }
}
