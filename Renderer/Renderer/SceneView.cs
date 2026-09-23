using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// A camera and the scenes drawn through it, with what it keeps of each. A frame has one for the map and
/// the spawn groups drawn with it, one for the 3D sky, and draws the first person viewmodel as a
/// variation of the map's.
/// </summary>
internal readonly record struct SceneView
{
    /// <summary>What this view keeps of each scene it draws, in draw order.</summary>
    public required IReadOnlyList<SceneViewState> States { get; init; }

    public required Camera Camera { get; init; }

    /// <summary>How the sky this view draws is placed, or <see langword="null"/> when it draws the map itself.</summary>
    public SkyTransform? Sky { get; init; }

    /// <summary>The fog the view is drawn with.</summary>
    public required WorldFogInfo Fog { get; init; }

    /// <summary>Whether the view culls against the PVS of the cluster it looks from.</summary>
    public bool UsesPvs { get; init; }

    /// <summary>The frustum culling is frozen at, or <see langword="null"/> to follow <see cref="Camera"/>.</summary>
    public Frustum? LockedCullFrustum { get; init; }

    /// <summary>The camera whose light binning this view reuses, or <see langword="null"/> when it has its own.</summary>
    public Camera? BinnedFor { get; init; }

    /// <summary>The 2D sky this view replaces its scenes' own with, or <see langword="null"/> to keep theirs.</summary>
    public SceneSkybox2D? SkyOverride { get; init; }

    public FogSpace FogSpace => Sky?.FogSpace ?? FogSpace.World;
}
