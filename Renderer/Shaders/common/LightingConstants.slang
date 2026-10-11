module LightingConstants;

public static const uint MAX_LIGHTS = 256;

public struct LightingConstantsData
{
    public float2 g_vLightmapUvScale;
    public bool g_bIsSkybox;
    public uint g_nNumBarnLights;
    public uint4 g_nNumLights;
    public float4 g_vSunDirection;            // xyz: surface-to-sun, zero when the scene has no sun
    public float4 g_vSunColor;                // rgb: color * brightness, a: flags
    public float4 g_vSunLightBakedShadowMask; // V2: one-hot baked shadow channel mask. V1: baked light index in x (-1 none)
    public float4 g_vLightPosition_Type[MAX_LIGHTS];
    public float4 g_vLightDirection_InvRange[MAX_LIGHTS];
    public float4x4 g_matLightToWorld[MAX_LIGHTS];
    public float4 g_vLightColor_Brightness[MAX_LIGHTS];
    public float4 g_vLightSpotInnerOuterConeCosines[MAX_LIGHTS];
    public float4 g_vLightFallOff[MAX_LIGHTS];

    public float4 g_vEnvMapSizeConstants;
};
