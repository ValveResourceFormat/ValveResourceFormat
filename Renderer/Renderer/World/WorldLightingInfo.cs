using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.World
{
    /// <summary>
    /// Storage format for environment map cubemap textures.
    /// </summary>
    public enum CubemapType : byte
    {
        /// <summary>No environment cubemap data.</summary>
        None,
        /// <summary>Each probe has its own individual cubemap texture.</summary>
        IndividualCubemaps,
        /// <summary>All probe cubemaps are packed into a single texture array.</summary>
        CubemapArray,
    }

    /// <summary>
    /// Storage format for light probe irradiance data.
    /// </summary>
    public enum LightProbeType : byte
    {
        /// <summary>No light probe data.</summary>
        None,
        /// <summary>Each probe has its own individual irradiance texture.</summary>
        IndividualProbes,
        /// <summary>All probe irradiance data is packed into a single atlas texture.</summary>
        ProbeAtlas,
    }

    /// <summary>Which light face a binned barn light slot holds, so a cull bit read back later traces to
    /// the light it was about. Rebuilt every frame, so it only holds against the frame that produced it.</summary>
    /// <param name="Light">Light owning the slot.</param>
    /// <param name="FaceIndex">Index into that light's <see cref="SceneLight.BarnFaces"/>.</param>
    public readonly record struct BarnLightFaceSlot(SceneLight Light, int FaceIndex);

    /// <summary>
    /// Scene lighting data including lightmaps, reflection probes, and shadow maps.
    /// </summary>
    public class WorldLightingInfo(Scene scene)
    {
        /// <summary>Gets the lightmap textures indexed by uniform name.</summary>
        public Dictionary<string, RenderTexture> Lightmaps { get; } = [];
        /// <summary>Gets the list of scene light probes.</summary>
        public List<SceneLightProbe> LightProbes { get; } = [];
        /// <summary>Gets the list of environment map probes.</summary>
        public List<SceneEnvMap> EnvMaps { get; } = [];
        /// <summary>Gets the list of real-time barn lights.</summary>
        public List<SceneLight> BarnLights { get; } = [];
        /// <summary>Gets the environment map lookup by handshake ID.</summary>
        public Dictionary<int, SceneEnvMap> EnvMapHandshakes { get; } = [];
        /// <summary>Gets the light probe lookup by handshake ID.</summary>
        public Dictionary<int, SceneLightProbe> ProbeHandshakes { get; } = [];
        /// <summary>Gets or sets a value indicating whether the scene has a complete and usable lightmap set.</summary>
        public bool HasValidLightmaps { get; set; }
        /// <summary>Gets or sets a value indicating whether the scene has a complete and usable light probe set.</summary>
        public bool HasValidLightProbes { get; set; }
        /// <summary>Gets or sets the lightmap version number from the world data.</summary>
        public int LightmapVersionNumber { get; set; }
        /// <summary>Gets or sets the game-specific lightmap sub-version number.</summary>
        public int LightmapGameVersionNumber { get; set; }
        /// <summary>Gets or sets the GPU lighting constants buffer for the scene.</summary>
        public LightingConstants LightingData { get; set; } = new();

        /// <summary>Gets or sets the storage format used for environment map cubemaps in this scene.</summary>
        public CubemapType CubemapType
        {
            get => (CubemapType)scene.RenderAttributes.GetValueOrDefault("S_SCENE_CUBEMAP_TYPE");
            set => scene.RenderAttributes["S_SCENE_CUBEMAP_TYPE"] = (byte)value;
        }

        /// <summary>Gets or sets the storage format used for light probe irradiance data in this scene.</summary>
        public LightProbeType LightProbeType
        {
            get => (LightProbeType)scene.RenderAttributes.GetValueOrDefault("S_SCENE_PROBE_TYPE");
            set => scene.RenderAttributes["S_SCENE_PROBE_TYPE"] = (byte)value;
        }

        /// <summary>
        /// Gets or sets whether barn, rect and omni lights take their intensity from <c>brightness_legacy</c>.
        /// </summary>
        public bool UsesLegacyBarnBrightness { get; set; }

        /// <summary>Gets a value indicating whether the lightmap contains baked shadow data.</summary>
        public bool HasBakedShadowsFromLightmap => scene.RenderAttributes.GetValueOrDefault("S_LIGHTMAP_VERSION_MINOR") > 0;
        /// <summary>Gets or sets a value indicating whether dynamic shadow rendering is enabled.</summary>
        public bool EnableDynamicShadows { get; set; } = true;

        /// <summary>Number of sun shadow cascades rendered and sampled. Cascade 0 is the tightest.</summary>
        public const int SunCascadeCount = 2;

        /// <summary>Gets the combined view-projection matrices used for sun shadow rendering, one per cascade.</summary>
        public Matrix4x4[] SunViewProjections { get; } = new Matrix4x4[SunCascadeCount];
        /// <summary>Gets the frustums used for sun light shadow caster culling, one per cascade.</summary>
        public Frustum[] SunLightFrustums { get; } = CreateSunLightFrustums();
        /// <summary>Gets the normalized direction sun light travels, away from the sun.</summary>
        public Vector3 SunCastDirection { get; private set; } = Vector3.UnitX;

        /// <summary>Gets the number of cascades in use after the last <see cref="UpdateSunLightFrustum"/> call. Cascades beyond it only keep their layer cleared, which samples as fully lit.</summary>
        public int ActiveSunCascadeCount { get; private set; } = SunCascadeCount;

        private static Frustum[] CreateSunLightFrustums()
        {
            var frustums = new Frustum[SunCascadeCount];

            for (var i = 0; i < frustums.Length; i++)
            {
                frustums[i] = new Frustum();
            }

            return frustums;
        }
        /// <summary>Gets or sets the depth bias applied to sun light shadows to reduce self-shadowing artifacts.</summary>
        public float SunLightShadowBias { get; set; } = 0.001f;
        /// <summary>Gets or sets a scale factor applied to the sun light shadow coverage area.</summary>
        public float SunLightShadowCoverageScale { get; set; } = 1f;
        /// <summary>Gets or sets a value indicating whether the sun light frustum is fitted to the scene bounds rather than the camera.</summary>
        public bool UseSceneBoundsForSunLightFrustum { get; set; }

        /// <summary>Gets the size of the barn light shadow atlas texture, as recorded by the last <see cref="BinBarnLights"/> call.</summary>
        public int BarnLightShadowAtlasSize { get; private set; } = 4096;

        /// <summary>Gets the shadow mapper that culls and packs light shadow faces each frame.</summary>
        public ShadowMapper ShadowMapper { get; } = new();

        private readonly BarnLightConstants[] BinnedBarnLightGpuData = new BarnLightConstants[BarnLightConstants.MAX_BARN_LIGHTS];
        private readonly BarnLightCullVolume[] BinnedBarnLightCullVolumes = new BarnLightCullVolume[BarnLightConstants.MAX_BARN_LIGHTS];
        private readonly BarnLightFaceSlot[] BinnedBarnLightFaceSlots = new BarnLightFaceSlot[BarnLightConstants.MAX_BARN_LIGHTS];

        private Dictionary<string, int> BarnLightCookiePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        private StorageBuffer? BarnLightStorageBuffer;
        private RenderTexture? BarnLightCookieAtlas { get; set; }
        private RenderTexture? DefaultCookieAtlas;
        private int CookieSamplerClampBorder;
        private int CookieSamplerWrap;

        /// <summary>Binds the scene's lightmap, light probe atlas, and barn light cookie textures to their reserved units.</summary>
        public void BindLightmapTextures()
        {
            foreach (var (name, texture) in Lightmaps)
            {
                if (!MaterialLoader.ReservedTextureSlotByName.TryGetValue(name, out var lightmapSlot))
                {
                    Debug.Assert(false, $"Lightmap texture '{name}' has no reserved slot. Add it to {nameof(MaterialLoader.ReservedTextureSlotByName)}.");
                    continue;
                }

                GL.BindTextureUnit((int)lightmapSlot, texture.Handle);
            }

            if (LightProbeType == LightProbeType.ProbeAtlas && LightProbes.Count > 0)
            {
                BindProbeTexture("g_tLPV_Irradiance", LightProbes[0].Irradiance);
                BindProbeTexture("g_tLPV_Shadows", LightProbes[0].DirectLightShadows);
            }

            // Always bind something, even when the scene has no cookies: the cookie samplers are 2D arrays,
            // and leaving their reserved units empty makes shaders sample an incomplete texture.
            var cookieAtlas = BarnLightCookieAtlas ?? (DefaultCookieAtlas ??= CreateDefaultCookieAtlas());

            if (CookieSamplerClampBorder == 0)
            {
                CreateCookieSamplers();
            }

            GL.BindTextureUnit((int)ReservedTextureSlots.LightCookieTexture, cookieAtlas.Handle);
            GL.BindSampler((int)ReservedTextureSlots.LightCookieTexture, CookieSamplerClampBorder);

            GL.BindTextureUnit((int)ReservedTextureSlots.LightCookieTextureWrap, cookieAtlas.Handle);
            GL.BindSampler((int)ReservedTextureSlots.LightCookieTextureWrap, CookieSamplerWrap);
        }

        /// <summary>Binds the per-draw light probe volume textures. Individual-probe scenes only.</summary>
        public void BindInstanceLightProbeTextures(SceneLightProbe lightProbe)
        {
            if (LightProbeType != LightProbeType.IndividualProbes)
            {
                return;
            }

            BindProbeTexture("g_tLPV_Irradiance", lightProbe.Irradiance);

            if (LightmapGameVersionNumber == 1)
            {
                BindProbeTexture("g_tLPV_Indices", lightProbe.DirectLightIndices);
                BindProbeTexture("g_tLPV_Scalars", lightProbe.DirectLightScalars);
            }
            else if (LightmapGameVersionNumber >= 2)
            {
                BindProbeTexture("g_tLPV_Shadows", lightProbe.DirectLightShadows);
            }
        }

        /// <summary>Binds a light probe volume texture to the unit its sampler reads.</summary>
        private static void BindProbeTexture(string samplerName, RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            var slot = MaterialLoader.ReservedTextureSlotByName[samplerName];
            GL.BindTextureUnit((int)slot, texture.Handle);
        }

        /// <summary>
        /// Registers an environment map with the scene, setting the cubemap type on the first entry.
        /// </summary>
        /// <param name="envmap">The environment map to add.</param>
        public void AddEnvironmentMap(SceneEnvMap envmap)
        {
            if (EnvMaps.Count == 0)
            {
                CubemapType = envmap.EnvMapTexture.Target switch
                {
                    TextureTarget.TextureCubeMapArray => CubemapType.CubemapArray,
                    TextureTarget.TextureCubeMap => CubemapType.IndividualCubemaps,
                    _ => CubemapType.None,
                };

                if (CubemapType == CubemapType.CubemapArray)
                {
                    Lightmaps.TryAdd("g_tEnvironmentMap", envmap.EnvMapTexture);
                }
            }
            else
            {
                var first = EnvMaps[0];
                if (envmap.EnvMapTexture.Target != first.EnvMapTexture.Target)
                {
                    scene.RendererContext.Logger.LogError("Envmap texture target mismatch {EnvMapTarget} != {FirstTarget}", envmap.EnvMapTexture.Target, first.EnvMapTexture.Target);
                }
            }

            EnvMaps.Add(envmap);

            if (envmap.HandShake > 0)
            {
                scene.LightingInfo.EnvMapHandshakes.Add(envmap.HandShake, envmap);
            }
        }

        /// <summary>
        /// Registers a light probe with the scene, validating its texture set against the lightmap version.
        /// </summary>
        /// <param name="lightProbe">The light probe to add.</param>
        public void AddProbe(SceneLightProbe lightProbe)
        {
            if (scene.LightingInfo.LightmapVersionNumber == 0)
            {
                return;
            }

            var validTextureSet = (scene.LightingInfo.LightmapGameVersionNumber, lightProbe) switch
            {
                (_, { Irradiance: null }) => false,
                (1, { DirectLightIndices: null } or { DirectLightScalars: null }) => false,
                (2 or 3 or 4, { DirectLightShadows: null }) => false,
                _ => true,
            };

            HasValidLightProbes = (scene.LightingInfo.LightProbes.Count == 0 || HasValidLightProbes) && validTextureSet;

            scene.LightingInfo.LightProbes.Add(lightProbe);

            if (lightProbe.HandShake > 0)
            {
                scene.LightingInfo.ProbeHandshakes.Add(lightProbe.HandShake, lightProbe);
            }
        }

        // Depth extent, on each side of the eye, that sun shadow casters are culled within. The
        // rendered depth range is tightened to the collected casters by FitSunLightDepthRange.
        private const float SunShadowCullDepthRange = 8192f;

        // How far each cascade's coverage square is shifted toward the view, as a fraction of its
        // half-extent. Keeps the camera comfortably inside the square while spending most of the
        // area on what is in front of it.
        private const float SunShadowForwardShiftFraction = 0.5f;

        // Cascade half-extents as fractions of the base coverage size, innermost first. The
        // outermost stays near the legacy single-map coverage, which was always generous; the
        // inner cascade spends its equal resolution on close-up density instead of area.
        private static readonly float[] SunCascadeExtentFractions = [1f / 8f, 1f / 1.2f];

        // Keeps the innermost cascade usable at low resolution settings, where the base
        // coverage is small enough that an eighth of it would hug the camera.
        private const float MinSunCascadeHalfExtent = 128f;

        private readonly Matrix4x4[] sunShadowView = new Matrix4x4[SunCascadeCount];
        private readonly Vector3[] sunShadowEye = new Vector3[SunCascadeCount];
        private readonly float[] sunShadowHalfExtent = new float[SunCascadeCount];
        private bool sunShadowFitsDepthToCasters;

        /// <summary>Recalculates <see cref="SunViewProjections"/> and <see cref="SunLightFrustums"/> to fit the current camera view. Cascade extents follow <see cref="SunCascadeExtentFractions"/>. The frustums get a generous depth range for caster culling; once a cascade's casters are collected, <see cref="FitSunLightDepthRange"/> tightens its rendered range around them.</summary>
        /// <param name="camera">The active camera used to position the sun shadow frustum.</param>
        /// <param name="shadowMapSize">The shadow map resolution used to compute coverage and texel snapping.</param>
        public void UpdateSunLightFrustum(Camera camera, float shadowMapSize = 512f)
        {
            // The uniform stores surface-to-sun; the frustum looks along the rays, away from the sun
            var toSun = new Vector3(LightingData.SunDirection.X, LightingData.SunDirection.Y, LightingData.SunDirection.Z);
            var sunDir = toSun.LengthSquared() > 0.0001f ? Vector3.Normalize(-toSun) : Vector3.UnitX;

            var baseHalfExtent = Math.Max(shadowMapSize / 2.5f, 512f) * SunLightShadowCoverageScale;
            var bias = 0.001f;

            // Shift each coverage square toward the view. A caster shares its light-space footprint
            // with the shadow it casts, so area behind the view catches nothing the visible region
            // needs; casters toward the sun are captured along depth, not by the square.
            var forwardOnLightPlane = camera.Forward - sunDir * Vector3.Dot(camera.Forward, sunDir);

            sunShadowFitsDepthToCasters = true;

            // When the whole scene fits into the first cascade, fit it directly and skip the rest
            var sceneBoundsMode = false;
            var sceneEye = Vector3.Zero;
            var sceneHalfExtent = 0f;
            var sceneNearPlaneExtend = 0f;

            if (UseSceneBoundsForSunLightFrustum)
            {
                var staticBounds = scene.StaticOctree.Root.GetBounds();
                var dynamicBounds = scene.DynamicOctree.Root.GetBounds();
                var sceneBounds = staticBounds.Union(dynamicBounds);
                var max = Math.Max(sceneBounds.Size.X, Math.Max(sceneBounds.Size.Y, sceneBounds.Size.Z));

                if (max > 0 && max < shadowMapSize)
                {
                    sceneNearPlaneExtend = max / 2f;
                    sceneEye = staticBounds.Center - sunDir * sceneNearPlaneExtend;
                    sceneHalfExtent = max * 1.6f;
                    bias = 0.01f;
                    sceneBoundsMode = true;
                    sunShadowFitsDepthToCasters = false;
                }
            }

            // A sun pointing straight down leaves no horizontal axis to snap against, and world up is
            // no use as a reference either, so the frame is completed against forward instead
            var upReference = MathF.Abs(sunDir.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX;
            var right = Vector3.Normalize(Vector3.Cross(sunDir, upReference));
            var up = Vector3.Cross(right, sunDir);

            // When the whole scene already fits into the first cascade, the wider ones add nothing
            var singleCascade = sceneBoundsMode;

            ActiveSunCascadeCount = singleCascade ? 1 : SunCascadeCount;

            for (var cascade = 0; cascade < SunCascadeCount; cascade++)
            {
                if (singleCascade && cascade > 0)
                {
                    // The shader falls through identical coordinates into the cleared outer layer,
                    // which reads fully lit, matching the single-frustum edge fade.
                    SunViewProjections[cascade] = SunViewProjections[0];
                    SunLightFrustums[cascade].SetEmpty();
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
                    bbox = MathF.Max(baseHalfExtent * SunCascadeExtentFractions[cascade], MinSunCascadeHalfExtent);
                    farPlane = SunShadowCullDepthRange;
                    nearPlaneExtend = SunShadowCullDepthRange;
                    eye = camera.Location + forwardOnLightPlane * (bbox * SunShadowForwardShiftFraction);
                }

                // Stabilize shadow map by snapping eye position to texel-sized increments in world space
                var texelWorldSize = (4.0f * bbox) / shadowMapSize;

                // Project eye onto shadow camera's right/up axes and snap
                var eyeOffsetX = Vector3.Dot(eye, right);
                var eyeOffsetY = Vector3.Dot(eye, up);
                var eyeOffsetZ = Vector3.Dot(eye, sunDir);

                eyeOffsetX = MathF.Round(eyeOffsetX / texelWorldSize) * texelWorldSize;
                eyeOffsetY = MathF.Round(eyeOffsetY / texelWorldSize) * texelWorldSize;

                eye = right * eyeOffsetX + up * eyeOffsetY + sunDir * eyeOffsetZ;

                var sunCameraView = Matrix4x4.CreateLookAt(eye, eye + sunDir, upReference);
                var sunCameraProjection = Matrix4x4.CreateOrthographicOffCenter(-bbox, bbox, -bbox, bbox, farPlane, -nearPlaneExtend);

                SunViewProjections[cascade] = sunCameraView * sunCameraProjection;
                SunLightFrustums[cascade].Update(SunViewProjections[cascade]);

                sunShadowView[cascade] = sunCameraView;
                sunShadowEye[cascade] = eye;
                sunShadowHalfExtent[cascade] = bbox;
            }

            SunLightShadowBias = bias;
            SunCastDirection = sunDir;
        }

        /// <summary>Tightens the depth range of a cascade's <see cref="SunViewProjections"/> entry around the collected shadow casters, given their extent along <see cref="SunCastDirection"/> in world units. A tight range shrinks the world-space size of the normalized shadow bias. Receivers outside the range clamp in the shader and still compare correctly against every caster, and since the fit derives from the same culled set that renders, it covers every rendered caster by construction. <see cref="SunLightFrustums"/> keeps the generous culling range.</summary>
        /// <param name="cascade">The cascade index to tighten.</param>
        /// <param name="casterMin">Smallest caster projection onto the cast direction, or <see cref="float.MaxValue"/> when no casters were collected.</param>
        /// <param name="casterMax">Largest caster projection onto the cast direction.</param>
        public void FitSunLightDepthRange(int cascade, float casterMin, float casterMax)
        {
            if (!sunShadowFitsDepthToCasters || casterMin > casterMax)
            {
                return;
            }

            var eyeDepth = Vector3.Dot(sunShadowEye[cascade], SunCastDirection);

            // Quantized so the range holds still while casters move within it
            const float quantize = 64f;
            var depthMin = Math.Clamp(MathF.Floor((casterMin - eyeDepth) / quantize) * quantize, -SunShadowCullDepthRange, SunShadowCullDepthRange - quantize);
            var depthMax = Math.Clamp(MathF.Ceiling((casterMax - eyeDepth) / quantize) * quantize, depthMin + quantize, SunShadowCullDepthRange);

            var bbox = sunShadowHalfExtent[cascade];
            var projection = Matrix4x4.CreateOrthographicOffCenter(-bbox, bbox, -bbox, bbox, depthMax, depthMin);

            SunViewProjections[cascade] = sunShadowView[cascade] * projection;
        }

        /// <summary>
        /// Stores stationary and dynamic light data into <see cref="LightingData"/> using the V1 lightmap format.
        /// Stationary lights sit at their baked lightmap index and are lit through the per-texel strength
        /// textures; dynamic lights are appended after them and evaluated per pixel.
        /// </summary>
        /// <param name="lights">The list of scene lights to store.</param>
        public void StoreLightMappedLights_V1(List<SceneLight> lights)
        {
            void AddLight(SceneLight light, uint index)
            {
                LightingData.LightPosition_Type[index] = new Vector4(light.Position, (int)light.Type);
                LightingData.LightDirection_InvRange[index] = new Vector4(light.Direction, 1.0f / light.Range);
                LightingData.LightToWorld[index] = light.Transform;

                // g_vBakedLightColor: linear color premultiplied by brightness, render-specular flag in w.
                // The strength texels hold plain sqrt(saturate(attenuation) * visibility); lights that
                // look far dimmer than their surroundings in baked cubemaps (like the "light_disabled"
                // prefabs) are scripted to raise their brightness at runtime, not scaled at load.
                var premultipliedColor = ColorSpace.SrgbGammaToLinear(light.Color) * light.Brightness * light.BrightnessScale;
                LightingData.LightColor_Brightness[index] = new Vector4(premultipliedColor, light.RenderSpecular ? 1f : 0f);

                // zw carry the remaining render gates (diffuse, transmissive); specular sits in the color
                // zw layout not confirmed to match real shaders
                var diffuseGate = light.RenderDiffuse ? 1f : 0f;
                var transmissiveGate = light.RenderTransmissive ? 1f : 0f;

                LightingData.LightSpotInnerOuterCosines[index] = light.Entity == SceneLight.EntityType.Ortho
                    ? new Vector4(light.SizeParams.X, light.SizeParams.Y, diffuseGate, transmissiveGate)
                    : new Vector4(
                        MathF.Cos(float.DegreesToRadians(light.SpotInnerAngle)),
                        MathF.Cos(float.DegreesToRadians(light.SpotOuterAngle)),
                        diffuseGate, transmissiveGate);

                // g_vSingleLightFalloffParams. The shader evaluates 1 / (x * d + y * d^2) in world units,
                // so the coefficients carry the range normalization: the entity's attenuation is stated
                // over the light's range, not per unit. Without it a quadratic light is 1/d^2 with no
                // scale, which is black everywhere past a couple of units. The bias then makes the curve
                // reach zero exactly at the range, where the normalized distance is 1.
                var invRange = light.Range > 0f ? 1f / light.Range : 0f;
                var falloffAtRange = light.AttenuationLinear + light.AttenuationQuadratic;

                LightingData.LightFallOff[index] = new Vector4(
                    light.AttenuationLinear * invRange,
                    light.AttenuationQuadratic * invRange * invRange,
                    light.Range * light.Range,
                    falloffAtRange > 0f ? 1f / falloffAtRange : 0f);
            }


            foreach (var light in lights)
            {
                if (light.Cost != SceneLight.LightCost.Stationary
                    || light.StationaryLightIndex >= LightingConstants.MAX_LIGHTS
                    || !light.Enabled)
                {
                    continue;
                }

                var index = (uint)light.StationaryLightIndex;
                AddLight(light, index);

                LightingData.StaticLightCount = Math.Max(LightingData.StaticLightCount, index + 1);
            }

            static bool IsDynamicSegmentLight(SceneLight light)
            {
                // Env light has its own fast path
                if (light.Entity == SceneLight.EntityType.Environment)
                {
                    return false;
                }

                // Only entities authored as per-pixel are dynamic
                return light.Cost == SceneLight.LightCost.Dynamic
                    && light.Enabled
                    && (light.RenderDiffuse || light.RenderSpecular || light.RenderTransmissive);
            }

            var currentLightIndex = LightingData.StaticLightCount;

            foreach (var light in lights.Where(IsDynamicSegmentLight))
            {
                if (currentLightIndex >= LightingConstants.MAX_LIGHTS)
                {
                    scene.RendererContext.Logger.LogWarning("Too many lights in scene. Some lights were removed");
                    break;
                }

                AddLight(light, currentLightIndex++);
            }

            LightingData.DynamicLightCount = currentLightIndex;

            scene.RendererContext.Logger.LogDebug(
                "Lightmap version {Major}.{Minor}: {Stationary} stationary and {Dynamic} per-pixel lights of {Total} light entities",
                LightmapVersionNumber, LightmapGameVersionNumber, LightingData.StaticLightCount,
                currentLightIndex - LightingData.StaticLightCount, lights.Count);

            var envLight = lights.FirstOrDefault(static l => l.Entity == SceneLight.EntityType.Environment);
            if (envLight != null)
            {
                var bakedLightIndex = envLight.Cost == SceneLight.LightCost.Stationary ? envLight.StationaryLightIndex : -1;
                StoreSunLight(envLight, new Vector4(bakedLightIndex, 0f, 0f, 0f));
            }
        }

        /// <summary>Points the sun uniforms at the given Euler angles, keeping the sun color.</summary>
        public void SetSunDirectionFromAngles(Vector3 angles)
        {
            LightingData.SunDirection = new Vector4(-EntityTransformHelper.EulerAnglesToForwardDirection(angles), 0f);
        }

        /// <summary>
        /// Stores the environment light into the dedicated sun uniforms, which work across all
        /// lightmap versions like the HLVR sun fast path. The baked shadow data is the V2 one-hot
        /// channel mask, or the sun's baked light index in X for V1.
        /// </summary>
        private void StoreSunLight(SceneLight envLight, Vector4 bakedShadowData)
        {
            var premultipliedColor = ColorSpace.SrgbGammaToLinear(envLight.Color) * envLight.Brightness * envLight.BrightnessScale;

            LightingData.SunDirection = new Vector4(-envLight.Direction, 0f);
            LightingData.SunColor = new Vector4(premultipliedColor, envLight.RenderSpecular ? 1f : 0f);
            LightingData.SunLightBakedShadowMask = bakedShadowData;
        }

        /// <summary>
        /// Stores environment light data and queues real-time barn lights using the V2 lightmap format.
        /// </summary>
        /// <param name="lights">The list of scene lights to store.</param>
        public void StoreLightMappedLights_V2(List<SceneLight> lights)
        {
            var envLight = lights.FirstOrDefault(static l => l.Entity == SceneLight.EntityType.Environment);

            if (envLight != null)
            {
                StoreSunLight(envLight, envLight.BakedShadowMask);
            }

            LightingData.NumBarnLights = 0; // changed dynamically

            var filtered = lights.Where(SceneLight.IsRealTimeLight).ToList();
            if (filtered.Count == 0)
            {
                return;
            }

            BarnLights.AddRange(filtered);
            RebuildCookieAtlas();
        }

        /// <summary>Clear renderable barn light lists.</summary>
        public void ClearBarnLights()
        {
            LightingData.NumBarnLights = 0;
            ShadowMapper.ShadowCasters.Clear();
        }

        /// <summary>
        /// Culls and bins visible barn lights for the current frame, packing their shadow faces into the atlas.
        /// </summary>
        /// <param name="camera">The camera used for culling and shadow resolution selection.</param>
        /// <param name="atlasSize">Pixel size of the shadow atlas texture.</param>
        public void BinBarnLights(Camera camera, int atlasSize)
        {
            BarnLightShadowAtlasSize = atlasSize;
            LightingData.NumBarnLights = 0;

            scene.LightBinner.PollBarnLightVisibility();

            ShadowMapper.Bin(BarnLights, camera, atlasSize, BarnLightCookiePaths,
                scene.LightBinner.VisibilitySequence);

            foreach (ref readonly var binned in ShadowMapper.BinnedLights)
            {
                var light = binned.Light;

                // Wanted shadows but got no placements, don't render the light.
                if (binned.WantsShadows && !binned.HasShadows)
                {
                    if (!light.WasDropped)
                    {
                        light.WasDropped = true;
                        scene.RendererContext.Logger.LogWarning("Too many shadow casting lights, dropping light '{LightName}'", light.Name);
                    }

                    continue;
                }

                if (LightingData.NumBarnLights + light.BarnFaces.Length > BarnLightConstants.MAX_BARN_LIGHTS)
                {
                    if (!light.WasDropped)
                    {
                        light.WasDropped = true;
                        scene.RendererContext.Logger.LogWarning(
                            "Max barn light count ({Max}) reached, dropping light '{LightName}'",
                            BarnLightConstants.MAX_BARN_LIGHTS, light.Name);
                    }

                    continue;
                }

                var anyFaceDropped = false;

                for (var faceIndex = 0; faceIndex < light.BarnFaces.Length; faceIndex++)
                {
                    var data = light.BarnFaces[faceIndex].GpuData;

                    if (binned.HasShadows && (binned.MaskCulledFaces & (1u << faceIndex)) == 0u)
                    {
                        var placement = ShadowMapper.GetFacePlacement(binned.FirstFaceIndex + faceIndex);

                        if (!placement.Region.IsValid)
                        {
                            if (!light.WasDropped && !anyFaceDropped)
                            {
                                scene.RendererContext.Logger.LogWarning(
                                    "Barn light shadow atlas is full, skipping shadow face of light '{LightName}' (size {Size})",
                                    light.Name, binned.FaceWidth);
                            }

                            anyFaceDropped = true;
                            continue;
                        }

                        data.BarnLightShadowOffsetScale = placement.OffsetScale;
                        data.BarnLightShadowScale = 1.0f;
                    }

                    var hasRangeCutoff = light.Entity == SceneLight.EntityType.Omni2 && light.FallOff > 0f;

                    BinnedBarnLightFaceSlots[LightingData.NumBarnLights] = new BarnLightFaceSlot(light, faceIndex);

                    BinnedBarnLightCullVolumes[LightingData.NumBarnLights] = new BarnLightCullVolume
                    {
                        FrustumToWorld = light.BarnFaces[faceIndex].FrustumToWorld,
                        ObbToWorld = light.BarnFaces[faceIndex].ObbToWorld,
                        RangeSphere = hasRangeCutoff
                            ? new Vector4(light.Transform.Translation, light.Range)
                            : default,
                    };

                    BinnedBarnLightGpuData[LightingData.NumBarnLights++] = data;
                }

                light.WasDropped = anyFaceDropped;
            }

            var binnedCount = (int)LightingData.NumBarnLights;
            BarnLightStorageBuffer?.Update(BinnedBarnLightGpuData, 0, binnedCount * Unsafe.SizeOf<BarnLightConstants>());
        }

        /// <summary>Clears cached shadow map data for all registered barn lights.</summary>
        public void ClearBarnShadowCache()
        {
            foreach (var light in BarnLights)
            {
                Scene.ClearShadowCache(light);
            }
        }

        private void RebuildCookieAtlas()
        {
            BarnLightCookieAtlas?.Delete();
            BarnLightCookieAtlas = null;

            BarnLightCookiePaths.Clear();
            var cookieTextures = new List<RenderTexture>();

            foreach (var light in BarnLights)
            {
                if (light.CookieTexturePath != null && BarnLightCookiePaths.TryAdd(light.CookieTexturePath, cookieTextures.Count + 1))
                {
                    var tex = scene.RendererContext.MaterialLoader.GetTexture(light.CookieTexturePath, true);
                    cookieTextures.Add(tex);
                }
            }

            if (cookieTextures.Count > 0)
            {
                using var _ = scene.RendererContext.RenderState.Scope();
                BarnLightCookieAtlas = BuildCookieAtlas(cookieTextures);
            }
        }

        /// <summary>
        /// Single white layer, used to keep the cookie texture units complete in scenes without cookies.
        /// Matches layer 0 of a real atlas, which barn lights without a cookie index into.
        /// </summary>
        private static RenderTexture CreateDefaultCookieAtlas()
        {
            var atlas = new RenderTexture(TextureTarget.Texture2DArray, 1, 1, 1, 1, "EmptyCookieAtlas");
            GL.TextureStorage3D(atlas.Handle, 1, SizedInternalFormat.Srgb8Alpha8, 1, 1, 1);
            GL.TextureSubImage3D(atlas.Handle, 0, 0, 0, 0, 1, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 255, 255, 255, 255 });

            return atlas;
        }

        private static RenderTexture BuildCookieAtlas(List<RenderTexture> textures)
        {
            var atlasSize = 512;
            foreach (var tex in textures)
            {
                atlasSize = Math.Max(atlasSize, Math.Max(tex.Width, tex.Height));
            }

            var numLayers = textures.Count + 1;

            var atlas = new RenderTexture(TextureTarget.Texture2DArray, atlasSize, atlasSize, numLayers, 1, "CookieAtlas");
            GL.TextureStorage3D(atlas.Handle, 1, SizedInternalFormat.Srgb8Alpha8, atlasSize, atlasSize, numLayers);

            GL.CreateFramebuffers(1, out int readFbo);
            GL.CreateFramebuffers(1, out int drawFbo);

#if DEBUG
            GL.ObjectLabel(ObjectLabelIdentifier.Framebuffer, readFbo, 18, "CookieAtlasBlitSrc");
            GL.ObjectLabel(ObjectLabelIdentifier.Framebuffer, drawFbo, 18, "CookieAtlasBlitDst");
#endif

            // First layer is full white
            GL.NamedFramebufferTextureLayer(drawFbo, FramebufferAttachment.ColorAttachment0, atlas.Handle, 0, 0);
            GL.ClearNamedFramebuffer(drawFbo, ClearBuffer.Color, 0, [1f, 1f, 1f, 1f]);

            for (var i = 0; i < textures.Count; i++)
            {
                var tex = textures[i];

                GL.NamedFramebufferTexture(readFbo, FramebufferAttachment.ColorAttachment0, tex.Handle, 0);
                GL.NamedFramebufferTextureLayer(drawFbo, FramebufferAttachment.ColorAttachment0, atlas.Handle, 0, i + 1);

                GL.BlitNamedFramebuffer(readFbo, drawFbo,
                    0, 0, tex.Width, tex.Height,
                    0, 0, atlasSize, atlasSize,
                    ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
            }

            GL.DeleteFramebuffer(readFbo);
            GL.DeleteFramebuffer(drawFbo);

            return atlas;
        }

        private void CreateCookieSamplers()
        {
            GL.CreateSamplers(1, out CookieSamplerClampBorder);

#if DEBUG
            GL.ObjectLabel(ObjectLabelIdentifier.Sampler, CookieSamplerClampBorder, nameof(CookieSamplerClampBorder).Length, nameof(CookieSamplerClampBorder));
#endif

            GL.SamplerParameter(CookieSamplerClampBorder, SamplerParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.SamplerParameter(CookieSamplerClampBorder, SamplerParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            GL.SamplerParameter(CookieSamplerClampBorder, SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);

            GL.CreateSamplers(1, out CookieSamplerWrap);

#if DEBUG
            GL.ObjectLabel(ObjectLabelIdentifier.Sampler, CookieSamplerWrap, nameof(CookieSamplerWrap).Length, nameof(CookieSamplerWrap));
#endif

            GL.SamplerParameter(CookieSamplerWrap, SamplerParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.SamplerParameter(CookieSamplerWrap, SamplerParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.SamplerParameter(CookieSamplerWrap, SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        }

        /// <summary>Allocates the GPU storage buffer used to pass barn light data to shaders.</summary>
        public void CreateBarnLightBuffer()
        {
            BarnLightStorageBuffer ??= StorageBuffer.Allocate<BarnLightConstants>(
                ReservedBufferSlots.BarnLights, nameof(ReservedBufferSlots.BarnLights), BarnLightConstants.MAX_BARN_LIGHTS, BufferUsageHint.DynamicDraw);
        }

        /// <summary>Binds the barn light storage buffer to its reserved shader slot.</summary>
        public void BindBarnLightBuffer()
        {
            BarnLightStorageBuffer?.BindBufferBase();
        }

        /// <summary>
        /// Gets what bounds every binned barn light face, in the order the shading pass indexes them, so
        /// a cull item's bit position is its light index.
        /// </summary>
        public ReadOnlySpan<BarnLightCullVolume> BinnedBarnLightVolumes
            => BinnedBarnLightCullVolumes.AsSpan(0, (int)LightingData.NumBarnLights);

        /// <summary>Gets which light face each binned slot holds, in the same order as
        /// <see cref="BinnedBarnLightVolumes"/>, so a cull bit traces back to the face it culled.</summary>
        public ReadOnlySpan<BarnLightFaceSlot> BinnedBarnLightFaces
            => BinnedBarnLightFaceSlots.AsSpan(0, (int)LightingData.NumBarnLights);

        /// <summary>Releases the barn light GPU buffer, cookie atlas texture, and sampler objects.</summary>
        public void DisposeBarnLights()
        {
            BarnLightStorageBuffer?.Delete();

            BarnLightCookieAtlas?.Delete();
            BarnLightCookieAtlas = null;

            DefaultCookieAtlas?.Delete();
            DefaultCookieAtlas = null;

            if (CookieSamplerClampBorder != 0)
            {
                GL.DeleteSampler(CookieSamplerClampBorder);
                CookieSamplerClampBorder = 0;
            }

            if (CookieSamplerWrap != 0)
            {
                GL.DeleteSampler(CookieSamplerWrap);
                CookieSamplerWrap = 0;
            }
        }
    }
}
