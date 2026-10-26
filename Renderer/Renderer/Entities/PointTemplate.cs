using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>point_template</c>. Spawns the entities compiled into its entity lump as a new instance each time it is
/// told to, placed at itself or at another entity. Each instance loads a spawn group of its own, which the
/// entities join or not as <see cref="SpawnGroupType"/> says.
/// </summary>
/// <remarks>
/// <para>
/// The game spawns nothing of a template when the map loads. A world spawned while stopped, as an editor shows
/// it, spawns each template's entities once where they were authored instead, on
/// <see cref="WorldLoader.TemplateLayerName"/>.
/// </para>
/// <para>
/// The template only describes an instance's spawn group: the world and entity lump to spawn, the placement, the
/// name fixups and the owner. The <see cref="SpawnGroupManager"/> loads it and spawns the lump from the loaded world of that name,
/// within the call, or on the next tick for an asynchronous template, then tells the template what spawned.
/// </para>
/// <para>
/// The <c>entityFilterName</c> filter is game code, so every entity of the lump spawns. Entities spawned after
/// load are not bound to the scene's cubemaps and light probes, which are only assigned when the scene is first
/// initialized.
/// </para>
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CPointTemplate">CPointTemplate</seealso>
public sealed class PointTemplate : BaseEntity
{
    /// <summary>Spawn flags for templates.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Instances take the names of the template's own spawn group, rather than a numbered name each.</summary>
        PreserveNames = 2,
    }

    /// <summary>Which spawn group the entities of an instance join.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/client/PointTemplateOwnerSpawnGroupType_t">PointTemplateOwnerSpawnGroupType_t</seealso>
    public enum OwnerSpawnGroupType
    {
        /// <summary>The spawn group the template spawned in.</summary>
        InsertIntoPointTemplateSpawnGroup = 0,

        /// <summary>The spawn group active when the instance spawns.</summary>
        InsertIntoCurrentlyActiveSpawnGroup = 1,

        /// <summary>A spawn group of the instance's own, which <see cref="DeleteCreatedSpawnGroups"/> unloads with its entities.</summary>
        InsertIntoNewlyCreatedSpawnGroup = 2,
    }

    /// <summary>Gets the world holding the template's entity lump, such as <c>maps/de_dust2</c>.</summary>
    public string WorldName { get; private set; } = string.Empty;

    /// <summary>Gets the name of the entity lump holding the template's entities, empty when it has none.</summary>
    public string EntityLumpName { get; private set; } = string.Empty;

    /// <summary>Gets whether the instances spawn on the tick after they load rather than within the call.</summary>
    public bool IsAsynchronous { get; private set; }

    /// <summary>Gets which spawn group the entities of an instance join.</summary>
    public OwnerSpawnGroupType SpawnGroupType { get; private set; }

    /// <summary>
    /// Gets what the instances spawned, gathered across them until <see cref="DeleteCreatedSpawnGroups"/>. An
    /// entity removed since drops out at the next instance.
    /// </summary>
    public IReadOnlyList<BaseEntity> SpawnedEntities => spawnedEntities;

    private readonly List<BaseEntity> spawnedEntities = [];

    // The spawn groups of the instances whose entities joined them, which unload with their entities
    private readonly List<SpawnGroup> createdSpawnGroups = [];

    /// <summary>Initializes a <c>point_template</c> from its keyvalues.</summary>
    public PointTemplate(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        WorldName = KeyValues.GetStringProperty("worldname") is { Length: > 0 } worldName
            ? WorldLoader.GetSpawnGroupMapName(worldName)
            : SpawnGroup?.MapName ?? string.Empty;
        EntityLumpName = KeyValues.GetStringProperty("entitylumpname") ?? string.Empty;
        IsAsynchronous = KeyValues.GetBooleanProperty("asynchronous");
        SpawnGroupType = KeyValues.GetStringProperty("spawngrouptype") switch
        {
            "INSERT_INTO_CURRENTLY_ACTIVE_SPAWN_GROUP" => OwnerSpawnGroupType.InsertIntoCurrentlyActiveSpawnGroup,
            "INSERT_INTO_NEWLY_CREATED_SPAWN_GROUP" => OwnerSpawnGroupType.InsertIntoNewlyCreatedSpawnGroup,

            // Stored as a number, or absent
            null => (OwnerSpawnGroupType)KeyValues.GetInt32Property("spawngrouptype"),
            _ => OwnerSpawnGroupType.InsertIntoPointTemplateSpawnGroup,
        };
    }

    /// <inheritdoc/>
    /// <remarks>The game spawns nothing of a template here. A stopped world shows its entities where they were authored.</remarks>
    public override void Activate()
    {
        if (!EntitySystem.Enabled)
        {
            SpawnTemplateInstance(RigidTransform, onSpawned: null, isPreview: true);
        }
    }

    /// <summary>Spawns an instance at the template, as the <c>ForceSpawn</c> input does.</summary>
    public void ForceSpawn() => SpawnTemplateInstance(RigidTransform, onSpawned: null, isPreview: false);

    /// <summary>
    /// Unloads the spawn groups the instances created, with their entities. Unless the template is asynchronous,
    /// every entity in <see cref="SpawnedEntities"/> is removed too. The template stays.
    /// </summary>
    public void DeleteCreatedSpawnGroups()
    {
        UnloadCreatedSpawnGroups();

        if (!IsAsynchronous)
        {
            foreach (var entity in spawnedEntities)
            {
                EntitySystem.Remove(entity);
            }
        }

        spawnedEntities.Clear();
    }

    // Every instance loads a spawn group, even a preview or an empty one, to spawn the lump found in the
    // template's world
    private void SpawnTemplateInstance(Matrix4x4 placement, Action<IReadOnlyList<BaseEntity>>? onSpawned, bool isPreview)
    {
        var spawnGroupManager = EntitySystem.SpawnGroupManager;
        var lump = spawnGroupManager.GetEntityLumpForTemplate(WorldName, EntityLumpName);

        if (lump == null && EntityLumpName.Length > 0)
        {
            EntitySystem.Logger.LogWarning("point_template '{TargetName}' found no entity lump named '{EntityLumpName}' in '{WorldName}'", TargetName, EntityLumpName, WorldName);
        }

        // The fixups of the spawn group the template is in, rather than of the one that spawned it
        var nameFixup = SpawnGroup?.NameFixup ?? NameFixup;

        var group = new SpawnGroup(WorldName, Scene, placement)
        {
            DescriptiveName = "point_template",
            EntityLumpName = EntityLumpName,
            EntityLump = lump,
            NameFixup = isPreview || HasSpawnFlags(SpawnFlag.PreserveNames)
                ? nameFixup
                : nameFixup with { Local = string.Create(CultureInfo.InvariantCulture, $"{nameFixup.Local}_{spawnGroupManager.NextTemplateInstanceIndex()}") },

            // A stopped world shows the entities as part of the map
            Owner = isPreview ? SpawnGroup : SpawnGroupType switch
            {
                OwnerSpawnGroupType.InsertIntoPointTemplateSpawnGroup => SpawnGroup,
                OwnerSpawnGroupType.InsertIntoCurrentlyActiveSpawnGroup => EntitySystem.ActiveSpawnGroup,
                _ => null,
            },

            // In game the instance spawns on the layer its lump would, as play hides the template layer with the
            // other tool layers
            LayerName = isPreview ? WorldLoader.TemplateLayerName : null,
        };

        spawnGroupManager.LoadSpawnGroup(group);

        if (onSpawned != null)
        {
            spawnGroupManager.ListenForSpawnGroupToFinish(group, onSpawned);
        }

        if (isPreview)
        {
            spawnGroupManager.SynchronouslySpawnGroup(group);
            return;
        }

        spawnGroupManager.ListenForSpawnGroupToFinish(group, OnSpawnGroupFinished);

        if (SpawnGroupType == OwnerSpawnGroupType.InsertIntoNewlyCreatedSpawnGroup)
        {
            createdSpawnGroups.Add(group);
        }

        if (!IsAsynchronous)
        {
            spawnGroupManager.SynchronouslySpawnGroup(group);
        }
    }

    private void OnSpawnGroupFinished(IReadOnlyList<BaseEntity> spawned)
    {
        // An asynchronous instance can finish after the template is gone
        if (IsRemoved)
        {
            return;
        }

        // Each removed entry takes the place of the last one, so what stays can change order, as in game
        for (var i = spawnedEntities.Count - 1; i >= 0; i--)
        {
            if (spawnedEntities[i].IsRemoved)
            {
                spawnedEntities[i] = spawnedEntities[^1];
                spawnedEntities.RemoveAt(spawnedEntities.Count - 1);
            }
        }

        spawnedEntities.AddRange(spawned);

        EntitySystem.TriggerOutput(this, "OnEntitySpawned", this);
    }

    private void UnloadCreatedSpawnGroups()
    {
        foreach (var group in createdSpawnGroups)
        {
            EntitySystem.SpawnGroupManager.UnloadSpawnGroup(group);
        }

        createdSpawnGroups.Clear();
    }

    /// <inheritdoc/>
    /// <remarks>Entities the instances spawned into other spawn groups stay.</remarks>
    protected override void OnRemove()
    {
        UnloadCreatedSpawnGroups();
        spawnedEntities.Clear();
    }

    [EntityInput("ForceSpawn")] private void InputForceSpawn(EntityInputData data) => ForceSpawn();

    [EntityInput("DeleteCreatedSpawnGroups")] private void InputDeleteCreatedSpawnGroups(EntityInputData data) => DeleteCreatedSpawnGroups();
}
