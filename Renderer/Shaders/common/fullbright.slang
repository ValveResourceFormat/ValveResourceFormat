#version 460

import utils;

public vec3 CalculateFullbrightLighting(vec3 albedo, vec3 normal, vec3 viewVector)
{
    float flFakeDiffuseLighting = saturate(dot(normal, viewVector)) * 0.7 + 0.3;

    vec3 vReflectionDirWs = reflect(viewVector, normal);

    float flFakeSpecularLighting = pow2( pow2(saturate(dot(viewVector, vReflectionDirWs))) ) * 0.05;

    float XtraLight1 = dot(vec3(0.6, 0.4, 1.0), pow2(saturate(normal)));
    float XtraLight2 = dot(vec3(0.6, 0.4, 0.2), pow2(saturate(-normal)));
    float xtraLight = XtraLight1 + XtraLight2;

    //return XtraLightDiffuse * albedo * flFakeDiffuseLighting + flFakeSpecularLighting;
    return xtraLight * albedo * flFakeDiffuseLighting + flFakeSpecularLighting;
}

