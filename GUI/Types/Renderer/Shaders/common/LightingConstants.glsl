
#define MAX_ENVMAPS 144

layout(std140) uniform LightingConstants {
    vec4 g_vLightmapUvScale;
    vec4 g_vClearColor;
    mat4 vLightPosition;
    vec4 vLightColor;
    vec4 g_vEnvMapSizeConstants;
    mat4 g_matEnvMapWorldToLocal[MAX_ENVMAPS];
    vec4[MAX_ENVMAPS] g_vEnvMapBoxMins;
    vec4[MAX_ENVMAPS] g_vEnvMapBoxMaxs;
    vec4[MAX_ENVMAPS] g_vEnvMapEdgeFadeDists;
    vec4[MAX_ENVMAPS] g_vEnvMapProxySphere;
    vec4[MAX_ENVMAPS] g_vEnvMapColorRotated;
    vec4[MAX_ENVMAPS] g_vEnvMapNormalizationSH;
} ;
