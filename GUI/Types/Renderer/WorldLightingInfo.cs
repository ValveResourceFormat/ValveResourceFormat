using System;
using System.Collections.Generic;
using System.Numerics;

namespace GUI.Types.Renderer;

class WorldLightingInfo
{
    public Dictionary<string, RenderTexture> Lightmaps { get; } = new();
    public Dictionary<int, SceneEnvMap> EnvMaps { get; } = new();
    public Dictionary<int, SceneLightProbe> LightProbes { get; } = new();
    public bool HasValidLightmaps { get; set; }
    public int LightmapVersionNumber { get; set; }
    public int LightmapGameVersionNumber { get; set; }
    public Vector2 LightmapUvScale { get; set; } = Vector2.One;

    public float[] EnvMapPositionsUniform { get; set; }
    public float[] EnvMapMinsUniform { get; set; }
    public float[] EnvMapMaxsUniform { get; set; }
}
