
uniform mat4 vLightPosition;
uniform vec4 vLightColor;

vec3 getSunDir()
{
    return -normalize(mat3(vLightPosition) * vec3(-1, 0, 0));
}

vec3 getSunColor()
{
    return vLightColor.rgb; //pow(vLightColor.rgb, vec3(2.2)) * pow(vLightColor.a, 0.5);
}




#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)

#define UseLightmapDirectionality 1

uniform float g_flDirectionalLightmapStrength = 1.0;
uniform float g_flDirectionalLightmapMinZ = 0.05;
uniform vec4 g_vLightmapParams = vec4(0.0); // ???? directional non-intensity?? it's set to 0.0 in all places ive looked

// I don't actually understand most of this, but it's what the code does- in HLA at least
vec3 ComputeLightmapShading(vec3 irradianceColor, vec4 irradianceDirection, vec3 normalMap)
{

#if UseLightmapDirectionality == 1
    // irradianceDirection.w is never used
    vec3 vTangentSpaceLightVector;

    vTangentSpaceLightVector.xy = UnpackFromColor(irradianceDirection.xy);
    vTangentSpaceLightVector.z = 1.0 - length(vTangentSpaceLightVector.xy); // wtf?? this is done incorrectly. sqrt should be applied after the one minus.

    float flDirectionality = mix(irradianceDirection.z, 1.0, g_flDirectionalLightmapStrength);
    vec3 vNonDirectionalLightmap = irradianceColor * saturate(flDirectionality + g_vLightmapParams.x); // r13.xyz directional lightmap intensity

    float NoL = ClampToPositive(dot(vTangentSpaceLightVector, normalMap));

    float LightMapZ = max(vTangentSpaceLightVector.z, g_flDirectionalLightmapMinZ);

    irradianceColor = mix(vNonDirectionalLightmap, irradianceColor, NoL / LightMapZ);
#endif

    return irradianceColor;
}

#endif
