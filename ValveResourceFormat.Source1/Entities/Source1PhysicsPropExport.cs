using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Entities;

/// <summary>
/// Writes the physics props of a map as the classes the game simulates, where the converted entities draw them as
/// <c>prop_dynamic</c>.
/// </summary>
internal static class Source1PhysicsPropExport
{
    /// <summary>Holds the classname a physics prop has in the Source 1 map, on the converted entity.</summary>
    public const string ClassNameKey = "vrf_export_classname";

    private const string PhysicsClassName = "prop_physics";
    private const string DynamicClassName = "prop_dynamic";
    private const string ClientSideClassName = "prop_physics_clientside";

    /// <summary>Marks a converted entity with the classname it has in the Source 1 map, when that is a physics prop.</summary>
    /// <param name="sourceClassName">The classname in the Source 1 map.</param>
    /// <param name="entity">The converted entity.</param>
    public static void Mark(string sourceClassName, EntityLump.Entity entity)
    {
        if (IsPhysicsProp(sourceClassName))
        {
            entity[ClassNameKey] = sourceClassName;
        }
    }

    /// <summary>
    /// Gives the marked entities the class of their Source 1 map. A client side prop is a <c>prop_physics</c> that exists
    /// on the client only. A prop whose model has no collision stays a <c>prop_dynamic</c>, which a model without
    /// physics cannot be simulated as.
    /// </summary>
    /// <param name="entities">The entities of the lump, which are not changed.</param>
    /// <param name="hasPhysics">Gets whether the model at a path has collision.</param>
    /// <returns>The entities to write.</returns>
    public static List<EntityLump.Entity> Apply(List<EntityLump.Entity> entities, Func<string, bool> hasPhysics)
    {
        var result = new List<EntityLump.Entity>(entities.Count);

        foreach (var entity in entities)
        {
            if (!entity.TryGetValue(ClassNameKey, out _))
            {
                result.Add(entity);
                continue;
            }

            result.Add(Convert(entity, hasPhysics));
        }

        return result;
    }

    private static EntityLump.Entity Convert(EntityLump.Entity entity, Func<string, bool> hasPhysics)
    {
        var sourceClassName = entity.GetStringProperty(ClassNameKey) ?? PhysicsClassName;
        var model = entity.GetStringProperty("model");
        var copy = Source1EntityCopy.Copy(entity, ClassNameKey);

        if (string.IsNullOrEmpty(model) || !hasPhysics(model))
        {
            copy["classname"] = DynamicClassName;
            return copy;
        }

        if (sourceClassName.Equals(ClientSideClassName, StringComparison.OrdinalIgnoreCase))
        {
            copy["classname"] = PhysicsClassName;
            copy["clientsideentity"] = 1L;
            return copy;
        }

        copy["classname"] = sourceClassName.ToLowerInvariant();
        return copy;
    }

    private static bool IsPhysicsProp(string className)
        => className.Equals(PhysicsClassName, StringComparison.OrdinalIgnoreCase)
        || className.Equals("prop_physics_override", StringComparison.OrdinalIgnoreCase)
        || className.Equals("prop_physics_multiplayer", StringComparison.OrdinalIgnoreCase)
        || className.Equals(ClientSideClassName, StringComparison.OrdinalIgnoreCase);
}
