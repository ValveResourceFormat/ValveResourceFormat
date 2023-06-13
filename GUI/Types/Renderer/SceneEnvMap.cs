namespace GUI.Types.Renderer;

class SceneEnvMap : SceneNode
{
    public int HandShake { get; init; }
    public RenderTexture EnvMapTexture { get; init; }

    /// <summary>
    /// If <see cref="EnvMapTexture"/> is an array, this is the depth index.
    /// </summary>
    public int ArrayIndex { get; init; }

    public SceneEnvMap(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }

    public override void Render(Scene.RenderContext context)
    {
    }

    public override void Update(Scene.UpdateContext context)
    {
    }
}
