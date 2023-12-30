#version 460

// Todo: include all lighting glsl files within lighting.glsl, so it's only one include.
// That would make the order of includes less important and easier to understand/maintain.
struct LightingTerms_t
{
    vec3 DiffuseDirect;
    vec3 DiffuseIndirect;
    vec3 SpecularDirect;
    vec3 SpecularIndirect;
#if defined(S_TRANSMISSIVE_BACKFACE_NDOTL)
    vec3 TransmissiveDirect;
#endif
    float SpecularOcclusion; // Lightmap AO
};

LightingTerms_t InitLighting()
{
#if defined(S_TRANSMISSIVE_BACKFACE_NDOTL) // temp
    return LightingTerms_t(vec3(0), vec3(0), vec3(0), vec3(0), vec3(0), 1.0);
#else
    return LightingTerms_t(vec3(0), vec3(0), vec3(0), vec3(0), 1.0);
#endif
}
