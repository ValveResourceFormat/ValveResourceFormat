using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// A scene viewer that shows a map and the entities it lists, so the entity list and the automation tools can inspect them.
    /// </summary>
    internal interface IEntityMapViewer
    {
        /// <summary>Gets the name of the map, or <see langword="null"/> when the viewer shows geometry without a map.</summary>
        string? MapName { get; }

        /// <summary>Gets the entities of the map, or <see langword="null"/> when the viewer shows geometry without a map.</summary>
        List<EntityLump.Entity>? MapEntities { get; }

        /// <summary>Gets the spawn groups that are drawn as scenes of their own, such as a 3D sky.</summary>
        IEnumerable<SpawnGroup> OwnSceneSpawnGroups { get; }

        /// <summary>Selects the scene node of an entity and moves the camera to it, or to the entity origin when it draws nothing.</summary>
        void SelectAndFocusEntity(EntityLump.Entity entity);

        /// <summary>Selects a scene node and moves the camera to it.</summary>
        void SelectAndFocusNode(SceneNode node);

        /// <summary>Gets the bounds to focus on for an entity that has no scene node.</summary>
        AABB EntityOriginBounds(EntityLump.Entity entity);
    }
}
