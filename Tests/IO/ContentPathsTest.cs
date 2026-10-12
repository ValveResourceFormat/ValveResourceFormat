using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat.IO;

namespace Tests.IO
{
    public class ContentPathsTest
    {
        [Test]
        public async Task OutputRootSnapsToAddonAndModRoots()
        {
            using var temp = new TempFolder();

            var gameAddon = temp.CreateFolder("game", "csgo_addons", "my_addon");
            var contentAddon = temp.CreateFolder("content", "csgo_addons", "my_addon");
            var mod = temp.CreateMod("mymod");
            var contentMod = temp.CreateFolder("content", "mymod");
            var unknownMod = temp.CreateFolder("game", "unknown");

            using (Assert.Multiple())
            {
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(gameAddon, "models", "props"))).IsEqualTo(gameAddon);
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(contentAddon, "unrelated", "deep"), "models")).IsEqualTo(contentAddon);
                await Assert.That(ContentPaths.ResolveOutputRoot(contentAddon)).IsEqualTo(contentAddon);
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(mod, "stuff", "deeper"))).IsEqualTo(mod);
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(contentMod, "stuff"))).IsEqualTo(contentMod);
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(unknownMod, "stuff"))).IsEqualTo(Path.Join(unknownMod, "stuff"));
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(temp.FullName, "game", "csgo_addons"))).IsEqualTo(Path.Join(temp.FullName, "game", "csgo_addons"));
            }
        }

        [Test]
        public async Task OutputRootStripsOnlyTheMatchingTailOfThePickedFolder()
        {
            using var temp = new TempFolder();

            var output = temp.CreateFolder("output");

            using (Assert.Multiple())
            {
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(output, "models", "props"), "models/props")).IsEqualTo(output);
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(output, "models"), "models/props")).IsEqualTo(output);
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(output, "materials"), "models/props")).IsEqualTo(Path.Join(output, "materials"));
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(output, "materials"))).IsEqualTo(Path.Join(output, "materials"));
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(output, "props"), "models/props")).IsEqualTo(Path.Join(output, "props"));
                await Assert.That(ContentPaths.ResolveOutputRoot(Path.Join(output, "models", "other"), "models/props")).IsEqualTo(Path.Join(output, "models", "other"));
                await Assert.That(ContentPaths.ResolveOutputRoot(output, "models/props")).IsEqualTo(output);
            }
        }

        [Test]
        public async Task ContentPathIsDerivedFromWhereAFileIsOnDisk()
        {
            using var temp = new TempFolder();

            var mod = temp.CreateMod("csgo");
            var addon = temp.CreateFolder("game", "csgo_addons", "my_addon");

            using (Assert.Multiple())
            {
                await Assert.That(ContentPaths.GetContentPathFromDisk(Path.Join(mod, "models", "props", "crate.vmdl_c"))).IsEqualTo("models/props/crate.vmdl_c");
                await Assert.That(ContentPaths.GetContentPathFromDisk(Path.Join(mod, "materials", "models", "crate.vmat_c"))).IsEqualTo("materials/models/crate.vmat_c");
                await Assert.That(ContentPaths.GetContentPathFromDisk(Path.Join(addon, "custom", "thing.vmdl_c"))).IsEqualTo("custom/thing.vmdl_c");
                await Assert.That(ContentPaths.GetContentPathFromDisk(Path.Join(temp.FullName, "random", "models", "crate.vmdl_c"))).IsEqualTo("models/crate.vmdl_c");
                await Assert.That(ContentPaths.GetContentPathFromDisk(Path.Join(temp.FullName, "random", "materials", "models", "crate.vmat_c"))).IsEqualTo("materials/models/crate.vmat_c");
                await Assert.That(ContentPaths.GetContentPathFromDisk(Path.Join(temp.FullName, "random", "crate.vmdl_c"))).IsNull();
                await Assert.That(ContentPaths.GetContentPathFromDiskOrFileName(Path.Join(temp.FullName, "random", "crate.vmdl_c"), warning: null)).IsEqualTo("crate.vmdl_c");
                await Assert.That(ContentPaths.FindContentRoot(Path.Join(mod, "models", "props"))).IsEqualTo(mod);
                await Assert.That(ContentPaths.FindContentRoot(addon)).IsEqualTo(addon);
                await Assert.That(ContentPaths.FindContentRoot(Path.Join(temp.FullName, "random"))).IsNull();
            }
        }

        [Test]
        public async Task ContentPathsAreBuiltWithForwardSlashes()
        {
            using (Assert.Multiple())
            {
                await Assert.That(ContentPaths.TrimCompiledSuffix("models/crate.vmdl_c")).IsEqualTo("models/crate.vmdl");
                await Assert.That(ContentPaths.TrimCompiledSuffix("models/crate.vmdl_C")).IsEqualTo("models/crate.vmdl");
                await Assert.That(ContentPaths.TrimCompiledSuffix("models/crate.vmdl")).IsEqualTo("models/crate.vmdl");
                await Assert.That(ContentPaths.TrimCompiledSuffix("vmdl_c".AsSpan()).ToString()).IsEqualTo("vmdl");
                await Assert.That(ContentPaths.HasCompiledSuffix("models/crate.vmdl")).IsFalse();
                await Assert.That(ContentPaths.Join("models\\props", "crate_body.dmx")).IsEqualTo("models/props/crate_body.dmx");
                await Assert.That(ContentPaths.Join(string.Empty, "crate_body.dmx")).IsEqualTo("crate_body.dmx");
                await Assert.That(ContentPaths.ChangeExtension("models\\crate.vmdl", ".dmx")).IsEqualTo("models/crate.dmx");
                await Assert.That(ContentPaths.GetDirectoryName("maps/de_dust2/world.vwrld")).IsEqualTo("maps/de_dust2");
                await Assert.That(ContentPaths.GetDirectoryName("world.vwrld")).IsEqualTo(string.Empty);
                await Assert.That(ContentPaths.GetCommonDirectory(["models/chicken/chicken.vmdl", "models/chicken/anims/idle.vnmclip"])).IsEqualTo("models/chicken");
                await Assert.That(ContentPaths.GetCommonDirectory(["models/chicken/chicken.vmdl", "materials/chicken.vmat"])).IsEqualTo(string.Empty);
                await Assert.That(ContentPaths.GetRelativePath(Path.GetTempPath(), Path.Join(Path.GetTempPath(), "models", "crate.vmdl"))).IsEqualTo("models/crate.vmdl");
            }
        }

        [Test]
        public async Task NormalizeResolvesDotsAndBringsEscapingPathsBackUnderTheRoot()
        {
            var absolutePath = Path.Join(Path.GetTempPath(), "materials", "crate.png");

            using (Assert.Multiple())
            {
                await Assert.That(ContentPaths.Normalize("models\\props\\.\\crate.dmx", out var escaped)).IsEqualTo("models/props/crate.dmx");
                await Assert.That(escaped).IsFalse();

                await Assert.That(ContentPaths.Normalize("../../models/crate.dmx", out escaped)).IsEqualTo("models/crate.dmx");
                await Assert.That(escaped).IsTrue();

                await Assert.That(ContentPaths.Normalize("models/props/../crate.dmx", out escaped)).IsEqualTo("models/crate.dmx");
                await Assert.That(escaped).IsFalse();

                await Assert.That(ContentPaths.Normalize("models/../../crate.dmx", out escaped)).IsEqualTo("crate.dmx");
                await Assert.That(escaped).IsTrue();

                await Assert.That(ContentPaths.Normalize(absolutePath, out escaped)).IsEqualTo(Path.GetRelativePath(Path.GetPathRoot(absolutePath)!, absolutePath).Replace('\\', '/'));
                await Assert.That(escaped).IsTrue();
            }
        }
    }
}
