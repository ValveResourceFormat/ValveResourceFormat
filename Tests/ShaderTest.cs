using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat.CompiledShader;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace Tests
{
    public class ShaderTest
    {
        [Test]
        public void ParseShaders()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Shaders");
            var files = Directory.GetFiles(path, "*.vcs");

            foreach (var file in files)
            {
                var shader = new ShaderFile();

                using var sw = new StringWriter(CultureInfo.InvariantCulture);
                var originalOutput = Console.Out;
                Console.SetOut(sw);

                shader.Read(file);

                Console.SetOut(originalOutput);
            }
        }

        [Test]
        public void TestVcsSourceTypes()
        {
            var filenamepath1 = "/sourcedir/multiblend_pcgl_40_ps.vcs";
            Assert.AreEqual(VcsProgramType.PixelShader, ComputeVCSFileName(filenamepath1).Item1);
            Assert.AreEqual(VcsPlatformType.PCGL, ComputeVCSFileName(filenamepath1).Item2);
            Assert.AreEqual(VcsShaderModelType._40, ComputeVCSFileName(filenamepath1).Item3);
            var filenamepath2 = "/sourcedir/solid_sky_pcgl_30_features.vcs";
            Assert.AreEqual(VcsProgramType.Features, ComputeVCSFileName(filenamepath2).Item1);
            Assert.AreEqual(VcsPlatformType.PCGL, ComputeVCSFileName(filenamepath2).Item2);
            var filenamepath3 = "/sourcedir/copytexture_pc_30_ps.vcs";
            Assert.AreEqual(VcsPlatformType.PC, ComputeVCSFileName(filenamepath3).Item2);
            Assert.AreEqual(VcsShaderModelType._30, ComputeVCSFileName(filenamepath3).Item3);
            var filenamepath4 = "/sourcedir/copytexture_pc_40_ps.vcs";
            Assert.AreEqual(VcsPlatformType.PC, ComputeVCSFileName(filenamepath4).Item2);
            var filenamepath5 = "/sourcedir/deferred_shading_pc_41_ps.vcs";
            Assert.AreEqual(VcsPlatformType.PC, ComputeVCSFileName(filenamepath5).Item2);
            Assert.AreEqual(VcsShaderModelType._41, ComputeVCSFileName(filenamepath5).Item3);
            var filenamepath6 = "/sourcedir/bloom_dota_mobile_gles_30_ps.vcs";
            Assert.AreEqual(VcsPlatformType.MOBILE_GLES, ComputeVCSFileName(filenamepath6).Item2);
            var filenamepath7 = "/sourcedir/cs_volumetric_fog_vulkan_50_cs.vcs";
            Assert.AreEqual(VcsProgramType.ComputeShader, ComputeVCSFileName(filenamepath7).Item1);
            Assert.AreEqual(VcsPlatformType.VULKAN, ComputeVCSFileName(filenamepath7).Item2);
            Assert.AreEqual(VcsShaderModelType._50, ComputeVCSFileName(filenamepath7).Item3);
            var filenamepath8 = "/sourcedir/bloom_dota_ios_vulkan_40_ps.vcs";
            Assert.AreEqual(VcsPlatformType.IOS_VULKAN, ComputeVCSFileName(filenamepath8).Item2);
            var filenamepath9 = "/sourcedir/flow_map_preview_android_vulkan_40_vs.vcs";
            Assert.AreEqual(VcsProgramType.VertexShader, ComputeVCSFileName(filenamepath9).Item1);
            Assert.AreEqual(VcsPlatformType.ANDROID_VULKAN, ComputeVCSFileName(filenamepath9).Item2);
            Assert.AreEqual(VcsShaderModelType._40, ComputeVCSFileName(filenamepath9).Item3);
        }
    }
}
