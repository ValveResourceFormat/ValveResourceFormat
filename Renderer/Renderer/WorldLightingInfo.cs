using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Storage format for environment map cubemap textures.
    /// </summary>
    public enum CubemapType : byte
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        None,
        IndividualCubemaps,
        CubemapArray,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }

    /// <summary>
    /// Storage format for light probe irradiance data.
    /// </summary>
    public enum LightProbeType : byte
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        None,
        IndividualProbes,
        ProbeAtlas,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }

    public struct BinnedShadowCaster
    {
        public SceneLight Light { get; set; }
        public Matrix4x4 WorldToFrustum { get; set; }
        public ShadowAtlasRegion Region { get; set; }
        public int FaceIndex { get; set; }
    }

    /// <summary>
    /// Scene lighting data including lightmaps, reflection probes, and shadow maps.
    /// </summary>
    public class WorldLightingInfo(Scene scene)
    {
        public Dictionary<string, RenderTexture> Lightmaps { get; } = [];
        public List<SceneLightProbe> LightProbes { get; } = [];
        public List<SceneEnvMap> EnvMaps { get; } = [];
        public Dictionary<int, SceneEnvMap> EnvMapHandshakes { get; } = [];
        public Dictionary<int, SceneLightProbe> ProbeHandshakes { get; } = [];
        public bool HasValidLightmaps { get; set; }
        public bool HasValidLightProbes { get; set; }
        public int LightmapVersionNumber { get; set; }
        public int LightmapGameVersionNumber { get; set; }
        public LightingConstants LightingData { get; set; } = new();

        public CubemapType CubemapType
        {
            get => (CubemapType)scene.RenderAttributes.GetValueOrDefault("S_SCENE_CUBEMAP_TYPE");
            set => scene.RenderAttributes["S_SCENE_CUBEMAP_TYPE"] = (byte)value;
        }

        public LightProbeType LightProbeType
        {
            get => (LightProbeType)scene.RenderAttributes.GetValueOrDefault("S_SCENE_PROBE_TYPE");
            set => scene.RenderAttributes["S_SCENE_PROBE_TYPE"] = (byte)value;
        }

        public bool HasBakedShadowsFromLightmap => scene.RenderAttributes.GetValueOrDefault("S_LIGHTMAP_VERSION_MINOR") > 0;
        public bool EnableDynamicShadows { get; set; } = true;

        public Matrix4x4 SunViewProjection { get; internal set; }
        public Frustum SunLightFrustum { get; } = new();
        public float SunLightShadowBias { get; set; } = 0.001f;
        public float SunLightShadowCoverageScale { get; set; } = 1f;
        public bool UseSceneBoundsForSunLightFrustum { get; set; }

        // Barn lights

        // This should very much be removed / changed (to not be constant)
        internal const int BarnLightShadowAtlasSize = 8192;
        private static readonly (float MaxDistance, int MaxResolution)[] ShadowTiers =
        [
            (384f, 1536),
            (768f, 512),
            (2048f, 256),
        ];

        private readonly BarnLightConstants[] BinnedBarnLightGpuData = new BarnLightConstants[BarnLightConstants.MAX_BARN_LIGHTS];
        private readonly List<ShadowRequest> ShadowRequests = [];
        private readonly ShadowAtlasPacker BarnLightPacker = new();

        private readonly List<SceneLight> BarnLightEntities = [];
        private Dictionary<string, int>? BarnLightCookiePaths;
        private StorageBuffer? BarnLightStorageBuffer;
        public List<BinnedShadowCaster> BinnedShadowCasters { get; } = [];
        private RenderTexture? BarnLightCookieAtlas { get; set; }
        private int CookieSamplerClampBorder;
        private int CookieSamplerWrap;

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

        public void AddProbe(SceneLightProbe lightProbe)
        {
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

            var currentLightIndex = 0u;

            foreach (var light in lights.Where(l => l.StationaryLightIndex >= 0).OrderBy(l => l.StationaryLightIndex))
            {
                currentLightIndex = (uint)light.StationaryLightIndex;

                if (currentLightIndex >= LightingConstants.MAX_LIGHTS)
                {
                    continue;
                }

                AddLight(light, (uint)light.StationaryLightIndex);
            }

            LightingData.NumLights[0] = currentLightIndex + 1;

            foreach (var light in lights.Where(l => l.StationaryLightIndex == -1))
            {
                if (currentLightIndex >= LightingConstants.MAX_LIGHTS)
                {
                    scene.RendererContext.Logger.LogWarning("Too many lights in scene. Some lights were removed");
                    break;
                }

                AddLight(light, currentLightIndex++);
            }

            LightingData.NumLights[1] = currentLightIndex;
        }

        public void StoreLightMappedLights_V2(List<SceneLight> lights)
        {
            // This loop is required for environment (sun) lights.
            // I don't know if there can be multiple instances, but just to be safe
            var envCount = 0u;
            foreach (var light in lights)
            {
                if (light.Entity != SceneLight.EntityType.Environment)
                {
                    continue;
                }

                if (envCount >= LightingConstants.MAX_LIGHTS)
                {
                    break;
                }

                LightingData.LightPosition_Type[envCount] = new Vector4(light.Position, (int)light.Type);
                LightingData.LightDirection_InvRange[envCount] = new Vector4(light.Direction, 1.0f / light.Range);
                LightingData.LightToWorld[envCount] = light.Transform;
                LightingData.LightColor_Brightness[envCount] = new Vector4(ColorSpace.SrgbGammaToLinear(light.Color), light.Brightness);
                LightingData.LightFallOff[envCount] = new Vector4(light.FallOff, light.Range, 0.0f, 0.0f);
                envCount++;
            }

            LightingData.NumLightsBakedShadowIndex[0] = envCount;

            var filtered = lights.Where(SceneLight.IsDynamicLight).ToList();

            if (filtered.Count == 0)
            {
                LightingData.NumBarnLights = 0;
                return;
            }

            BarnLightEntities.AddRange(filtered);
            RebuildCookieAtlas();
        }

        private static int GetResolutionCap(float distance)
        {
            for (var i = 0; i < ShadowTiers.Length; i++)
            {
                if (distance <= ShadowTiers[i].MaxDistance)
                {
                    return ShadowTiers[i].MaxResolution;
                }
            }

            return ShadowTiers.Length > 0 ? ShadowTiers[^1].MaxResolution : int.MaxValue;
        }

        private static (int W, int H) ApplyDistanceCap(int w, int h, float distance)
        {
            var cap = GetResolutionCap(distance);
            var maxDim = Math.Max(w, h);
            return maxDim <= cap
                ? (w, h)
                : (w * cap / maxDim, h * cap / maxDim);
        }

        private bool barnLightsLoggedOnce;
        public void BinBarnLights(Frustum cameraFrustum, Vector3 cameraPosition)
        {
            LightingData.NumBarnLights = 0;
            BinnedShadowCasters.Clear();

            const int MaxShadowCasters = 8; // todo: prioritize close up lights

            if (BarnLightEntities is null || BarnLightEntities.Count == 0)
            {
                LightingData.NumBarnLights = 0;
                return;
            }

            ShadowRequests.Clear();

            foreach (var light in BarnLightEntities)
            {
                if (light.PrecomputedFieldsValid && !cameraFrustum.Intersects(light.PrecomputedBounds))
                {
                    continue;
                }

                if (light.IsDirty)
                {
                    light.ComputeBarnFaces(BarnLightCookiePaths!);
                    light.IsDirty = false;
                }

                if (light.BarnFaces is null)
                {
                    continue;
                }

                if (light.CastShadows > 0)
                {
                    var (w, h) = light.GetShadowFaceDimensions();
                    var distance = Vector3.Distance(cameraPosition, light.Position);
                    (w, h) = ApplyDistanceCap(w, h, distance);

                    for (var i = 0; i < light.BarnFaces.Length; i++)
                    {
                        ShadowRequests.Add(new ShadowRequest { Width = w, Height = h });
                    }
                }
            }

            Span<ShadowAtlasRegion> atlasRegions = [];
            if (ShadowRequests.Count > 0)
            {
                atlasRegions = BarnLightPacker.Pack(BarnLightShadowAtlasSize, ShadowRequests);
            }

            var requestIndex = 0;
            var numShadowCasters = 0;
            foreach (var light in BarnLightEntities)
            {
                if (light.PrecomputedFieldsValid && !cameraFrustum.Intersects(light.PrecomputedBounds))
                {
                    continue;
                }

                if (light.BarnFaces is null)
                {
                    continue;
                }

                if (LightingData.NumBarnLights >= BarnLightConstants.MAX_BARN_LIGHTS)
                {
                    break;
                }

                for (var faceIndex = 0; faceIndex < light.BarnFaces.Length; faceIndex++)
                {
                    if (LightingData.NumBarnLights >= BarnLightConstants.MAX_BARN_LIGHTS)
                    {
                        break;
                    }

                    var face = light.BarnFaces[faceIndex];
                    var data = face.GpuData;

                    if (light.CastShadows > 0 && atlasRegions.Length > 0 && numShadowCasters < MaxShadowCasters)
                    {
                        var region = atlasRegions[requestIndex++];

                        if (region.IsValid)
                        {
                            var atlasScale = new Vector2(region.Width, region.Height) / BarnLightShadowAtlasSize;
                            var atlasOffset = new Vector2(region.X, region.Y) / BarnLightShadowAtlasSize;
                            var bakedScale = atlasScale * 0.5f;
                            var bakedOffset = atlasOffset + bakedScale;

                            data.BarnLightShadowOffsetScale = new Vector4(
                                bakedOffset.X, bakedOffset.Y,
                                bakedScale.X, bakedScale.Y
                            );
                            data.BarnLightShadowScale = 1.0f;

                            BinnedShadowCasters.Add(new BinnedShadowCaster
                            {
                                WorldToFrustum = face.WorldToFrustum,
                                Region = region,
                                Light = light,
                                FaceIndex = faceIndex,
                            });
                            numShadowCasters++;
                        }
                        else
                        {
                            scene.RendererContext.Logger.LogWarning(
                                "Barn light shadow atlas is full, skipping light '{LightName}' (size {Size})",
                                light.Name, light.ShadowMapSize);
                            continue;
                        }
                    }

                    BinnedBarnLightGpuData[LightingData.NumBarnLights++] = data;
                }
            }

            if (LightingData.NumBarnLights == BarnLightConstants.MAX_BARN_LIGHTS && !barnLightsLoggedOnce)
            {
                scene.RendererContext.Logger.LogWarning("Max barn light count ({Max}) reached, some lights will be missing", BarnLightConstants.MAX_BARN_LIGHTS);
                barnLightsLoggedOnce = true;
            }

            BarnLightStorageBuffer?.Update(BinnedBarnLightGpuData, 0, (int)LightingData.NumBarnLights * Unsafe.SizeOf<BarnLightConstants>());
        }

        private void RebuildCookieAtlas()
        {
            BarnLightCookieAtlas?.Delete();
            BarnLightCookieAtlas = null;

            BarnLightCookiePaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var cookieTextures = new List<RenderTexture>();

            foreach (var light in BarnLightEntities!)
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

        public void CreateBarnLightBuffer()
        {
            BarnLightStorageBuffer = StorageBuffer.Allocate<BarnLightConstants>(
                ReservedBufferSlots.BarnLights, BarnLightConstants.MAX_BARN_LIGHTS, BufferUsageHint.DynamicDraw);
        }

        public void BindBarnLightBuffer()
        {
            BarnLightStorageBuffer?.BindBufferBase();
        }

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
