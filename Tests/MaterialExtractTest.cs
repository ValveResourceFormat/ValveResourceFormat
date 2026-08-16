using System.IO;
using System.Threading.Tasks;
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
                Resource = null!,
                ShaderName = "vr_complex.vfx",
            };

            mockMaterial.IntParams["F_TRANSLUCENT"] = translucent ? 1 : 0;
            return mockMaterial;
        }

        [Test]
        [MatrixDataSource]
        public async Task TextureInputsForFeatureState([Matrix] bool translucent)
        {
            var vr_complex_expected_inputs = new[] {
                (ChannelMapping.RGB, "TextureColor"),
                (ChannelMapping.A, translucent ? "TextureTranslucency" : "TextureMetalness")
            };

            var result = new BasicShaderDataProvider().GetInputsForTexture("g_tColor", GetMockMaterial(translucent));
            await Assert.That(result).IsEquivalentTo(vr_complex_expected_inputs);
        }

        [Test]
        [MatrixDataSource]
        public async Task TextureInputPaths([Matrix] bool translucent)
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
            await Assert.That(result).IsEquivalentTo(vr_complex_expected_inputs);
        }

        [Test]
        public async Task TestVmatExtract()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "point_worldtext_default.vmat_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var materialExtract = new MaterialExtract(resource);

            await Assert.That(materialExtract.ToValveMaterial()).IsNotEmpty();
        }

        [Test]
        [MatrixDataSource]
        public async Task ToMaterialMapsHdrCubemap([Matrix] bool withUnpackInfo)
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "Textures", "cubemap.vtex_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            MaterialExtract.UnpackInfo[] mapsToUnpack = withUnpackInfo
                ? [new MaterialExtract.UnpackInfo
                {
                    TextureType = "TextureEnvironmentMap",
                    FileName = "materials/env_map.exr",
                    Channel = ChannelMapping.RGBA,
                }]
                : [];

            using var contentFile = new TextureExtract(resource).ToMaterialMaps(mapsToUnpack);

            await Assert.That(contentFile.SubFiles).Count().IsEqualTo(1);
            await Assert.That(contentFile.SubFiles[0].FileName).IsEqualTo(withUnpackInfo ? "env_map.exr" : "cubemap.exr");
            var extracted = contentFile.SubFiles[0].Extract?.Invoke();
            await Assert.That(extracted).IsNotNull();
            await Assert.That(extracted).IsNotEmpty();
        }

        public static IEnumerable<(SKColor, ChannelMapping, SKColor)> PngImageChannelsSource()
        {
            var c1234 = new SKColor(1, 2, 3, 4);
            yield return (c1234, ChannelMapping.R, new SKColor(1, 1, 1));
            yield return (c1234, ChannelMapping.G, new SKColor(2, 2, 2));
            yield return (c1234, ChannelMapping.B, new SKColor(3, 3, 3));
            yield return (c1234, ChannelMapping.A, new SKColor(4, 4, 4, 255));

            yield return (c1234, ChannelMapping.RG, new SKColor(1, 2, 3, 255));
            yield return (c1234, ChannelMapping.RGB, new SKColor(1, 2, 3, 255));

            yield return (c1234, ChannelMapping.AG, new SKColor(4, 2, 0));

            yield return (c1234, ChannelMapping.RGBA, c1234);

            yield return (c1234, ChannelMapping.NULL, SKColors.Black);

            yield return (
                new SKColor(1, 2, 3, 4),
                ChannelMapping.FromChannels(1, 2, 0), // GBR
                new SKColor(2, 3, 1)
            );

            yield return (
                new SKColor(1, 2, 3, 4),
                ChannelMapping.FromChannels(1, 2, 0, 3), // GBRA
                new SKColor(2, 3, 1, 4)
            );

            var alpha0 = new SKColor(1, 2, 3, 0);
            yield return (alpha0, ChannelMapping.RGBA, alpha0);
            yield return (alpha0, ChannelMapping.R, new SKColor(1, 1, 1));
            yield return (alpha0, ChannelMapping.G, new SKColor(2, 2, 2));
            yield return (alpha0, ChannelMapping.B, new SKColor(3, 3, 3));
            yield return (alpha0, ChannelMapping.A, new SKColor(0, 0, 0, 255));
        }

        [Test, MethodDataSource(nameof(PngImageChannelsSource))]
        public async Task TestPngImageChannels(SKColor colorIn, ChannelMapping channels, SKColor colorOut)
        {
            using var img = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            // Using img.SetPixel(0, 0, colorIn); does not work here because when alpha is 0, it sets all channels to 0
            using var pixelmap = img.PeekPixels();
            var pixels = pixelmap.GetPixelSpan<SKColor>();
            pixels[0] = colorIn;

            await Assert.That(img.GetPixel(0, 0)).IsEqualTo(colorIn).Because("Failed on setup");

            var png = TextureExtract.ToPngImageChannels(img, channels);
            using var result = SKBitmap.Decode(png, img.Info);
            using (Assert.Multiple())
            {
                await Assert.That(result.Width).IsEqualTo(1);
                await Assert.That(result.Height).IsEqualTo(1);
                await Assert.That(result.ColorType).IsEqualTo(SKColorType.Bgra8888);
                await Assert.That(result.AlphaType).IsEqualTo(SKAlphaType.Unpremul);

                await Assert.That(result.GetPixel(0, 0)).IsEqualTo(colorOut);
            }
        }
    }
}
