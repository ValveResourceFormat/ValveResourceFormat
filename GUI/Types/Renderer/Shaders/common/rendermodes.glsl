#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_Normals 0
#define renderMode_Tangents 0
#define renderMode_BumpMap 0
#define renderMode_BumpNormals 0
#define renderMode_Illumination 0



#if renderMode_FullBright == 1
vec3 CalculateFullbrightLighting(vec3 albedo, vec3 normal, vec3 viewVector)
{
    float flFakeDiffuseLighting = saturate(dot(normal, -viewVector)) * 0.7 + 0.3;

    vec3 vReflectionDirWs = reflect(viewVector, normal);

    float flFakeSpecularLighting = pow2(pow2(saturate(dot(-viewVector, vReflectionDirWs)))) * 0.05;

    float XtraLight1 = dot(vec3(0.6, 0.4, 1.0), pow2(saturate(normal)));
    float XtraLight2 = dot(vec3(0.6, 0.4, 0.2), pow2(saturate(-normal)));
    float xtraLight = XtraLight1 + XtraLight2;
    
    //return XtraLightDiffuse * albedo * flFakeDiffuseLighting + flFakeSpecularLighting;
    return xtraLight * albedo * flFakeDiffuseLighting + flFakeSpecularLighting;
}
#endif
