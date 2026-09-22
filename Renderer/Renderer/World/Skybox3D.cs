using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// A map's 3D sky: the scene its sky map was loaded into, and how that scene maps to the world.
/// </summary>
/// <remarks>
/// Nothing in the sky is scaled. The sky map is placed rigidly by the <c>skybox_reference</c>, and the
/// <c>sky_camera</c> scale only moves the camera the sky is drawn through. Sky space is the space the
/// nodes of <see cref="Scene"/> live in; <see cref="SkyToWorld"/> maps it to where the viewer sees it.
/// </remarks>
public sealed class Skybox3D
{
    /// <summary>Gets the scene the sky map was loaded into.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the rigid transform the <c>skybox_reference</c> places the sky map with.</summary>
    public Matrix4x4 ReferenceTransform { get; }

    /// <summary>Gets the <c>sky_camera</c> position in sky space, which lines up with the reference position in the world.</summary>
    public Vector3 Origin { get; }

    /// <summary>Gets how many world units one unit of sky stands for. Always positive.</summary>
    public float Scale { get; }

    /// <summary>Gets the transform from sky space to where the viewer sees it in the world.</summary>
    public Matrix4x4 SkyToWorld { get; }

    /// <summary>Gets the conversion of fog authored in world units into sky space.</summary>
    public FogSpace FogSpace { get; }

    /// <summary>
    /// Gets the fog the sky is drawn with: the sky map's own gradient fog when it has an active one,
    /// otherwise the world's, and always the world's cubemap fog.
    /// </summary>
    public WorldFogInfo FogInfo
    {
        get
        {
            // Read through rather than taken once: the sky loads part way through the map, before the
            // map's own fog may have spawned
            field.SetToSkyView(worldFog, Scene.FogInfo);
            return field;
        }
    } = new();

    private readonly WorldFogInfo worldFog;

    /// <summary>Gets the entities of the sky map.</summary>
    public IReadOnlySet<Entity> Entities { get; }

    /// <summary>Initializes the 3D sky from a loaded and placed sky scene.</summary>
    /// <param name="scene">The scene the sky map was loaded into, already placed by <paramref name="referenceTransform"/>.</param>
    /// <param name="referenceTransform">The transform of the <c>skybox_reference</c>.</param>
    /// <param name="origin">The <c>sky_camera</c> position in sky space.</param>
    /// <param name="scale">How many world units one unit of sky stands for.</param>
    /// <param name="worldFog">The fog of the map this sky belongs to.</param>
    /// <param name="entities">The entities of the sky map.</param>
    public Skybox3D(Scene scene, Matrix4x4 referenceTransform, Vector3 origin, float scale, WorldFogInfo worldFog, IReadOnlySet<Entity> entities)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentNullException.ThrowIfNull(worldFog);
        ArgumentNullException.ThrowIfNull(entities);

        Scene = scene;
        this.worldFog = worldFog;
        ReferenceTransform = referenceTransform;
        Origin = origin;
        Scale = scale;
        Entities = entities;

        var reference = referenceTransform.Translation;

        SkyToWorld = Matrix4x4.CreateTranslation(-origin)
            * Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateTranslation(reference);

        FogSpace = new FogSpace(1f / scale, origin.Z - reference.Z / scale);

        scene.ToViewerWorld = SkyToWorld;
        scene.MarkerScale = 1f / scale;
    }

    /// <summary>Maps a position in sky space to where the viewer sees it in the world.</summary>
    public Vector3 ToWorld(Vector3 position) => Vector3.Transform(position, SkyToWorld);

    /// <summary>Maps the authored origin of one of <see cref="Entities"/> to where the viewer sees it in the world.</summary>
    public Vector3 EntityOriginToWorld(Vector3 origin) => ToWorld(Vector3.Transform(origin, ReferenceTransform));

    /// <summary>
    /// Sets <paramref name="camera"/> up to view the sky as <paramref name="from"/> views the world:
    /// same orientation and projection, from the matching position in sky space.
    /// </summary>
    /// <remarks>
    /// The clip planes are divided by the scale like the position, so sky fragments get the same depth
    /// as a sky scaled up to world size would.
    /// </remarks>
    public void ConfigureCamera(Camera camera, Camera from)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(from);

        camera.CopyFrom(from);
        camera.FieldOfView = from.FieldOfView;
        camera.Location = (from.Location - ReferenceTransform.Translation) / Scale + Origin;
        camera.NearPlane = from.NearPlane / Scale;
        camera.FarPlane = from.FarPlane / Scale;

        camera.CreateProjectionMatrix();
        camera.RecalculateMatrices();
    }
}
