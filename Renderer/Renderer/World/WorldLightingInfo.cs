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

        /// <summary>Gets a value indicating whether the lightmap contains baked shadow data.</summary>
        public bool HasBakedShadowsFromLightmap => scene.RenderAttributes.GetValueOrDefault("S_LIGHTMAP_VERSION_MINOR") > 0;
        /// <summary>Gets or sets a value indicating whether dynamic shadow rendering is enabled.</summary>
        public bool EnableDynamicShadows { get; set; } = true;

        /// <summary>Gets or sets the combined view-projection matrix used for sun shadow rendering.</summary>
        public Matrix4x4 SunViewProjection { get; internal set; }
        /// <summary>Gets the frustum used for sun light shadow culling.</summary>
        public Frustum SunLightFrustum { get; } = new();
        /// <summary>Gets or sets the depth bias applied to sun light shadows to reduce self-shadowing artifacts.</summary>
        public float SunLightShadowBias { get; set; } = 0.001f;
        /// <summary>Gets or sets a scale factor applied to the sun light shadow coverage area.</summary>
        public float SunLightShadowCoverageScale { get; set; } = 1f;
        /// <summary>Gets or sets a value indicating whether the sun light frustum is fitted to the scene bounds rather than the camera.</summary>
        public bool UseSceneBoundsForSunLightFrustum { get; set; }

        // Barn lights
        /// <summary>Gets the size of the barn light shadow atlas texture, as recorded by the last <see cref="BinBarnLights"/> call.</summary>
        public int BarnLightShadowAtlasSize { get; private set; } = 4096;

        /// <summary>Gets the shadow mapper that culls and packs light shadow faces each frame.</summary>
        public ShadowMapper ShadowMapper { get; } = new();

        private readonly BarnLightConstants[] BinnedBarnLightGpuData = new BarnLightConstants[BarnLightConstants.MAX_BARN_LIGHTS];
        private bool barnLightBudgetWarned;

        private Dictionary<string, int> BarnLightCookiePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        private StorageBuffer? BarnLightStorageBuffer;
        private RenderTexture? BarnLightCookieAtlas { get; set; }
        private int CookieSamplerClampBorder;
        private int CookieSamplerWrap;

        /// <summary>
        /// Binds lightmap, light probe, and barn light cookie textures to the given shader.
        /// </summary>
        /// <param name="shader">The shader to bind lightmap textures to.</param>
        public void SetLightmapTextures(Shader shader)
        {
            var i = 0;
            foreach (var (Name, Texture) in Lightmaps)
            {
                var slot = (int)ReservedTextureSlots.Lightmap1 + i;
                Debug.Assert(slot <= (int)ReservedTextureSlots.EnvironmentMap, "Too many lightmap textures. Reserve more slots!");
                i++;

                shader.SetTexture(slot, Name, Texture);
            }

            if (LightProbeType == LightProbeType.ProbeAtlas && LightProbes.Count > 0)
            {
                shader.SetTexture((int)ReservedTextureSlots.Probe1, "g_tLPV_Irradiance", LightProbes[0].Irradiance);
                shader.SetTexture((int)ReservedTextureSlots.Probe2, "g_tLPV_Shadows", LightProbes[0].DirectLightShadows);
            }

            if (BarnLightCookieAtlas != null)
            {
                shader.SetTexture((int)ReservedTextureSlots.LightCookieTexture, "g_tLightCookieTexture", BarnLightCookieAtlas);
                GL.BindSampler((int)ReservedTextureSlots.LightCookieTexture, CookieSamplerClampBorder);

                shader.SetTexture((int)ReservedTextureSlots.LightCookieTextureWrap, "g_tLightCookieTextureWrap", BarnLightCookieAtlas);
                GL.BindSampler((int)ReservedTextureSlots.LightCookieTextureWrap, CookieSamplerWrap);
            }
        }

        /// <summary>
        /// Binds the per-draw light-probe volume textures for a draw bound to <paramref name="lightProbe"/>:
        /// the probe's irradiance plus the lightmap-version specific direct-light data. Only applies to
        /// individual-probe scenes; in probe-atlas scenes the shared atlas textures are bound per shader by
        /// <see cref="SetLightmapTextures"/> and the shader picks the probe by its index. Slots bound here
        /// are queued into <paramref name="boundSlots"/> when provided, so the caller can unbind them after
        /// the draw.
        /// </summary>
        public void SetInstanceLightProbeTextures(Shader shader, SceneLightProbe lightProbe, Queue<int>? boundSlots = null)
        {
            if (LightProbeType != LightProbeType.IndividualProbes)
            {
                return;
            }

            BindProbeTexture(shader, ReservedTextureSlots.Probe1, "g_tLPV_Irradiance", lightProbe.Irradiance, boundSlots);

            if (LightmapGameVersionNumber == 1)
            {
                BindProbeTexture(shader, ReservedTextureSlots.Probe2, "g_tLPV_Indices", lightProbe.DirectLightIndices, boundSlots);
                BindProbeTexture(shader, ReservedTextureSlots.Probe3, "g_tLPV_Scalars", lightProbe.DirectLightScalars, boundSlots);
            }
            else if (LightmapGameVersionNumber >= 2)
            {
                BindProbeTexture(shader, ReservedTextureSlots.Probe2, "g_tLPV_Shadows", lightProbe.DirectLightShadows, boundSlots);
            }
        }

        private static void BindProbeTexture(Shader shader, ReservedTextureSlots slot, string name, RenderTexture? texture, Queue<int>? boundSlots)
        {
            if (texture != null && shader.SetTexture((int)slot, name, texture))
            {
                boundSlots?.Enqueue((int)slot);
            }
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

        /// <summary>
        /// Recalculates <see cref="SunViewProjection"/> and <see cref="SunLightFrustum"/> to fit the current camera view.
        /// </summary>
        /// <param name="camera">The active camera used to position the sun shadow frustum.</param>
        /// <param name="shadowMapSize">The shadow map resolution used to compute coverage and texel snapping.</param>
        public void UpdateSunLightFrustum(Camera camera, float shadowMapSize = 512f)
        {
            var sunMatrix = LightingData.LightToWorld[0];
            var sunDir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, sunMatrix with { Translation = Vector3.Zero })); // why is sun dir calculated like so?.

            var bbox = Math.Max(shadowMapSize / 2.5f, 512f) * SunLightShadowCoverageScale;
            var farPlane = 8096f;
            var nearPlaneExtend = 1000f;
            var bias = 0.001f;

            // Move near plane away from camera, in light direction, to capture shadow casters.
            // This could be improved using scene bounds.
            var eye = camera.Location - sunDir * nearPlaneExtend;

            if (UseSceneBoundsForSunLightFrustum)
            {
                var staticBounds = scene.StaticOctree.Root.GetBounds();
                var dynamicBounds = scene.DynamicOctree.Root.GetBounds();
                var sceneBounds = staticBounds.Union(dynamicBounds);
                var max = Math.Max(sceneBounds.Size.X, Math.Max(sceneBounds.Size.Y, sceneBounds.Size.Z));

                if (max > 0 && max < shadowMapSize)
                {
                    nearPlaneExtend = max / 2f;
                    eye = staticBounds.Center - sunDir * nearPlaneExtend;
                    bbox = max * 1.6f;
                    farPlane = bbox;
                    bias = 0.01f;
                }
            }

            // Stabilize shadow map by snapping eye position to texel-sized increments in world space
            var texelWorldSize = (4.0f * bbox) / shadowMapSize;
            var right = Vector3.Normalize(Vector3.Cross(sunDir, Vector3.UnitZ));
            var up = Vector3.Cross(right, sunDir);

            // Project eye onto shadow camera's right/up axes and snap
            var eyeOffsetX = Vector3.Dot(eye, right);
            var eyeOffsetY = Vector3.Dot(eye, up);
            var eyeOffsetZ = Vector3.Dot(eye, sunDir);

            eyeOffsetX = MathF.Round(eyeOffsetX / texelWorldSize) * texelWorldSize;
            eyeOffsetY = MathF.Round(eyeOffsetY / texelWorldSize) * texelWorldSize;

            eye = right * eyeOffsetX + up * eyeOffsetY + sunDir * eyeOffsetZ;

            var sunCameraView = Matrix4x4.CreateLookAt(eye, eye + sunDir, Vector3.UnitZ);
            var sunCameraProjection = Matrix4x4.CreateOrthographicOffCenter(-bbox, bbox, -bbox, bbox, farPlane, -nearPlaneExtend);

            SunViewProjection = sunCameraView * sunCameraProjection;
            SunLightShadowBias = bias;
            SunLightFrustum.Update(SunViewProjection);
        }

        /// <summary>
        /// Stores stationary and dynamic light data into <see cref="LightingData"/> using the V1 lightmap format.
        /// </summary>
        /// <param name="lights">The list of scene lights to store.</param>
        public void StoreLightMappedLights_V1(List<SceneLight> lights)
        {
            void AddLight(SceneLight light, uint index)
            {
                LightingData.LightPosition_Type[index] = new Vector4(light.Position, (int)light.Type);
                LightingData.LightDirection_InvRange[index] = new Vector4(light.Direction, 1.0f / light.Range);

                //Matrix4x4.Invert(light.Transform, out var lightToWorld);
                LightingData.LightToWorld[index] = light.Transform;

                LightingData.LightColor_Brightness[index] = new Vector4(ColorSpace.SrgbGammaToLinear(light.Color), light.Brightness);
                LightingData.LightSpotInnerOuterCosines[index] = new Vector4(MathF.Cos(light.SpotInnerAngle), MathF.Cos(light.SpotOuterAngle), 0.0f, 0.0f);
                LightingData.LightFallOff[index] = new Vector4(light.FallOff, light.Range, light.AttenuationLinear, light.AttenuationQuadratic);
            }

            var staticLights = lights.Where(l => l.StationaryLightIndex >= 0).OrderBy(l => l.StationaryLightIndex).ToList();
            var dynamicLights = lights.Where(l => l.StationaryLightIndex == -1).ToList();

            foreach (var light in staticLights)
            {
                var index = (uint)light.StationaryLightIndex;

                if (index >= LightingConstants.MAX_LIGHTS)
                {
                    continue;
                }

                AddLight(light, index);

                LightingData.StaticLightCount = index + 1;
            }

            var currentLightIndex = LightingData.StaticLightCount;

            foreach (var light in dynamicLights)
            {
                if (currentLightIndex >= LightingConstants.MAX_LIGHTS)
                {
                    scene.RendererContext.Logger.LogWarning("Too many lights in scene. Some lights were removed");
                    break;
                }

                AddLight(light, currentLightIndex++);
            }

            LightingData.DynamicLightCount = currentLightIndex;

            var envLight = lights.FirstOrDefault(l => l.Entity == SceneLight.EntityType.Environment);
            if (envLight != null)
            {
                LightingData.LightToWorld[0] = envLight.Transform;
                LightingData.LightPosition_Type[0] = new Vector4(envLight.Position, (int)envLight.Type);
                LightingData.LightColor_Brightness[0] = new Vector4(ColorSpace.SrgbGammaToLinear(envLight.Color), envLight.Brightness);
            }
        }

        /// <summary>
        /// Stores environment light data and queues real-time barn lights using the V2 lightmap format.
        /// </summary>
        /// <param name="lights">The list of scene lights to store.</param>
        public void StoreLightMappedLights_V2(List<SceneLight> lights)
        {
            var envLight = lights.FirstOrDefault(l => l.Entity == SceneLight.EntityType.Environment);

            if (envLight != null)
            {
                LightingData.LightPosition_Type[0] = new Vector4(envLight.Position, (int)envLight.Type);
                LightingData.LightDirection_InvRange[0] = new Vector4(envLight.Direction, 1.0f / envLight.Range);
                LightingData.LightToWorld[0] = envLight.Transform;
                LightingData.LightColor_Brightness[0] = new Vector4(ColorSpace.SrgbGammaToLinear(envLight.Color), envLight.Brightness);
                LightingData.LightFallOff[0] = new Vector4(envLight.FallOff, envLight.Range, 0.0f, 0.0f);
                LightingData.SunLightBakedShadowMask = envLight.BakedShadowMask;

                LightingData.StaticLightCount = 1;
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

        /// <summary>
        /// Culls and bins visible barn lights for the current frame, packing their shadow faces into the atlas.
        /// </summary>
        /// <param name="camera">The camera used for culling and shadow resolution selection.</param>
        /// <param name="atlasSize">Pixel size of the shadow atlas texture.</param>
        public void BinBarnLights(Camera camera, int atlasSize)
        {
            BarnLightShadowAtlasSize = atlasSize;
            LightingData.NumBarnLights = 0;

            ShadowMapper.Bin(BarnLights, camera, atlasSize, BarnLightCookiePaths);

            foreach (ref readonly var binned in ShadowMapper.BinnedLights)
            {
                var light = binned.Light;

                if (LightingData.NumBarnLights + light.BarnFaces.Length > BarnLightConstants.MAX_BARN_LIGHTS)
                {
                    if (!barnLightBudgetWarned)
                    {
                        barnLightBudgetWarned = true;
                        scene.RendererContext.Logger.LogWarning(
                            "Max barn light count ({Max}) reached, some lights will be dropped", BarnLightConstants.MAX_BARN_LIGHTS);
                    }

                    continue;
                }

                for (var faceIndex = 0; faceIndex < light.BarnFaces.Length; faceIndex++)
                {
                    var data = light.BarnFaces[faceIndex].GpuData;

                    if (binned.HasShadows)
                    {
                        var placement = ShadowMapper.GetFacePlacement(binned.FirstFaceIndex + faceIndex);

                        if (placement.Region.IsValid)
                        {
                            data.BarnLightShadowOffsetScale = placement.OffsetScale;
                            data.BarnLightShadowScale = 1.0f;
                        }
                    }

                    BinnedBarnLightGpuData[LightingData.NumBarnLights++] = data;
                }
            }

            BarnLightStorageBuffer?.Update(BinnedBarnLightGpuData, 0, (int)LightingData.NumBarnLights * Unsafe.SizeOf<BarnLightConstants>());
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
                BarnLightCookieAtlas = BuildCookieAtlas(cookieTextures);
                CreateCookieSamplers();
            }
        }

        private static RenderTexture BuildCookieAtlas(List<RenderTexture> textures)
        {
            var atlasSize = 512;
            foreach (var tex in textures)
            {
                atlasSize = Math.Max(atlasSize, Math.Max(tex.Width, tex.Height));
            }

            var numLayers = textures.Count + 1;

            var atlas = new RenderTexture(TextureTarget.Texture2DArray, atlasSize, atlasSize, numLayers, 1);
            GL.TextureStorage3D(atlas.Handle, 1, SizedInternalFormat.Srgb8Alpha8, atlasSize, atlasSize, numLayers);

            GL.CreateFramebuffers(1, out int readFbo);
            GL.CreateFramebuffers(1, out int drawFbo);

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
            GL.SamplerParameter(CookieSamplerClampBorder, SamplerParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.SamplerParameter(CookieSamplerClampBorder, SamplerParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            GL.SamplerParameter(CookieSamplerClampBorder, SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);

            GL.CreateSamplers(1, out CookieSamplerWrap);
            GL.SamplerParameter(CookieSamplerWrap, SamplerParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.SamplerParameter(CookieSamplerWrap, SamplerParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.SamplerParameter(CookieSamplerWrap, SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        }

        /// <summary>Allocates the GPU storage buffer used to pass barn light data to shaders.</summary>
        public void CreateBarnLightBuffer()
        {
            BarnLightStorageBuffer ??= StorageBuffer.Allocate<BarnLightConstants>(
                ReservedBufferSlots.BarnLights, BarnLightConstants.MAX_BARN_LIGHTS, BufferUsageHint.DynamicDraw);
        }

        /// <summary>Binds the barn light storage buffer to its reserved shader slot.</summary>
        public void BindBarnLightBuffer()
        {
            BarnLightStorageBuffer?.BindBufferBase();
        }

        /// <summary>Releases the barn light GPU buffer, cookie atlas texture, and sampler objects.</summary>
        public void DisposeBarnLights()
        {
            BarnLightStorageBuffer?.Delete();

            BarnLightCookieAtlas?.Delete();
            BarnLightCookieAtlas = null;

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
