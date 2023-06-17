using System.Collections.Generic;
using System.Numerics;

namespace GUI.Types.Renderer;

class WorldLightingInfo
{
    public Dictionary<string, RenderTexture> Lightmaps { get; } = new();
    public Dictionary<int, SceneEnvMap> EnvMaps { get; } = new();
    public Dictionary<int, SceneLightProbe> LightProbes { get; } = new();
    public int LightmapGameVersionNumber { get; init; }
    public Vector2 LightmapUvScale { get; init; }

    public RenderTexture BRDFLookup { get; set; }

    public float[] EnvMapPositionsUniform { get; set; }
    public float[] EnvMapMinsUniform { get; set; }
    public float[] EnvMapMaxsUniform { get; set; }
}
