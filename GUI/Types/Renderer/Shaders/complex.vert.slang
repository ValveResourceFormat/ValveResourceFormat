#version 460

#include "common/utils.glsl"
#include "common/features.glsl"
#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"
#include "complex_features.glsl"

#include "common/instancing.glsl"
#include "common/animation.glsl"
#include "common/morph.glsl"

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;
#include "common/compression.glsl"

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_NOTINT 0
#define F_VERTEX_COLOR 0
#define F_TEXTURE_ANIMATION 0
uniform int F_TEXTURE_ANIMATION_MODE;
#define F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS 0
//End of parameter defines

#if defined(vr_simple_2way_blend_vfx) || defined (csgo_simple_2way_blend_vfx)
    #define simple_2way_blend_vfx
#endif
#if defined(vr_simple_2way_blend_vfx) || defined(vr_simple_2way_parallax_vfx) || defined(vr_simple_3way_parallax_vfx) || defined(vr_simple_blend_to_triplanar_vfx) || defined(vr_simple_blend_to_xen_membrane_vfx)
    #define vr_blend_vfx_common
#endif

#if D_BAKED_LIGHTING_FROM_LIGHTMAP == 1
    in vec2 vLightmapUV;
    out vec3 vLightmapUVScaled;
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    #if defined(vr_standard_vfx)
        #undef F_VERTEX_COLOR
        #undef F_PAINT_VERTEX_COLORS
        in vec4 vCOLOR1;
    #else
        in vec4 vPerVertexLighting;
    #endif
    out vec3 vPerVertexLightingOut;
#endif

#if (F_LAYERS > 0) || defined(simple_2way_blend_vfx) || defined(vr_blend_vfx_common) || defined(vr_standard_vfx_blend) || defined(environment_blend_vfx)
    #if defined(vr_standard_vfx_blend)
        #define vBLEND_COLOR vTEXCOORD2
    #else
        #define vBLEND_COLOR vTEXCOORD4
    #endif

    #if defined(vr_blend_vfx_common)
        #define vBLEND_ALPHA vTEXCOORD5
        in vec4 vBLEND_ALPHA;
    #endif

    in vec4 vBLEND_COLOR;
    out vec4 vColorBlendValues;
#endif

#if defined(foliage_vfx_common)
    #if defined(csgo_foliage_vfx)
        #define vFoliageParams vCOLOR
    #elif defined(vr_complex_vfx)
        #define vFoliageParams vTEXCOORD3
    #endif
    in vec4 vFoliageParams;
    out vec4 vFoliageParamsOut;

    // Vertex Animation
    uniform float g_flSwayAmount = 0.25;
    uniform float g_flSwaySpeed = 1.0;
    uniform float g_flSwayNoiseScale = 1.0;
    uniform float g_flSwayFalloff = 1.0;
    uniform float g_flFlutterAmount = 0.25;
    uniform float g_flFlutterSpeed = 1.0;
    uniform float g_flFlutterNoiseScale = 1.0;
    uniform float g_flFlutterFalloff = 1.0;
    uniform sampler3D g_tNoiseMap;

    // todo: from env_wind
    const vec4 g_vWindDirection = vec4(0.0, -1.0, 0.0, 0.0);
    const vec4 g_vWindStrengthFreqMulHighStrength = vec4(0.5);

    struct FoliageAnimParams
    {
        // Base animation strengths and falloffs
        float swayStrength;
        float flutterStrength;
        float swayFalloff;
        float flutterFalloff;

        // Time-based offsets
        vec3 swayTimeOffset;
        vec3 flutterTimeOffset;

        // Noise sampling scales
        vec3 swayNoiseScale;
        vec3 flutterNoiseScale;

        // Height-based parameters
        float heightBlend;
        vec3 heightOffset;
    };

    // Function to calculate animated position
    vec3 vertexAnimation(vec3 pos, FoliageAnimParams params)
    {
        // Sample and calculate sway noise
        vec3 swayNoisePos = ((pos + params.heightOffset) * params.swayNoiseScale) + params.swayTimeOffset;
        float swayNoiseMask = clamp(pow(textureLod(g_tNoiseMap, swayNoisePos * vec3(0.4, 0.4, 0.125), 0.0).z, 2.0) * 1.5, 0.0, 1.0);
        vec3 swayNoise = (textureLod(g_tNoiseMap, swayNoisePos, 0.0).xyz - vec3(0.5)) * 2.0;
        vec3 swayDir = vec3(swayNoise.xy, swayNoise.y * 0.5) * 0.5;

        // Calculate final sway offset
        vec3 swayOffset = ((swayDir * swayNoiseMask + g_vWindDirection.xyz) * params.swayFalloff *
                        g_vWindStrengthFreqMulHighStrength.x * params.swayStrength) * swayNoiseMask;

        // Sample and calculate flutter noise
        vec3 flutterNoisePos = ((pos + vec3(params.heightBlend * 10.0, params.heightBlend * 5.0, params.heightBlend * 5.0)) *
                            params.flutterNoiseScale) + params.flutterTimeOffset;
        vec3 flutterNoise = (textureLod(g_tNoiseMap, flutterNoisePos, 0.0).xyz - vec3(0.5)) * 2.0;

        // Calculate final flutter offset
        vec3 flutterOffset = ((flutterNoise * g_vWindDirection.xyz + flutterNoise * swayDir) *
                            g_vWindStrengthFreqMulHighStrength.x * params.flutterStrength) * params.flutterFalloff;

        // Combine effects with gravity compensation
        vec3 combinedOffset = swayOffset + flutterOffset;
        vec3 gravityComp = vec3(0.0, 0.0, -length(combinedOffset.xy) * 0.314);
        return pos + combinedOffset + gravityComp;
    }
#endif

#if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
    in vec4 vTEXCOORD1;
    out vec2 vTexCoord2;
#endif

#if !defined(vFoliageParams) && ((F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1))
    in vec4 vCOLOR;
#endif

centroid out vec4 vVertexColorOut;
out vec3 vFragPosition;
out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;
centroid out vec3 vCentroidNormalOut;
out vec2 vTexCoordOut;

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)
uniform float g_flModelTintAmount = 1.0;
uniform float g_flFadeExponent = 1.0;

uniform vec2 g_vTexCoordOffset;
uniform vec2 g_vTexCoordScale = vec2(1.0);
uniform vec2 g_vTexCoordScrollSpeed;
uniform vec2 g_vTexCoordCenter = vec2(0.5);
uniform float g_flTexCoordRotation = 0.0;

#if (F_TEXTURE_ANIMATION == 1)
    uniform vec2 g_vAnimationGrid = vec2(1, 1);
    uniform int g_nNumAnimationCells = 1;
    uniform float g_flAnimationTimePerFrame;
    uniform float g_flAnimationTimeOffset;
    uniform float g_flAnimationFrame;
#endif

#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
    uniform float g_vSphericalAnisotropyAngle = 180.0;
    uniform vec3 g_vSphericalAnisotropyPole = vec3(0, 0, 1);

    out vec3 vAnisoBitangentOut;

    vec3 GetSphericalProjectedAnisoBitangent(vec3 normal, vec3 tangent)
    {
        float angle = (g_vSphericalAnisotropyAngle / 90.0) - 1.0;

        vec3 vAnisoTangent = cross(g_vSphericalAnisotropyPole.xyz, normal);
        // Prevent length of 0
        vAnisoTangent = mix(vAnisoTangent, tangent, bvec3(length(vec3(equal(vAnisoTangent, vec3(0.0)))) != 0.0));

        if (g_vSphericalAnisotropyAngle != 0.0)
        {
            vec3 rotated1 = mix(cross(normal, vAnisoTangent), vAnisoTangent, ClampToPositive(g_vSphericalAnisotropyAngle));
            vAnisoTangent = mix(rotated1, -vAnisoTangent, ClampToPositive(-g_vSphericalAnisotropyAngle));
        }
        return vAnisoTangent;
    }
#endif

vec2 GetAnimatedUVs(vec2 texCoords)
{
    #if (F_TEXTURE_ANIMATION == 1)
        uint frame = uint(g_flAnimationFrame);
        uint cells = uint(g_nNumAnimationCells);
        if (F_TEXTURE_ANIMATION_MODE == 0) // Sequential
            frame = uint((g_flAnimationTimeOffset + g_flTime) / g_flAnimationTimePerFrame) % cells;
        else if (F_TEXTURE_ANIMATION_MODE == 1) // Random
            frame = uint(Random2D(vec2(g_flAnimationFrame)) * float(g_nNumAnimationCells));

        vec2 atlasGridInv = vec2(1.0) / g_vAnimationGrid.xy;
        vec2 atlasOffset = vec2(uvec2(
            frame % uint(g_vAnimationGrid.x),
            uint(float(frame) * atlasGridInv.x)
        )) * atlasGridInv;

        texCoords = texCoords * atlasGridInv + atlasOffset;
    #endif

    texCoords = RotateVector2D(texCoords, g_flTexCoordRotation, g_vTexCoordScale.xy, g_vTexCoordOffset.xy, g_vTexCoordCenter.xy);
    return texCoords + g_vTexCoordScrollSpeed.xy * g_flTime;
}

vec4 GetTintColorLinear(vec4 vTint)
{
    vec4 TintFade = vec4(1.0);
#if F_NOTINT == 0
    TintFade.rgb = mix(vec3(1.0), SrgbGammaToLinear(vTint.rgb) * g_vColorTint.rgb, g_flModelTintAmount);
#endif
    TintFade.a = pow(vTint.a, g_flFadeExponent);
    return TintFade;
}

#if (F_DETAIL_TEXTURE > 0)

    uniform float g_flDetailTexCoordRotation = 0.0;
    uniform vec2 g_vDetailTexCoordOffset = vec2(0.0);
    uniform vec2 g_vDetailTexCoordScale = vec2(1.0);
    out vec2 vDetailTexCoords;

    #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
        uniform bool g_bUseSecondaryUvForDetailTexture = false; // this doesn't always show up
    #endif

#endif

#include "features/csgo_character_eyes_vs.glsl"

void main()
{
    ObjectData_t object = GetObjectData();

    mat4 skinTransform = object.transform * getSkinMatrix();
    vFragPosition = (skinTransform * vec4(vPOSITION + getMorphOffset(), 1.0)).xyz;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    mat3 normalTransform = adjoint(skinTransform);
    vNormalOut = normalize(normalTransform * normal);
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);

    #if defined(foliage_vfx_common)
        // Interpolate out for debug visualization
        vFoliageParamsOut = vFoliageParams;

        // Clamp foliage parameters
        vec4 foliageParams = clamp(vFoliageParams, vec4(0.001), vec4(1.0));

        // Initialize animation parameters
        FoliageAnimParams animParams;

        // Calculate strengths
        animParams.swayStrength = 150.0 * g_flSwayAmount;
        animParams.flutterStrength = 25.0 * g_flFlutterAmount;
        animParams.swayFalloff = pow(foliageParams.x, g_flSwayFalloff);
        animParams.flutterFalloff = pow(foliageParams.z, g_flFlutterFalloff);

        // Calculate height blend and offset
        animParams.heightBlend = (2.0 * foliageParams.y) - 1.0;
        animParams.heightOffset = vec3(animParams.heightBlend * 100.0, 0.0, animParams.heightBlend * 50.0);

        // Set time-based movement offsets
        animParams.swayTimeOffset = vec3(-0.3, 0.0, -0.03) * g_flSwaySpeed * g_flTime;
        animParams.flutterTimeOffset = vec3(-0.35, 0.0, 0.1) * g_flFlutterSpeed * g_flTime;

        // Set noise sampling scales
        animParams.swayNoiseScale = vec3(0.00075, 0.00075, 0.00005) * g_flSwayNoiseScale;
        animParams.flutterNoiseScale = vec3(0.0025, 0.0025, 0.0005) * g_flFlutterNoiseScale;

        vFragPosition = vertexAnimation(vFragPosition.xyz, animParams);
        // todo: animate normaltangent
    #endif

    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

#if (F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS == 1)
    vAnisoBitangentOut = normalTransform * GetSphericalProjectedAnisoBitangent(normal, tangent.xyz);
#endif

#if defined(csgo_character_vfx) && (F_EYEBALLS == 1)
    eyeInterpolator = GetCharacterEyeInterpolator(vPOSITION);
#endif

    vTexCoordOut = GetAnimatedUVs(vTEXCOORD.xy);

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    #if defined(vr_standard_vfx)
        vec4 vPerVertexLighting = vCOLOR1;
    #endif
    vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
    vPerVertexLightingOut = pow2(Light);
#endif

    vVertexColorOut = GetTintColorLinear(object.vTint);

#if !defined(vFoliageParams) && ((F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1))
    vVertexColorOut *= SrgbGammaToLinear(vCOLOR);
#endif

#if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
    vTexCoord2 = vTEXCOORD1.xy;
#endif

#if (F_DETAIL_TEXTURE > 0)
    #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
        vec2 detailCoords = (g_bUseSecondaryUvForDetailTexture || (F_FORCE_UV2 == 1)) ? vTexCoord2 : vTexCoordOut;
    #else
        #define detailCoords vTexCoordOut
    #endif
    vDetailTexCoords = RotateVector2D(detailCoords, g_flDetailTexCoordRotation, g_vDetailTexCoordScale.xy, g_vDetailTexCoordOffset.xy);
#endif

#if defined(vBLEND_COLOR)
    vColorBlendValues = vBLEND_COLOR;

    #if defined(vr_blend_vfx_common)
        vColorBlendValues.y = max(0.5 * vBLEND_ALPHA.x, 0.1);
    #endif
#endif

    vCentroidNormalOut = vNormalOut;
}
