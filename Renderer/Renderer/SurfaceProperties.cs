using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer;

/// <summary>Physics and sound properties of each surface, from <c>surfaceproperties.vsurf</c>.</summary>
public sealed class SurfaceProperties
{
    /// <summary>One surface. A sound is <see langword="null"/> when neither the surface nor its bases name one.</summary>
    /// <param name="Name">Surface property name, e.g. <c>wood_crate</c>.</param>
    /// <param name="Friction">Contact friction.</param>
    /// <param name="Elasticity">Restitution factor.</param>
    /// <param name="Density">Density that body mass is computed from.</param>
    /// <param name="BreakSound">Sound event played on break.</param>
    /// <param name="ImpactSoftSound">Sound event of a light knock.</param>
    /// <param name="ImpactHardSound">Sound event of a hard hit.</param>
    /// <param name="ImpactHardThreshold">Impact volume, from 0 to 1, at which a hit sounds hard.</param>
    public sealed record Surface(
        string Name,
        float Friction,
        float Elasticity,
        float Density,
        string? BreakSound = null,
        string? ImpactSoftSound = null,
        string? ImpactHardSound = null,
        float ImpactHardThreshold = 0.5f);

    /// <summary>Surface used for unknown hashes, matching the <c>default</c> entry.</summary>
    public static readonly Surface Fallback = new("default", 0.8f, 0.25f, 2000f);

    private readonly Dictionary<uint, Surface> surfacesByHash = [];

    /// <summary>Finds a surface by name hash, or returns <see cref="Fallback"/>.</summary>
    public Surface Find(uint nameHash)
        => surfacesByHash.TryGetValue(nameHash, out var surface) ? surface : Fallback;

    /// <summary>Loads the surface property table, or returns <see langword="null"/> when there is none.</summary>
    public static SurfaceProperties? Load(IFileLoader fileLoader)
    {
        if (fileLoader.LoadFileCompiled("surfaceproperties/surfaceproperties.vsurf")?.DataBlock is not BinaryKV3 kv3)
        {
            return null;
        }

        var table = new SurfaceProperties();
        var baseHashes = new Dictionary<uint, uint>();

        // Physics fields are flattened into each entry by the compiler, sounds are not
        foreach (var entry in kv3.Data.Root.GetArray("SurfacePropertiesList"))
        {
            var name = entry.GetStringProperty("surfacePropertyName");

            if (name == null)
            {
                continue;
            }

            var hash = (uint)entry.GetUnsignedIntegerProperty("m_nameHash");
            var physics = entry.GetSubCollection("physics");
            var sounds = entry.GetSubCollection("audiosounds");
            var audio = entry.GetSubCollection("audioparams");

            table.surfacesByHash[hash] = new Surface(
                name,
                physics?.GetFloatProperty("friction", Fallback.Friction) ?? Fallback.Friction,
                physics?.GetFloatProperty("elasticity", Fallback.Elasticity) ?? Fallback.Elasticity,
                physics?.GetFloatProperty("density", Fallback.Density) ?? Fallback.Density,
                NonEmpty(sounds?.GetStringProperty("break")),
                NonEmpty(sounds?.GetStringProperty("impactsoft")),
                NonEmpty(sounds?.GetStringProperty("impacthard")),
                audio?.GetFloatProperty("impactHardThreshold", Fallback.ImpactHardThreshold) ?? Fallback.ImpactHardThreshold);

            baseHashes[hash] = (uint)entry.GetUnsignedIntegerProperty("m_baseNameHash");
        }

        var inherited = new List<(uint Hash, Surface Surface)>();

        foreach (var (hash, surface) in table.surfacesByHash)
        {
            var withBase = surface with
            {
                BreakSound = surface.BreakSound ?? table.FindBaseSound(baseHashes, hash, static s => s.BreakSound),
                ImpactSoftSound = surface.ImpactSoftSound ?? table.FindBaseSound(baseHashes, hash, static s => s.ImpactSoftSound),
                ImpactHardSound = surface.ImpactHardSound ?? table.FindBaseSound(baseHashes, hash, static s => s.ImpactHardSound),
            };

            if (withBase != surface)
            {
                inherited.Add((hash, withBase));
            }
        }

        foreach (var (hash, surface) in inherited)
        {
            table.surfacesByHash[hash] = surface;
        }

        return table.surfacesByHash.Count > 0 ? table : null;
    }

    private const int MaxBaseDepth = 8;

    private string? FindBaseSound(Dictionary<uint, uint> baseHashes, uint hash, Func<Surface, string?> sound)
    {
        for (var depth = 0; depth < MaxBaseDepth && baseHashes.TryGetValue(hash, out var baseHash) && baseHash != 0; depth++)
        {
            if (surfacesByHash.TryGetValue(baseHash, out var baseSurface) && sound(baseSurface) is { } found)
            {
                return found;
            }

            hash = baseHash;
        }

        return null;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
