using System.Diagnostics;
using System.Linq;
using GUI.Types.Renderer.UniformBuffers;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer;

partial class Scene
{
    public enum CubemapType : byte
    {
        None,
        IndividualCubemaps,
        CubemapArray,
    }

    public enum LightProbeType : byte
    {
        None,
        IndividualProbes,
        ProbeAtlas,
    }

    public class WorldLightingInfo(Scene scene)
    {
        public Dictionary<string, RenderTexture> Lightmaps { get; } = [];
        public List<SceneLightProbe> LightProbes { get; set; } = [];
        public List<SceneEnvMap> EnvMaps { get; set; } = [];
        public Dictionary<int, SceneEnvMap> EnvMapHandshakes { get; } = [];
        public Dictionary<int, SceneLightProbe> ProbeHandshakes { get; } = [];
        public bool HasValidLightmaps { get; set; }
        public bool HasValidLightProbes { get; set; }
        public int LightmapVersionNumber { get; set; }
        public int LightmapGameVersionNumber { get; set; }
        public LightingConstants LightingData { get; set; } = new();

        public CubemapType CubemapType
        {
            get => (CubemapType)scene.RenderAttributes.GetValueOrDefault("SCENE_CUBEMAP_TYPE");
            set => scene.RenderAttributes["SCENE_CUBEMAP_TYPE"] = (byte)value;
        }

        public LightProbeType LightProbeType
        {
            get => (LightProbeType)scene.RenderAttributes.GetValueOrDefault("SCENE_PROBE_TYPE");
            set => scene.RenderAttributes["SCENE_PROBE_TYPE"] = (byte)value;
        }

        public bool HasBakedShadowsFromLightmap => scene.RenderAttributes.GetValueOrDefault("LightmapGameVersionNumber") > 0;
        public bool EnableDynamicShadows { get; set; } = true;

        public Matrix4x4 SunViewProjection { get; internal set; }
        public Frustum SunLightFrustum = new();
        public float SunLightShadowBias { get; set; } = 0.001f;
        public bool UseSceneBoundsForSunLightFrustum { get; set; }

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
                    Log.Error(nameof(WorldLightingInfo), $"Envmap texture target mismatch {envmap.EnvMapTexture.Target} != {first.EnvMapTexture.Target}");
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
                (2 or 3, { DirectLightShadows: null }) => false,
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

            var bbox = Math.Max(shadowMapSize / 2.5f, 512f);
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

                LightingData.LightColor_Brightness[index] = new Vector4(RenderMaterial.SrgbGammaToLinear(light.Color), light.Brightness);
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
                    Log.Warn("Lightbinner", "Too many lights in scene. Some lights were removed.");
                    break;
                }

                AddLight(light, currentLightIndex++);
            }

            LightingData.NumLights[1] = currentLightIndex;
        }

        public void StoreLightMappedLights_V2(List<SceneLight> lights)
        {
            var currentShadowIndex = 0;
            var totalCount = 0u;

            foreach (var light in lights.OrderBy(l => l.StationaryLightIndex))
            {
                if (totalCount >= LightingConstants.MAX_LIGHTS)
                {
                    Log.Warn("Lightbinner", "Too many lights in scene. Some lights were removed.");
                    break;
                }

                if (light.StationaryLightIndex < 0 || light.StationaryLightIndex > 3)
                {
                    continue;
                }

                if (currentShadowIndex != light.StationaryLightIndex)
                {
                    LightingData.NumLightsBakedShadowIndex[currentShadowIndex] = totalCount;
                    currentShadowIndex = light.StationaryLightIndex;
                }

                LightingData.LightPosition_Type[totalCount] = new Vector4(light.Position, (int)light.Type);
                LightingData.LightDirection_InvRange[totalCount] = new Vector4(light.Direction, 1.0f / light.Range);

                //Matrix4x4.Invert(light.Transform, out var lightToWorld);
                LightingData.LightToWorld[totalCount] = light.Transform;

                LightingData.LightColor_Brightness[totalCount] = new Vector4(RenderMaterial.SrgbGammaToLinear(light.Color), light.Brightness);

                LightingData.LightFallOff[totalCount] = new Vector4(light.FallOff, light.Range, 0.0f, 0.0f);

                totalCount++;
            }

            LightingData.NumLightsBakedShadowIndex[currentShadowIndex] = totalCount;
        }
    }
}
