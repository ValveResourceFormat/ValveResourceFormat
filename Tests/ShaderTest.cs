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
                var shader = new ShaderCollection();

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
            Assert.AreEqual(VcsSourceType.Glsl, GetVcsSourceType(filenamepath1));
            var filenamepath2 = "/sourcedir/solid_sky_pcgl_30_features.vcs";
            Assert.AreEqual(VcsSourceType.Glsl, GetVcsSourceType(filenamepath2));
            var filenamepath3 = "/sourcedir/copytexture_pc_30_ps.vcs";
            Assert.AreEqual(VcsSourceType.DXIL, GetVcsSourceType(filenamepath3));
            var filenamepath4 = "/sourcedir/copytexture_pc_40_ps.vcs";
            Assert.AreEqual(VcsSourceType.DXBC, GetVcsSourceType(filenamepath4));
            var filenamepath5 = "/sourcedir/deferred_shading_pc_41_ps.vcs";
            Assert.AreEqual(VcsSourceType.DXBC, GetVcsSourceType(filenamepath5));
            var filenamepath6 = "/sourcedir/bloom_dota_mobile_gles_30_ps.vcs";
            Assert.AreEqual(VcsSourceType.MobileGles, GetVcsSourceType(filenamepath6));
            var filenamepath7 = "/sourcedir/cs_volumetric_fog_vulkan_50_cs.vcs";
            Assert.AreEqual(VcsSourceType.Vulkan, GetVcsSourceType(filenamepath7));
            var filenamepath8 = "/sourcedir/bloom_dota_ios_vulkan_40_ps.vcs";
            Assert.AreEqual(VcsSourceType.IosVulkan, GetVcsSourceType(filenamepath8));
            var filenamepath9 = "/sourcedir/flow_map_preview_android_vulkan_40_vs.vcs";
            Assert.AreEqual(VcsSourceType.AndroidVulkan, GetVcsSourceType(filenamepath9));
        }
    }
}
