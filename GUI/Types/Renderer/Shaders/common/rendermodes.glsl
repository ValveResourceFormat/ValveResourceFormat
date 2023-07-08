#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_Normals 0
#define renderMode_Tangents 0
#define renderMode_BumpMap 0
#define renderMode_BumpNormals 0
#define renderMode_Illumination 0

// this is found in gameinfo.gi. TODO: Set uniform manually using gameinfo? It'd fix the CS2 differences

// HALF-LIFE ALYX / APERTURE DESK JOB
#if defined(generic)
    const vec3 shaderIdColor = vec3(255, 0, 255); // 255 255 255 in cs2
#elif defined(vr_simple)
    const vec3 shaderIdColor = vec3(0, 255, 0);
#elif defined(vr_bloody_simple)
    const vec3 shaderIdColor = vec3(64, 255, 0);
#elif defined(vr_simple_2way_blend)
    const vec3 shaderIdColor = vec3(186, 255, 0);
#elif defined(vr_simple_2layer_parallax)
    const vec3 shaderIdColor = vec3(255, 186, 0);
#elif defined(vr_simple_3layer_parallax)
    const vec3 shaderIdColor = vec3(255, 64, 0);
#elif defined(vr_simple_blend_to_triplanar)
    const vec3 shaderIdColor = vec3(255, 186, 128);
#elif defined(vr_simple_blend_to_xen_membrane)
    const vec3 shaderIdColor = vec3(255, 128, 186);
#elif defined(vr_basic)
    const vec3 shaderIdColor = vec3(255, 255, 255);
#elif defined(vr_complex)
    const vec3 shaderIdColor = vec3(128, 0, 186);
#elif defined(vr_glass)
    const vec3 shaderIdColor = vec3(0, 255, 255);
#elif defined(vr_shatterglass)
    const vec3 shaderIdColor = vec3(0, 0, 255);

// CS2
//#elif defined(generic)
//    shaderIdColor = vec3(255, 255, 255);
#elif defined(csgo_simple)
    const vec3 shaderIdColor = vec3(128, 128, 128);
#elif defined(csgo_complex)
    const vec3 shaderIdColor = vec3(64, 32, 128);
#elif defined(csgo_vertexlitgeneric)
    const vec3 shaderIdColor = vec3(240, 0, 0);
#elif defined(csgo_unlitgeneric)
    const vec3 shaderIdColor = vec3(240, 32, 192);
#elif defined(csgo_lightmappedgeneric)
    const vec3 shaderIdColor = vec3(128, 0, 0);

#elif defined(csgo_character)
    const vec3 shaderIdColor = vec3(0, 0, 255);
#elif defined(csgo_static_overlay)
    const vec3 shaderIdColor = vec3(0, 255, 255);
#elif defined(csgo_projected_decals)
    const vec3 shaderIdColor = vec3(0, 128, 128);
#elif defined(csgo_environment)
    const vec3 shaderIdColor = vec3(128, 192, 64);
#elif defined(csgo_environment_blend)
    const vec3 shaderIdColor = vec3(64, 128, 0);
#elif defined(csgo_glass)
    const vec3 shaderIdColor = vec3(128, 32, 128);
#elif defined(csgo_weapon)
    const vec3 shaderIdColor = vec3(192, 128, 64);
#elif defined(csgo_water_fancy)
    const vec3 shaderIdColor = vec3(64, 128, 240);
#elif defined(cables)
    const vec3 shaderIdColor = vec3(128, 64, 64);
#elif defined(spritecard)
    const vec3 shaderIdColor = vec3(240, 240, 0);
#else
    const vec3 shaderIdColor = vec3(0, 255, 0);
#endif


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
