using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests;

/// <summary>
/// Decompiles models from locally installed Source 2 games and validates that the decompiled
/// source recompiles successfully with the game's own resourcecompiler.
///
/// These tests detect local Source 2 Workshop Tools installations through Steam and are
/// skipped when no usable installation is present (e.g. on the CI).
/// </summary>
[ClassDataSource<ResourceCompilerTest.GameInstallations>(Shared = SharedType.PerClass)]
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

        public void Dispose()
        {
            FileLoader.Dispose();
            Package.Dispose();
        }
    }

    /// <summary>
    /// Lazily opens and shares each game's Workshop Tools installation across this fixture's test
    /// cases, disposing them and their staged test addons once every test has finished.
    /// </summary>
    public sealed class GameInstallations : IAsyncDisposable
    {
        private readonly Dictionary<int, GameInstallation> installations = [];

        public GameInstallation Get(WorkshopToolsGame game)
        {
            if (installations.TryGetValue(game.AppId, out var cached))
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
                Skip.Test($"{game.Name} (appid {game.AppId}) is not installed.");
            }

            var gamePath = steamGame.Value.GamePath;
            var resourceCompiler = Path.Combine(gamePath, "game", "bin", "win64", "resourcecompiler.exe");
            if (!File.Exists(resourceCompiler))
            {
                Skip.Test($"{game.Name} is installed without its Workshop Tools ({resourceCompiler} does not exist).");
            }

            var pakPath = Path.Combine(gamePath, "game", game.ModFolder, "pak01_dir.vpk");
            if (!File.Exists(pakPath))
            {
                Skip.Test($"{pakPath} does not exist.");
            }

            var package = new Package();
            package.Read(pakPath);

            var installation = new GameInstallation
            {
                ResourceCompilerPath = resourceCompiler,
                Package = package,
                FileLoader = new GameFileLoader(package, pakPath),
                ContentAddonPath = Path.Combine(gamePath, "content", game.ModFolder + "_addons", TestAddonName),
                GameAddonPath = Path.Combine(gamePath, "game", game.ModFolder + "_addons", TestAddonName),
            };

            installations[game.AppId] = installation;
            return installation;
        }

        public ValueTask DisposeAsync()
        {
            foreach (var installation in installations.Values)
            {
                installation.Dispose();

                if (Directory.Exists(installation.ContentAddonPath))
                {
                    Directory.Delete(installation.ContentAddonPath, recursive: true);
                }

                if (Directory.Exists(installation.GameAddonPath))
                {
                    Directory.Delete(installation.GameAddonPath, recursive: true);
                }
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

        using var contentFile = FileExtract.Extract(resource, installation.FileLoader);
        await Assert.That(contentFile.Data).IsNotEmpty().Because($"Decompiling {assetPath} produced no vmdl data.");

        // Stage the decompiled source into the test addon at the original relative path,
        // so the compiled output lands at the same relative path on the game side.
        var vmdlPath = Path.Combine(installation.ContentAddonPath, assetPath[..^"_c".Length]);
        WriteContentFile(contentFile, vmdlPath);

        var (exitCode, compilerOutput) = RunResourceCompiler(installation.ResourceCompilerPath, vmdlPath);
        var compiledPath = Path.Combine(installation.GameAddonPath, assetPath);

        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsZero().Because($"resourcecompiler failed for {assetPath}.\n{Tail(compilerOutput)}");
            await Assert.That(File.Exists(compiledPath)).IsTrue().Because($"resourcecompiler produced no output for {assetPath}.\n{Tail(compilerOutput)}");
        }

        // The compiled output must load back as a valid model resource.
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
            // Morphs may not be lost wholesale. The exact controller set is deliberately not
            // compared: the compiler derives an implicit controller from every raw delta state,
            // so a recompiled model legitimately exposes extra single-side controllers next to
            // the authored paired ones (eyeUp/eyeDown next to eyeDownAndUp), and rules using ops
            // with no ModelDoc expression equivalent cannot be reconstructed.
            if (GetFlexControllerNames(original).Count > 0)
            {
                await Assert.That(GetFlexControllerNames(recompiled))
                    .IsNotEmpty()
                    .Because($"{assetPath}: flex controllers lost after recompile.");
            }

            // Material groups (skins) may not be lost.
            var originalMaterialGroups = GetMaterialGroupNames(originalModel);
            if (originalMaterialGroups.Count > 0)
            {
                await Assert.That(GetMaterialGroupNames(recompiledModel).ToHashSet())
                    .IsSupersetOf(originalMaterialGroups)
                    .Because($"{assetPath}: material groups lost after recompile.");
            }

            // No sequence may be lost. Implicit '@'-prefixed sequences are compiler-generated
            // (e.g. turn lookFrame layers) and are allowed to differ.
            var originalSequences = GetSequenceNames(original);
            if (originalSequences.Count > 0)
            {
                await Assert.That(GetSequenceNames(recompiled).ToHashSet())
                    .IsSupersetOf(originalSequences)
                    .Because($"{assetPath}: sequences lost after recompile.");
            }

            // Cloth: the soft-body data must survive.
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
            ? (morph.Data.GetArray("m_FlexControllers") ?? []).Select(static controller => controller.GetStringProperty("m_szName")).ToList()
            : [];

    private static List<string> GetMaterialGroupNames(Model model)
        => (model.Data.GetArray("m_materialGroups") ?? [])
            .Where(static group => group.GetArray<string>("m_materials") is { Length: > 0 })
            .Select(static group => group.GetStringProperty("m_name"))
            .ToList();

    private static List<string> GetSequenceNames(Resource resource)
    {
        if (resource.GetBlockByType(BlockType.ASEQ) is not KeyValuesOrNTRO sequenceData)
        {
            return [];
        }

        return (sequenceData.Data.GetArray("m_localS1SeqDescArray") ?? [])
            .Select(static sequence => sequence.GetStringProperty("m_sName"))
            .Where(static name => !string.IsNullOrEmpty(name) && !name.StartsWith('@'))
            .ToList();
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

    private static (int ExitCode, string Output) RunResourceCompiler(string resourceCompilerPath, string vmdlPath)
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
        startInfo.ArgumentList.Add(vmdlPath);

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var outputLock = new object();

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
            Assert.Fail($"resourcecompiler timed out after {CompileTimeoutMs / 1000}s compiling {vmdlPath}.\n{Tail(output.ToString())}");
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
