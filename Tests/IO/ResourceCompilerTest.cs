using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.IO;

/// <summary>
/// Decompiles models and mix graphs from Source 2 games and validates that the decompiled source
/// recompiles successfully with the game's own resourcecompiler.
///
/// Each game is found through its Steam installation, and its tests are skipped when it is not
/// installed or has no Workshop Tools. The tests stage and compile a temporary addon
/// (<c>vrf_recompile_test_&lt;pid&gt;</c>) inside the installation's <c>content</c> and <c>game</c>
/// addon folders, and delete it after every test.
///
/// Opt in, run with <c>dotnet test --treenode-filter "/*/*/ResourceCompilerTest/*"</c>.
/// </summary>
[Explicit]
[ClassDataSource<GameInstallations>(Shared = SharedType.PerClass)]
[NotInParallel(nameof(ResourceCompilerTest))]
public class ResourceCompilerTest(ResourceCompilerTest.GameInstallations installations)
{
    /// <summary>
    /// Staged under the shared game installation, so the name carries the process id to keep
    /// concurrent test runs out of each other's addon.
    /// </summary>
    private static readonly string TestAddonName = $"vrf_recompile_test_{Environment.ProcessId}";
    private const int CompileTimeoutMs = 10 * 60 * 1000;

    public sealed record WorkshopToolsGame(int AppId, string Name, string ModFolder)
    {
        public override string ToString() => Name;
    }

    private static readonly WorkshopToolsGame Dota2 = new(570, "Dota 2", "dota");
    private static readonly WorkshopToolsGame CounterStrike2 = new(730, "Counter-Strike 2", "csgo");

    // Models exercising the vmdl features the exporter emits: morphs and flex rules, activity
    // modifiers, turn/1D blend sequences, material groups (skins), LODs, cloth chains and sheets.
    private static readonly (WorkshopToolsGame Game, string AssetPath)[] ModelCases = [
        (Dota2, "models/heroes/legion_commander/legion_commander.vmdl_c"),
        (Dota2, "models/heroes/dark_willow/dark_willow.vmdl_c"),
        (Dota2, "models/heroes/primal_beast/primal_beast_base.vmdl_c"),
        (Dota2, "models/items/legion_commander/dark_carnival_legion_commander/dark_carnival_legion_commander_base.vmdl_c"),
        (CounterStrike2, "models/chicken/chicken.vmdl_c"),
        (CounterStrike2, "agents/models/ctm_sas/ctm_sas.vmdl_c"),
        (CounterStrike2, "weapons/models/knife/knife_bayonet/weapon_knife_bayonet.vmdl_c"),
        (CounterStrike2, "weapons/keychains/missinglink/vmdl/kc_missinglink_bigfoot.vmdl_c"),
    ];

    public sealed class GameInstallation : IDisposable
    {
        public required string ResourceCompilerPath { get; init; }
        public required Package Package { get; init; }
        public required GameFileLoader FileLoader { get; init; }

        /// <summary>content/&lt;mod&gt;_addons/&lt;addon&gt; - decompiled sources are staged here.</summary>
        public required string ContentAddonPath { get; init; }

        /// <summary>game/&lt;mod&gt;_addons/&lt;addon&gt; - resourcecompiler writes compiled resources here.</summary>
        public required string GameAddonPath { get; init; }

        public void DeleteTestAddon()
        {
            if (Directory.Exists(ContentAddonPath))
            {
                Directory.Delete(ContentAddonPath, recursive: true);
            }

            if (Directory.Exists(GameAddonPath))
            {
                Directory.Delete(GameAddonPath, recursive: true);
            }
        }

        public void Dispose()
        {
            FileLoader.Dispose();
            Package.Dispose();
            DeleteTestAddon();
        }
    }

    /// <summary>
    /// Lazily opens and shares each game's installation across this fixture's test cases, disposing
    /// them once every test has finished.
    /// </summary>
    public sealed class GameInstallations : IAsyncDisposable
    {
        private readonly Dictionary<WorkshopToolsGame, GameInstallation> installations = [];

        public GameInstallation Get(WorkshopToolsGame game)
        {
            if (installations.TryGetValue(game, out var cached))
            {
                return cached;
            }

            if (!OperatingSystem.IsWindows())
            {
                Skip.Test("Source 2 Workshop Tools are only available on Windows.");
            }

            var steamGame = GameFolderLocator.FindSteamGameByAppId(game.AppId);
            if (!steamGame.HasValue)
            {
                Skip.Test($"Steam game with AppId {game.AppId} not present.");
            }

            var gamePath = steamGame.Value.GamePath;
            var resourceCompiler = Path.Combine(gamePath, "game", "bin", "win64", "resourcecompiler.exe");
            if (!File.Exists(resourceCompiler))
            {
                Skip.Test($"{game.Name} has no Workshop Tools ({resourceCompiler} does not exist).");
            }

            var pakPath = Path.Combine(gamePath, "game", game.ModFolder, "pak01_dir.vpk");
            if (!File.Exists(pakPath))
            {
                Skip.Test($"{pakPath} does not exist.");
            }

            var package = Package.Open(pakPath);

            var installation = new GameInstallation
            {
                ResourceCompilerPath = resourceCompiler,
                Package = package,
                FileLoader = new GameFileLoader(package, pakPath),
                ContentAddonPath = Path.Combine(gamePath, "content", game.ModFolder + "_addons", TestAddonName),
                GameAddonPath = Path.Combine(gamePath, "game", game.ModFolder + "_addons", TestAddonName),
            };

            installations[game] = installation;
            return installation;
        }

        public ValueTask DisposeAsync()
        {
            foreach (var installation in installations.Values)
            {
                installation.Dispose();
            }

            installations.Clear();

            return ValueTask.CompletedTask;
        }
    }

    [Test, MethodDataSource(nameof(ModelCases))]
    public async Task RecompileDecompiledModel((WorkshopToolsGame Game, string AssetPath) testCase)
    {
        var (game, assetPath) = testCase;
        var installation = installations.Get(game);

        var entry = installation.Package.FindEntry(assetPath);
        if (entry == null)
        {
            Skip.Test($"{assetPath} no longer exists in {game.Name}'s pak01_dir.vpk.");
        }

        installation.Package.ReadEntry(entry, out var rawFileData);

        using var resource = new Resource { FileName = assetPath };
        resource.Read(new MemoryStream(rawFileData));

        await Assert.That(resource.DataBlock).IsAssignableTo<Model>().Because($"{assetPath} is not a model resource.");

        try
        {
            await RecompileAndCompare(installation, resource, assetPath);
        }
        finally
        {
            installation.DeleteTestAddon();
        }
    }

    // Every graph is compiled in one run, since graphs inline the other graphs of their folder as subgraphs.
    [Test]
    public async Task RecompileDecompiledGraphs()
    {
        const string GraphsFolder = "sounds/graphs";

        var installation = installations.Get(CounterStrike2);

        // The compiler writes every graph as a sound, vmix_c files next to them come from older compiles
        var entries = installation.Package.Entries!["vsnd_c"]
            .Where(static entry => string.Equals(entry.DirectoryName, GraphsFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(entries).IsNotEmpty();

        Resource ReadEntry(PackageEntry entry)
        {
            installation.Package.ReadEntry(entry, out var rawFileData);

            var resource = new Resource { FileName = entry.GetFullPath() };
            resource.Read(new MemoryStream(rawFileData));
            return resource;
        }

        try
        {
            var contentFolder = Path.Combine(installation.ContentAddonPath, GraphsFolder);

            foreach (var entry in entries)
            {
                using var resource = ReadEntry(entry);
                using var contentFile = FileExtract.Extract(resource, installation.FileLoader);

                WriteContentFile(contentFile, Path.Combine(contentFolder, $"{entry.FileName}.vmix"));
            }

            var (_, compilerOutput) = RunResourceCompiler(installation.ResourceCompilerPath, Path.Combine(contentFolder, "*.vmix"));
            var differences = new List<string>();

            foreach (var entry in entries)
            {
                using var resource = ReadEntry(entry);
                var compiledPath = Path.Combine(installation.GameAddonPath, resource.FileName!);

                if (!File.Exists(compiledPath))
                {
                    differences.Add($"{resource.FileName}: did not compile");
                    continue;
                }

                using var recompiled = new Resource { FileName = compiledPath };
                recompiled.Read(compiledPath);

                if (FindBlockDifference(resource, recompiled) is { } difference)
                {
                    differences.Add($"{resource.FileName}: {difference}");
                }
            }

            await Assert.That(differences).IsEmpty().Because($"{string.Join('\n', differences)}\n{Tail(compilerOutput)}");
        }
        finally
        {
            installation.DeleteTestAddon();
        }
    }

    /// <summary>
    /// Finds the first difference between the compiled blocks of two resources, leaving out the blocks
    /// that record how the file was compiled and what it depends on.
    /// </summary>
    private static string? FindBlockDifference(Resource original, Resource recompiled)
    {
        static List<Block> ContentBlocks(Resource resource)
            => [.. resource.Blocks.Where(static block => block.Type is not (BlockType.RED2 or BlockType.REDI or BlockType.RERL or BlockType.NTRO))];

        var originalBlocks = ContentBlocks(original);
        var recompiledBlocks = ContentBlocks(recompiled);

        if (!originalBlocks.Select(static block => block.Type).SequenceEqual(recompiledBlocks.Select(static block => block.Type)))
        {
            return $"blocks {string.Join(' ', originalBlocks.Select(static block => block.Type))} became {string.Join(' ', recompiledBlocks.Select(static block => block.Type))}";
        }

        foreach (var (originalBlock, recompiledBlock) in originalBlocks.Zip(recompiledBlocks))
        {
            var originalLines = originalBlock.ToString().Split('\n');
            var recompiledLines = recompiledBlock.ToString().Split('\n');

            for (var i = 0; i < Math.Max(originalLines.Length, recompiledLines.Length); i++)
            {
                var originalLine = i < originalLines.Length ? originalLines[i].Trim() : "<end>";
                var recompiledLine = i < recompiledLines.Length ? recompiledLines[i].Trim() : "<end>";

                if (originalLine != recompiledLine)
                {
                    return $"{originalBlock.Type} line {i + 1}: \"{originalLine}\" became \"{recompiledLine}\"";
                }
            }
        }

        return null;
    }

    private static async Task RecompileAndCompare(GameInstallation installation, Resource resource, string assetPath)
    {
        using var contentFile = FileExtract.Extract(resource, installation.FileLoader);
        await Assert.That(contentFile.Data).IsNotEmpty().Because($"Decompiling {assetPath} produced no vmdl data.");

        var vmdlPath = Path.Combine(installation.ContentAddonPath, assetPath[..^"_c".Length]);
        WriteContentFile(contentFile, vmdlPath);

        var (exitCode, compilerOutput) = RunResourceCompiler(installation.ResourceCompilerPath, vmdlPath);
        var compiledPath = Path.Combine(installation.GameAddonPath, assetPath);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsZero().Because($"resourcecompiler failed for {assetPath}.\n{Tail(compilerOutput)}");
            await Assert.That(File.Exists(compiledPath)).IsTrue().Because($"resourcecompiler produced no output for {assetPath}.\n{Tail(compilerOutput)}");
        }

        using var recompiled = new Resource { FileName = compiledPath };
        recompiled.Read(compiledPath);
        await Assert.That(recompiled.DataBlock).IsAssignableTo<Model>().Because($"Recompiled {assetPath} is not a valid model resource.");

        await CompareRecompiledModel(resource, recompiled, assetPath);
    }

    /// <summary>
    /// Compares the recompiled model against the original on the data the decompiled source is
    /// expected to carry through the round-trip. Deliberately coarse (nothing may be lost) rather
    /// than byte-exact: compiled output differs across compiler versions.
    /// </summary>
    private static async Task CompareRecompiledModel(Resource original, Resource recompiled, string assetPath)
    {
        var originalModel = (Model)original.DataBlock!;
        var recompiledModel = (Model)recompiled.DataBlock!;

        using (Assert.Multiple())
        {
            // The compiler derives an implicit controller from every raw delta state, so a recompiled
            // model can expose single-side controllers (eyeUp, eyeDown) next to the paired ones.
            var originalControllers = GetFlexControllerNames(original);
            if (originalControllers.Count > 0)
            {
                await Assert.That(GetFlexControllerNames(recompiled).ToHashSet())
                    .IsSupersetOf(originalControllers)
                    .Because($"{assetPath}: flex controllers lost after recompile.");
            }

            var originalMaterialGroups = GetMaterialGroupNames(originalModel);
            if (originalMaterialGroups.Count > 0)
            {
                await Assert.That(GetMaterialGroupNames(recompiledModel).ToHashSet())
                    .IsSupersetOf(originalMaterialGroups)
                    .Because($"{assetPath}: material groups lost after recompile.");
            }

            // Implicit '@'-prefixed sequences are compiler-generated (e.g. turn lookFrame layers).
            var originalSequences = GetSequenceNames(original);
            if (originalSequences.Count > 0)
            {
                await Assert.That(GetSequenceNames(recompiled).ToHashSet())
                    .IsSupersetOf(originalSequences)
                    .Because($"{assetPath}: sequences lost after recompile.");
            }

            if (originalModel.GetEmbeddedPhys()?.FeModel != null)
            {
                await Assert.That(recompiledModel.GetEmbeddedPhys()?.FeModel)
                    .IsNotNull()
                    .Because($"{assetPath}: cloth (FeModel) lost after recompile.");
            }
        }
    }

    private static List<string> GetFlexControllerNames(Resource resource)
        => resource.GetBlockByType(BlockType.MRPH) is Morph morph
            ? [.. (morph.Data.GetArray("m_FlexControllers") ?? []).Select(static controller => controller.GetStringProperty("m_szName"))]
            : [];

    private static List<string> GetMaterialGroupNames(Model model)
        => [.. (model.Data.GetArray("m_materialGroups") ?? [])
            .Where(static group => group.GetArray<string>("m_materials") is { Length: > 0 })
            .Select(static group => group.GetStringProperty("m_name"))];

    private static List<string> GetSequenceNames(Resource resource)
    {
        if (resource.GetBlockByType(BlockType.ASEQ) is not KeyValuesOrNTRO sequenceData)
        {
            return [];
        }

        return [.. (sequenceData.Data.GetArray("m_localS1SeqDescArray") ?? [])
            .Select(static sequence => sequence.GetStringProperty("m_sName"))
            .Where(static name => !string.IsNullOrEmpty(name) && !name.StartsWith('@'))];
    }

    private static void WriteContentFile(ContentFile contentFile, string outFilePath)
    {
        var outFolder = Path.GetDirectoryName(outFilePath)!;
        Directory.CreateDirectory(outFolder);

        if (contentFile.Data != null)
        {
            File.WriteAllBytes(outFilePath, contentFile.Data);
        }

        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            WriteContentFile(additionalFile, Path.Combine(outFolder, Path.GetFileName(additionalFile.FileName)));
        }

        foreach (var subFile in contentFile.SubFiles)
        {
            var subFileData = subFile.Extract?.Invoke();

            if (subFileData is { Length: > 0 })
            {
                File.WriteAllBytes(Path.Combine(outFolder, subFile.FileName), subFileData);
            }
        }
    }

    private static (int ExitCode, string Output) RunResourceCompiler(string resourceCompilerPath, string inputPath)
    {
        var startInfo = new ProcessStartInfo(resourceCompilerPath)
        {
            WorkingDirectory = Path.GetDirectoryName(resourceCompilerPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-nop4");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var outputLock = new Lock();

        void OnDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                lock (outputLock)
                {
                    output.AppendLine(e.Data);
                }
            }
        }

        process.OutputDataReceived += OnDataReceived;
        process.ErrorDataReceived += OnDataReceived;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(CompileTimeoutMs))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"resourcecompiler timed out after {CompileTimeoutMs / 1000}s compiling {inputPath}.\n{Tail(output.ToString())}");
        }

        // Flush the remaining async output after the process has exited.
        process.WaitForExit();

        lock (outputLock)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    private static string Tail(string text, int maxLength = 8000)
        => text.Length <= maxLength ? text : "[...]" + text[^maxLength..];
}
