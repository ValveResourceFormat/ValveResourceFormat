using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The input handlers each entity class declares with <see cref="EntityInputAttribute"/>, Source's entity
/// data description tables. Built once per class when the class registers with <see cref="EntityFactory"/>,
/// so firing an input is a dictionary lookup and a delegate call rather than a chain of name comparisons.
/// </summary>
/// <remarks>
/// Concurrent because not every bind happens at startup: the player is never seen by the factory and binds
/// as its world loads, and two worlds can load at once. A built table is frozen, so only publishing one
/// needs guarding.
/// </remarks>
internal static class EntityInputTable
{
    private static readonly ConcurrentDictionary<Type, FrozenDictionary<string, Action<BaseEntity, EntityInputData>>> Tables = [];

    /// <summary>
    /// Builds the input table for an entity class. The <see cref="DynamicallyAccessedMembersAttribute"/> is
    /// what keeps the handlers alive under trimming: registering a class is also what declares that its
    /// methods are reached by reflection.
    /// </summary>
    /// <typeparam name="T">The entity class to scan.</typeparam>
    public static void Bind<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] T>()
        where T : BaseEntity
    {
        if (Tables.ContainsKey(typeof(T)))
        {
            return;
        }

        // Two threads reaching here for one class both build a table and one of them wins the publish
        // below. The loser's copy is identical and thrown away, which is cheaper than holding a lock
        // across the reflection.

        var handlers = new Dictionary<string, Action<BaseEntity, EntityInputData>>(StringComparer.OrdinalIgnoreCase);

        // Instance methods here include the protected ones inherited from BaseEntity, so an entity keeps
        // every input its bases declared without restating them.
        foreach (var method in typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var attribute = method.GetCustomAttribute<EntityInputAttribute>();

            if (attribute == null)
            {
                continue;
            }

            var handler = method.CreateDelegate<Action<T, EntityInputData>>();

            // Two handlers claiming one input name is a mistake worth failing on
            handlers.Add(attribute.Name, (entity, data) => handler((T)entity, data));
        }

        Tables.TryAdd(typeof(T), handlers.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Runs the handler an entity declared for an input.
    /// </summary>
    /// <returns><see langword="true"/> when a handler ran.</returns>
    public static bool TryDispatch(BaseEntity entity, string inputName, EntityInputData data)
    {
        // A class's table already carries the inputs it inherited, so its own entry is the whole answer
        if (!Tables.TryGetValue(entity.GetType(), out var table) || !table.TryGetValue(inputName, out var handler))
        {
            return false;
        }

        handler(entity, data);
        return true;
    }
}
