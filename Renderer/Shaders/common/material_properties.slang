#version 460

#ifndef VRF_MATERIAL_PROPERTIES_INCLUDE
#define VRF_MATERIAL_PROPERTIES_INCLUDE

// Struct full of everything needed for lighting, for easy access around the shader.

struct MaterialProperties_t
{
    vec3 PositionWS;
    vec3 GeometricNormal;
    vec3 Tangent;
    vec3 Bitangent;
    vec3 ViewDir;

    vec3 Albedo;
    float Opacity;
    float Metalness;
    vec3 Normal;
    vec3 NormalMap;
    vec3 SoftNormal;
    vec2 Roughness;
    float IsometricRoughness;
    vec2 RoughnessTex;
    float EnvironmentBlur;
    float Reflectance; // Read where ENV_SPECULAR_SCHLICK is set: the fresnel at normal incidence, before the metalness tints it
    float FresnelExponent; // Read where ENV_SPECULAR_SCHLICK is set: the power the fresnel rises by
    vec3 IndirectLightTexture; // Read where INDIRECT_LIGHT_TEXTURE is set
    float GeometricRoughness; // The roughness floor from the change of the geometric normal, 0 where it does not apply
    float AmbientOcclusion;
    float Height;
    vec3 DiffuseAO; // vec3 because Diffuse AO can be tinted
    float SpecularAO;
    float RetroReflectivity;
    float SSSMask;
    float MouthMask;
    float WaterEdgeBlend;
    float WaterColumnDepth;
    float ClothMask;

    // Sheen lobe, layered over the specular. Zero roughness disables it.
    vec3 SheenColor;
    float SheenRoughness;

    vec3 DiffuseColor;
    vec3 SpecularColor;
    vec3 TransmissiveColor;
    float DirectDiffuseScale;
    vec3 IllumColor;

    vec3 LightmapUv;

    vec3 AmbientGeometricNormal; // Indirect geometric normal
    vec3 AmbientNormal; // Indirect normal

#if (DIFFUSE_WRAP)
    vec3 DiffuseWrapColor;
#endif

#if (F_USE_NPR_LIGHTING == 1)
    float RimLightMask;
#endif

#if ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1))
    vec3 AnisotropicTangent;
    vec3 AnisotropicBitangent;
#endif
};

#endif
