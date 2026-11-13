using ValveResourceFormat.Renderer.SceneEnvironment;
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

    /// <summary>The camera whose light binning this view reuses, or <see langword="null"/> when this view bins lights itself.</summary>
    public Camera? BinnedFor { get; init; }

    /// <summary>The 2D sky that replaces the 2D skies of the scenes of this view, or <see langword="null"/> to keep the skies of the scenes.</summary>
    public SceneSkybox2D? SkyOverride { get; init; }

    public bool SkyCastsSunShadows { get; init; }

    public bool SkyReceivesSunShadows { get; init; }

    public FogSpace FogSpace => Sky?.FogSpace ?? FogSpace.World;
}
