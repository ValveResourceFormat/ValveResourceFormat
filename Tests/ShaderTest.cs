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
        public void TestVcsSourceTypes()
        {
            var filenamepath1 = "/sourcedir/multiblend_pcgl_40_ps.vcs";
            Assert.AreEqual(VcsProgramType.PixelShader, ComputeVCSFileName(filenamepath1).ProgramType);
            Assert.AreEqual(VcsPlatformType.PCGL, ComputeVCSFileName(filenamepath1).PlatformType);
            Assert.AreEqual(VcsShaderModelType._40, ComputeVCSFileName(filenamepath1).ShaderModelType);
            var filenamepath2 = "/sourcedir/solid_sky_pcgl_30_features.vcs";
            Assert.AreEqual(VcsProgramType.Features, ComputeVCSFileName(filenamepath2).ProgramType);
            Assert.AreEqual(VcsPlatformType.PCGL, ComputeVCSFileName(filenamepath2).PlatformType);
            var filenamepath3 = "/sourcedir/copytexture_pc_30_ps.vcs";
            Assert.AreEqual(VcsPlatformType.PC, ComputeVCSFileName(filenamepath3).PlatformType);
            Assert.AreEqual(VcsShaderModelType._30, ComputeVCSFileName(filenamepath3).ShaderModelType);
            var filenamepath4 = "/sourcedir/copytexture_pc_40_ps.vcs";
            Assert.AreEqual(VcsPlatformType.PC, ComputeVCSFileName(filenamepath4).PlatformType);
            var filenamepath5 = "/sourcedir/deferred_shading_pc_41_ps.vcs";
            Assert.AreEqual(VcsPlatformType.PC, ComputeVCSFileName(filenamepath5).PlatformType);
            Assert.AreEqual(VcsShaderModelType._41, ComputeVCSFileName(filenamepath5).ShaderModelType);
            var filenamepath6 = "/sourcedir/bloom_dota_mobile_gles_30_ps.vcs";
            Assert.AreEqual(VcsPlatformType.MOBILE_GLES, ComputeVCSFileName(filenamepath6).PlatformType);
            var filenamepath7 = "/sourcedir/cs_volumetric_fog_vulkan_50_cs.vcs";
            Assert.AreEqual(VcsProgramType.ComputeShader, ComputeVCSFileName(filenamepath7).ProgramType);
            Assert.AreEqual(VcsPlatformType.VULKAN, ComputeVCSFileName(filenamepath7).PlatformType);
            Assert.AreEqual(VcsShaderModelType._50, ComputeVCSFileName(filenamepath7).ShaderModelType);
            var filenamepath8 = "/sourcedir/bloom_dota_ios_vulkan_40_ps.vcs";
            Assert.AreEqual(VcsPlatformType.IOS_VULKAN, ComputeVCSFileName(filenamepath8).PlatformType);
            var filenamepath9 = "/sourcedir/flow_map_preview_android_vulkan_40_vs.vcs";
            Assert.AreEqual(VcsProgramType.VertexShader, ComputeVCSFileName(filenamepath9).ProgramType);
            Assert.AreEqual(VcsPlatformType.ANDROID_VULKAN, ComputeVCSFileName(filenamepath9).PlatformType);
            Assert.AreEqual(VcsShaderModelType._40, ComputeVCSFileName(filenamepath9).ShaderModelType);
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

            Assert.That(vfx, Does.Contain("Description = \"Error shader\""));
            Assert.That(vfx, Does.Contain("DevShader = true"));
        }
    }
}
