using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The <see cref="EntityInputAttribute"/> handlers of each entity class, built once when the class
/// registers with <see cref="EntityFactory"/>, so firing an input is a dictionary lookup.
/// </summary>
/// <remarks>
/// Concurrent because not every bind happens at startup: the player never goes through the factory and
/// binds as its world loads, and two worlds can load at once. Built tables are frozen, so only publishing
/// one needs guarding.
/// </remarks>
internal static class EntityInputTable
{
    private static readonly ConcurrentDictionary<Type, FrozenDictionary<string, Action<BaseEntity, EntityInputData>>> Tables = [];

    /// <summary>
    /// Builds the input table for an entity class. <see cref="DynamicallyAccessedMembersAttribute"/> keeps
    /// the handlers alive under trimming.
    /// </summary>
    /// <typeparam name="T">The entity class to scan.</typeparam>
    public static void Bind<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] T>()
        where T : BaseEntity
    {
        if (Tables.ContainsKey(typeof(T)))
        {
            return;
        }

        // Racing threads may both build the table; the loser's copy is discarded, which is cheaper
        // than holding a lock across the reflection

        var handlers = new Dictionary<string, Action<BaseEntity, EntityInputData>>(StringComparer.OrdinalIgnoreCase);

        // Includes inherited protected methods, so each entity keeps its bases' inputs
        foreach (var method in typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var attribute = method.GetCustomAttribute<EntityInputAttribute>();

            if (attribute == null)
            {
                continue;
            }

            var handler = method.CreateDelegate<Action<T, EntityInputData>>();

            // A duplicate input name is a mistake, so let Add throw
            handlers.Add(attribute.Name, (entity, data) => handler((T)entity, data));
        }

        Tables.TryAdd(typeof(T), handlers.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Runs the entity's handler for an input.</summary>
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
