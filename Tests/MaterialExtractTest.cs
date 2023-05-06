using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ShaderDataProvider;
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
        public void TextureInputsForFeatureState([Values(false, true)] bool translucent)
        {
            var vr_complex_expected_inputs = new[] {
                (ChannelMapping.RGB, "TextureColor"),
                (ChannelMapping.A, translucent ? "TextureTranslucency" : "TextureMetalness")
            };

            var result = new BasicShaderDataProvider().GetInputsForTexture("g_tColor", GetMockMaterial(translucent));
            Assert.That(result, Is.EquivalentTo(vr_complex_expected_inputs));
        }

        [Test]
        public void TextureInputPaths([Values(false, true)] bool translucent)
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
                .GetTextureUnpackInfos("g_tColor", "test_color_jpg_65b7aff5.vtex", false, false);
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
    }
}
