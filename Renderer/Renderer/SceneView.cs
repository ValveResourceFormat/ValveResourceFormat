using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer;

internal readonly record struct SceneView
{
    public required List<SceneViewState> States { get; init; }

    public required Camera Camera { get; init; }

    public SkyTransform? Sky { get; init; }

    public required WorldFogInfo Fog { get; init; }

    /// <summary>The frustum culling is frozen at, or <see langword="null"/> to follow <see cref="Camera"/>.</summary>
    public Frustum? LockedCullFrustum { get; init; }

    /// <summary>The camera whose light binning this view reuses, or <see langword="null"/> when it has its own.</summary>
    public Camera? BinnedFor { get; init; }

    public FogSpace FogSpace => Sky?.FogSpace ?? FogSpace.World;
}
