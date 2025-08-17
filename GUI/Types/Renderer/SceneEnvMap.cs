namespace GUI.Types.Renderer;

class SceneEnvMap : SceneNode
{
    public int HandShake { get; init; }
    public required RenderTexture EnvMapTexture { get; init; }

    public Vector3 Tint { get; init; } = Vector3.One;

    /// <summary>
    /// If <see cref="EnvMapTexture"/> is an array, this is the depth index.
    /// </summary>
    public int ArrayIndex { get; init; }

    /// <summary>
    /// If multiple volumes contain an object, the highest priority volume takes precedence.
    /// </summary>
    public int IndoorOutdoorLevel { get; init; }

    public Vector3 EdgeFadeDists { get; init; }

    /// <summary>
    /// 0 = Sphere, 1 = Box
    /// </summary>
    public int ProjectionMode { get; init; }

    public SceneEnvMap(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }
}
