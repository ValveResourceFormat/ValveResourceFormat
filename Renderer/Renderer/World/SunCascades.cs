namespace ValveResourceFormat.Renderer.World;

/// <summary>Sun shadow cascades fitted to a camera.</summary>
public sealed class SunCascades
{
    /// <summary>Number of sun shadow cascades, the tightest first.</summary>
    public const int Count = 2;

    /// <summary>Gets the orthographic camera of each cascade.</summary>
    public Camera[] Cameras { get; } = CreateCameras();

    /// <summary>Gets the normalized direction sun light travels, away from the sun.</summary>
    public Vector3 CastDirection { get; private set; } = Vector3.UnitX;

    /// <summary>Gets the number of cascades in use after the last <see cref="Fit"/>.</summary>
    public int ActiveCount { get; private set; } = Count;

    /// <summary>Gets the depth bias applied to sun light shadows to reduce self-shadowing artifacts.</summary>
    public float ShadowBias { get; private set; } = 0.001f;

    // Depth extent, on each side of the eye, that sun shadow casters are culled within. The
    // rendered depth range is tightened to the collected casters by FitDepthRange.
    private const float CullDepthRange = 8192f;

    // How far each cascade's coverage square is shifted toward the view, as a fraction of its
    // half-extent. Keeps the camera comfortably inside the square while spending most of the
    // area on what is in front of it.
    private const float ForwardShiftFraction = 0.5f;

    // Cascade half-extents as fractions of the base coverage size, innermost first. The
    // outermost stays near the legacy single-map coverage, which was always generous; the
    // inner cascade spends its equal resolution on close-up density instead of area.
    private static readonly float[] ExtentFractions = [1f / 8f, 1f / 1.2f];

    // Keeps the innermost cascade usable at low resolution settings, where the base
    // coverage is small enough that an eighth of it would hug the camera.
    private const float MinHalfExtent = 128f;

    private readonly Vector3[] eyes = new Vector3[Count];
    private readonly float[] halfExtents = new float[Count];
    private bool fitsDepthToCasters;

    private static Camera[] CreateCameras()
    {
        var cameras = new Camera[Count];

        for (var i = 0; i < cameras.Length; i++)
        {
            cameras[i] = new Camera();
        }

        return cameras;
    }

    /// <summary>Fits the <see cref="Cameras"/> to a camera, with a depth range wide enough to cull casters in.</summary>
    public void Fit(Scene scene, Camera camera, float shadowMapSize)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        var sun = scene.LightingInfo;

        // The uniform stores surface-to-sun; the frustum looks along the rays, away from the sun
        var toSun = new Vector3(sun.LightingData.SunDirection.X, sun.LightingData.SunDirection.Y, sun.LightingData.SunDirection.Z);
        var sunDir = MathUtils.SafeNormalize(-toSun, Vector3.UnitX, 0.0001f);

        var baseHalfExtent = Math.Max(shadowMapSize / 2.5f, 512f) * sun.SunLightShadowCoverageScale;
        var bias = 0.001f;

        // Shift each coverage square toward the view. A caster shares its light-space footprint
        // with the shadow it casts, so area behind the view catches nothing the visible region
        // needs; casters toward the sun are captured along depth, not by the square.
        var forwardOnLightPlane = MathUtils.ProjectOntoPlane(camera.Forward, sunDir);

        fitsDepthToCasters = true;

        // When the whole scene fits into the first cascade, fit it directly and skip the rest
        var sceneBoundsMode = false;
        var sceneEye = Vector3.Zero;
        var sceneHalfExtent = 0f;
        var sceneNearPlaneExtend = 0f;

        if (sun.UseSceneBoundsForSunLightFrustum)
        {
            var staticBounds = scene.StaticOctree.GetBounds();
            var dynamicBounds = scene.DynamicOctree.GetBounds();
            var sceneBounds = staticBounds.Union(dynamicBounds);
            var max = sceneBounds.Size.MaxComponent();

            if (max > 0 && max < shadowMapSize)
            {
                sceneNearPlaneExtend = max / 2f;
                sceneEye = staticBounds.Center - sunDir * sceneNearPlaneExtend;
                sceneHalfExtent = max * 1.6f;
                bias = 0.01f;
                sceneBoundsMode = true;
                fitsDepthToCasters = false;
            }
        }

        // A sun pointing straight down leaves no horizontal axis to snap against, and world up is
        // no use as a reference either, so the frame is completed against forward instead
        var upReference = MathF.Abs(sunDir.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX;
        var right = Vector3.Normalize(Vector3.Cross(sunDir, upReference));
        var up = Vector3.Cross(right, sunDir);

        // When the whole scene already fits into the first cascade, the wider ones add nothing
        var singleCascade = sceneBoundsMode;

        ActiveCount = singleCascade ? 1 : Count;

        for (var cascade = 0; cascade < Count; cascade++)
        {
            if (singleCascade && cascade > 0)
            {
                // The shader falls through identical coordinates into the cleared outer layer,
                // which reads fully lit, matching the single-frustum edge fade.
                Cameras[cascade].CopyFrom(Cameras[0]);
                continue;
            }

            float bbox;
            float farPlane;
            float nearPlaneExtend;
            Vector3 eye;

            if (sceneBoundsMode)
            {
                bbox = sceneHalfExtent;
                farPlane = bbox;
                nearPlaneExtend = sceneNearPlaneExtend;
                eye = sceneEye;
            }
            else
            {
                bbox = MathF.Max(baseHalfExtent * ExtentFractions[cascade], MinHalfExtent);
                farPlane = CullDepthRange;
                nearPlaneExtend = CullDepthRange;
                eye = camera.Location + forwardOnLightPlane * (bbox * ForwardShiftFraction);
            }

            // Stabilize shadow map by snapping eye position to texel-sized increments in world space
            var texelWorldSize = (4.0f * bbox) / shadowMapSize;

            var eyeOffsetX = Vector3.Dot(eye, right);
            var eyeOffsetY = Vector3.Dot(eye, up);
            var eyeOffsetZ = Vector3.Dot(eye, sunDir);

            eyeOffsetX = MathF.Round(eyeOffsetX / texelWorldSize) * texelWorldSize;
            eyeOffsetY = MathF.Round(eyeOffsetY / texelWorldSize) * texelWorldSize;

            eye = right * eyeOffsetX + up * eyeOffsetY + sunDir * eyeOffsetZ;

            var view = Matrix4x4.CreateLookAt(eye, eye + sunDir, upReference);
            var projection = Matrix4x4.CreateOrthographicOffCenter(-bbox, bbox, -bbox, bbox, farPlane, -nearPlaneExtend);

            Cameras[cascade].SetViewProjection(view, projection);

            eyes[cascade] = eye;
            halfExtents[cascade] = bbox;
        }

        ShadowBias = bias;
        CastDirection = sunDir;
    }

    /// <summary>Tightens the depth range of a cascade to the extent of its casters along <see cref="CastDirection"/>.</summary>
    public void FitDepthRange(int cascade, float casterMin, float casterMax)
    {
        // A tight range keeps the shadow bias small in world units, and receivers outside it clamp in the shader
        if (!fitsDepthToCasters || casterMin > casterMax)
        {
            return;
        }

        var eyeDepth = Vector3.Dot(eyes[cascade], CastDirection);

        // Quantized so the range holds still while casters move within it
        const float quantize = 64f;
        var depthMin = Math.Clamp(MathF.Floor((casterMin - eyeDepth) / quantize) * quantize, -CullDepthRange, CullDepthRange - quantize);
        var depthMax = Math.Clamp(MathF.Ceiling((casterMax - eyeDepth) / quantize) * quantize, depthMin + quantize, CullDepthRange);

        var bbox = halfExtents[cascade];
        var projection = Matrix4x4.CreateOrthographicOffCenter(-bbox, bbox, -bbox, bbox, depthMax, depthMin);

        var camera = Cameras[cascade];
        camera.SetViewProjection(camera.CameraViewMatrix, projection);
    }
}
