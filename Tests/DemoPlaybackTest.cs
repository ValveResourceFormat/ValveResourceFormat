using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using ValveResourceFormat.DemoPlayback;

namespace Tests;

public class DemoPlaybackTest
{
    [Test]
    public void CharacterNameBuildsProfessionalModelPath()
    {
        var candidates = CsDemoPlayerModelPaths.BuildCandidatesFromCharacterName("customplayer_tm_professional_varf5").ToArray();

        Assert.That(candidates[0], Is.EqualTo("agents/models/tm_professional/tm_professional_varf5.vmdl"));
        Assert.That(candidates, Does.Contain("characters/models/tm_professional/tm_professional_varf5.vmdl"));
    }

    [Test]
    public void DetectsCs2DemoMagic()
    {
        ReadOnlySpan<byte> header = "PBDEMS2\0"u8;

        Assert.That(CsDemoFormat.IsAccepted(header, "match.dem"), Is.True);
    }

    [Test]
    public void DetectsDemoExtension()
    {
        Assert.That(CsDemoFormat.IsAccepted([0x00, 0x01, 0x02, 0x03], "match.dem"), Is.True);
    }

    [Test]
    public void RejectsUnknownHeaderAndExtension()
    {
        Assert.That(CsDemoFormat.IsAccepted([0x00, 0x01, 0x02, 0x03], "match.bin"), Is.False);
    }

    [Test]
    public void CharacterNameBuildsExpectedModelCandidates()
    {
        var candidates = CsDemoPlayerModelPaths.BuildCandidatesFromCharacterName("customplayer_ctm_st6_variantn").ToArray();

        Assert.That(candidates[0], Is.EqualTo("agents/models/ctm_st6/ctm_st6_variantn.vmdl"));
        Assert.That(candidates, Does.Contain("characters/models/ctm_st6/ctm_st6_variantn.vmdl"));
    }

    [Test]
    public void MapBasedCharacterNameProducesNoExactCandidates()
    {
        var candidates = CsDemoPlayerModelPaths.BuildCandidatesFromCharacterName("customplayer_ct_map_based");

        Assert.That(candidates, Is.Empty);
        Assert.That(CsDemoPlayerModelPaths.IsMapBasedCharacterName("customplayer_ct_map_based"), Is.True);
    }

    [Test]
    public void GetLoadCandidatesPrefersAgentsPath()
    {
        var candidates = CsDemoPlayerModelPaths.GetLoadCandidates("characters/models/ctm_fbi/ctm_fbi.vmdl").ToArray();

        Assert.That(candidates[0], Is.EqualTo("agents/models/ctm_fbi/ctm_fbi.vmdl"));
        Assert.That(candidates[1], Is.EqualTo("characters/models/ctm_fbi/ctm_fbi.vmdl"));
    }

    [Test]
    public void MapTeamDefaultUsesKnownMapTable()
    {
        Assert.That(
            CsDemoPlayerModelPaths.TryGetMapTeamDefault("de_dust2", CsDemoTeam.CounterTerrorist, out var ctModel),
            Is.True);
        Assert.That(ctModel, Is.EqualTo("characters/models/ctm_fbi/ctm_fbi.vmdl"));

        Assert.That(
            CsDemoPlayerModelPaths.TryGetMapTeamDefault("maps/de_dust2", CsDemoTeam.Terrorist, out var tModel),
            Is.True);
        Assert.That(tModel, Is.EqualTo("characters/models/tm_leet/tm_leet_varianta.vmdl"));
    }

    [Test]
    public void ResolverFallsBackToTeamDefaultWhenModelMissing()
    {
        var fileAccess = new CsDemoFileAccess(new NullFileLoader());
        var resolver = new CsDemoPlayerModelResolver(fileAccess);
        var player = new CsDemoPlayerState(
            0,
            1,
            "Test",
            CsDemoTeam.CounterTerrorist,
            true,
            100,
            0,
            Vector3.Zero,
            0,
            0,
            null,
            "customplayer_ctm_st6_variantn",
            5405);

        var resolution = resolver.Resolve(player, "de_unknown");

        Assert.That(resolution.FallbackReason, Is.EqualTo(CsDemoPlayerModelFallbackReason.GlobalTeamDefault));
        Assert.That(resolution.ResolvedModelPath, Is.EqualTo(CsDemoPlayerModelPaths.CtTeamDefault));
    }

    [Test]
    public void ResolverUsesMapDefaultForMapBasedCharacter()
    {
        var fileAccess = new CsDemoFileAccess(new NullFileLoader());
        var resolver = new CsDemoPlayerModelResolver(fileAccess);
        var player = new CsDemoPlayerState(
            0,
            1,
            "Test",
            CsDemoTeam.Terrorist,
            true,
            100,
            0,
            Vector3.Zero,
            0,
            0,
            null,
            "customplayer_tm_map_based",
            ushort.MaxValue);

        var resolution = resolver.Resolve(player, "de_dust2");

        Assert.That(resolution.FallbackReason, Is.EqualTo(CsDemoPlayerModelFallbackReason.MapTeamDefault));
        Assert.That(resolution.ResolvedModelPath, Is.EqualTo("characters/models/tm_leet/tm_leet_varianta.vmdl"));
    }

    [Test]
    public async Task DemoPlayerStateIncludesCharacterFieldsWhenDemoPresent()
    {
        var demoPath = FindDemoPath();
        if (demoPath == null)
        {
            Assert.Ignore("No sample demo available under demos/.");
        }

        using var playback = await CsDemoPlayback.LoadAsync(demoPath);
        var frame = await playback.SeekToTickAsync(playback.Summary.TickCount / 2);

        Assert.That(frame.Players, Is.Not.Empty);
        Assert.That(frame.Players.Any(static player => player.PawnCharacterName != null || player.PawnCharacterDefIndex != 0), Is.True);
    }

    [Test]
    public async Task DemoFrameIncludesActiveWeaponNamesWhenDemoPresent()
    {
        var demoPath = FindDemoPath("anim.dem") ?? FindDemoPath();
        if (demoPath == null)
        {
            Assert.Ignore("No sample demo available under demos/.");
        }

        using var playback = await CsDemoPlayback.LoadAsync(demoPath);
        var midTick = playback.Summary.TickCount / 2;
        var frame = await playback.SeekToTickAsync(midTick);
        var weapons = frame.Players
            .Where(static player => player.IsAlive && !string.IsNullOrWhiteSpace(player.ActiveWeapon))
            .Select(static player => player.ActiveWeapon!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        TestContext.WriteLine($"Demo: {Path.GetFileName(demoPath)} tick={midTick} weapons=[{string.Join(", ", weapons)}] fireEvents={frame.PlayerAnimEvents.Count(static e => e.Kind == CsDemoPlayerAnimEventKind.FirePrimary)}");

        Assert.That(weapons, Is.Not.Empty);
        Assert.That(weapons.All(static weapon => weapon.Length > 1), Is.True, "ActiveWeapon should be a class name, not a numeric token.");
    }

    private static string? FindDemoPath(string? fileName = null)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        var demosDirectory = Path.Combine(repoRoot, "demos");

        if (!Directory.Exists(demosDirectory))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var explicitPath = Path.Combine(demosDirectory, fileName);
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        return Directory.EnumerateFiles(demosDirectory, "*.dem").FirstOrDefault();
    }

    private sealed class NullFileLoader : ValveResourceFormat.IO.IFileLoader
    {
        public ValveResourceFormat.Resource? LoadFile(string file) => null;

        public ValveResourceFormat.Resource? LoadFileCompiled(string file) => null;

        public ValveResourceFormat.CompiledShader.ShaderCollection? LoadShader(string shaderName) => null;
    }
}
