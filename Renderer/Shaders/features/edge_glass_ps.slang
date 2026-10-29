#version 460

#ifndef VRF_EDGE_GLASS_PS_INCLUDE
#define VRF_EDGE_GLASS_PS_INCLUDE

// Edge fresnel glass of materials with F_GLASS.

uniform bool g_bFresnel = true;
uniform float g_flEdgeColorFalloff = 3.0;
uniform float g_flEdgeColorMaxOpacity = 0.5;
uniform float g_flEdgeColorThickness = 0.1;
uniform vec3 g_vEdgeColor = vec3(0.5, 0.8, 0.5); // SrgbRead(true)
uniform float g_flRefractScale = 0.1;

// The albedo and opacity of the glass, the edge color fading in towards the silhouette
vec4 GetGlassMaterial(MaterialProperties_t mat, float opacityScale)
{
    float viewDotNormalInv = clamp(1.0 - (dot(mat.ViewDir, mat.Normal) - g_flEdgeColorThickness), 0.0001, 1.0);
    float fresnel = saturate(pow(viewDotNormalInv, g_flEdgeColorFalloff)) * g_flEdgeColorMaxOpacity;
    vec4 fresnelColor = vec4(g_vEdgeColor.xyz, g_bFresnel ? fresnel : 0.0);

    return mix(vec4(mat.Albedo, mat.Opacity), fresnelColor, opacityScale);
}

#endif
