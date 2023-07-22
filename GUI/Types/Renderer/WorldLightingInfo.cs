using System.Collections.Generic;
using GUI.Types.Renderer.UniformBuffers;

namespace GUI.Types.Renderer;

class WorldLightingInfo
{
    public Dictionary<string, RenderTexture> Lightmaps { get; } = new();
    public Dictionary<int, SceneEnvMap> EnvMaps { get; } = new();
    public Dictionary<int, SceneLightProbe> LightProbes { get; } = new();
    public bool HasValidLightmaps { get; set; }
    public int LightmapVersionNumber { get; set; }
    public int LightmapGameVersionNumber { get; set; }
    public LightingConstants LightingData { get; set; } = new();
}
