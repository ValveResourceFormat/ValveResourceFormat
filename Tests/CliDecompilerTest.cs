using System.Diagnostics;
using System.IO;
using CLI;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class CliDecompilerTest
    {
        private const string TextureEntry = "materials/cs_italy/ground/tile_floor_diamond_1_color_psd_87178d3c.vtex_c";
        private const string SecondTextureEntry = "materials/cs_italy/ground/tile_floor_diamond_1_height_psd_3cf8aead.vtex_c";
        private const string SingleEntryPrefix = "materials/cs_italy/ground/tile_floor_diamond_1_color_psd_87178d3c";
        private const string ModelEntry = "maps/ui/nametag/worldnodes/node000_lr0_c2_s_cb_mesh_mat0_tile_floor_diam.vmdl_c";

        private string OutputDirectory = null!;
        private string VpkPath = null!;

        [SetUp]
        public void SetUp()
        {
            OutputDirectory = Path.Combine(Path.GetTempPath(), "vrf_cli_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(OutputDirectory);
            VpkPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "small_map_with_material.vpk");
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(OutputDirectory, true);
        }

        [Test]
        public void ExactFilteredEntryUsesOutputFilenameWhenDecompiling()
        {
            var outputPath = Path.Combine(OutputDirectory, "requested.png");

            var result = RunCli("--input", VpkPath, "--vpk_filepath", TextureEntry, "-d", "--output", outputPath);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Message);
                Assert.That(File.Exists(outputPath), Is.True, result.Message);
                Assert.That(Directory.Exists(outputPath), Is.False, result.Message);
                Assert.That(File.Exists(Path.Combine(OutputDirectory, Path.GetFileName(Path.ChangeExtension(TextureEntry, "png")))), Is.False, result.Message);
            });
        }

        [Test]
        public void ExactFilteredEntryUsesOutputFilenameWhenExportingRawFile()
        {
            var outputPath = Path.Combine(OutputDirectory, "requested.vtex_c");

            var result = RunCli("--input", VpkPath, "--vpk_filepath", TextureEntry, "--output", outputPath);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Message);
                Assert.That(File.Exists(outputPath), Is.True, result.Message);
                Assert.That(Directory.Exists(outputPath), Is.False, result.Message);
            });
        }

        [Test]
        public void PrefixFilterKeepsOutputAsDirectory()
        {
            var outputPath = Path.Combine(OutputDirectory, "prefix-output");

            var result = RunCli("--input", VpkPath, "--vpk_filepath", SingleEntryPrefix, "-d", "--output", outputPath);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Message);
                Assert.That(Directory.Exists(outputPath), Is.True, result.Message);
                Assert.That(File.Exists(Path.Combine(outputPath, Path.ChangeExtension(TextureEntry, "png"))), Is.True, result.Message);
            });
        }

        [Test]
        public void MultipleExactFiltersKeepOutputAsDirectory()
        {
            var outputPath = Path.Combine(OutputDirectory, "multiple-output");
            var filters = $"{TextureEntry},{SecondTextureEntry}";

            var result = RunCli("--input", VpkPath, "--vpk_filepath", filters, "-d", "--output", outputPath);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Message);
                Assert.That(Directory.Exists(outputPath), Is.True, result.Message);
                Assert.That(File.Exists(Path.Combine(outputPath, Path.ChangeExtension(TextureEntry, "png"))), Is.True, result.Message);
                Assert.That(File.Exists(Path.Combine(outputPath, Path.ChangeExtension(SecondTextureEntry, "png"))), Is.True, result.Message);
            });
        }

        [Test]
        public void UnfilteredVpkKeepsOutputAsDirectory()
        {
            var outputPath = Path.Combine(OutputDirectory, "all-output");

            var result = RunCli("--input", VpkPath, "--output", outputPath);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Message);
                Assert.That(Directory.Exists(outputPath), Is.True, result.Message);
                Assert.That(File.Exists(Path.Combine(outputPath, TextureEntry)), Is.True, result.Message);
            });
        }

        [Test]
        public void ExactFilteredGltfExportUsesRequestedFilenameWithFormatExtension()
        {
            var requestedPath = Path.Combine(OutputDirectory, "requested.output");
            var gltfPath = Path.ChangeExtension(requestedPath, "glb");

            var result = RunCli("--input", VpkPath, "--vpk_filepath", ModelEntry, "-d", "--gltf_export_format", "glb", "--output", requestedPath);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Message);
                Assert.That(File.Exists(gltfPath), Is.True, result.Message);
                Assert.That(Directory.Exists(requestedPath), Is.False, result.Message);
            });
        }

        private static CliResult RunCli(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(typeof(Decompiler).Assembly.Location);

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                Assert.Fail("CLI process did not exit within 30 seconds.");
            }

            return new CliResult(
                process.ExitCode,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult());
        }

        private readonly record struct CliResult(int ExitCode, string StandardOutput, string StandardError)
        {
            public string Message => $"stdout:{Environment.NewLine}{StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{StandardError}";
        }
    }
}
