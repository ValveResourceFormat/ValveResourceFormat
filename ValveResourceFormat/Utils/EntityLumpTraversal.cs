using ValveResourceFormat.IO;
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
        /// <param name="fileLoader">Loads referenced child lumps by name.</param>
        /// <param name="rootTransform">Transform applied to top-level entities.</param>
        /// <param name="onMissingChildLump">Called with the lump name when a referenced child lump can't be resolved.</param>
        /// <returns>Each entity with its parent transform.</returns>
        public static IEnumerable<TraversedEntity> EnumerateEntities(
            VEntityLump lump,
            IFileLoader fileLoader,
            Matrix4x4 rootTransform,
            Action<string>? onMissingChildLump = null)
        {
            return Traverse(lump, fileLoader, rootTransform, fromTemplate: false, [], [], onMissingChildLump);
        }

        // Lazily mutates childLumps/visited during enumeration; safe because both consumers materialize the result.
        private static IEnumerable<TraversedEntity> Traverse(
            VEntityLump lump,
            IFileLoader fileLoader,
            Matrix4x4 parentTransform,
            bool fromTemplate,
            Dictionary<string, VEntityLump> childLumps,
            HashSet<string> visited,
            Action<string>? onMissingChildLump)
        {
            foreach (var childLumpName in lump.GetChildEntityNames())
            {
                var childLump = fileLoader.LoadFileCompiled(childLumpName)?.DataBlock as VEntityLump;

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
                    // guard against a malformed template cycle (A's lump references B's, B's back to A)
                    if (!visited.Add(entityLumpName))
                    {
                        continue;
                    }

                    var childTransform = EntityTransformHelper.CalculateRigidTransformationMatrix(entity) * parentTransform;

                    foreach (var nested in Traverse(templateLump, fileLoader, childTransform, fromTemplate: true, childLumps, visited, onMissingChildLump))
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
