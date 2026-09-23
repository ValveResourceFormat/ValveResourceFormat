namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// How a 3D sky is drawn: the camera it is seen through follows the main camera, moved into sky space.
/// </summary>
/// <remarks>
/// Nothing in the sky is scaled. The sky map is placed rigidly, and the <c>sky_camera</c> scale only moves
/// the camera the sky is drawn through: a viewer at <see cref="Reference"/> sees the sky from
/// <see cref="Origin"/>, and a step of <see cref="Scale"/> units in the world is one unit in the sky.
/// Sky space is the space the nodes of the sky's scene live in.
/// </remarks>
public readonly record struct SkyTransform
{
    /// <summary>Gets the world position that lines up with <see cref="Origin"/>, the <c>skybox_reference</c>'s.</summary>
    public Vector3 Reference { get; }

    /// <summary>Gets the <c>sky_camera</c> position in sky space.</summary>
    public Vector3 Origin { get; }

    /// <summary>Gets how many world units one unit of sky stands for. Always positive.</summary>
    public float Scale { get; }

    /// <summary>Initializes the transform of a 3D sky.</summary>
    /// <param name="reference">The world position that lines up with <paramref name="origin"/>.</param>
    /// <param name="origin">The <c>sky_camera</c> position in sky space.</param>
    /// <param name="scale">How many world units one unit of sky stands for.</param>
    public SkyTransform(Vector3 reference, Vector3 origin, float scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        Reference = reference;
        Origin = origin;
        Scale = scale;
    }

    /// <summary>
    /// Works out a 3D sky from its entities, as the game does every frame: seen from the <c>sky_camera</c>
    /// of the sky's world group, lined up with the <c>skybox_reference</c> that placed it. Only their
    /// positions count, so neither rotates the view.
    /// </summary>
    /// <param name="reference">Where the <c>skybox_reference</c> is.</param>
    /// <param name="skyCamera">The sky's <c>sky_camera</c>, or <see langword="null"/> for a sky without one, seen from its origin at 1:1.</param>
    public static SkyTransform FromEntities(Vector3 reference, Entities.SkyCamera? skyCamera)
        => skyCamera == null
            ? new(reference, Vector3.Zero, 1f)
            : new(reference, skyCamera.Transform.Translation, skyCamera.SkyScale);

    /// <summary>Gets the transform from sky space to where the viewer sees it in the world.</summary>
    public Matrix4x4 SkyToWorld => Matrix4x4.CreateTranslation(-Origin)
        * Matrix4x4.CreateScale(Scale)
        * Matrix4x4.CreateTranslation(Reference);

    /// <summary>Gets the conversion of fog authored in world units into sky space.</summary>
    public FogSpace FogSpace => new(1f / Scale, Origin.Z - Reference.Z / Scale);

    /// <summary>Maps a position in sky space to where the viewer sees it in the world.</summary>
    public Vector3 ToWorld(Vector3 skyPosition) => Vector3.Transform(skyPosition, SkyToWorld);

    /// <summary>Maps a position in the world to the sky space position it is seen from.</summary>
    public Vector3 ToSky(Vector3 worldPosition) => (worldPosition - Reference) / Scale + Origin;

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
        camera.Location = ToSky(from.Location);
        camera.NearPlane = from.NearPlane / Scale;
        camera.FarPlane = from.FarPlane / Scale;

        camera.CreateProjectionMatrix();
        camera.RecalculateMatrices();
    }
}
