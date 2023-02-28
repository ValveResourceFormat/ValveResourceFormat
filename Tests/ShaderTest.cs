using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace Tests
{
    public class ShaderTest
    {
        public static string ShadersDir
            => Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Shaders");

        [Test]
        public void ParseShaders()
        {
            var files = Directory.GetFiles(ShadersDir, "*.vcs");

            foreach (var file in files)
            {
                using var shader = new ShaderFile();

                using var sw = new StringWriter(CultureInfo.InvariantCulture);
                var originalOutput = Console.Out;
                Console.SetOut(sw);

                shader.Read(file);
                shader.PrintSummary();

                Console.SetOut(originalOutput);

                if (shader.ZframesLookup.Count > 0)
                {
                    var zframe = shader.GetDecompressedZFrame(0);
                    Assert.That(zframe, Is.Not.Empty);
                }
            }
        }

        [Test]
        public void TestVcsFileName()
        {
            var testCases = new (string FileName, string ShaderName, VcsPlatformType Platform, VcsShaderModelType ShaderModel, VcsProgramType ProgramType)[]
            {
                ("/sourcedir/multiblend_pcgl_40_ps.vcs", "multiblend", VcsPlatformType.PCGL, VcsShaderModelType._40, VcsProgramType.PixelShader),
                ("/sourcedir/solid_sky_pcgl_30_features.vcs", "solid_sky", VcsPlatformType.PCGL, VcsShaderModelType._30, VcsProgramType.Features),
                ("/sourcedir/copytexture_pc_30_ps.vcs", "copytexture", VcsPlatformType.PC, VcsShaderModelType._30, VcsProgramType.PixelShader),
                ("/sourcedir/copytexture_pc_40_ps.vcs", "copytexture", VcsPlatformType.PC, VcsShaderModelType._40, VcsProgramType.PixelShader),
                ("/sourcedir/deferred_shading_pc_41_ps.vcs", "deferred_shading", VcsPlatformType.PC, VcsShaderModelType._41, VcsProgramType.PixelShader),
                ("/sourcedir/bloom_dota_mobile_gles_30_ps.vcs", "bloom_dota", VcsPlatformType.MOBILE_GLES, VcsShaderModelType._30, VcsProgramType.PixelShader),
                ("/sourcedir/cs_volumetric_fog_vulkan_50_cs.vcs", "cs_volumetric_fog", VcsPlatformType.VULKAN, VcsShaderModelType._50, VcsProgramType.ComputeShader),
                ("/sourcedir/bloom_dota_ios_vulkan_40_ps.vcs", "bloom_dota", VcsPlatformType.IOS_VULKAN, VcsShaderModelType._40, VcsProgramType.PixelShader),
                ("/sourcedir/flow_map_preview_android_vulkan_40_vs.vcs", "flow_map_preview", VcsPlatformType.ANDROID_VULKAN, VcsShaderModelType._40, VcsProgramType.VertexShader),
            };

            foreach (var testCase in testCases)
            {
                var result = ComputeVCSFileName(testCase.FileName);
                Assert.AreEqual(testCase.ShaderName, result.ShaderName);
                Assert.AreEqual(testCase.Platform, result.PlatformType);
                Assert.AreEqual(testCase.ShaderModel, result.ShaderModelType);
                Assert.AreEqual(testCase.ProgramType, result.ProgramType);
            }
        }

        [Test]
        public void CompiledShaderInResourceThrows()
        {
            var path = Path.Combine(ShadersDir, "error_pcgl_40_ps.vcs");
            using var resource = new Resource();

            var ex = Assert.Throws<InvalidDataException>(() => resource.Read(path));

            Assert.That(ex.Message, Does.Contain("Use CompiledShader"));
        }

        [Test]
        public void ChannelTest()
        {
            Assert.That(ChannelMapping.R.PackedValue, Is.EqualTo(0xFFFFFF00));
            Assert.That(ChannelMapping.G.PackedValue, Is.EqualTo(0xFFFFFF01));
            Assert.That(ChannelMapping.B.PackedValue, Is.EqualTo(0xFFFFFF02));
            Assert.That(ChannelMapping.A.PackedValue, Is.EqualTo(0xFFFFFF03));
            Assert.That(ChannelMapping.RGB.PackedValue, Is.EqualTo(0xFF020100));
            Assert.That(ChannelMapping.RGBA.PackedValue, Is.EqualTo(0x03020100));

            Assert.That(ChannelMapping.RGBA.Channels[0], Is.EqualTo(ChannelMapping.Channel.R));
            Assert.That(ChannelMapping.RGBA.Channels[1], Is.EqualTo(ChannelMapping.Channel.G));
            Assert.That(ChannelMapping.AG.Channels[1], Is.EqualTo(ChannelMapping.Channel.G));

            Assert.That(ChannelMapping.RGBA.Count, Is.EqualTo(4));
            Assert.That(ChannelMapping.RGB.Count, Is.EqualTo(3));
            Assert.That(ChannelMapping.RG.Count, Is.EqualTo(2));
            Assert.That(ChannelMapping.G.Count, Is.EqualTo(1));
            Assert.That(ChannelMapping.NULL.Count, Is.EqualTo(0));

            Assert.That(ChannelMapping.RGBA.ValidChannels, Is.EqualTo(new[] { ChannelMapping.Channel.R, ChannelMapping.Channel.G, ChannelMapping.Channel.B, ChannelMapping.Channel.A }));
            Assert.That(ChannelMapping.RGB.ValidChannels, Is.EqualTo(new[] { ChannelMapping.Channel.R, ChannelMapping.Channel.G, ChannelMapping.Channel.B }));
            Assert.That(ChannelMapping.RG.ValidChannels, Is.EqualTo(new[] { ChannelMapping.Channel.R, ChannelMapping.Channel.G }));
            Assert.That(ChannelMapping.AG.ValidChannels, Is.EqualTo(new[] { ChannelMapping.Channel.A, ChannelMapping.Channel.G }));
            Assert.That(ChannelMapping.A.ValidChannels, Is.EqualTo(new[] { ChannelMapping.Channel.A }));
            Assert.That(ChannelMapping.R.ValidChannels, Is.EqualTo(new[] { ChannelMapping.Channel.R }));

            Assert.That((byte)ChannelMapping.R, Is.EqualTo(0x00));
            Assert.That((byte)ChannelMapping.G, Is.EqualTo(0x01));
            Assert.That((byte)ChannelMapping.B, Is.EqualTo(0x02));
            Assert.That((byte)ChannelMapping.A, Is.EqualTo(0x03));

            Assert.That(ChannelMapping.R.ToString(), Is.EqualTo("R"));
            Assert.That(ChannelMapping.G.ToString(), Is.EqualTo("G"));
            Assert.That(ChannelMapping.B.ToString(), Is.EqualTo("B"));
            Assert.That(ChannelMapping.A.ToString(), Is.EqualTo("A"));
            Assert.That(ChannelMapping.AG.ToString(), Is.EqualTo("AG"));
            Assert.That(ChannelMapping.RGB.ToString(), Is.EqualTo("RGB"));
            Assert.That(ChannelMapping.NULL.ToString(), Is.EqualTo("0xFFFFFFFF"));

            Assert.Throws<ArgumentOutOfRangeException>(() => ChannelMapping.FromChannels(0x04));
            Assert.Throws<ArgumentOutOfRangeException>(() => ChannelMapping.FromChannels(0x05));

            Assert.That(ChannelMapping.FromChannels(0xFF), Is.EqualTo(ChannelMapping.NULL));
        }

        [Test]
        public void VfxShaderExtract_Invalid()
        {
            var path = Path.Combine(ShadersDir, "error_pcgl_40_ps.vcs");
            using var shader = new ShaderFile();
            shader.Read(path);

            var ex = Assert.Throws<InvalidOperationException>(() => new ShaderExtract(ShaderCollection.FromEnumerable(new[] { shader })));

            Assert.That(ex.Message, Does.Contain("cannot continue without at least a features file"));
        }

        [Test]
        public void VfxShaderExtract_Minimal()
        {
            var path = Path.Combine(ShadersDir, "error_pc_40_features.vcs");
            using var shader = new ShaderFile();
            shader.Read(path);

            var extract = new ShaderExtract(ShaderCollection.FromEnumerable(new[] { shader }));

            var vfx = extract.ToVFX(ShaderExtract.ShaderExtractParams.Inspect);
            vfx = extract.ToVFX(ShaderExtract.ShaderExtractParams.Export);

            Assert.That(vfx.VfxContent, Does.Contain("Description = \"Error shader\""));
            Assert.That(vfx.VfxContent, Does.Contain("DevShader = true"));
        }

        [Test]
        public void VfxShaderExtract_OptionsTest()
        {
            using var collection = new ShaderCollection();
            foreach (var file in Directory.GetFiles(ShadersDir, "error_pc_40_*.vcs"))
            {
                var shader = new ShaderFile();
                shader.Read(file);
                collection.Add(shader);
            }

            var extract = new ShaderExtract(collection);

            var optionsToTest = new[]
            {
                ShaderExtract.ShaderExtractParams.Inspect,
                ShaderExtract.ShaderExtractParams.Export,
                new ShaderExtract.ShaderExtractParams { },
                new ShaderExtract.ShaderExtractParams { CollapseBuffers_InInclude = true },
                new ShaderExtract.ShaderExtractParams { ZFrameReadingCap = -1 },
                new ShaderExtract.ShaderExtractParams { ZFrameReadingCap = 0 },
                new ShaderExtract.ShaderExtractParams { ZFrameReadingCap = 1 },
                new ShaderExtract.ShaderExtractParams { StaticComboAttributes_NoSeparateGlobals = true },
                new ShaderExtract.ShaderExtractParams { StaticComboAttributes_NoConditionalReduce = true },
            };

            foreach (var options in optionsToTest)
            {
                var vfx = extract.ToVFX(options);
                Assert.That(vfx.VfxContent, Does.Contain("Description = \"Error shader\""));
                Assert.That(vfx.VfxContent, Does.Contain("DevShader = true"));
            }
        }
    }
}
