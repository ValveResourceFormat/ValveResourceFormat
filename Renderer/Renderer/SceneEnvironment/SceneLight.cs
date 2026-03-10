using ValveResourceFormat.Renderer.Buffers;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
///     Scene node representing a light source with type, color, and attenuation.
/// </summary>
public class SceneLight(Scene scene) : SceneNode(scene)
{
    /// <summary>
    ///     Source 2 light entity class names.
    /// </summary>
    public enum EntityType
    {
        /// <summary>Directional environment light (sun/sky).</summary>
        Environment,
        /// <summary>Omnidirectional point light.</summary>
        Omni,
        /// <summary>Cone-shaped spot light.</summary>
        Spot,
        /// <summary>Second-generation omnidirectional point light.</summary>
        Omni2,
        /// <summary>Barn-door shaped area light.</summary>
        Barn,
        /// <summary>Rectangular area light.</summary>
        Rect,
    }

    /// <summary>
    ///     Shader light type for rendering calculations.
    /// </summary>
    public enum LightType
    {
        /// <summary>Infinite directional light (no position, parallel rays).</summary>
        Directional,
        /// <summary>Omnidirectional point light radiating in all directions.</summary>
        Point,
        /// <summary>Cone-shaped spot light with inner and outer angles.</summary>
        Spot,
    }

    /// <summary>GPU data and frustum matrix for one face of a barn or omni2 light.</summary>
    public struct BarnFaceData
    {
        /// <summary>Gets or sets the GPU constant buffer data for this light face.</summary>
        public BarnLightConstants GpuData { get; set; }

        /// <summary>Gets or sets the world-to-frustum projection matrix for shadow rendering.</summary>
        public Matrix4x4 WorldToFrustum { get; set; }
    }

    /// <summary>
    ///     Light index to a baked lightmap.
    ///     Range: 0..255 for GameLightmapVersion 1 and 0..3 for GameLightmapVersion 2.
    /// </summary>
    public int StationaryLightIndex { get; set; }

    /// <summary>Gets or sets the world-space position of the light.</summary>
    public Vector3 Position { get; set; }

    /// <summary>Gets or sets the normalized direction the light faces.</summary>
    public Vector3 Direction { get; set; }

    /// <summary>Gets or sets the sRGB color of the light.</summary>
    public Vector3 Color { get; set; } = Vector3.One;

    /// <summary>Gets or sets the brightness (intensity) of the light.</summary>
    public float Brightness { get; set; } = 1.0f;

    /// <summary>Gets or sets the additional brightness scale multiplier.</summary>
    public float BrightnessScale { get; set; } = 1.0f;

    /// <summary>Gets or sets the maximum range of the light in world units.</summary>
    public float Range { get; set; } = 512.0f;

    /// <summary>Gets or sets the distance falloff skirt fraction.</summary>
    public float FallOff { get; set; } = 1.0f;

    /// <summary>Gets or sets the inner cone half-angle for spot lights in degrees.</summary>
    public float SpotInnerAngle { get; set; }

    /// <summary>Gets or sets the outer cone half-angle for spot lights in degrees.</summary>
    public float SpotOuterAngle { get; set; } = 45.0f;

    /// <summary>Gets or sets the linear attenuation coefficient.</summary>
    public float AttenuationLinear { get; set; }

    /// <summary>Gets or sets the quadratic attenuation coefficient.</summary>
    public float AttenuationQuadratic { get; set; }

    /// <summary>Gets or sets the horizontal soft edge width for barn lights.</summary>
    public float SoftX { get; set; } = 0.25f;

    /// <summary>Gets or sets the vertical soft edge width for barn lights.</summary>
    public float SoftY { get; set; } = 0.25f;

    /// <summary>Gets or sets the near skirt distance for perspective barn lights.</summary>
    public float SkirtNear { get; set; }

    /// <summary>Gets or sets the shape blend factor for barn lights.</summary>
    public float Shape { get; set; }

    /// <summary>Gets or sets the 2D shear offset for barn lights.</summary>
    public Vector2 Shear { get; set; }

    /// <summary>Gets or sets the half-width, half-height, and near-plane reciprocal for barn lights.</summary>
    public Vector3 SizeParams { get; set; } = new(16, 16, 0.0625f);

    /// <summary>Gets or sets the luminaire (source) size used for area light calculations.</summary>
    public float LuminaireSize { get; set; } = 4f;

    /// <summary>Gets or sets the luminaire anisotropy for capsule-shaped area lights.</summary>
    public float LuminaireAnisotropy { get; set; }

    /// <summary>Gets or sets the luminaire shape index (0 = sphere, 1 = capsule, 2 = rect).</summary>
    public int LuminaireShape { get; set; }

    /// <summary>Gets or sets the minimum roughness clamped for specular highlight calculations.</summary>
    public float MinRoughness { get; set; } = 0.04f;

    /// <summary>Gets or sets the material path of the cookie texture, or <see langword="null"/> if none.</summary>
    public string? CookieTexturePath { get; set; }

    /// <summary>Gets or sets the shader light type used during rendering.</summary>
    public LightType Type { get; set; }

    /// <summary>Gets or sets the entity class that created this light.</summary>
    public EntityType Entity { get; set; }

    /// <summary>Gets or sets the direct light contribution mode (0 = off, 1 = stationary, 2 = dynamic).</summary>
    public int DirectLight { get; set; } = 2;

    /// <summary>Gets or sets whether this light casts shadows.</summary>
    public int CastShadows { get; set; } = 1;

    /// <summary>Gets or sets the shadow map resolution in texels.</summary>
    public int ShadowMapSize { get; set; } = 1024;

    /// <summary>Gets or sets whether the precomputed OBB fields below are valid.</summary>
    public bool PrecomputedFieldsValid { get; set; }

    /// <summary>Gets or sets the center of the precomputed oriented bounding box.</summary>
    public Vector3 PrecomputedObbOrigin { get; set; }

    /// <summary>Gets or sets the half-extents of the precomputed oriented bounding box.</summary>
    public Vector3 PrecomputedObbExtent { get; set; }

    /// <summary>Gets or sets the Euler angles of the precomputed oriented bounding box.</summary>
    public Vector3 PrecomputedObbAngles { get; set; }

    /// <summary>Gets or sets the axis-aligned precomputed world bounds.</summary>
    public AABB PrecomputedBounds { get; set; }

    /// <summary>Gets or sets the number of precomputed sub-frusta for omni lights.</summary>
    public int PrecomputedSubfrusta { get; set; }

    /// <summary>Gets or sets the centers of the precomputed sub-OBBs, one per frustum.</summary>
    public Vector3[]? PrecomputedSubObbOrigins { get; set; }

    /// <summary>Gets or sets the half-extents of the precomputed sub-OBBs, one per frustum.</summary>
    public Vector3[]? PrecomputedSubObbExtents { get; set; }

    /// <summary>Gets or sets the Euler angles of the precomputed sub-OBBs, one per frustum.</summary>
    public Vector3[]? PrecomputedSubObbAngles { get; set; }

    // Precomputed barn light faces (1 for a barn light, 1-6 for an omni light)
    /// <summary>Gets the precomputed face data array (1 face for barn lights, 1–6 for omni lights).</summary>
    public BarnFaceData[] BarnFaces { get; private set; } = [];

    // Marks a barn light dirty. This will recalculate all faces.
    /// <summary>Gets or sets whether this light's face data needs to be recomputed.</summary>
    public bool IsDirty { get; set; } = true;
    internal bool WillDrawShadows { get; set; }

    internal Dictionary<int, (int FrustumHash, DepthOnlyDrawBuckets? DrawCalls)> FaceShadowCache { get; } = [];

    /// <summary>
    /// Returns whether the given entity classname is a recognized light type, and which <see cref="EntityType"/> it maps to.
    /// </summary>
    public static (bool Accepted, EntityType Type) IsAccepted(string classname)
    {
        if (!classname.StartsWith("light_", StringComparison.OrdinalIgnoreCase))
        {
            return (false, EntityType.Environment);
        }

        var accepted = Enum.TryParse(classname[6..], true, out EntityType entityType);
        return (accepted, entityType);
    }

    /// <summary>
    /// Creates a <see cref="SceneLight"/> populated from entity key-value properties.
    /// </summary>
    public static SceneLight FromEntityProperties(Scene scene, EntityType type, Entity entity)
    {
        var light = new SceneLight(scene)
        {
            StationaryLightIndex =
                entity.GetPropertyUnchecked("bakedshadowindex", entity.GetPropertyUnchecked("bakelightindex", -1)),
            Entity = type,
            Type = type switch
            {
                EntityType.Environment => LightType.Directional,

                EntityType.Omni => LightType.Point,
                EntityType.Omni2 => LightType.Point,

                EntityType.Barn => LightType.Spot,
                EntityType.Rect => LightType.Spot,
                EntityType.Spot => LightType.Spot,
                _ => throw new NotImplementedException()
            },

            Color = entity.GetColor32Property("color"),

            Brightness = type switch
            {
                EntityType.Environment or EntityType.Omni or EntityType.Spot => entity.GetPropertyUnchecked(
                    "brightness", 1.0f),
                _ => entity.GetPropertyUnchecked("brightness_lumens", 224.0f)
            },

            BrightnessScale = entity.GetPropertyUnchecked("brightnessscale", 1.0f),
            Range = entity.GetPropertyUnchecked("range", 512.0f),
            FallOff = entity.GetPropertyUnchecked("skirt", 0.1f)
        };

        var isNewLightType = type is EntityType.Omni2 or EntityType.Barn or EntityType.Rect;

        if (!isNewLightType)
        {
            light.AttenuationLinear = entity.GetPropertyUnchecked("attenuation1", 0.0f);
            light.AttenuationQuadratic = entity.GetPropertyUnchecked("attenuation2", 0.0f);
        }

        if (isNewLightType || type is EntityType.Environment)
        {
            light.DirectLight = entity.GetPropertyUnchecked("directlight", 2);
            light.CastShadows = entity.GetPropertyUnchecked("castshadows", 1);
            light.ShadowMapSize = entity.GetPropertyUnchecked("shadowmapsize", 1024);
            if (light.ShadowMapSize <= 0)
            {
                light.ShadowMapSize = 1024;
            }
        }

        if (type is EntityType.Spot or EntityType.Barn)
        {
            light.SpotInnerAngle = entity.GetPropertyUnchecked("innerconeangle", light.SpotInnerAngle);
            light.SpotOuterAngle = entity.GetPropertyUnchecked("outerconeangle", light.SpotOuterAngle);
        }

        if (type is EntityType.Barn)
        {
            light.SoftX = entity.GetPropertyUnchecked("soft_x", 0.25f);
            light.SoftY = entity.GetPropertyUnchecked("soft_y", 0.25f);
            light.SkirtNear = entity.GetPropertyUnchecked("skirt_near", 0.05f);
            light.Shape = entity.GetPropertyUnchecked("shape", 0f);
            light.LuminaireSize = entity.GetPropertyUnchecked("luminaire_size", 4f);
            light.LuminaireShape = entity.GetPropertyUnchecked("luminaire_shape", 0);
            light.LuminaireAnisotropy = entity.GetPropertyUnchecked("luminaire_anisotropy", 0f);
            light.SizeParams = entity.GetVector3Property("size_params");
            light.CookieTexturePath = entity.GetProperty<string>("lightcookie");
            light.MinRoughness = entity.GetPropertyUnchecked("minroughness", 0.04f);

            light.Shear = entity.GetVector2Property("shear");
        }

        if (type is EntityType.Omni2)
        {
            light.SpotInnerAngle = entity.GetPropertyUnchecked("inner_angle", 0f);
            light.SpotOuterAngle = entity.GetPropertyUnchecked("outer_angle", 180f);
            light.SizeParams = entity.GetVector3Property("size_params");
            light.CookieTexturePath = entity.GetProperty<string>("lightcookie");
            light.MinRoughness = entity.GetPropertyUnchecked("minroughness", 0.04f);
            light.LuminaireShape = entity.GetPropertyUnchecked("shape", 0);
            light.LuminaireSize = entity.GetPropertyUnchecked("luminaire_size", 0f);
        }

        if (isNewLightType)
        {
            light.PrecomputedFieldsValid = entity.GetPropertyUnchecked<int>("precomputedfieldsvalid") != 0;
            if (light.PrecomputedFieldsValid)
            {
                light.PrecomputedObbOrigin = entity.GetVector3Property("precomputedobborigin");
                light.PrecomputedObbExtent = entity.GetVector3Property("precomputedobbextent");
                light.PrecomputedObbAngles = entity.GetVector3Property("precomputedobbangles");
                var boundsMins = entity.GetVector3Property("precomputedboundsmins");
                var boundsMaxs = entity.GetVector3Property("precomputedboundsmaxs");
                light.PrecomputedBounds = new AABB(boundsMins, boundsMaxs);

                light.PrecomputedSubfrusta = entity.GetPropertyUnchecked<int>("precomputedsubfrusta");
                if (light.PrecomputedSubfrusta > 0)
                {
                    light.PrecomputedSubObbOrigins = new Vector3[light.PrecomputedSubfrusta];
                    light.PrecomputedSubObbExtents = new Vector3[light.PrecomputedSubfrusta];
                    light.PrecomputedSubObbAngles = new Vector3[light.PrecomputedSubfrusta];

                    for (var i = 0; i < light.PrecomputedSubfrusta; i++)
                    {
                        light.PrecomputedSubObbOrigins[i] = entity.GetVector3Property($"precomputedobborigin{i}");
                        light.PrecomputedSubObbExtents[i] = entity.GetVector3Property($"precomputedobbextent{i}");
                        light.PrecomputedSubObbAngles[i] = entity.GetVector3Property($"precomputedobbangles{i}");
                    }
                }
            }
        }

        light.Position = entity.GetVector3Property("origin");
        light.Direction = AnglesToDirection(entity.GetVector3Property("angles"));
        return light;
    }

    internal static bool IsRealTimeLight(SceneLight light)
    {
        if (light.Entity is not (EntityType.Barn or EntityType.Omni2))
        {
            return false;
        }

        return light.DirectLight switch
        {
            0 => false,
            1 => light.StationaryLightIndex is >= 0 and <= 3,
            _ => true
        };
    }

    /// <summary>
    /// Converts Euler pitch/yaw angles to a normalized forward direction vector.
    /// </summary>
    public static Vector3 AnglesToDirection(Vector3 angles)
    {
        var (sinPitch, cosPitch) = MathF.SinCos(angles.X);
        var (sinYaw, cosYaw) = MathF.SinCos(angles.Y);

        return Vector3.Normalize(new Vector3(cosYaw * cosPitch, sinYaw * cosPitch, sinPitch));
    }

    /// <summary>
    /// Recomputes the <see cref="BarnFaces"/> array from the current light properties.
    /// </summary>
    /// <param name="cookiePaths">Map from cookie material path to cookie atlas index.</param>
    public void ComputeBarnFaces(Dictionary<string, int> cookiePaths)
    {
        if (Entity == EntityType.Barn)
        {
            if (BarnFaces is not { Length: 1 })
            {
                BarnFaces = new BarnFaceData[1];
            }

            BarnFaces[0] = ComputeBarnLightFace(this, cookiePaths);
        }
        else if (Entity == EntityType.Omni2)
        {
            ComputeOmni2Faces(this, cookiePaths);
        }
    }

    /// <summary>
    /// Returns the shadow map pixel dimensions for this light, accounting for aspect ratio.
    /// </summary>
    public (int W, int H) GetShadowFaceDimensions()
    {
        var size = ShadowMapSize;
        if (Entity == EntityType.Omni2)
        {
            return (size, size);
        }

        var aspect = SizeParams.X / SizeParams.Y;
        return aspect >= 1f
            ? (size, (int)MathF.Round(size / aspect))
            : ((int)MathF.Round(size * aspect), size);
    }

    private static (Matrix4x4 WorldToFrustum, Vector4 Position, float SkirtNear, float SkirtFar, float Divisor)
        ComputeOrthographicBarnGeometry(SceneLight light, Vector3 forwardDir, Vector3 upDir, Vector3 rightDir)
    {
        var eyePosition = light.Transform.Translation;

        var lightView = Matrix4x4.CreateLookAtLeftHanded(eyePosition,
            eyePosition + forwardDir, upDir);

        var shearMatrix = Matrix4x4.Identity;
        shearMatrix.M31 = -light.Shear.X / light.Range;
        shearMatrix.M32 = -light.Shear.Y / light.Range;

        var lightProj = Matrix4x4.CreateOrthographicOffCenterLeftHanded(
            -light.SizeParams.X, light.SizeParams.X,
            -light.SizeParams.Y, light.SizeParams.Y,
            0f, light.Range);

        var worldToFrustum = lightView * shearMatrix * lightProj;

        var skirtNear = light.SkirtNear > 0 ? 1f / light.SkirtNear : 0f;
        var skirtFar = light.FallOff > 0 ? 1f / light.FallOff : 0f;

        var divisor = light.SizeParams.X * light.SizeParams.Y / (MathF.PI * 10f);

        var projectionDir = Vector3.Normalize(
            light.Range * forwardDir + light.Shear.X * rightDir + light.Shear.Y * upDir);
        var position = new Vector4(-projectionDir, 0f);

        return (worldToFrustum, position, skirtNear, skirtFar, divisor);
    }

    private static (Matrix4x4 WorldToFrustum, Vector4 Position, float SkirtNear, float SkirtFar, float Divisor)
        ComputePerspectiveBarnGeometry(SceneLight light, Vector3 forwardDir, Vector3 upDir, Vector3 rightDir)
    {
        var nearPlane = 1f / light.SizeParams.Z;
        var farPlane = nearPlane + light.Range;

        var centerX = light.Shear.X / light.Range * nearPlane;
        var centerY = light.Shear.Y / light.Range * nearPlane;

        var eyePosition = light.Transform.Translation
                          - nearPlane * forwardDir - centerX * rightDir - centerY * upDir;

        var lightProj = Matrix4x4.CreatePerspectiveOffCenterLeftHanded(
            centerX - light.SizeParams.X, centerX + light.SizeParams.X,
            centerY - light.SizeParams.Y, centerY + light.SizeParams.Y,
            nearPlane, farPlane);
        var lightView = Matrix4x4.CreateLookAtLeftHanded(eyePosition,
            eyePosition + forwardDir, upDir);

        var worldToFrustum = lightView * lightProj;
        if (!Matrix4x4.Invert(worldToFrustum, out var frustumToWorld))
        {
            frustumToWorld = Matrix4x4.Identity;
        }

        var skirtNear = 0f;
        if (light.SkirtNear > 0)
        {
            var depthNear = nearPlane + light.SkirtNear * light.Range;
            skirtNear = depthNear / (farPlane * light.SkirtNear);
        }

        var skirtFar = 0f;
        if (light.FallOff > 0)
        {
            var depthFar = nearPlane + (1f - light.FallOff) * light.Range;
            skirtFar = depthFar / (nearPlane * light.FallOff);
        }

        var eyeToOriginDistSq = centerX * centerX + centerY * centerY + nearPlane * nearPlane;
        var solidAngle = ComputeFrustumSolidAngle(frustumToWorld, eyePosition);
        var divisor = solidAngle * eyeToOriginDistSq / (4f * MathF.PI * 10f);

        var position = new Vector4(eyePosition, eyeToOriginDistSq);

        return (worldToFrustum, position, skirtNear, skirtFar, divisor);
    }

    private static BarnFaceData ComputeBarnLightFace(SceneLight light, Dictionary<string, int> cookiePaths)
    {
        var noTranslation = light.Transform with { Translation = Vector3.Zero };
        var forwardDir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, noTranslation));
        var upDir = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, noTranslation));
        var rightDir = Vector3.Normalize(Vector3.Cross(upDir, forwardDir));

        var (worldToFrustum, barnLightPosition, skirtNear, skirtFar, divisor) = light.SizeParams.Z <= 0
            ? ComputeOrthographicBarnGeometry(light, forwardDir, upDir, rightDir)
            : ComputePerspectiveBarnGeometry(light, forwardDir, upDir, rightDir);

        var colorIntensity = divisor > 0.000001f ? light.Brightness * light.BrightnessScale / divisor : 0f;
        var linearColor = ColorSpace.SrgbGammaToLinear(light.Color) * colorIntensity;

        var cookieW = 0f;
        Vector4 cookieParams;
        if (light.CookieTexturePath != null && cookiePaths.TryGetValue(light.CookieTexturePath, out var cookieIndex))
        {
            cookieW = cookieIndex + 1f;
            cookieParams = new Vector4(0f, 0f, 1f, 1f);
        }
        else
        {
            cookieParams = new Vector4(1f - light.SoftX, 1f - light.SoftY, light.Shape, 0f);
        }

        var orientationQ = Quaternion.CreateFromRotationMatrix(light.Transform);

        return new BarnFaceData
        {
            GpuData = new BarnLightConstants
            {
                BarnFrustum = worldToFrustum,
                BarnLightPosition = barnLightPosition,
                BarnLightDistanceFade_vSkirt = new Vector4(1f, 0f, skirtNear, skirtFar),
                BarnLightColor_flCookie = new Vector4(linearColor, cookieW),
                BarnLightOrientationQ = new Vector4(orientationQ.X, orientationQ.Y, orientationQ.Z, orientationQ.W),
                BarnLightAngleFade = new Vector3(1f, 0f, 0f),
                BarnLightShadowOffsetScale = Vector4.Zero,
                BarnLightCookieParameters = cookieParams,
                BarnLightBakedShadowMask = GetBakedShadowMask(light.StationaryLightIndex),
                BarnLightMinRoughness = MathF.Max(0.04f, light.MinRoughness),
                BarnLightShadowScale = 0f,
                PathTraceIndex_BarnLightFlags = 0xFFFF0000u,
                BarnIlluminationFromWorld = ShouldEnableOBB(light)
                    ? ComputeIlluminationFromWorld(light.PrecomputedObbOrigin, light.PrecomputedObbExtent,
                        light.PrecomputedObbAngles)
                    : default
            },
            WorldToFrustum = worldToFrustum,
        };
    }

    private static void ComputeOmni2Faces(SceneLight light, Dictionary<string, int> cookiePaths)
    {
        var noTranslation = light.Transform with { Translation = Vector3.Zero };
        var forward = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, noTranslation));
        var right = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, noTranslation));
        var up = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, noTranslation));

        var angleRad = float.DegreesToRadians(Math.Clamp(light.SpotOuterAngle, 1f, 180f));
        var heightOffset = light.LuminaireShape is 1 or 2 ? light.SizeParams.Y : 0f;
        var sideRange = angleRad < MathF.PI / 2f
            ? MathF.Max(light.Range - heightOffset, 0f) * MathF.Sin(angleRad) + heightOffset
            : light.Range;
        var faceCount = GetOmni2FaceCount(light.SpotOuterAngle, heightOffset);

        if (light.BarnFaces is not { Length: var currentLen } || currentLen != faceCount)
        {
            light.BarnFaces = new BarnFaceData[faceCount];
        }

        var origin = light.Transform.Translation;
        var nearPlane = 1f;
        var orientationQ = Quaternion.CreateFromRotationMatrix(light.Transform);
        var linearColor = ComputeOmni2Color(light);

        var cookieW = 0f;
        var cookieParams = new Vector4(1f, 1f, 0f, 0f);
        if (light.CookieTexturePath != null && cookiePaths.TryGetValue(light.CookieTexturePath, out var cookieIndex))
        {
            cookieW = -(cookieIndex + 1f);
            cookieParams = new Vector4(0f, 0f, 1f, 1f);
        }

        var (distFadeOffset, distFadeRate) = ComputeDistanceFade(light.Range, light.FallOff);
        var (angleBias, angleScale) = ComputeAngleFade(light.SpotOuterAngle, light.SpotInnerAngle);
        var angleFadeZ = light.LuminaireShape switch
        {
            1 or 2 => light.SizeParams.Y,
            0 => light.LuminaireSize,
            _ => 0f
        };

        BarnFaceData MakeFace(Vector3 faceForward, Vector3 faceUp, float range, int faceIndex)
        {
            var lightView = Matrix4x4.CreateLookAtLeftHanded(origin, origin + faceForward, faceUp);
            var lightProj = Matrix4x4.CreatePerspectiveLeftHanded(2f * nearPlane, 2f * nearPlane, nearPlane, nearPlane + range);
            var worldToFrustum = lightView * lightProj;

            return new BarnFaceData
            {
                GpuData = new BarnLightConstants
                {
                    BarnFrustum = worldToFrustum,
                    BarnLightPosition = new Vector4(origin, 1f),
                    BarnLightDistanceFade_vSkirt = new Vector4(distFadeOffset, distFadeRate, 0f, 0f),
                    BarnLightColor_flCookie = new Vector4(linearColor, cookieW),
                    BarnLightOrientationQ = new Vector4(orientationQ.X, orientationQ.Y, orientationQ.Z, orientationQ.W),
                    BarnLightAngleFade = new Vector3(angleBias, angleScale, angleFadeZ),
                    BarnLightShadowOffsetScale = Vector4.Zero,
                    BarnLightCookieParameters = cookieParams,
                    BarnLightBakedShadowMask = GetBakedShadowMask(light.StationaryLightIndex),
                    BarnLightMinRoughness = MathF.Max(0.04f, light.MinRoughness),
                    BarnLightShadowScale = 0f,
                    PathTraceIndex_BarnLightFlags = 0xFFFF0000u,
                    BarnIlluminationFromWorld = GetOmni2FaceOBB(light, faceIndex)
                },
                WorldToFrustum = worldToFrustum,
            };
        }

        light.BarnFaces[0] = MakeFace(forward, up, light.Range, 0);

        if (faceCount >= 5)
        {
            light.BarnFaces[1] = MakeFace(-up, forward, sideRange, 1);
            light.BarnFaces[2] = MakeFace(up, -forward, sideRange, 2);
            light.BarnFaces[3] = MakeFace(right, up, sideRange, 3);
            light.BarnFaces[4] = MakeFace(-right, up, sideRange, 4);
        }

        if (faceCount >= 6)
        {
            light.BarnFaces[5] = MakeFace(-forward, up, MathF.Abs(MathF.Cos(angleRad)) * light.Range, 5);
        }
    }

    private static Vector3 ComputeOmni2Color(SceneLight light)
    {
        var outerRad = float.DegreesToRadians(Math.Clamp(light.SpotOuterAngle, 1f, 180f));
        var innerRad = float.DegreesToRadians(Math.Clamp(light.SpotInnerAngle, 0f, light.SpotOuterAngle));

        var avgCos = (MathF.Cos(outerRad) + MathF.Cos(innerRad)) * 0.5f;
        var coneSolidAngle = MathF.Min(MathF.Tau * (1f - avgCos), 4f * MathF.PI);

        var colorIntensity = light.Brightness * light.BrightnessScale * (4f * MathF.PI * 10f) / coneSolidAngle;
        return ColorSpace.SrgbGammaToLinear(light.Color) * colorIntensity;
    }

    private static (float Offset, float Rate) ComputeDistanceFade(float range, float skirt)
    {
        if (skirt <= 0f)
        {
            return (1f, 0f);
        }

        if (skirt >= 1f)
        {
            return (1f, -1f / range);
        }

        return (1f / skirt, -1f / (range * skirt));
    }

    private static (float Bias, float Scale) ComputeAngleFade(float outerAngleDeg, float innerAngleDeg)
    {
        if (outerAngleDeg >= 180f)
        {
            return (1f, 0f);
        }

        if (outerAngleDeg <= 0f)
        {
            return (0f, 0f);
        }

        var cosOuter = MathF.Cos(float.DegreesToRadians(outerAngleDeg));
        var cosInner = innerAngleDeg > 0f
            ? MathF.Cos(float.DegreesToRadians(Math.Clamp(innerAngleDeg, 0f, outerAngleDeg - 0.001f)))
            : 1f;

        cosOuter = MathF.Min(cosOuter, 1f - 1e-7f);

        var denom = cosOuter - cosInner;
        if (denom >= 0f)
        {
            denom = -1e-7f;
        }

        return (cosOuter / denom, 1f / denom);
    }

    private static Vector4 GetBakedShadowMask(int stationaryLightIndex)
    {
        return stationaryLightIndex switch
        {
            0 => new Vector4(1, 0, 0, 0),
            1 => new Vector4(0, 1, 0, 0),
            2 => new Vector4(0, 0, 1, 0),
            3 => new Vector4(0, 0, 0, 1),
            _ => Vector4.Zero
        };
    }

    private static float SphericalTriangleArea(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        var a = MathF.Acos(Math.Clamp(Vector3.Dot(p2, p3), -1f, 1f));
        var b = MathF.Acos(Math.Clamp(Vector3.Dot(p1, p3), -1f, 1f));
        var c = MathF.Acos(Math.Clamp(Vector3.Dot(p1, p2), -1f, 1f));

        if (a == 0f || b == 0f || c == 0f)
        {
            return 0f;
        }

        var s = (a + b + c) * 0.5f;
        var sinS = MathF.Sin(s);
        var sinSMinusA = MathF.Sin(s - a);
        var sinSMinusB = MathF.Sin(s - b);
        var sinSMinusC = MathF.Sin(s - c);

        var t1 = MathF.Sqrt(MathF.Max(0f, sinSMinusC * sinSMinusA / (sinS * sinSMinusB)));
        var t2 = MathF.Sqrt(MathF.Max(0f, sinSMinusA * sinSMinusB / (sinSMinusC * sinS)));
        var t3 = MathF.Sqrt(MathF.Max(0f, sinSMinusB * sinSMinusC / (sinSMinusA * sinS)));

        return 2f * (MathF.Atan(t1) + MathF.Atan(t2) + MathF.Atan(t3)) - MathF.PI;
    }

    private static float ComputeFrustumSolidAngle(Matrix4x4 frustumToWorld, Vector3 viewPos)
    {
        var a = ProjectNDCToWorld(frustumToWorld, -1, -1, 0);
        var b = ProjectNDCToWorld(frustumToWorld, 1, -1, 0);
        var c = ProjectNDCToWorld(frustumToWorld, 1, 1, 0);
        var d = ProjectNDCToWorld(frustumToWorld, -1, 1, 0);

        var dirA = Vector3.Normalize(a - viewPos);
        var dirB = Vector3.Normalize(b - viewPos);
        var dirC = Vector3.Normalize(c - viewPos);
        var dirD = Vector3.Normalize(d - viewPos);

        return SphericalTriangleArea(dirA, dirB, dirC) + SphericalTriangleArea(dirA, dirC, dirD);
    }

    private static OpenTK.Mathematics.Matrix3x4 ComputeIlluminationFromWorld(Vector3 center, Vector3 extent, Vector3 angles)
    {
        var rotMatrix = EntityTransformHelper.CreateRotationMatrixFromEulerAngles(angles);

        var axis0 = new Vector3(rotMatrix.M11, rotMatrix.M12, rotMatrix.M13);
        var axis1 = new Vector3(rotMatrix.M21, rotMatrix.M22, rotMatrix.M23);
        var axis2 = new Vector3(rotMatrix.M31, rotMatrix.M32, rotMatrix.M33);

        var inv = new Vector3(0.999f) / extent;

        var translation = new Vector3(
            -Vector3.Dot(center, axis0),
            -Vector3.Dot(center, axis1),
            -Vector3.Dot(center, axis2)
        ) * inv;

        axis0 *= inv.X;
        axis1 *= inv.Y;
        axis2 *= inv.Z;

        return new OpenTK.Mathematics.Matrix3x4(
            axis0.X, axis0.Y, axis0.Z, translation.X,
            axis1.X, axis1.Y, axis1.Z, translation.Y,
            axis2.X, axis2.Y, axis2.Z, translation.Z
        );
    }

    private static OpenTK.Mathematics.Matrix3x4 GetOmni2FaceOBB(SceneLight light, int faceIndex)
    {
        if (light.PrecomputedSubObbOrigins != null
            && faceIndex < light.PrecomputedSubObbOrigins.Length)
        {
            return ComputeIlluminationFromWorld(
                light.PrecomputedSubObbOrigins[faceIndex],
                light.PrecomputedSubObbExtents![faceIndex],
                light.PrecomputedSubObbAngles![faceIndex]);
        }

        return default;
    }

    private static Vector3 ProjectNDCToWorld(Matrix4x4 frustumToWorld, float x, float y, float z)
    {
        var v = Vector4.Transform(new Vector4(x, y, z, 1f), frustumToWorld);
        return new Vector3(v.X, v.Y, v.Z) / v.W;
    }

    private static bool ShouldEnableOBB(SceneLight light)
    {
        if (!light.PrecomputedFieldsValid)
        {
            return false;
        }

        return light.StationaryLightIndex is >= 0 and <= 3
               || light.CastShadows > 0;
    }

    private static int GetOmni2FaceCount(float outerAngleDeg, float heightOffset)
    {
        var angleRad = float.DegreesToRadians(Math.Clamp(outerAngleDeg, 1f, 180f));

        if (heightOffset > 0f)
        {
            angleRad = MathF.Max(MathF.PI / 2f, angleRad);
        }

        return angleRad switch
        {
            <= MathF.PI / 4f => 1,
            <= 3f * MathF.PI / 4f => 5,
            _ => 6
        };
    }
}
