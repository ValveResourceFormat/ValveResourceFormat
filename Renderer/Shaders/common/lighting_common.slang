#version 460

import LightingConstants;

public struct LightingTerms_t
{
    public vec3 DiffuseDirect = 0;
    public vec3 DiffuseIndirect = 0;
    public vec3 SpecularDirect = 0;
    public vec3 SpecularIndirect = 0;
    public vec3 TransmissiveDirect = 0;
    public float SpecularOcclusion = 1.0; // Lightmap AO
};


//SLANG TODO: I am not removing this yet, but we don't need this "constructor" anymore.
public LightingTerms_t InitLighting()
{
    return LightingTerms_t(vec3(0), vec3(0), vec3(0), vec3(0), vec3(0), 1.0);
}

public vec3 GetEnvLightDirection(uint nLightIndex)
{
    return normalize(mat3(g_matLightToWorld[nLightIndex]) * vec3(-1, 0, 0));
}

public vec3 GetLightPositionWs(uint nLightIndex)
{
    return g_vLightPosition_Type[nLightIndex].xyz;
}

public bool IsDirectionalLight(uint nLightIndex)
{
    return g_vLightPosition_Type[nLightIndex].a == 0.0;
}

public vec3 GetLightDirection(vec3 vPositionWs, uint nLightIndex)
{
    if (IsDirectionalLight(nLightIndex))
    {
        return GetEnvLightDirection(nLightIndex);
    }

    vec3 lightPosition = GetLightPositionWs(nLightIndex);
    vec3 lightVector = normalize(lightPosition - vPositionWs);

    return lightVector;
}

public vec3 GetLightColor(uint nLightIndex)
{
    vec3 vColor = g_vLightColor_Brightness[nLightIndex].rgb;
    float flBrightness = g_vLightColor_Brightness[nLightIndex].a;

    return vColor * flBrightness;
}

