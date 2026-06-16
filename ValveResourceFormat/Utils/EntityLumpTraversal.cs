using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;
using VEntityLump = ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// Walks an entity lump plus the child lumps its <c>point_template</c> entities reference, pairing each entity with the parent transform that applies to it.
    /// </summary>
    public static class EntityLumpTraversal
    {
        /// <summary>
        /// An entity from <see cref="EnumerateEntities"/>, with the parent transform that applies to it and whether it came from a <c>point_template</c> child lump.
        /// </summary>
        public readonly record struct TraversedEntity(Entity Entity, Matrix4x4 ParentTransform, bool FromTemplate);

        /// <summary>
        /// Enumerates <paramref name="lump"/>'s entities and, recursively, the entities of child lumps its
        /// <c>point_template</c> entities reference. Template children inherit the template's rigid transform (no scale).
        /// </summary>
        /// <param name="lump">The root entity lump.</param>
        /// <param name="resolveChildLump">Resolves a referenced child lump by name, or <see langword="null"/> if it can't be loaded.</param>
        /// <param name="rootTransform">Transform applied to top-level entities.</param>
        /// <param name="onMissingChildLump">Called with the lump name when a referenced child lump can't be resolved.</param>
        /// <returns>Each entity with its parent transform.</returns>
        public static IEnumerable<TraversedEntity> EnumerateEntities(
            VEntityLump lump,
            Func<string, VEntityLump?> resolveChildLump,
            Matrix4x4 rootTransform,
            Action<string>? onMissingChildLump = null)
        {
            return Traverse(lump, resolveChildLump, rootTransform, fromTemplate: false, [], onMissingChildLump);
        }

        private static IEnumerable<TraversedEntity> Traverse(
            VEntityLump lump,
            Func<string, VEntityLump?> resolveChildLump,
            Matrix4x4 parentTransform,
            bool fromTemplate,
            Dictionary<string, VEntityLump> childLumps,
            Action<string>? onMissingChildLump)
        {
            foreach (var childLumpName in lump.GetChildEntityNames())
            {
                var childLump = resolveChildLump(childLumpName);

                if (childLump != null)
                {
                    // shared so nested templates can reach lumps registered higher up
                    childLumps.TryAdd(childLump.Name, childLump);
                }
            }

            foreach (var entity in lump.GetEntities())
            {
                yield return new TraversedEntity(entity, parentTransform, fromTemplate);

                if (entity.GetStringProperty("classname") != "point_template")
                {
                    continue;
                }

                // empty when the template has no compiled children
                var entityLumpName = entity.GetStringProperty("entitylumpname");

                if (string.IsNullOrEmpty(entityLumpName))
                {
                    continue;
                }

                if (childLumps.TryGetValue(entityLumpName, out var templateLump))
                {
                    var childTransform = EntityTransformHelper.CalculateRigidTransformationMatrix(entity) * parentTransform;

                    foreach (var nested in Traverse(templateLump, resolveChildLump, childTransform, fromTemplate: true, childLumps, onMissingChildLump))
                    {
                        yield return nested;
                    }
                }
                else
                {
                    onMissingChildLump?.Invoke(entityLumpName);
                }
            }
        }
    }
}
