namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// Maps between world space and the sky space of a 3D sky.
/// </summary>
/// <remarks>
/// The sky geometry is never scaled; the <c>sky_camera</c> scale only moves the camera, so
/// <see cref="Scale"/> world units are one sky unit.
/// </remarks>
public readonly record struct SkyTransform
{
    /// <summary>Gets the <c>skybox_reference</c> position in world space, which lines up with <see cref="Origin"/>.</summary>
    public Vector3 Reference { get; }

    /// <summary>Gets the <c>sky_camera</c> position in sky space.</summary>
    public Vector3 Origin { get; }

    /// <summary>Gets how many world units one sky unit stands for. Always positive.</summary>
    public float Scale { get; }

    /// <summary>Initializes a new instance of the <see cref="SkyTransform"/> struct.</summary>
    public SkyTransform(Vector3 reference, Vector3 origin, float scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        Reference = reference;
        Origin = origin;
        Scale = scale;
    }

    /// <summary>
    /// Creates the transform from entity positions only, so neither entity rotates the view.
    /// Without a <c>sky_camera</c> the sky is seen from its origin at 1:1.
    /// </summary>
    public static SkyTransform FromEntities(Vector3 reference, Entities.SkyCamera? skyCamera)
        => skyCamera == null
            ? new(reference, Vector3.Zero, 1f)
            : new(reference, skyCamera.Transform.Translation, skyCamera.SkyScale);

    /// <summary>Gets the transform from sky space to world space.</summary>
    public Matrix4x4 SkyToWorld => Matrix4x4.CreateTranslation(-Origin)
        * Matrix4x4.CreateScale(Scale)
        * Matrix4x4.CreateTranslation(Reference);

    /// <summary>Gets the conversion of fog authored in world units into sky space.</summary>
    public FogSpace FogSpace => new(1f / Scale, Origin.Z - Reference.Z / Scale);

    /// <summary>Maps a sky space position to world space.</summary>
    public Vector3 ToWorld(Vector3 skyPosition) => Vector3.Transform(skyPosition, SkyToWorld);

    /// <summary>Maps a world position to sky space.</summary>
    public Vector3 ToSky(Vector3 worldPosition) => (worldPosition - Reference) / Scale + Origin;

    /// <summary>Sets <paramref name="camera"/> up to view the sky from the sky space position matching <paramref name="from"/>.</summary>
    /// <remarks>
    /// Clip planes are divided by the scale too, so sky depth matches a sky scaled up to world size.
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
