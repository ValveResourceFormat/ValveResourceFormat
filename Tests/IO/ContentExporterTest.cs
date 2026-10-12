using System.IO;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.IO;

namespace Tests.IO
{
    [NotInParallel(nameof(ContentExporterTest))]
    public class ContentExporterTest
    {
        private static ContentFile TextFile(string fileName, string text) => new()
        {
            FileName = fileName,
            Data = Encoding.UTF8.GetBytes(text),
        };

        [Test]
        public async Task FilesThatEscapeTheRootAreWrittenUnderIt()
        {
            using var temp = new TempFolder();

            var warnings = new List<string>();
            var output = temp.CreateFolder("output");
            using var contentFile = TextFile("scenes/list.vcdlist", "list");
            contentFile.AddSubFile("scenes/../../../escaped.vcd", () => "scene"u8.ToArray());

            new ContentExporter(output) { Warning = warnings.Add }.Export(contentFile);

            using (Assert.Multiple())
            {
                await Assert.That(File.Exists(Path.Join(output, "escaped.vcd"))).IsTrue();
                await Assert.That(Directory.GetFiles(temp.FullName, "escaped.vcd", SearchOption.AllDirectories)).Count().IsEqualTo(1);
                await Assert.That(warnings).Count().IsEqualTo(1);
            }
        }

        [Test]
        public async Task SamePathWithSameContentIsWrittenOnce()
        {
            using var temp = new TempFolder();

            var warnings = new List<string>();
            var exporter = new ContentExporter(temp.FullName) { Warning = warnings.Add };
            var clipExtracts = 0;

            ContentFile CreateModel(string name)
            {
                var model = TextFile($"models/{name}.vmdl", $"model {name} uses animation/shared.dmx");
                var clip = TextFile("animation/shared.vnmclip", "clip");
                clip.AddSubFile("animation/shared.dmx", () =>
                {
                    clipExtracts++;
                    return "dmx"u8.ToArray();
                });
                model.AdditionalFiles.Add(clip);
                return model;
            }

            using (var first = CreateModel("first"))
            {
                exporter.Export(first);
            }

            using (var second = CreateModel("second"))
            {
                exporter.Export(second);
            }

            using (Assert.Multiple())
            {
                await Assert.That(exporter.WrittenFileCount).IsEqualTo(4);
                await Assert.That(clipExtracts).IsEqualTo(2);
                await Assert.That(warnings).IsEmpty();
                await Assert.That(exporter.IsHandled("animation/shared.vnmclip_c")).IsTrue();
                await Assert.That(exporter.IsHandled("models/second.vmdl_c")).IsTrue();
                await Assert.That(exporter.IsHandled("models/third.vmdl_c")).IsFalse();
            }
        }

        [Test]
        public async Task SamePathWithDifferentContentIsRenamedAndRepointed()
        {
            using var temp = new TempFolder();

            var warnings = new List<string>();
            var exporter = new ContentExporter(temp.FullName) { Warning = warnings.Add };

            using var first = TextFile("animation/first.vnmclip", "m_sourceFilename = \"animation/source.dmx\"");
            first.AddSubFile("animation/source.dmx", () => "first"u8.ToArray());
            exporter.Export(first);

            using var second = TextFile("animation/second.vnmclip", "m_sourceFilename = \"animation/source.dmx\"");
            second.AddSubFile("animation/source.dmx", () => "second"u8.ToArray());
            exporter.Export(second);

            var animationFolder = Path.Join(temp.FullName, "animation");

            using (Assert.Multiple())
            {
                await Assert.That(await File.ReadAllTextAsync(Path.Join(animationFolder, "source.dmx"))).IsEqualTo("first");
                await Assert.That(await File.ReadAllTextAsync(Path.Join(animationFolder, "source_1.dmx"))).IsEqualTo("second");
                await Assert.That(await File.ReadAllTextAsync(Path.Join(animationFolder, "first.vnmclip"))).Contains("\"animation/source.dmx\"");
                await Assert.That(await File.ReadAllTextAsync(Path.Join(animationFolder, "second.vnmclip"))).Contains("\"animation/source_1.dmx\"");
                await Assert.That(warnings).Count().IsEqualTo(1);
            }
        }

        [Test]
        public async Task RenameIsPointedAtOnlyInTheFileThatReferencesIt()
        {
            using var temp = new TempFolder();

            var warnings = new List<string>();
            var exporter = new ContentExporter(temp.FullName) { Warning = warnings.Add };

            using var model = TextFile("models/crate.vmdl", "anim \"models/anims/idle.dmx\"");
            model.AddSubFile("models/anims/idle.dmx", () => "model idle"u8.ToArray());

            var clip = TextFile("models/anims/idle.vnmclip", "m_sourceFilename = \"models/anims/idle.dmx\"");
            clip.AddSubFile("models/anims/idle.dmx", () => "clip idle"u8.ToArray());
            model.AdditionalFiles.Add(clip);

            exporter.Export(model);

            var folder = Path.Join(temp.FullName, "models");

            using (Assert.Multiple())
            {
                await Assert.That(await File.ReadAllTextAsync(Path.Join(folder, "crate.vmdl"))).IsEqualTo("anim \"models/anims/idle.dmx\"");
                await Assert.That(await File.ReadAllTextAsync(Path.Join(folder, "anims", "idle.dmx"))).IsEqualTo("model idle");
                await Assert.That(await File.ReadAllTextAsync(Path.Join(folder, "anims", "idle.vnmclip"))).IsEqualTo("m_sourceFilename = \"models/anims/idle_1.dmx\"");
                await Assert.That(await File.ReadAllTextAsync(Path.Join(folder, "anims", "idle_1.dmx"))).IsEqualTo("clip idle");
                await Assert.That(warnings).Count().IsEqualTo(1);
            }
        }

        [Test]
        public async Task TwoDifferentFilesOfOneParentAtOnePathKeepItsReferences()
        {
            using var temp = new TempFolder();

            var warnings = new List<string>();
            var exporter = new ContentExporter(temp.FullName) { Warning = warnings.Add };

            const string ModelText = "mesh \"models/crate.dmx\" physics \"models/crate.dmx\"";
            using var model = TextFile("models/crate.vmdl", ModelText);
            model.AddSubFile("models/crate.dmx", () => "mesh"u8.ToArray());
            model.AddSubFile("models/crate.dmx", () => "physics"u8.ToArray());

            exporter.Export(model);

            var folder = Path.Join(temp.FullName, "models");

            using (Assert.Multiple())
            {
                await Assert.That(await File.ReadAllTextAsync(Path.Join(folder, "crate.vmdl"))).IsEqualTo(ModelText);
                await Assert.That(await File.ReadAllTextAsync(Path.Join(folder, "crate.dmx"))).IsEqualTo("mesh");
                await Assert.That(await File.ReadAllTextAsync(Path.Join(folder, "crate_1.dmx"))).IsEqualTo("physics");
                await Assert.That(warnings).Count().IsEqualTo(2);
            }
        }

        [Test]
        public async Task ExistingFilesFollowThePolicy()
        {
            using var temp = new TempFolder();

            var path = Path.Join(temp.FullName, "materials", "crate.vmat");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await File.WriteAllTextAsync(path, "hand made");
            new ContentExporter(temp.FullName) { ExistingFileAction = ExistingFileAction.Skip }.WriteFile("materials/crate.vmat", "decompiled"u8.ToArray());
            var afterSkip = await File.ReadAllTextAsync(path);

            var prompts = 0;
            var confirming = new ContentExporter(temp.FullName)
            {
                ConfirmExistingFiles = _ =>
                {
                    prompts++;
                    return ExistingFileAction.Overwrite;
                },
            };
            confirming.WriteFile("materials/crate.vmat", "decompiled"u8.ToArray());
            await File.WriteAllTextAsync(Path.Join(temp.FullName, "materials", "other.vmat"), "hand made");
            confirming.WriteFile("materials/other.vmat", "decompiled"u8.ToArray());

            var cancelling = new ContentExporter(temp.FullName) { ConfirmExistingFiles = _ => ExistingFileAction.Cancel };

            using (Assert.Multiple())
            {
                await Assert.That(afterSkip).IsEqualTo("hand made");
                await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("decompiled");
                await Assert.That(prompts).IsEqualTo(1);
                await Assert.That(() => cancelling.WriteFile("materials/crate.vmat", "again"u8.ToArray())).Throws<OperationCanceledException>();
            }
        }

        [Test]
        public async Task TargetFileTakesTheMainFileOnly()
        {
            using var temp = new TempFolder();

            var target = Path.Join(temp.FullName, "picked", "renamed.vmdl");
            var exporter = new ContentExporter(temp.FullName);

            using var model = TextFile("models/crate.vmdl", "model");
            model.AddSubFile("models/crate_body.dmx", () => "dmx"u8.ToArray());
            exporter.Export(model, target);

            using var texture = new ContentFile { FileName = "materials/crate.vtex" };
            texture.AddSubFile("materials/crate.png", () => "png"u8.ToArray());
            exporter.Export(texture, Path.Join(temp.FullName, "picked", "image.png"));

            using (Assert.Multiple())
            {
                await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("model");
                await Assert.That(File.Exists(Path.Join(temp.FullName, "models", "crate_body.dmx"))).IsTrue();
                await Assert.That(File.Exists(Path.Join(temp.FullName, "models", "crate.vmdl"))).IsFalse();
                await Assert.That(await File.ReadAllTextAsync(Path.Join(temp.FullName, "picked", "image.png"))).IsEqualTo("png");
            }
        }
    }
}
