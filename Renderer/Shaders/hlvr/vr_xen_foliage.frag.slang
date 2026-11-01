#version 460

#include "vr_xen_foliage_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 1
#define DIFFUSE_SOFT_NORMAL 1
#define DIFFUSE_AO_COLOR_BLEED
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "../common/surface_foliage.slang"
#include "../features/detail_texture_ps.slang"
#include "self_illum_flow_ps.slang"
#include "../features/triplanar_ps.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)
uniform sampler2D g_tMaskParameters; // SrgbRead(true)
uniform float g_flDiffuseNormalBlur = 0.5;
uniform float g_flOpacityScale = 1.0;
uniform vec3 g_vDiffuseWrapColor = vec3(1.0, 0.5, 0.3);
uniform float g_flAmbientOcclusionDirectDiffuse = 1.0;
uniform float g_flAmbientOcclusionDirectSpecular = 1.0;

#if (F_XEN_GLOW == 1)
    in vec3 vXenGlowCenterOut;

    uniform sampler2D g_tXenGlowMask;
    uniform vec3 g_vXenGlowColor = vec3(0.1, 0.2, 0.5);
    uniform float g_flXenGlowPower = 1.0;
    uniform float g_flXenGlowRadius = 24.0;
    uniform float g_flXenGlowUserBrightness = 0.0;
#endif

#if (F_XEN_ANIMATION_TEXTURE == 1)
    in float vXenAnimationDisplacementOut;

    uniform vec4 g_vMembraneColor = vec4(0.77, 0.68, 0.55, 1.0); // SrgbRead(true)
#endif

#if (F_SELF_ILLUM == 1)
    uniform sampler2D g_tSelfIllumFlowWaveform;
    uniform vec3 g_vSelfIllumTint = vec3(1.0); // SrgbRead(true)
    uniform float g_flSelfIllumOverallScale = 1.0;
    uniform float g_flSelfIllumBrightness = 0.0;
    uniform float g_flSelfIllumFlowAmount = 0.0;
    uniform float g_flSelfIllumFlowSpeed = 0.0;
    uniform float g_flSelfIllumFlowFrequency = 1.0;
    uniform float g_flSelfIllumCustomFlowTimer = 0.0;
#endif

#if (F_BLEND == 1)
    in vec4 vColorBlendValues;

    uniform sampler2D g_tBlendMask;
    uniform sampler2D g_tColorB; // SrgbRead(true)
    uniform sampler2D g_tNormalB;
    uniform float g_flBlendUVScale = 1.0;
    uniform float g_flTriplanarScale = 64.0;
    uniform float g_flMetalnessB = 0.0;
#endif

#if (F_SELF_ILLUM == 1)

// The mask is in red, and blue offsets the phase of the flowing waveform
vec3 GetXenSelfIllumination(vec4 maskParameters)
{
    float flowTime = GetFlowTime(g_flSelfIllumFlowSpeed, g_flSelfIllumCustomFlowTimer);
    float flowPhase = exp2(g_flSelfIllumFlowFrequency) * maskParameters.b;
    float waveform = texture(g_tSelfIllumFlowWaveform, vec2(flowTime + flowPhase, 0.0)).r;
    float flow = waveform * g_flSelfIllumFlowAmount + (1.0 - g_flSelfIllumFlowAmount);

    vec3 tint = GetSelfIllumTint(g_flSelfIllumOverallScale, g_flSelfIllumBrightness, g_vSelfIllumTint);

    // Only the instance tint applies here, but the vertex color also carries g_vColorTint
    return tint * maskParameters.r * vVertexColorOut.rgb * flow;
}

#endif

// Direct diffuse is lit by a blurrier mip of the normal map, without the detail normal
vec3 GetXenDiffuseNormal(vec2 texCoord, MaterialProperties_t mat)
{
    vec2 normalTexels = texCoord * vec2(textureSize(g_tNormal, 0));
    vec2 normalTexelsDdx = dFdx(normalTexels);
    vec2 normalTexelsDdy = dFdy(normalTexels);
    float blurLod = max(0.5 * log2(max(length_squared(normalTexelsDdx), length_squared(normalTexelsDdy))), g_flDiffuseNormalBlur);

    vec3 blurredNormalMap = DecodeHemiOctahedronNormal(textureLod(g_tNormal, texCoord, blurLod).rg);
    return CalculateWorldNormal(blurredNormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
}

#if (F_XEN_GLOW == 1)

// A pulsing glow around the prop's glow center, strongest where the view ray passes closest to it
vec3 GetXenGlow(vec2 texCoord)
{
    vec3 glowCenter = vXenGlowCenterOut - g_vCameraPositionWs;
    vec3 viewRay = normalize(vFragPosition - g_vCameraPositionWs);
    vec3 rayToCenter = glowCenter - viewRay * dot(glowCenter, viewRay);
    float rayDistanceSquared = dot(rayToCenter, rayToCenter);
    float radiusSquared = g_flXenGlowRadius * g_flXenGlowRadius;

    if (rayDistanceSquared >= radiusSquared)
    {
        return vec3(0.0);
    }

    float falloff = pow(sqrt(radiusSquared - rayDistanceSquared) / g_flXenGlowRadius, 16.0);
    float pulse = sin(g_flTime * 2.0) * 0.25 + 0.75;
    vec3 glowMask = texture(g_tXenGlowMask, texCoord).rgb;

    return glowMask * g_flXenGlowUserBrightness * g_vXenGlowColor * g_flXenGlowPower * falloff * pulse * 0.2;
}

#endif

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

#if (DIFFUSE_WRAP)
    mat.DiffuseWrapColor = g_vDiffuseWrapColor;
#endif

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = color.rgb;
    InitOpacity(mat, color.a, g_flOpacityScale, F_TRANSLUCENT != 0, false, texCoord, g_flAlphaTestReference);

    mat.Albedo *= vVertexColorOut.rgb;

#if (F_METALNESS_TEXTURE == 1)
    // The alpha is packed with metalness, and it still sets opacity too
    mat.Metalness = color.a;
#endif

    mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;
    mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);

    // The detail overlay is always applied with the normals, blended in pow 2.2 space and faded out with distance
    ApplyDetailTextureWithOptions(mat.Albedo, mat.NormalMap, texCoord, true, true, true);

    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.SoftNormal = GetXenDiffuseNormal(texCoord, mat);

#if (F_XEN_ANIMATION_TEXTURE == 1)
    // Membranes stretched by the animation turn towards the membrane color. The stretch should also
    // weaken the detail color and bend the detail normal the other way, which is not done here
    if (F_DETAIL_TEXTURE == 1)
    {
        float membraneAmount = vXenAnimationDisplacementOut * g_vMembraneColor.a;
        mat.Albedo = mix(mat.Albedo, g_vMembraneColor.rgb, membraneAmount);
    }
#endif

    vec4 maskParameters = texture(g_tMaskParameters, texCoord);
    mat.AmbientOcclusion = maskParameters.a;

#if (F_SELF_ILLUM == 1)
    mat.IllumColor = GetXenSelfIllumination(maskParameters);
#endif

#if (F_XEN_GLOW == 1)
    mat.IllumColor += GetXenGlow(texCoord);
#endif

#if (F_BLEND == 1)
    float blendMask = texture(g_tBlendMask, texCoord * g_flBlendUVScale).r;
    float blendFactor = ApplyBlendModulation(vColorBlendValues.r, blendMask, vColorBlendValues.g);

    // The second layer is projected along the world axes and is already in world space
    vec3 projectionNormal = normalize(vNormalOut);
    Triplanar_t triplanar = GetTriplanar(vFragPosition, projectionNormal, g_flTriplanarScale);

    vec3 colorB = SampleTriplanar(g_tColorB, triplanar).rgb;
    float roughnessB;
    vec3 normalB = SampleTriplanarNormal(g_tNormalB, triplanar, projectionNormal, roughnessB);

    mat.Albedo = mix(mat.Albedo, colorB, blendFactor);
    mat.Normal = normalize(mix(mat.Normal, normalB, blendFactor));
    mat.RoughnessTex = mix(mat.RoughnessTex, vec2(roughnessB), blendFactor);
    mat.Metalness = g_flMetalnessB * blendFactor;
    mat.IllumColor *= 1.0 - blendFactor;
#endif

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

void main()
{
#if (F_BLEND == 1)
    vec3 blendValues = vColorBlendValues.rgb;
#else
    vec3 blendValues = vec3(0.0);
#endif

    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.SpecularIgnoresOpacity = F_TRANSLUCENT == 2;
    options.DirectDiffuseAO = g_flAmbientOcclusionDirectDiffuse;
    options.DirectSpecularAO = g_flAmbientOcclusionDirectSpecular;
    options.BlendValues = blendValues;
    if (RenderSurface(g_tColor, options))
    {
        return;
    }

#if (S_MODE_DEPTH == 0)
    if (g_iRenderMode == renderMode_FoliageParams)
    {
        outputColor.rgb = vFoliageParamsOut.rgb;
    }
#endif
}
