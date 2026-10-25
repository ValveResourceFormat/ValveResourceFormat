using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>skybox_reference</c>. Places a map as the 3D sky, which the last one to activate shows, seen through the
/// first <c>sky_camera</c> of the sky's world group.
/// </summary>
/// <remarks>
/// <para>
/// The entity loads nothing itself. The spawn group it is in loads the map it places, as it does for a
/// <c>point_prefab</c>, before the reference spawns.
/// </para>
/// <para>
/// TODO: Without a sky camera the game uses the <c>sky_camera</c> named by an <c>ActivateSkybox</c> input, else
/// the newest <c>sky_camera</c>, even one in the main world group, so a map with only a <c>sky_camera</c> draws
/// itself as its sky. Neither the fallback nor the input is handled here, and a sky in the main world group is
/// never drawn.
/// </para>
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CSkyboxReference">CSkyboxReference</seealso>
public sealed class SkyboxReference : BaseEntity
{
    private SkyCamera? skyCamera;

    /// <summary>
    /// Gets the world group the sky is loaded into, the reference's own when it names none, such as
    /// <c>skyboxWorldGroup0</c>.
    /// </summary>
    public string? SkyWorldGroup { get; }

    /// <summary>Initializes a <c>skybox_reference</c> from its keyvalues.</summary>
    public SkyboxReference(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
        SkyWorldGroup = SpawnGroupLoader.GetPlacedWorldGroup(KeyValues, Scene.WorldGroup);
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        EntitySystem.ActiveSkyboxReference = this;
    }

    /// <summary>Finds the camera the sky is seen through, looked for again until there is one.</summary>
    /// <returns>The first <c>sky_camera</c> of the sky's world group, or <see langword="null"/>.</returns>
    public SkyCamera? FindSkyCamera()
    {
        if (skyCamera is { IsRemoved: false })
        {
            return skyCamera;
        }

        skyCamera = FindSkyCamera(EntitySystem, SkyWorldGroup);
        return skyCamera;
    }

    /// <summary>Finds the camera a world group is seen through when it is shown as a 3D sky.</summary>
    /// <returns>The first <c>sky_camera</c> of the world group, or <see langword="null"/>.</returns>
    internal static SkyCamera? FindSkyCamera(EntitySystem entitySystem, string? worldGroup)
    {
        foreach (var entity in entitySystem.Entities)
        {
            if (entity is SkyCamera candidate && !candidate.IsRemoved && candidate.Scene.WorldGroup == worldGroup)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        if (EntitySystem.ActiveSkyboxReference == this)
        {
            EntitySystem.ActiveSkyboxReference = null;
        }
    }
}
