using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using ValveResourceFormat.DemoPlayback;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests;

[TestFixture]
public class DemoPlayerModelDiagnostic
{
    [Test]
    [Explicit("Manual diagnostic for player model loading")]
    public async Task PrintPlayerModelResolutionForDemo()
    {
        var demoPath = @"c:\Users\ayden\Documents\Github Projects\cs2 viewer\ValveResourceFormat\demos\yashfartt.dem";
        Assert.That(File.Exists(demoPath), Is.True, $"Demo not found: {demoPath}");

        var gameInfo = @"F:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo\gameinfo.gi";
        if (!File.Exists(gameInfo))
        {
            Assert.Ignore($"CS2 gameinfo not found at {gameInfo}");
        }

        using var loader = new GameFileLoader(null, gameInfo);
        var fileAccess = new CsDemoFileAccess(loader);
        var resolver = new CsDemoPlayerModelResolver(fileAccess);

        using var playback = await CsDemoPlayback.LoadAsync(demoPath);
        var frame = await playback.SeekToTickAsync(playback.Summary.TickCount / 2);

        var outputPath = Path.Combine(Path.GetTempPath(), "vrf-player-model-diag.txt");
        using var writer = new StreamWriter(outputPath);
        void Log(string line)
        {
            writer.WriteLine(line);
            Console.WriteLine(line);
        }

        Log($"Map: {playback.Summary.MapName}");
        Log($"Players alive: {frame.Players.Count(static p => p.IsAlive)}");

        foreach (var player in frame.Players.Where(static p => p.IsAlive).Take(10))
        {
            var resolution = resolver.Resolve(player, playback.Summary.MapName);
            var resource = loader.LoadFileCompiled(resolution.ResolvedModelPath);
            var model = resource?.DataBlock as Model;

            Log(
                $"slot={player.Slot} team={player.Team} name={player.Name} char={player.PawnCharacterName} def={player.PawnCharacterDefIndex} -> {resolution.ResolvedModelPath} ({resolution.FallbackReason}) loaded={model != null}");

            foreach (var candidate in CsDemoPlayerModelPaths.BuildCandidatesFromCharacterName(player.PawnCharacterName))
            {
                Log($"  candidate {candidate} exists={fileAccess.ModelExists(candidate)}");
            }
        }

        Log($"Wrote diagnostic to {outputPath}");
    }
}
