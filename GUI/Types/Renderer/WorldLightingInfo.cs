using System.Collections.Generic;
using System.Diagnostics;
using GUI.Types.Renderer.UniformBuffers;

namespace GUI.Types.Renderer;

class WorldLightingInfo
{
    public Dictionary<string, RenderTexture> Lightmaps { get; } = [];
    public Dictionary<int, SceneEnvMap> EnvMaps { get; } = [];
    public Dictionary<int, SceneLightProbe> LightProbes { get; } = [];
    public bool HasValidLightmaps { get; set; }
    public int LightmapVersionNumber { get; set; }
    public int LightmapGameVersionNumber { get; set; }
    public LightingConstants LightingData { get; set; } = new();

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
    }
}
