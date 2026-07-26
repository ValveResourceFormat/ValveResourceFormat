namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Environment map reflection probe with box or sphere projection.
/// </summary>
public class SceneEnvMap : SceneNode
{
    /// <summary>
    /// Skin added to the probe's box on every side. The shader tests the fragment against the grown box,
    /// so anything deciding where the probe reaches has to grow it by the same amount or describe a
    /// smaller volume than the one that actually reflects.
    /// </summary>
    public const float BoundsExtend = 0.02f;

    /// <summary>Gets the handshake value used to match this env map to scene nodes during precomputation.</summary>
    public int HandShake { get; init; }

    /// <summary>Gets the cubemap or cubemap-array texture for this environment map.</summary>
    public required RenderTexture EnvMapTexture { get; init; }

    /// <summary>Gets the color tint applied to reflections from this env map.</summary>
    public Vector3 Tint { get; init; } = Vector3.One;

    /// <summary>
    /// If <see cref="EnvMapTexture"/> is an array, this is the depth index.
    /// </summary>
    public int ArrayIndex { get; init; }

    /// <summary>
    /// If multiple volumes contain an object, the highest priority volume takes precedence.
    /// </summary>
    public int IndoorOutdoorLevel { get; init; }

    /// <summary>Gets the per-axis edge fade distances used for box projection blending.</summary>
    public Vector3 EdgeFadeDists { get; init; }

    /// <summary>
    /// 0 = Sphere, 1 = Box
    /// </summary>
    public int ProjectionMode { get; init; }

    /// <summary>Gets or sets the shader-side index assigned to this env map for UBO packing.</summary>
    public int ShaderIndex { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneEnvMap"/> class with the given local-space bounds.
    /// </summary>
    /// <param name="scene">The scene this node belongs to.</param>
    /// <param name="bounds">The local-space bounds of the env map volume.</param>
    public SceneEnvMap(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }
}
