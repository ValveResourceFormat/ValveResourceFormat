using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Gameplay;

// Step and land sound events per surface property, from scripts/surfaceproperties_footsteps.txt
internal sealed class FootstepSoundTable
{
    public readonly record struct SurfaceSounds(string StepLeft, string StepRight, string Land);

    // The viewer carries CT weapons, so it walks with CT feet
    private const string PlayerTable = "ct_player";

    // Water steps have no land event of their own, landing in water splashes like a water volume
    private const string WaterLandSound = "Land_WaterVol.StepLeft";

    private static readonly SurfaceSounds Fallback = new("CT_Concrete.StepLeft", "CT_Concrete.StepLeft", "Land_Concrete.StepLeft");

    private static readonly uint DefaultSurface = StringToken.Get("default");

    private readonly Dictionary<uint, SurfaceSounds> soundsBySurface = [];
    private readonly Dictionary<uint, uint> surfaceBases = [];

    // Plays the fallback sounds everywhere
    public FootstepSoundTable()
    {
    }

    public FootstepSoundTable(GameFileLoader fileLoader)
    {
        LoadSurfaceBases(fileLoader);
        LoadFootsteps(fileLoader);
    }

    private void LoadSurfaceBases(GameFileLoader fileLoader)
    {
        using var resource = fileLoader.LoadFileCompiled("surfaceproperties/surfaceproperties.vsurf");

        if (resource?.DataBlock is not BinaryKV3 kv3)
        {
            return;
        }

        foreach (var surface in kv3.Data.Root.GetArray("SurfacePropertiesList") ?? [])
        {
            if (surface.GetStringProperty("surfacePropertyName") is { } name && surface.GetStringProperty("base") is { } baseName)
            {
                surfaceBases[StringToken.Get(name)] = StringToken.Get(baseName);
            }
        }
    }

    private void LoadFootsteps(GameFileLoader fileLoader)
    {
        using var stream = fileLoader.GetFileStream("scripts/surfaceproperties_footsteps.txt");

        if (stream == null)
        {
            return;
        }

        var surfaces = KVDocumentExtensions.ParseKV3(stream).Root.GetSubCollection(PlayerTable)?.GetArray("SurfacePropertiesList") ?? [];

        foreach (var surface in surfaces)
        {
            // An entry that names no sound leaves the surface to its base
            if (surface.GetStringProperty("surfacePropertyName") is not { } name
                || surface.GetStringProperty("walkleft") is not { Length: > 0 } stepLeft)
            {
                continue;
            }

            var stepRight = surface.GetStringProperty("walkright") is { Length: > 0 } right ? right : stepLeft;

            soundsBySurface[StringToken.Get(name)] = new SurfaceSounds(stepLeft, stepRight, GetLandSound(stepLeft));
        }
    }

    // Each team step event has a land event for the same material, "CT_Wood.StepLeft" lands as "Land_Wood.StepLeft"
    private static string GetLandSound(string stepSound)
    {
        var material = stepSound[(stepSound.IndexOf('_', StringComparison.Ordinal) + 1)..];

        return material.StartsWith("Water.", StringComparison.OrdinalIgnoreCase)
            ? WaterLandSound
            : string.Concat("Land_", material);
    }

    public SurfaceSounds Find(uint surfacePropertyHash)
    {
        var hash = surfacePropertyHash;

        for (var depth = 0; depth < 16; depth++)
        {
            if (soundsBySurface.TryGetValue(hash, out var sounds))
            {
                return sounds;
            }

            if (!surfaceBases.TryGetValue(hash, out hash))
            {
                break;
            }
        }

        return soundsBySurface.GetValueOrDefault(DefaultSurface, Fallback);
    }
}
