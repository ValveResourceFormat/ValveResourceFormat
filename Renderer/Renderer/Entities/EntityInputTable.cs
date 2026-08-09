using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The input handlers each entity class declares with <see cref="EntityInputAttribute"/>, Source's entity
/// data description tables. Built once per class when the class registers with <see cref="EntityFactory"/>,
/// so firing an input is a dictionary lookup and a delegate call rather than a chain of name comparisons.
/// </summary>
internal static class EntityInputTable
{
    private static readonly Dictionary<Type, FrozenDictionary<string, Action<BaseEntity, EntityInputData>>> Tables = [];

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

        Tables[typeof(T)] = handlers.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs the handler an entity declared for an input.
    /// </summary>
    /// <param name="entity">The entity receiving the input.</param>
    /// <param name="inputName">The input's name, matched case-insensitively.</param>
    /// <param name="data">The parameter and the entities that sent it.</param>
    /// <returns><see langword="true"/> when a handler ran.</returns>
    public static bool TryDispatch(BaseEntity entity, string inputName, EntityInputData data)
    {
        // Walking up covers an entity class that subclasses a registered one without registering itself
        for (var type = entity.GetType(); type != null; type = type.BaseType)
        {
            if (!Tables.TryGetValue(type, out var table))
            {
                continue;
            }

            if (!table.TryGetValue(inputName, out var handler))
            {
                return false;
            }

            handler(entity, data);
            return true;
        }

        return false;
    }
}
