#version 460

#include "common/ViewConstants.slang"
#include "common/utils.slang"
#include "common/light_cull.slang"

const float kBusyTile = 12.0;

uniform float g_flOverlayAlpha;

uniform uint g_nOverlayTileBase;
uniform uint g_nOverlayWords;

layout(location = 0) out vec4 outputColor;

void main()
{
    if (g_nOverlayWords == 0u)
    {
        discard;
    }

    uvec2 tile = uvec2(gl_FragCoord.xy) >> uvec2(g_nLightTileShift);
    tile = min(tile, uvec2(g_nLightTileCols, g_nLightTileRows) - 1u);

    uint tileBase = g_nOverlayTileBase + (((tile.y * g_nLightTileCols) + tile.x) * g_nOverlayWords);

    uint count = 0u;

    for (uint word = 0u; word < g_nOverlayWords; word++)
    {
        count += uint(bitCount(g_CullBits[tileBase + word]));
    }

    if (count == 0u)
    {
        discard;
    }

    float t = saturate(float(count) / kBusyTile) * 3.0;

    vec3 color = mix(vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 0.0), saturate(t));
    color = mix(color, vec3(1.0, 1.0, 0.0), saturate(t - 1.0));
    color = mix(color, vec3(1.0, 0.0, 0.0), saturate(t - 2.0));

    outputColor = vec4(color, g_flOverlayAlpha);
}
