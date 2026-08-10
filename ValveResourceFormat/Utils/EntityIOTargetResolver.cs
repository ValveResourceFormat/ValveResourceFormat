using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// Outcome of resolving an entity I/O connection target against a set of entities.
    /// </summary>
    public enum EntityIOTargetOutcome
    {
        /// <summary>At least one entity matched the target.</summary>
        Matched,

        /// <summary>The connection carries no target to resolve.</summary>
        Empty,

        /// <summary>The target is bound at runtime from the firing context (<c>!activator</c>, <c>!caller</c>) and names no map entity.</summary>
        Special,

        /// <summary>The target addresses entities by name or class, but nothing in the set matched.</summary>
        NotFound,

        /// <summary>The target type addresses entities in a way map data alone cannot answer (class inheritance, components, handles).</summary>
        Unsupported,
    }

    /// <summary>
    /// Resolves entity I/O connection targets to the entities they address: target names match
    /// case-insensitively, a target holding wildcards matches every name
    /// <see cref="ResourceTypes.EntityLump.EntityNameMatches"/> accepts, and class name targets
    /// address every entity of that class.
    /// </summary>
    public sealed class EntityIOTargetResolver
    {
        private readonly Dictionary<string, List<Entity>> byTargetName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Entity>> byClassName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Indexes <paramref name="entities"/> by target name and class name. Targets resolve only
        /// against this set, so it should hold every entity a connection could reach.
        /// </summary>
        /// <param name="entities">The entities connection targets are resolved against.</param>
        public EntityIOTargetResolver(IEnumerable<Entity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);

            foreach (var entity in entities)
            {
                Index(byTargetName, entity.TargetName, entity);
                Index(byClassName, entity.GetStringProperty("classname"), entity);
            }
        }

        private static void Index(Dictionary<string, List<Entity>> index, string? key, Entity entity)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!index.TryGetValue(key, out var list))
            {
                list = [];
                index[key] = list;
            }

            list.Add(entity);
        }

        /// <summary>
        /// Gets the entities carrying <paramref name="targetName"/> as their exact <c>targetname</c>,
        /// ignoring case. Empty when no entity has that name.
        /// </summary>
        /// <param name="targetName">The target name to look up.</param>
        /// <returns>The entities with that name.</returns>
        public IReadOnlyList<Entity> GetByTargetName(string? targetName)
            => !string.IsNullOrEmpty(targetName) && byTargetName.TryGetValue(targetName, out var list) ? list : [];

        /// <summary>
        /// Resolves a connection's target, appending every matching entity to <paramref name="results"/>.
        /// </summary>
        /// <param name="connection">A connection of <see cref="Entity.Connections"/>.</param>
        /// <param name="results">Receives the matching entities. Existing contents are kept.</param>
        /// <returns>Whether the target matched, and if not, why.</returns>
        public EntityIOTargetOutcome Resolve(Connection connection, List<Entity> results)
        {
            ArgumentNullException.ThrowIfNull(connection);

            return Resolve(connection.TargetName, connection.TargetType, results);
        }

        /// <summary>
        /// Resolves a connection target, appending every matching entity to <paramref name="results"/>.
        /// </summary>
        /// <param name="targetName">The connection's <c>m_targetName</c>.</param>
        /// <param name="targetType">The connection's <c>m_targetType</c>.</param>
        /// <param name="results">Receives the matching entities. Existing contents are kept.</param>
        /// <returns>Whether the target matched, and if not, why.</returns>
        public EntityIOTargetOutcome Resolve(string? targetName, EntityIOTargetType targetType, List<Entity> results)
        {
            ArgumentNullException.ThrowIfNull(results);

            if (targetType is EntityIOTargetType.SpecialActivator or EntityIOTargetType.SpecialCaller)
            {
                return EntityIOTargetOutcome.Special;
            }

            if (string.IsNullOrEmpty(targetName))
            {
                return EntityIOTargetOutcome.Empty;
            }

            if (targetName[0] == '!')
            {
                return EntityIOTargetOutcome.Special;
            }

            var before = results.Count;

            switch (targetType)
            {
                case EntityIOTargetType.EntityName:
                    AddByTargetName(targetName, results);
                    break;

                case EntityIOTargetType.ClassName:
                    AddByClassName(targetName, results);
                    break;

                case EntityIOTargetType.EntityNameOrClassName:
                    AddByTargetName(targetName, results);

                    if (results.Count == before)
                    {
                        AddByClassName(targetName, results);
                    }

                    break;

                default:
                    return EntityIOTargetOutcome.Unsupported;
            }

            return results.Count > before ? EntityIOTargetOutcome.Matched : EntityIOTargetOutcome.NotFound;
        }

        private void AddByTargetName(string targetName, List<Entity> results)
        {
            if (byTargetName.TryGetValue(targetName, out var exact))
            {
                results.AddRange(exact);
                return;
            }

            if (!targetName.Contains('*', StringComparison.Ordinal) && !targetName.Contains('?', StringComparison.Ordinal))
            {
                return;
            }

            foreach (var (name, entities) in byTargetName)
            {
                if (EntityNameMatches(targetName, name))
                {
                    results.AddRange(entities);
                }
            }
        }

        private void AddByClassName(string className, List<Entity> results)
        {
            if (byClassName.TryGetValue(className, out var entities))
            {
                results.AddRange(entities);
            }
        }
    }
}
