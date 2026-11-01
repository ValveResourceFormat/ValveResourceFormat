using System.Collections.Concurrent;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// A visibility layer scene nodes are put on, which hides every node on it while it is disabled. A node may be on
/// several layers and is only shown while all of them are enabled, see <see cref="SceneNode.Layers"/>. Each scene
/// keeps which layers are disabled, see <see cref="Scene.SetLayerEnabled"/>.
/// </summary>
/// <remarks>
/// The layers the renderer itself puts nodes on are static instances. Layers named by loaded files, such as the
/// world layers of a map, are found by name with <see cref="Get"/>, so the same name is the same layer in every
/// world.
/// </remarks>
public sealed class WorldLayer
{
    private static readonly ConcurrentDictionary<string, WorldLayer> NamedLayers = new(StringComparer.Ordinal);

    /// <summary>Layer of world geometry the map put on no layer.</summary>
    public static readonly WorldLayer NoLayer = new("No layer");

    /// <summary>Layer of every entity, and of everything entities draw.</summary>
    public static readonly WorldLayer Entities = new("Entities");

    /// <summary>Layer of the entities that start disabled, which the game keeps hidden until enabled.</summary>
    public static readonly WorldLayer DisabledEntities = new("Entities/Disabled");

    /// <summary>
    /// Layer of the entities a <c>point_template</c> shows while the world is stopped. The template itself is not on
    /// it, and what it spawns in game is not either.
    /// </summary>
    public static readonly WorldLayer Templates = new("Entities/Templates") { IsToolsOnly = true };

    /// <summary>Layer of particle systems.</summary>
    public static readonly WorldLayer Particles = new("Entities/Particles");

    /// <summary>Layer of the visibility clusters a map was compiled with.</summary>
    public static readonly WorldLayer VisibilityClusters = new("Visibility clusters");

    /// <summary>Gets the name the layer is listed by.</summary>
    public string Name { get; }

    /// <summary>Gets whether the layer is switched by the renderer alone and not offered to the user, such as the first person model.</summary>
    public bool IsInternal { get; internal init; }

    /// <summary>Gets whether the layer only holds what the editor draws, hidden while only what the game shows is.</summary>
    public bool IsToolsOnly { get; internal init; }

    internal WorldLayer(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the layer of a name that comes from a loaded file, such as a world layer of a map, creating it the first
    /// time the name is asked for.
    /// </summary>
    /// <param name="name">The name of the layer.</param>
    /// <returns>The one layer of that name.</returns>
    public static WorldLayer Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return NamedLayers.GetOrAdd(name, static name => new WorldLayer(name));
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
