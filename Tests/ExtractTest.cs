using System.Collections.Generic;
using NUnit.Framework;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class MaterialExtractTest
    {
        [Test]
        public void TextureInputsForFeatureState([Values(false, true)] bool translucent)
        {
            var featureState = new Dictionary<string, long>
            {
                ["F_TRANSLUCENT"] = translucent ? 1 : 0
            };

            var vr_complex_expected_inputs = new[] {
                (MaterialExtract.Channel.RGB, "TextureColor"),
                (MaterialExtract.Channel.A, translucent ? "TextureTranslucency" : "TextureMetalness")
            };

            var result = MaterialExtract.GetTextureInputs("vr_complex.vfx", "g_tColor", featureState);
            Assert.That(result, Is.EquivalentTo(vr_complex_expected_inputs));
        }

        [Test]
        public void TextureInputPaths([Values(false, true)] bool translucent)
        {
            var mockMaterial = new Material()
            {
                ShaderName = "vr_complex.vfx",
            };

            mockMaterial.IntParams["F_TRANSLUCENT"] = translucent ? 1 : 0;

            var vr_complex_expected_inputs = new[] {
                new MaterialExtract.UnpackInfo()
                {
                    TextureType = "TextureColor",
                    FileName = "test_color.png",
                    Channel = MaterialExtract.Channel.RGB
                },
                new MaterialExtract.UnpackInfo()
                {
                    TextureType = translucent ? "TextureTranslucency" : "TextureMetalness",
                    FileName = translucent ? "test_65b7aff5-A.png" : "test_65b7aff5_metal.png",
                    Channel = MaterialExtract.Channel.A
                }
            };

            var result = MaterialExtract.GetTextureUnpackInfos("g_tColor", "test_color_jpg_65b7aff5.vtex", mockMaterial, false, false);
            Assert.That(result, Is.EquivalentTo(vr_complex_expected_inputs));
        }
    }
}
