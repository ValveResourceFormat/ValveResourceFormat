using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// An entity as the editor holds it, for the helpers that draw it: where it was placed, what its keys say,
/// and the other entities of its map.
/// </summary>
public interface IHelperOwner
{
    /// <summary>Gets the scene the entity is drawn in.</summary>
    Scene Scene { get; }

    /// <summary>Gets the keyvalues of the entity.</summary>
    EntityLump.Entity KeyValues { get; }

    /// <summary>Gets the classname of the entity.</summary>
    string Classname { get; }

    /// <summary>Gets the class of the entity, or <see langword="null"/> when its game declares none of that name.</summary>
    EntityClass? EntityClass { get; }

    /// <summary>Gets where the entity was placed, with its scale.</summary>
    Matrix4x4 Transform { get; }

    /// <summary>Gets where the entity was placed, without its scale.</summary>
    Matrix4x4 RigidTransform { get; }

    /// <summary>Gets the visibility layer of what the entity draws.</summary>
    string LayerName { get; }

    /// <summary>
    /// Gets the bounds of what the entity is made of besides its helpers, such as the meshes of a solid entity
    /// or the model the game draws it with, in the world, or <see langword="null"/> where there is nothing.
    /// </summary>
    AABB? ContentBounds { get; }

    /// <summary>
    /// Gets the bounds of the model the entity is drawn with, in the space of the entity, or
    /// <see langword="null"/> where it has none.
    /// </summary>
    AABB? LocalBounds { get; }

    /// <summary>Gets the helpers of the entity, in the order its class declares them.</summary>
    IReadOnlyList<EntityHelper> Helpers { get; }

    /// <summary>Adds a node the entity is drawn with to the scene, tied to the entity.</summary>
    /// <param name="node">The node. A node without a layer is given the one of the entity.</param>
    /// <param name="followsEntity">
    /// Whether the node is placed at the entity itself, so that an entity that moves takes it along. A node
    /// placed some other way, such as a box that does not turn with the entity, stays where it was put.
    /// </param>
    void AddNode(SceneNode node, bool followsEntity = false);

    /// <summary>Finds the entities of the map with a name.</summary>
    /// <param name="name">The <c>targetname</c> to look for.</param>
    /// <returns>Every entity with that name.</returns>
    IReadOnlyList<IHelperOwner> FindByName(string name);

    /// <summary>Finds the first entity of the map with a key set to a value.</summary>
    /// <param name="key">The key to look at.</param>
    /// <param name="value">The value to look for, without regard to case.</param>
    /// <returns>The entity, or <see langword="null"/> when none has it.</returns>
    IHelperOwner? FindByKey(string key, string value);

    /// <summary>Finds the entities of the map of a class.</summary>
    /// <param name="classname">The classname to look for.</param>
    /// <returns>Every entity of that class.</returns>
    IEnumerable<IHelperOwner> FindByClass(string classname);

    /// <summary>Gets the path the entity is a node of, or <see langword="null"/> when it is not a path node.</summary>
    IHelperOwner? Path { get; }

    /// <summary>Finds a node of the path the entity is.</summary>
    /// <param name="index">Zero based index of the node along the path.</param>
    /// <returns>The node, or <see langword="null"/> when the entity is not a path or has no such node.</returns>
    IHelperOwner? FindPathNode(int index);
}
