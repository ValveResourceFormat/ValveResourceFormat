using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;

namespace Tests;

/// <summary>
/// Manual regression tests used for large game files not included in CI.
/// </summary>
public class GameExtractTests
{
    static readonly (int AppId, string Game, string AssetName)[] AnimGraphs = [
        (730, "game/csgo", "characters/models/shared/animgraphs/player_ct.vanmgrph")
    ];

    [Test, MethodDataSource(nameof(AnimGraphs))]
    public async Task ExtractAnimGraph((int appId, string gameFolder, string assetName) testCase)
    {
        var testLocalPath = Path.Combine("Files", "GameExtract", $"{testCase.appId}", Path.GetFileName(testCase.assetName));
        var outputPathRepo = Path.Combine(TestContext.TestDirectory!, "../../", testLocalPath);

        // Use this to create or update correct output files
        var UpdateFiles = false;

        if (UpdateFiles == false && !File.Exists(outputPathRepo))
        {
            Skip.Test($"Sample output file not present.");
        }

        var game = GameFolderLocator.FindSteamGameByAppId(testCase.appId);
        if (!game.HasValue)
        {
            Skip.Test($"Steam game with AppId {testCase.appId} not present.");
        }

        var gamePath = game.Value.GamePath;
        var pak01 = Path.Combine(gamePath, testCase.gameFolder, "pak01_dir.vpk");

        using var archive = new Package();
        archive.Read(pak01);

        using var fileLoader = new GameFileLoader(archive, null);
        using var asset = fileLoader.LoadFileCompiled(testCase.assetName);

        if (asset == null)
        {
            Skip.Test($"{testCase.assetName} no longer exists on {pak01}.");
        }

        var outputPath = Path.Combine(TestContext.TestDirectory!, testLocalPath);

        using var extractor = new AnimationGraphExtract(asset, fileLoader);
        var content = extractor.ToContentFile();

        await Assert.That(content.Data).IsNotNull().Because($"Failed to extract {testCase.assetName}.");
        Debug.Assert(content.Data != null);

        if (UpdateFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPathRepo)!);
            await File.WriteAllBytesAsync(outputPathRepo, content.Data);
            return;
        }

        // Compare with existing file
        var expectedKv3 = await File.ReadAllTextAsync(outputPath);
        var actualKv3 = System.Text.Encoding.UTF8.GetString(content.Data);

        await Assert.That(actualKv3).IsEqualTo(expectedKv3).Because($"Extracted file differs. {testCase.assetName}.");
    }
}
