using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// The game's surface property table, <c>surfaceproperties/surfaceproperties.vsurf</c>: what each
/// physics surface is made of (friction, elasticity, density) and what it sounds like when hit.
/// Physics shapes name their surface as a <see cref="StringToken"/> hash, which is the same hash
/// the table's <c>m_nameHash</c> carries, so the two link up without any name list of our own.
/// </summary>
public sealed class SurfaceProperties
{
    /// <summary>
    /// One surface: its physical response and its impact sound events. Sound names are
    /// <see langword="null"/> when the surface defines none.
    /// </summary>
    /// <param name="Name">The surface property name, e.g. <c>wood_crate</c>.</param>
    /// <param name="Friction">Contact friction.</param>
    /// <param name="Elasticity">Bounciness, a restitution factor.</param>
    /// <param name="Density">Material density, what the body's mass is computed from.</param>
    /// <param name="ImpactSoft">Sound event of a gentle impact.</param>
    /// <param name="ImpactHard">Sound event of a hard impact.</param>
    /// <param name="ImpactHardThreshold">
    /// How hard, as a fraction of the reference impact speed, a hit must be to count as hard.
    /// </param>
    public sealed record Surface(
        string Name,
        float Friction,
        float Elasticity,
        float Density,
        string? ImpactSoft,
        string? ImpactHard,
        float ImpactHardThreshold);

    /// <summary>
    /// What an unknown surface behaves as, mirroring the table's own <c>default</c> entry, for a
    /// shape whose hash the table does not carry.
    /// </summary>
    public static readonly Surface Fallback = new("default", 0.8f, 0.25f, 2000f, null, null, 0.5f);

    private readonly Dictionary<uint, Surface> surfacesByHash = [];

    /// <summary>
    /// Finds the surface a physics shape names, by the hash it names it with.
    /// </summary>
    /// <returns>The surface, or <see cref="Fallback"/> when the table does not know the hash.</returns>
    public Surface Find(uint nameHash)
        => surfacesByHash.TryGetValue(nameHash, out var surface) ? surface : Fallback;

    /// <summary>
    /// Loads the game's surface property table.
    /// </summary>
    /// <returns>The table, or <see langword="null"/> when the game has none to offer.</returns>
    public static SurfaceProperties? Load(IFileLoader fileLoader)
    {
        if (fileLoader.LoadFileCompiled("surfaceproperties/surfaceproperties.vsurf")?.DataBlock is not BinaryKV3 kv3)
        {
            return null;
        }

        var table = new SurfaceProperties();

        // The compiler flattens each surface's inheritance chain into its entry, so every field is
        // read off the entry itself; an absent sound is authored as an empty string
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
            var parameters = entry.GetSubCollection("audioparams");

            table.surfacesByHash[hash] = new Surface(
                name,
                physics?.GetFloatProperty("friction", Fallback.Friction) ?? Fallback.Friction,
                physics?.GetFloatProperty("elasticity", Fallback.Elasticity) ?? Fallback.Elasticity,
                physics?.GetFloatProperty("density", Fallback.Density) ?? Fallback.Density,
                NullIfEmpty(sounds?.GetStringProperty("impactsoft")),
                NullIfEmpty(sounds?.GetStringProperty("impacthard")),
                parameters?.GetFloatProperty("impactHardThreshold", Fallback.ImpactHardThreshold) ?? Fallback.ImpactHardThreshold);
        }

        return table.surfacesByHash.Count > 0 ? table : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
