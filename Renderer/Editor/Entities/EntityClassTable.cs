namespace ValveResourceFormat.Renderer.Editor.Entities;

/// <summary>
/// The entity classes of a game, which the entities of its maps are shown with.
/// </summary>
public sealed class EntityClassTable
{
    private readonly Dictionary<string, EntityClass> classesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> declaredNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the classes, in the order the game declares them.</summary>
    public IReadOnlyList<EntityClass> Classes { get; }

    /// <summary>Gets the grid the game lays its navigation out on, where it declares one.</summary>
    public (int EdgeSize, int OffsetX, int OffsetY, int PickHeight)? GridNav { get; }

    internal EntityClassTable(IEnumerable<EntityClass> classes, (int EdgeSize, int OffsetX, int OffsetY, int PickHeight)? gridNav)
    {
        var ordered = new List<EntityClass>();

        // A class declared again is the first of its name
        foreach (var entityClass in classes)
        {
            if (!classesByName.TryAdd(entityClass.Name, entityClass))
            {
                continue;
            }

            ordered.Add(entityClass);
            declaredNames.Add(entityClass.Name);
            declaredNames.UnionWith(entityClass.BaseClasses);
        }

        Classes = ordered;
        GridNav = gridNav;
    }

    /// <summary>Finds a class by its name, without regard to case.</summary>
    /// <param name="name">Name of the class.</param>
    /// <returns>The class, or <see langword="null"/> when the game declares none of that name.</returns>
    public EntityClass? FindClass(string name) => classesByName.GetValueOrDefault(name);

    /// <summary>
    /// Gets whether the game declares a class of a name, either one entities can be of or one other classes are based on.
    /// </summary>
    /// <param name="name">Name of the class.</param>
    /// <returns>Whether it does.</returns>
    public bool Declares(string name) => declaredNames.Contains(name);
}
