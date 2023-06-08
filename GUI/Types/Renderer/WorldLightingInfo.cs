using System.Collections.Generic;
using System.Numerics;

namespace GUI.Types.Renderer
{
    public record WorldLightingInfo(
        Dictionary<string, RenderTexture> Lightmaps,
        int LightmapGameVersionNumber,
        Vector2 LightmapUvScale
    );
}
