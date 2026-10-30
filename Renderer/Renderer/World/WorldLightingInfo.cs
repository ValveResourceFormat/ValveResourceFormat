using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.World
{
    /// <summary>Which light face a binned barn light slot holds, so a cull bit read back later traces to
    /// the light it was about. Rebuilt every frame, so it only holds against the frame that produced it.</summary>
    /// <param name="Light">Light owning the slot.</param>
    /// <param name="FaceIndex">Index into that light's <see cref="SceneLight.BarnFaces"/>.</param>
    public readonly record struct BarnLightFaceSlot(SceneLight Light, int FaceIndex);

    /// <summary>
    /// Scene lighting data including lightmaps, reflection probes, and shadow maps.
    /// </summary>
    public class WorldLightingInfo(Scene scene) : IParticleLighting
    {
        /// <summary>Gets the lightmap textures indexed by uniform name.</summary>
        public Dictionary<string, RenderTexture> Lightmaps { get; } = [];
        /// <summary>Gets the list of scene light probes.</summary>
        public List<SceneLightProbe> LightProbes { get; } = [];
        /// <summary>Gets the atlas the light probes sample, once <see cref="BuildProbeAtlas"/> has run.</summary>
        public LightProbeAtlas? ProbeAtlas { get; private set; }
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

        /// <inheritdoc/>
        /// <remarks>A scene without baked lighting answers white, as the particle shaders light it; one with baked lighting answers a flat grey.</remarks>
        public Vector3 SampleAmbientLight(Vector3 position)
            => HasValidLightProbes || HasValidLightmaps ? new Vector3(0.3f) : Vector3.One;

        /// <summary>
        /// Creates baked lighting combo args for an object, based on object and scene lighting state.
        /// </summary>
        public Dictionary<string, byte> CreateShaderArguments(bool hasLightmapUvs = false, bool hasVertexLighting = false)
        {
            var arguments = new Dictionary<string, byte>(scene.RenderAttributes);

            if (hasLightmapUvs && HasValidLightmaps)
            {
                arguments["D_BAKED_LIGHTING_FROM_LIGHTMAP"] = 1;
            }
            else if (hasVertexLighting)
            {
                arguments["D_BAKED_LIGHTING_FROM_VERTEX_STREAM"] = 1;
            }
            else if (HasValidLightProbes)
            {
                arguments["D_BAKED_LIGHTING_FROM_PROBE"] = 1;
            }

            return arguments;
        }

        /// <summary>Gets a value indicating whether the lightmap contains baked shadow data.</summary>
        public bool HasBakedShadowsFromLightmap => HasValidLightmaps && scene.RenderAttributes.GetValueOrDefault("S_LIGHTMAP_VERSION_MINOR") > 0;
        /// <summary>Gets or sets a value indicating whether dynamic shadow rendering is enabled.</summary>
        public bool EnableDynamicShadows { get; set; } = true;

        /// <summary>Gets or sets a scale factor applied to the sun light shadow coverage area.</summary>
        public float SunLightShadowCoverageScale { get; set; } = 1f;
        /// <summary>Gets or sets a value indicating whether the sun light frustum is fitted to the scene bounds rather than the camera.</summary>
        public bool UseSceneBoundsForSunLightFrustum { get; set; }

        /// <summary>Gets the size of the barn light shadow atlas texture, as recorded by the last <see cref="BinBarnLights"/> call.</summary>
        public int BarnLightShadowAtlasSize { get; private set; } = 4096;

        /// <summary>Gets the shadow mapper that culls and packs light shadow faces each frame.</summary>
        public ShadowMapper ShadowMapper { get; } = new();

        private readonly BarnLightConstants[] BinnedBarnLightGpuData = new BarnLightConstants[BarnLightConstants.MAX_BARN_LIGHT_FACES];
        private readonly BarnLightCullVolume[] BinnedBarnLightCullVolumes = new BarnLightCullVolume[BarnLightConstants.MAX_BARN_LIGHT_FACES];
        private readonly BarnLightFaceSlot[] BinnedBarnLightFaceSlots = new BarnLightFaceSlot[BarnLightConstants.MAX_BARN_LIGHT_FACES];

        private BarnLightSortKey[] BarnLightSortKeys = new BarnLightSortKey[BarnLightConstants.MAX_BARN_LIGHTS];

        /// <summary>What decides which visible lights make the per view cap, in order of priority.</summary>
        private struct BarnLightSortKey : IComparable<BarnLightSortKey>
        {
            public int BinnedIndex;
            public float ScreenSize;
            public float DistanceSquared;
            public float Luminance;
            public float Facing;
            public float LightFade;
            public float ShadowFade;

            /// <summary>Orders the most important light first: larger on screen, then nearer, brighter and facing the camera's view.</summary>
            public readonly int CompareTo(BarnLightSortKey other)
            {
                var screenSize = other.ScreenSize.CompareTo(ScreenSize);
                if (screenSize != 0)
                {
                    return screenSize;
                }

                var distance = DistanceSquared.CompareTo(other.DistanceSquared);
                if (distance != 0)
                {
                    return distance;
                }

                var luminance = other.Luminance.CompareTo(Luminance);
                if (luminance != 0)
                {
                    return luminance;
                }

                var facing = other.Facing.CompareTo(Facing);
                return facing != 0 ? facing : BinnedIndex.CompareTo(other.BinnedIndex);
            }
        }

        private Dictionary<string, int> BarnLightCookiePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        private StorageBuffer? BarnLightStorageBuffer;
        private RenderTexture? BarnLightCookieAtlas { get; set; }
        private RenderTexture? DefaultCookieAtlas;
        private Dictionary<string, int> LightCookieSlices { get; } = new(StringComparer.OrdinalIgnoreCase);
        private RenderTexture? LightCookieVolume;
        private bool LightCookiesLoaded;
        private int CookieSamplerClampBorder;
        private int CookieSamplerWrap;
        private bool WarnedAboutTooManyLights;

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

            // Shaders always declare the array, and sample nothing from it in a scene without environment maps
            var envMapArray = EnvMapArray ?? scene.RendererContext.MaterialLoader.GetDefaultCubeArray();
            GL.BindTextureUnit((int)ReservedTextureSlots.EnvironmentMap, envMapArray.Handle);

            if (ProbeAtlas != null)
            {
                // A texture no volume has is still sampled, and reads as no direct light and no shadow
                var noDirectLight = scene.RendererContext.MaterialLoader.GetDefaultBlackVolume();

                BindProbeTexture("g_tLPV_Irradiance", ProbeAtlas.Irradiance);
                BindProbeTexture("g_tLPV_Indices", ProbeAtlas.DirectLightIndices ?? noDirectLight);
                BindProbeTexture("g_tLPV_Scalars", ProbeAtlas.DirectLightScalars ?? noDirectLight);
                BindProbeTexture("g_tLPV_Shadows", ProbeAtlas.DirectLightShadows ?? noDirectLight);
            }

            // Always bind something, even when the scene has no cookies: the cookie samplers are 2D arrays,
            // and leaving their reserved units empty makes shaders sample an incomplete texture.
            var cookieAtlas = BarnLightCookieAtlas ?? (DefaultCookieAtlas ??= CreateDefaultCookieAtlas());

            EnsureCookieSamplers();

            GL.BindTextureUnit((int)ReservedTextureSlots.LightCookieTexture, cookieAtlas.Handle);
            GL.BindSampler((int)ReservedTextureSlots.LightCookieTexture, CookieSamplerClampBorder);

            GL.BindTextureUnit((int)ReservedTextureSlots.LightCookieTextureWrap, cookieAtlas.Handle);
            GL.BindSampler((int)ReservedTextureSlots.LightCookieTextureWrap, CookieSamplerWrap);

            BindLightCookieVolume();
        }

        /// <summary>Binds the light cookie volume of spot lights, or a black one when the scene has none.</summary>
        public void BindLightCookieVolume()
        {
            EnsureCookieSamplers();

            // The light cookie volume is a 3D sampler, so it needs a texture of its own kind bound as well
            var cookieVolume = LightCookieVolume ?? scene.RendererContext.MaterialLoader.GetDefaultBlackVolume();

            GL.BindTextureUnit((int)ReservedTextureSlots.LightCookieVolume, cookieVolume.Handle);
            GL.BindSampler((int)ReservedTextureSlots.LightCookieVolume, CookieSamplerClampBorder);
        }

        private void EnsureCookieSamplers()
        {
            if (CookieSamplerClampBorder == 0)
            {
                CreateCookieSamplers();
            }
        }

        /// <summary>
        /// Sets up the atlas the light probes sample: the one the map baked, or else one packed from the volumes that
        /// were each baked into textures of their own. Only volumes in it are lit by, see <see cref="IsInProbeAtlas"/>.
        /// </summary>
        internal void BuildProbeAtlas()
        {
            if (ProbeAtlas != null || LightProbes.Count == 0)
            {
                return;
            }

            // Shaders read one atlas, so volumes with textures of their own are not packed with a baked one
            if (LightProbes.Find(static volume => IsInBakedAtlas(volume) && volume.Irradiance != null) is { } bakedVolume)
            {
                ProbeAtlas = LightProbeAtlas.FromBaked(bakedVolume);

                if (LightProbes.Count(static volume => !IsInBakedAtlas(volume)) is > 0 and var ownTextures)
                {
                    scene.RendererContext.Logger.LogWarning("{Count} light probe volumes with textures of their own are left out, the map baked the others into an atlas", ownTextures);
                }

                return;
            }

            var rendererContext = scene.RendererContext;
            ProbeAtlas = LightProbeAtlas.Pack(LightProbes, rendererContext.MaterialLoader, rendererContext.Logger);
        }

        /// <summary>Gets the cubemap array the environment maps sample, once <see cref="BuildEnvMapArray"/> has run.</summary>
        public RenderTexture? EnvMapArray { get; private set; }

        private RenderTexture? packedEnvMapArray;

        /// <summary>
        /// Sets up the cubemap array the environment maps sample: the one the map baked, or else one packed from the
        /// cubemaps baked as separate textures. Environment maps not in it are removed.
        /// </summary>
        internal void BuildEnvMapArray()
        {
            if (EnvMapArray != null || EnvMaps.Count == 0)
            {
                return;
            }

            // Shaders read one array, so separate cubemaps are not packed with a baked one
            var array = EnvMaps.Find(static envMap => envMap.EnvMapTexture.Target == TextureTarget.TextureCubeMapArray)?.EnvMapTexture;

            if (array == null)
            {
                var rendererContext = scene.RendererContext;
                array = packedEnvMapArray = EnvironmentMapArray.Pack(EnvMaps, rendererContext.MaterialLoader, rendererContext.Logger);
            }

            if (array == null)
            {
                EnvMaps.Clear();
                EnvMapHandshakes.Clear();
                return;
            }

            if (EnvMaps.RemoveAll(envMap => envMap.EnvMapTexture != array) is > 0 and var removed)
            {
                scene.RendererContext.Logger.LogWarning("{Count} environment maps are not in the cubemap array and are left out", removed);

                foreach (var (handshake, envMap) in EnvMapHandshakes.ToList())
                {
                    if (envMap.EnvMapTexture != array)
                    {
                        EnvMapHandshakes.Remove(handshake);
                    }
                }
            }

            EnvMapArray = array;
        }

        /// <summary>Deletes the cubemap array, when it was packed for the scene.</summary>
        internal void DeleteEnvMapArray()
        {
            packedEnvMapArray?.Delete();
            packedEnvMapArray = null;
            EnvMapArray = null;
        }

        // A volume placed in an atlas the map baked, rather than with textures of its own
        private static bool IsInBakedAtlas(SceneLightProbe volume) => volume.AtlasSize != Vector3.Zero;

        /// <summary>Gets whether a light probe volume has a place in <see cref="ProbeAtlas"/>, and so can be sampled.</summary>
        /// <param name="volume">The volume.</param>
        internal bool IsInProbeAtlas(SceneLightProbe volume)
            => ProbeAtlas != null && volume.Irradiance != null && volume.AtlasSize != Vector3.Zero;

        /// <summary>Deletes the light probe atlas, when it was packed for the scene.</summary>
        internal void DeleteProbeAtlas()
        {
            ProbeAtlas?.Delete();
            ProbeAtlas = null;
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
        /// Registers an environment map with the scene. Separate cubemaps are packed into an array before they are
        /// sampled, see <see cref="BuildEnvMapArray"/>.
        /// </summary>
        /// <param name="envmap">The environment map to add.</param>
        public void AddEnvironmentMap(SceneEnvMap envmap)
        {
            EnvMaps.Add(envmap);

            if (envmap.HandShake > 0)
            {
                scene.LightingInfo.EnvMapHandshakes.Add(envmap.HandShake, envmap);
            }
        }

        /// <summary>Forgets every registered environment map and light probe, for the entities that register them to spawn again.</summary>
        internal void ClearVolumes()
        {
            EnvMaps.Clear();
            EnvMapHandshakes.Clear();
            LightProbes.Clear();
            ProbeHandshakes.Clear();
            DeleteProbeAtlas();
            DeleteEnvMapArray();
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
                (2 or 3 or 4 or 5, { DirectLightShadows: null }) => false,
                _ => true,
            };

            HasValidLightProbes = (scene.LightingInfo.LightProbes.Count == 0 || HasValidLightProbes) && validTextureSet;

            scene.LightingInfo.LightProbes.Add(lightProbe);

            if (lightProbe.HandShake > 0)
            {
                scene.LightingInfo.ProbeHandshakes[lightProbe.HandShake] = lightProbe;
            }
        }

        private List<SceneLight> storedLights = [];
        private bool usesUniformLightStore;

        /// <summary>
        /// Stores the map's lights to GPU, replacing any stored before.
        /// </summary>
        public void StoreLights(List<SceneLight> lights)
        {
            storedLights = lights;
            usesUniformLightStore = (LightmapVersionNumber, LightmapGameVersionNumber) is (6, 0) or (8, 0) or (8, 1);

            if (usesUniformLightStore)
            {
                LoadLightCookies();
                StoreLightMappedLights_V1(lights);

                // Not in the store itself, which light styles run every tick
                scene.RendererContext.Logger.LogDebug(
                    "Lightmap version {Major}.{Minor}: {Stationary} stationary and {Dynamic} per-pixel lights of {Total} light entities",
                    LightmapVersionNumber, LightmapGameVersionNumber, LightingData.StaticLightCount,
                    LightingData.DynamicLightCount - LightingData.StaticLightCount, lights.Count);
            }
            else
            {
                StoreLightMappedLights_V2(lights);
            }
        }

        private bool storedLightsChanged;

        /// <summary>Marks the stored lights for <see cref="StoreChangedLights"/>.</summary>
        public void MarkStoredLightsChanged() => storedLightsChanged = true;

        /// <summary>Stores the lights again if they changed since they were last stored.</summary>
        public void StoreChangedLights()
        {
            if (!storedLightsChanged)
            {
                return;
            }

            storedLightsChanged = false;
            UpdateGpuLightBuffers();
        }

        /// <summary>Gets the index of a light in the lighting constants, or -1 for a light that is not stored there.</summary>
        internal int GetStoredLightIndex(SceneLight light)
        {
            if (!usesUniformLightStore)
            {
                return -1;
            }

            if (DynamicLightIndices.TryGetValue(light, out var dynamicIndex))
            {
                return (int)dynamicIndex;
            }

            var isStored = light.Cost == SceneLight.LightCost.Stationary
                && light.Enabled
                && light.StationaryLightIndex is >= 0 and < LightingConstants.MAX_LIGHTS;

            return isStored ? light.StationaryLightIndex : -1;
        }

        /// <summary>Re-stores the GPU light properties.</summary>
        public void UpdateGpuLightBuffers()
        {
            if (storedLights.Count == 0)
            {
                return;
            }

            if (usesUniformLightStore)
            {
                StoreLightMappedLights_V1(storedLights);
                return;
            }

            var envLight = storedLights.FirstOrDefault(static l => l.Entity == SceneLight.EntityType.Environment);
            if (envLight != null)
            {
                StoreSunLight(envLight, envLight.BakedShadowMask);
            }
        }

        /// <summary>
        /// Stores stationary and dynamic light data into <see cref="LightingData"/> using the V1 lightmap format.
        /// Stationary lights sit at their baked lightmap index and are lit through the per-texel strength
        /// textures; dynamic lights are appended after them and evaluated per pixel.
        /// </summary>
        /// <param name="lights">The list of scene lights to store.</param>
        public void StoreLightMappedLights_V1(List<SceneLight> lights)
        {
            void AddLight(SceneLight light, uint index, float colorScale)
            {
                var range = light.GetAttenuatedRange();

                LightingData.LightPosition_Type[index] = new Vector4(light.Position, (int)light.Type);
                LightingData.LightDirection_InvRange[index] = new Vector4(light.Direction, range > 0f ? 1f / range : 0f);
                LightingData.LightToWorld[index] = light.Transform;

                // g_vBakedLightColor: linear color premultiplied by brightness, render-specular flag in w.
                // Stationary lights also carry the scale of their baked strengths; lights that look far
                // dimmer than their surroundings in baked cubemaps (like the "light_disabled" prefabs)
                // are scripted to raise their brightness at runtime, not scaled at load.
                var premultipliedColor = ColorSpace.SrgbGammaToLinear(light.Color) * light.Brightness * light.BrightnessScale * light.StyleBrightness * colorScale;
                LightingData.LightColor_Brightness[index] = new Vector4(premultipliedColor, light.RenderSpecular ? 1f : 0f);

                // zw carry the remaining render gates (diffuse, transmissive); specular sits in the color
                // zw layout not confirmed to match real shaders
                var diffuseGate = light.RenderDiffuse ? 1f : 0f;
                var transmissiveGate = light.RenderTransmissive ? 1f : 0f;

                var innerCosine = Math.Clamp(MathF.Cos(float.DegreesToRadians(light.SpotInnerAngle)), 0f, 1f);
                var outerCosine = Math.Clamp(MathF.Cos(float.DegreesToRadians(light.SpotOuterAngle)), 0f, 1f);

                LightingData.LightSpotInnerOuterCosines[index] = light.Entity == SceneLight.EntityType.Ortho
                    ? new Vector4(light.SizeParams.X, light.SizeParams.Y, diffuseGate, transmissiveGate)
                    : new Vector4(innerCosine, outerCosine, diffuseGate, transmissiveGate);

                // g_vSingleLightFalloffParams: the raw linear and quadratic terms, as the shader evaluates
                // saturate(1 / (x * d + y * d^2) - w) in world units before the color, which carries the
                // q*10000 + l*100 + c scale. The bias w brings the curve to zero at the range; a light with a
                // constant term gets none.
                // TODO: light_ortho packs other values here, with no distance limit in z
                var linear = light.AttenuationLinear;
                var quadratic = light.AttenuationQuadratic;
                var hasBias = light.AttenuationConstant == 0f && range > 0f;
                var falloffAtRange = (quadratic * range * range) + (linear * range) + 1.1920929e-7f;

                LightingData.LightFallOff[index] = new Vector4(linear, quadratic, range * range, hasBias ? 1f / falloffAtRange : 0f);

                var cookieSlice = -1;

                if (light.CookieName != null && LightCookieSlices.TryGetValue(light.CookieName, out var slice))
                {
                    cookieSlice = slice;
                }

                var outerHalfAngle = float.DegreesToRadians(Math.Clamp(light.SpotOuterAngle, 1f, 89f));

                ref var shadowParams = ref LightingData.LightShadowParams[index];
                shadowParams.Y = cookieSlice;
                shadowParams.Z = 1f / MathF.Tan(outerHalfAngle);
            }

            var usesFastPath = scene.RendererContext.FileLoader.GameInfo.VrLightBinnerSingleLightFastPath;

            // The light indices may change, so the shadow slots are assigned again when the shadows are binned
            ClearDynamicLightShadows();
            ShadowedDynamicLights.Clear();
            ShadowedDynamicLightIndices.Clear();
            DynamicLightIndices.Clear();
            MixedShadowedLights.Clear();

            foreach (var light in lights)
            {
                if (light.Cost != SceneLight.LightCost.Stationary
                    || light.StationaryLightIndex >= LightingConstants.MAX_LIGHTS
                    || !light.Enabled)
                {
                    continue;
                }

                var index = (uint)light.StationaryLightIndex;
                AddLight(light, index, light.BakedStrengthScale);

                if (usesFastPath && light.HasVrMixedLocalShadow)
                {
                    MixedShadowedLights.Add(light);
                    ShadowedDynamicLightIndices[light] = index;
                }

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
                    if (!WarnedAboutTooManyLights)
                    {
                        WarnedAboutTooManyLights = true;
                        scene.RendererContext.Logger.LogWarning("Too many lights in scene. Some lights were removed");
                    }

                    break;
                }

                if (light.Entity == SceneLight.EntityType.Spot && light.CastShadows == 1)
                {
                    ShadowedDynamicLights.Add(light);
                    ShadowedDynamicLightIndices[light] = currentLightIndex;
                }

                if (usesFastPath && light.IsFastPathFxLight && light.Range > 0f)
                {
                    // Brightened by its color alone and falling off as 1 / distance - 1 / range
                    AddLight(light, currentLightIndex, 1f);
                    LightingData.LightFallOff[currentLightIndex] = new Vector4(1f, 0f, light.Range * light.Range, 1f / light.Range);
                }
                else
                {
                    AddLight(light, currentLightIndex, light.AttenuationColorScale);
                }

                DynamicLightIndices[light] = currentLightIndex;
                currentLightIndex++;
            }

            LightingData.DynamicLightCount = currentLightIndex;

            var envLight = lights.FirstOrDefault(static l => l.Entity == SceneLight.EntityType.Environment);
            if (envLight != null)
            {
                var bakedLightIndex = envLight.Cost == SceneLight.LightCost.Stationary ? envLight.StationaryLightIndex : -1;

                // The VR light binner only gives the sun a real-time shadow when the light asks for mixed shadows
                if (usesFastPath && !(envLight.MixedShadows && envLight.CastShadows == 1))
                {
                    bakedLightIndex = -1;
                }

                StoreSunLight(envLight, new Vector4(bakedLightIndex, 0f, 0f, 0f));

                if (LightmapGameVersionNumber == 0 && envLight.Cost == SceneLight.LightCost.Static)
                {
                    LightingData.SunColor = Vector4.Zero;
                }
            }
        }

        /// <summary>Points the sun uniforms at the given Euler angles, keeping the sun color.</summary>
        public void SetSunDirectionFromAngles(Vector3 angles)
        {
            LightingData.SunDirection = new Vector4(-EntityTransformHelper.EulerAnglesToForwardDirection(angles), LightingData.SunDirection.W);
            UpdateAmbientLighting();
        }

        /// <summary>
        /// Fills the ambient lighting with a sky color that fades into a ground color over the hemisphere, for
        /// scenes without an environment light of their own.
        /// </summary>
        /// <param name="sky">Linear color of the ambient lighting facing up.</param>
        /// <param name="ground">Linear color of the ambient lighting facing down.</param>
        public void SetHemisphereAmbientLighting(Vector3 sky, Vector3 ground)
        {
            skyLight = sky;
            groundLight = ground;
            UpdateAmbientLighting();
        }

        private Vector3 skyLight;
        private Vector3? groundLight;
        private Vector3 skyAmbientBounce;
        private float skyAmbientBounceAlpha = 1f;
        private bool? lowerHemisphereIsBlack;

        /// <summary>
        /// Rebuilds the ambient lighting that fills in where no light probe volume reaches: the sky from
        /// above, and the sun and sky the ground bounces back from below. It only varies with height.
        /// </summary>
        private void UpdateAmbientLighting()
        {
            if (groundLight is { } ground)
            {
                var skyVertical = (skyLight - ground) * 0.5f;
                var skyConstant = (skyLight + ground) * 0.5f;

                LightingData.AmbientLightingSHR = new Vector4(0f, 0f, skyVertical.X, skyConstant.X);
                LightingData.AmbientLightingSHG = new Vector4(0f, 0f, skyVertical.Y, skyConstant.Y);
                LightingData.AmbientLightingSHB = new Vector4(0f, 0f, skyVertical.Z, skyConstant.Z);
                return;
            }

            var sunDown = -LightingData.SunDirection.Z;
            var sunBounce = LightingData.SunColor.AsVector3() * skyAmbientBounce * 0.5f;

            Vector3 vertical;
            Vector3 constant;

            // Games whose sun has a lower hemisphere setting ignore the bounce alpha, the others blend the sky by it
            var hasLowerHemisphereSetting = lowerHemisphereIsBlack.HasValue
                || scene.RendererContext.FileLoader.GameInfo.VrLightBinnerSingleLightFastPath;

            if (hasLowerHemisphereSetting)
            {
                var isLowerHemisphereBlack = lowerHemisphereIsBlack ?? true;

                // A black lower hemisphere halves the sky and moves it all into the upper half, otherwise it lights every direction alike
                var sky = isLowerHemisphereBlack ? skyLight * 0.5f : skyLight;
                var upperSky = isLowerHemisphereBlack ? sky : Vector3.Zero;
                var skyBounce = sky * skyAmbientBounce * 0.5f;

                vertical = upperSky - skyBounce + (sunBounce * sunDown);
                constant = sky + skyBounce + (sunBounce * MathF.Abs(sunDown));
            }
            else
            {
                var halfBounce = 0.5f * skyAmbientBounceAlpha;
                var skyBounce = skyLight * skyAmbientBounce * halfBounce;
                var bouncedSun = sunBounce * skyAmbientBounceAlpha;

                vertical = (skyLight * halfBounce) - skyBounce + (bouncedSun * sunDown);
                constant = (skyLight * (1f - halfBounce)) + skyBounce + (bouncedSun * MathF.Abs(sunDown));
            }

            LightingData.AmbientLightingSHR = new Vector4(0f, 0f, vertical.X, constant.X);
            LightingData.AmbientLightingSHG = new Vector4(0f, 0f, vertical.Y, constant.Y);
            LightingData.AmbientLightingSHB = new Vector4(0f, 0f, vertical.Z, constant.Z);
        }

        /// <summary>
        /// Gets the roughness the highlight of the sun is no sharper than. Only CS2 keeps the sun from shining as a
        /// point; the other games leave its highlight as sharp as the material is.
        /// </summary>
        /// <param name="lightMinRoughness">The minimum roughness the light itself asks for.</param>
        /// <returns>The minimum roughness, zero where it does not apply.</returns>
        public float GetSunMinRoughness(float lightMinRoughness = SceneLight.MinSpecularRoughness)
            => scene.RendererContext.FileLoader.GameInfo.Game == Game.Csgo
                ? MathF.Max(SceneLight.MinSpecularRoughness, lightMinRoughness)
                : 0f;

        /// <summary>
        /// Stores the environment light into the dedicated sun uniforms, which work across all
        /// lightmap versions. The baked shadow data is the V2 one-hot channel mask, or the sun's
        /// baked light index in X for V1.
        /// </summary>
        private void StoreSunLight(SceneLight envLight, Vector4 bakedShadowData)
        {
            var brightness = envLight.Brightness * envLight.BrightnessScale * envLight.StyleBrightness;
            var premultipliedColor = ColorSpace.SrgbGammaToLinear(envLight.Color) * brightness;

            LightingData.SunDirection = new Vector4(-envLight.Direction, GetSunMinRoughness(envLight.MinRoughness));
            LightingData.SunColor = new Vector4(premultipliedColor, envLight.RenderSpecular ? 1f : 0f);
            LightingData.SunLightBakedShadowMask = bakedShadowData;

            skyLight = ColorSpace.SrgbGammaToLinear(envLight.SkyColor) * envLight.SkyIntensity;
            groundLight = envLight.GroundColor is { } groundColor
                ? ColorSpace.SrgbGammaToLinear(groundColor) * envLight.SkyIntensity
                : null;
            skyAmbientBounce = ColorSpace.SrgbGammaToLinear(envLight.SkyAmbientBounce);
            skyAmbientBounceAlpha = envLight.SkyAmbientBounceAlpha;
            lowerHemisphereIsBlack = envLight.LowerHemisphereIsBlack;
            UpdateAmbientLighting();

            var ambientDirectionColor = ColorSpace.SrgbGammaToLinear(envLight.AmbientDirectionColor) * envLight.AmbientDirectionIntensity;
            LightingData.AmbientDirection_ShadowAmount = new Vector4(envLight.AmbientDirection, envLight.AmbientShadowAmount);
            LightingData.AmbientDirectionColor = new Vector4(ambientDirectionColor, 0f);

            // The independent part of the highlight is scaled like the sun, the rest only by the light falling on the albedo
            var specularColor = ColorSpace.SrgbGammaToLinear(envLight.SpecularColor);
            var specularIndependence = MathUtils.Saturate(envLight.SpecularIndependence);
            LightingData.SpecularDirection_Power = new Vector4(envLight.SpecularDirection, envLight.SpecularPower);
            LightingData.SpecularAlbedoColor = new Vector4(specularColor * (1f - specularIndependence), 0f);
            LightingData.SpecularLitColor = new Vector4(specularColor * specularIndependence * brightness, 0f);

            LastStaticSunCascade = GetLastStaticSunCascade(envLight);
            IsSunShadowBaked = envLight.HasMixedShadows;

            HasOwnSun = true;
            isSunBorrowed = false;
        }

        private int GetLastStaticSunCascade(SceneLight envLight)
        {
            if (!envLight.HasMixedShadows)
            {
                return int.MaxValue;
            }

            // CS2 ignores the light's key and renders static objects into every cascade at its high shadow quality settings
            if (scene.RendererContext.FileLoader.GameInfo.Game == Game.Csgo)
            {
                return int.MaxValue;
            }

            return envLight.CascadeRenderStaticObjects;
        }

        /// <summary>
        /// Gets the last sun shadow cascade that static objects cast into, -1 for none. With mixed shadows the
        /// lightmap holds their sun shadows, so only the nearest cascades redraw them.
        /// </summary>
        public int LastStaticSunCascade { get; private set; } = int.MaxValue;

        /// <summary>Gets whether the map's own sun is a mixed light, so the lightmap holds the sun shadows of static geometry.</summary>
        public bool IsSunShadowBaked { get; private set; }

        /// <summary>Gets whether the source map has an environment light.</summary>
        public bool HasOwnSun { get; private set; }

        private bool isSunBorrowed;

        /// <summary>Uses the sun from <paramref name="donor"/> when this map has none, or clears it when <paramref name="donor"/> is <see langword="null"/>.</summary>
        public void BorrowSun(WorldLightingInfo? donor)
        {
            if (HasOwnSun)
            {
                return;
            }

            if (donor == null)
            {
                if (isSunBorrowed)
                {
                    LightingData.SunDirection = Vector4.Zero;
                    LightingData.SunColor = Vector4.Zero;
                    LightingData.AmbientLightingSHR = Vector4.Zero;
                    LightingData.AmbientLightingSHG = Vector4.Zero;
                    LightingData.AmbientLightingSHB = Vector4.Zero;
                    LightingData.AmbientDirection_ShadowAmount = Vector4.Zero;
                    LightingData.AmbientDirectionColor = Vector4.Zero;
                    LightingData.SpecularDirection_Power = Vector4.Zero;
                    LightingData.SpecularAlbedoColor = Vector4.Zero;
                    LightingData.SpecularLitColor = Vector4.Zero;
                    LastStaticSunCascade = int.MaxValue;
                    isSunBorrowed = false;
                }

                return;
            }

            LightingData.SunDirection = donor.LightingData.SunDirection;
            LightingData.SunColor = donor.LightingData.SunColor;
            LightingData.AmbientLightingSHR = donor.LightingData.AmbientLightingSHR;
            LightingData.AmbientLightingSHG = donor.LightingData.AmbientLightingSHG;
            LightingData.AmbientLightingSHB = donor.LightingData.AmbientLightingSHB;
            LightingData.AmbientDirection_ShadowAmount = donor.LightingData.AmbientDirection_ShadowAmount;
            LightingData.AmbientDirectionColor = donor.LightingData.AmbientDirectionColor;
            LightingData.SpecularDirection_Power = donor.LightingData.SpecularDirection_Power;
            LightingData.SpecularAlbedoColor = donor.LightingData.SpecularAlbedoColor;
            LightingData.SpecularLitColor = donor.LightingData.SpecularLitColor;

            // The borrowed sun is the same light, so the same cascades take static objects
            LastStaticSunCascade = donor.LastStaticSunCascade;

            // These lightmaps have no sun baked in, so nothing is in baked shadow
            LightingData.SunLightBakedShadowMask = new Vector4(-1f, 0f, 0f, 0f);

            isSunBorrowed = true;
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
            BarnLights.Clear();

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
            ClearDynamicLightShadows();
        }

        private readonly List<SceneLight> ShadowedDynamicLights = [];
        private readonly Dictionary<SceneLight, uint> ShadowedDynamicLightIndices = [];
        private readonly Dictionary<SceneLight, uint> DynamicLightIndices = [];
        private readonly List<SceneLight> MixedShadowedLights = [];
        private readonly List<SceneLight> FastPathShadowedLights = [];
        private readonly List<(int Priority, float Importance, uint Index)> FastPathFxLights = [];

        private const int FastPathMaxFxLights = 8;

        /// <summary>
        /// Picks which dynamic lights the VR light binner shades from a viewpoint: the most important shadowed spot
        /// or ortho light in full, up to eight diffuse-only point lights, and none of the rest.
        /// </summary>
        private void SelectFastPathLights(Vector3 viewPosition)
        {
            FastPathShadowedLights.Clear();
            FastPathFxLights.Clear();

            SceneLight? singleLight = null;
            var singleImportance = float.MinValue;

            foreach (var (light, index) in DynamicLightIndices)
            {
                LightingData.LightShadowParams[index].W = -1f;

                var distanceFade = light.GetDistanceFade(viewPosition);

                if (distanceFade <= 0f)
                {
                    continue;
                }

                var importance = light.GetFastPathImportance(viewPosition, distanceFade);

                if (light.IsFastPathFxLight)
                {
                    FastPathFxLights.Add((light.Priority, importance, index));
                }
                else if (light.IsFastPathSingleLightCandidate)
                {
                    KeepMoreImportant(light, importance, ref singleLight, ref singleImportance);
                }
            }

            if (singleLight != null)
            {
                LightingData.LightShadowParams[DynamicLightIndices[singleLight]].W = 0f;

                if (ShadowedDynamicLightIndices.ContainsKey(singleLight))
                {
                    FastPathShadowedLights.Add(singleLight);
                }
            }

            // One mixed local light gets a shadow map per view, the nearest
            SceneLight? mixedLight = null;
            var mixedImportance = float.MinValue;

            foreach (var light in MixedShadowedLights)
            {
                var distanceFade = light.GetDistanceFade(viewPosition);

                if (distanceFade <= 0f)
                {
                    continue;
                }

                var importance = light.GetFastPathImportance(viewPosition, distanceFade);

                KeepMoreImportant(light, importance, ref mixedLight, ref mixedImportance);
            }

            if (mixedLight != null)
            {
                FastPathShadowedLights.Add(mixedLight);
            }

            FastPathFxLights.Sort(static (a, b) =>
            {
                var byPriority = b.Priority.CompareTo(a.Priority);
                return byPriority != 0 ? byPriority : b.Importance.CompareTo(a.Importance);
            });

            var fxCount = Math.Min(FastPathFxLights.Count, FastPathMaxFxLights);

            for (var i = 0; i < fxCount; i++)
            {
                LightingData.LightShadowParams[FastPathFxLights[i].Index].W = 1f;
            }
        }

        // Highest priority wins, then the highest importance, and the earlier light keeps a tie
        private static void KeepMoreImportant(SceneLight light, float importance, ref SceneLight? best, ref float bestImportance)
        {
            if (best != null)
            {
                var isMoreImportant = light.Priority != best.Priority
                    ? light.Priority > best.Priority
                    : importance > bestImportance;

                if (!isMoreImportant)
                {
                    return;
                }
            }

            best = light;
            bestImportance = importance;
        }

        private void ClearDynamicLightShadows()
        {
            foreach (var index in ShadowedDynamicLightIndices.Values)
            {
                LightingData.LightShadowParams[index].X = -1f;
            }
        }

        /// <summary>
        /// Packs the shadow faces of the shadowed per-pixel spot lights of the V1 light store into the barn light
        /// shadow atlas, and points their lights at them.
        /// </summary>
        private void BinDynamicLightShadows(Camera camera, int atlasSize)
        {
            ClearDynamicLightShadows();

            var shadowedLights = ShadowedDynamicLights;

            if (scene.RendererContext.FileLoader.GameInfo.VrLightBinnerSingleLightFastPath)
            {
                SelectFastPathLights(camera.Location);
                shadowedLights = FastPathShadowedLights;
            }

            ShadowMapper.Bin(shadowedLights, camera, atlasSize, BarnLightCookiePaths, visibilitySequence: 0);

            var slot = 0;

            foreach (ref readonly var binned in ShadowMapper.BinnedLights)
            {
                if (!binned.HasShadows || slot >= LightingConstants.MAX_SHADOWED_DYNAMIC_LIGHTS)
                {
                    continue;
                }

                var placement = ShadowMapper.GetFacePlacement(binned.FirstFaceIndex);

                if (!placement.Region.IsValid)
                {
                    continue;
                }

                var light = binned.Light;
                LightingData.LightWorldToShadow[slot] = light.BarnFaces[0].WorldToFrustum;
                LightingData.LightShadowOffsetScale[slot] = placement.OffsetScale;
                LightingData.LightShadowParams[ShadowedDynamicLightIndices[light]].X = slot;
                slot++;
            }
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

            if (usesUniformLightStore)
            {
                BinDynamicLightShadows(camera, atlasSize);
                return;
            }

            var binner = scene.ShadingLightBinner;
            binner?.PollBarnLightVisibility();

            ShadowMapper.Bin(BarnLights, camera, atlasSize, BarnLightCookiePaths,
                binner?.VisibilitySequence ?? 0);

            var tanHalfHorizontalFov = MathF.Tan(camera.GetFOV() * 0.5f) * camera.AspectRatio;
            var binnedLights = ShadowMapper.BinnedLights;

            if (BarnLightSortKeys.Length < binnedLights.Length)
            {
                BarnLightSortKeys = new BarnLightSortKey[binnedLights.Length];
            }

            var keyCount = 0;

            for (var binnedIndex = 0; binnedIndex < binnedLights.Length; binnedIndex++)
            {
                ref readonly var binned = ref binnedLights[binnedIndex];
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

                // Lights and their shadows fade out as they get small on screen
                var screenSize = light.ComputeScreenSize(camera.Location, tanHalfHorizontalFov);
                var (lightFade, shadowFade) = light.ComputeScreenSizeFades(screenSize);

                if (lightFade <= 0f)
                {
                    continue;
                }

                var faceColor = light.BarnFaces[0].GpuData.BarnLightColor_flCookie.AsVector3();

                BarnLightSortKeys[keyCount++] = new BarnLightSortKey
                {
                    BinnedIndex = binnedIndex,
                    ScreenSize = screenSize,
                    DistanceSquared = Vector3.DistanceSquared(camera.Location, light.BoundsCenter),
                    Luminance = MathUtils.Luminance(faceColor),
                    Facing = Vector3.Dot(camera.Forward, light.Direction),
                    LightFade = lightFade,
                    ShadowFade = shadowFade,
                };
            }

            var sortedKeys = BarnLightSortKeys.AsSpan(0, keyCount);
            sortedKeys.Sort();

            // Past the cap the least important lights are dropped silently, counted per light while the
            // buffers below hold one entry per face
            var keptKeys = sortedKeys[..Math.Min(keyCount, BarnLightConstants.MAX_BARN_LIGHTS)];

            foreach (var key in keptKeys)
            {
                ref readonly var binned = ref binnedLights[key.BinnedIndex];
                var light = binned.Light;

                var lightFade = key.LightFade;
                var shadowFade = key.ShadowFade;
                var colorScale = lightFade * light.StyleBrightness;

                var anyFaceDropped = false;
                var hasRangeCutoff = light.UsesOmni2Faces && light.FallOff > 0f;

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
                        data.BarnLightShadowScale = shadowFade;
                    }

                    data.BarnLightColor_flCookie *= new Vector4(colorScale, colorScale, colorScale, 1f);

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

        private const string LightCookieTablePath = "materials/effects/lightcookies.txt";
        private const string LightCookieTexturePath = "materials/effects/lightcookies.vtex";

        /// <summary>
        /// Reads the table that maps the cookie names of spot lights to slices of the light cookie volume, and loads
        /// that texture. Games without the table have no light cookies.
        /// </summary>
        private void LoadLightCookies()
        {
            if (LightCookiesLoaded)
            {
                return;
            }

            LightCookiesLoaded = true;

            var fileLoader = scene.RendererContext.FileLoader;
            var (pathOnDisk, _, packageEntry) = fileLoader.FindFile(LightCookieTablePath, logNotFound: false);

            if (pathOnDisk == null && packageEntry == null)
            {
                return;
            }

            using var stream = fileLoader.GetFileStream(LightCookieTablePath);

            if (stream == null)
            {
                return;
            }

            KVObject table;

            try
            {
                table = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
            }
            catch (KeyValueException)
            {
                return;
            }

            var cookieVolume = scene.RendererContext.MaterialLoader.GetTexture(LightCookieTexturePath, srgbRead: true);

            if (cookieVolume.Target != TextureTarget.Texture3D)
            {
                scene.RendererContext.Logger.LogWarning("{Texture} is not a volume texture, light cookies are disabled", LightCookieTexturePath);
                return;
            }

            foreach (var (name, value) in table)
            {
                if (value.ValueType == KVValueType.Collection
                    || !int.TryParse((string)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slice))
                {
                    continue;
                }

                LightCookieSlices[name] = Math.Clamp(slice, 0, cookieVolume.Depth - 1);
            }

            LightCookieVolume = cookieVolume;
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
                using var _ = GraphicsContext.RenderState.Scope();
                BarnLightCookieAtlas = BuildCookieAtlas(cookieTextures);
            }
        }

        /// <summary>
        /// Single white layer, used to keep the cookie texture units complete in scenes without cookies.
        /// Matches layer 0 of a real atlas, which barn lights without a cookie index into.
        /// </summary>
        private static RenderTexture CreateDefaultCookieAtlas()
        {
            var atlas = RenderTexture.Create3D(TextureTarget.Texture2DArray, 1, 1, 1, ImageFormat.RGBA8888, 1, "EmptyCookieAtlas", srgb: true);
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

            var atlas = RenderTexture.Create3D(TextureTarget.Texture2DArray, atlasSize, atlasSize, numLayers, ImageFormat.RGBA8888, 1, "CookieAtlas", srgb: true);

            var readFbo = GraphicsDevice.CreateFramebuffer("CookieAtlasBlitSrc");
            var drawFbo = GraphicsDevice.CreateFramebuffer("CookieAtlasBlitDst");

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
            var clampBorder = new Sampler(nameof(CookieSamplerClampBorder));
            clampBorder.SetWrapMode(RsTextureAddressMode.Border, RsTextureAddressMode.Border);
            clampBorder.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            CookieSamplerClampBorder = clampBorder.Handle;

            var wrap = new Sampler(nameof(CookieSamplerWrap));
            wrap.SetWrapMode(RsTextureAddressMode.Wrap, RsTextureAddressMode.Wrap);
            wrap.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            CookieSamplerWrap = wrap.Handle;
        }

        /// <summary>Allocates the GPU storage buffer used to pass barn light data to shaders.</summary>
        public void CreateBarnLightBuffer()
        {
            BarnLightStorageBuffer ??= StorageBuffer.Allocate<BarnLightConstants>(ReservedBufferSlots.BarnLights, nameof(ReservedBufferSlots.BarnLights), BarnLightConstants.MAX_BARN_LIGHT_FACES, BufferUsage.Dynamic);
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

            // The material loader owns the light cookie volume
            LightCookieVolume = null;
            LightCookieSlices.Clear();
            LightCookiesLoaded = false;

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
