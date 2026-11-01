#version 460

#ifndef VRF_TEXTURING_INCLUDE
#define VRF_TEXTURING_INCLUDE

#include "view_constants.slang"
#include "lighting_constants.slang"
#include "shared_combos.slang"
#include "utils.slang"
#include "fullbright.slang"
#include "material_properties.slang"

float GetCurvature(vec3 vNormal, vec3 vPositionWS)
{
    return length(fwidth(vNormal)) / length(fwidth(vPositionWS));
}

// Geometric roughness. Essentially just Specular Anti-Aliasing
float CalculateGeometricRoughnessFactor(vec3 geometricNormal)
{
    vec3 normalDerivX = dFdx(geometricNormal);
    vec3 normalDerivY = dFdy(geometricNormal);
    float normalChange = saturate(max(length_squared(normalDerivX), length_squared(normalDerivY)));

    return pow(normalChange, 0.333);
}

void InitProperties(out MaterialProperties_t mat, vec3 GeometricNormal, vec3 PositionWS, vec3 Tangent, vec3 Bitangent)
{
    mat.PositionWS = PositionWS;
    mat.ViewDir = normalize(g_vCameraPositionWs - PositionWS);
    float invNormalLength = 1.0 / length(GeometricNormal);
    mat.GeometricNormal = GeometricNormal * invNormalLength;
    mat.Tangent = Tangent * invNormalLength;
    mat.Bitangent = Bitangent * invNormalLength;

    mat.Albedo = vec3(0.0);
    mat.Opacity = 1.0;
    mat.Metalness = 0.0;
    mat.Normal = vec3(0.0);
    mat.NormalMap = vec3(0, 0, 1);
    mat.SoftNormal = vec3(0, 0, 1);

    mat.Roughness = vec2(0.0);
    mat.IsometricRoughness = 0.0;
    mat.RoughnessTex = vec2(0.0);
    mat.EnvironmentBlur = 1.0;
    mat.Reflectance = 0.0;
    mat.FresnelExponent = 5.0;
    mat.AmbientOcclusion = 1.0;
    mat.Height = 0.5;
    mat.DiffuseAO = vec3(1.0);
    mat.SpecularAO = 1.0;
    mat.RetroReflectivity = 0.0;
    mat.SSSMask = 0.0;
    mat.MouthMask = 0.0;
    mat.WaterEdgeBlend = 0.0;
    mat.WaterColumnDepth = 0.0;
    mat.ClothMask = 0.0;
    mat.SheenColor = vec3(0.0);
    mat.SheenRoughness = 0.0;

    mat.DiffuseColor = vec3(0.0);
    mat.SpecularColor = vec3(0.04);
    mat.TransmissiveColor = vec3(0.0);
    mat.DirectDiffuseScale = 1.0;
    mat.IllumColor = vec3(0.0);
    mat.BloomAmount = 0.0;

    mat.LightmapUv = vec3(0.0);

    mat.AmbientGeometricNormal = vec3(0.0); // Indirect geometric normal
    mat.AmbientNormal = vec3(0.0); // Indirect normal
#if (DIFFUSE_WRAP)
    mat.DiffuseWrapColor = vec3(1.0);
#endif

#if ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1))
    mat.AnisotropicTangent = vec3(0.0);
    mat.AnisotropicBitangent = vec3(0.0);
#endif

#if (F_USE_FRONT_FACE_NORMALS_FOR_BACK_FACES == 0)
    if (F_RENDER_BACKFACES == 1 && F_DONT_FLIP_BACKFACE_NORMALS == 0)
    {
        // when rendering backfaces, they invert normal so it appears front-facing
        mat.GeometricNormal *= gl_FrontFacing ? 1.0 : -1.0;
    }
#endif
}

void AdjustRoughnessByGeometricNormal(inout MaterialProperties_t mat)
{
#if (GEOMETRIC_ROUGHNESS)
    float geometricRoughnessFactor = CalculateGeometricRoughnessFactor(mat.GeometricNormal);

    vec2 geometricAdjusted = max(mat.RoughnessTex, vec2(geometricRoughnessFactor));

    mat.GeometricRoughness = geometricRoughnessFactor;
    mat.Roughness = geometricAdjusted;
#else
    mat.GeometricRoughness = 0.0;
    mat.Roughness = mat.RoughnessTex;
#endif
}

vec3 CalculateIridescence(float hueAngle, float intensity)
{
    float segmentIndex = floor(hueAngle * 6.0);
    float segmentPhase = fract(hueAngle * 6.0);

    // Calculate transition values
    float fadeOut = intensity * (1.0 - segmentPhase);
    float fadeIn = intensity * segmentPhase;

    // Use modulo arithmetic to create a smooth color wheel
    int colorIndex = int(segmentIndex) % 6;

    // Return color based on segment
    switch(colorIndex)
    {
        case 0: return vec3(intensity, fadeIn, 0.0);  // Red -> Yellow
        case 1: return vec3(fadeOut, intensity, 0.0); // Yellow -> Green
        case 2: return vec3(0.0, intensity, fadeIn);  // Green -> Cyan
        case 3: return vec3(0.0, fadeOut, intensity); // Cyan -> Blue
        case 4: return vec3(fadeIn, 0.0, intensity);  // Blue -> Magenta
        default: return vec3(intensity, 0.0, fadeOut); // Magenta -> Red
    }
}

T ContrastBrightness<T : IFloat>(T value, float contrast, float brightness)
{
    return saturate(((value - T(0.5)) * T(contrast) + T(0.5)) * T(brightness));
}

vec3 RotateHue(vec3 color, float angle)
{
    const vec3 greyAxis = vec3(0.57735); // normalize(vec3(1.0))
    float cosAngle = cos(angle);
    return color * cosAngle + cross(greyAxis, color) * sin(angle) + greyAxis * dot(greyAxis, color) * (1.0 - cosAngle);
}

#if defined(DIFFUSE_AO_COLOR_BLEED)

uniform vec3 g_vAmbientOcclusionColorBleed = vec3(0.4, 0.15, 0.13); // SrgbRead(true)

void SetDiffuseColorBleed(inout MaterialProperties_t mat)
{
    vec3 vAmbientOcclusionExponent = vec3(1.0) - g_vAmbientOcclusionColorBleed.rgb;
    vAmbientOcclusionExponent = mix(vec3(1.0), vAmbientOcclusionExponent, (F_SSS_MASK == 1) ? mat.SSSMask : 1.0);
    mat.DiffuseAO = SafePow(mat.DiffuseAO, vAmbientOcclusionExponent);
}

#endif

//-------------------------------------------------------------------------
//                              NORMALS
//-------------------------------------------------------------------------

// Prevent over-interpolation of vertex normals
vec3 SwitchCentroidNormal(vec3 vNormalWs, vec3 vCentroidNormalWs)
{
    return ( length_squared(vNormalWs) >= 1.01 ) ? vCentroidNormalWs : vNormalWs;
}

//Calculate the normal of this fragment in world space
vec3 CalculateWorldNormal(vec3 normalMap, vec3 normal, vec3 tangent, vec3 bitangent)
{
    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * normalMap);
}

// The inverse of CalculateWorldNormal
vec3 CalculateTangentNormal(vec3 normalWs, vec3 normal, vec3 tangent, vec3 bitangent)
{
    return normalize(vec3(dot(normalWs, tangent), dot(normalWs, bitangent), dot(normalWs, normal)));
}

#if (F_USE_BENT_NORMALS == 1)

    uniform sampler2D g_tBentNormal;

    void GetBentNormal(inout MaterialProperties_t mat, vec2 texCoords)
    {
        vec3 vBentNormalTs = DecodeHemiOctahedronNormal(texture(g_tBentNormal, texCoords).xy);
        mat.AmbientGeometricNormal = CalculateWorldNormal(vBentNormalTs, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

        // this is how they blend in the bent normal; by re-converting the normal map to tangent space using the bent geo normal
        mat.AmbientNormal = CalculateWorldNormal(mat.NormalMap, mat.AmbientGeometricNormal, mat.Tangent, mat.Bitangent);
    }
#endif

//-------------------------------------------------------------------------
//                              ALPHA TEST
//-------------------------------------------------------------------------

#if (F_ALPHA_TEST > 0) || (ALPHA_TEST_BY_BLEND_MODE == 1)

    uniform float g_flAntiAliasedEdgeStrength = 1.0;

    float AlphaTestAntiAliasing(float flOpacity, vec2 UVs, float flReference, float flEdgeStrength)
    {
        bool isMsaa = gl_NumSamples > 1;

    #if (ALPHA_TEST_COVERAGE_BY_UV_FOOTPRINT)
        if (!isMsaa)
        {
            if ((flOpacity - flReference) - 0.001 < 0.0)
            {
                discard;
            }

            return flOpacity;
        }

        // Sharp anti-aliased edge using shader derivatives, fading back to the opacity itself where the texture is minified
        float flAlphaTestAA = saturate((flOpacity - flReference) / max(fwidth(flOpacity), 0.000001) + 0.5);
        float flMinification = saturate(4.0 * length(fwidth(UVs)));
        flOpacity = mix(flAlphaTestAA, flOpacity, mix(1.0, flMinification, flEdgeStrength));

        if (flOpacity - 0.001 < 0.0)
        {
            discard;
        }

        return flOpacity;
    #else
        const float epsilon = 1 / 255.0;
        float flTestValue = isMsaa
            ? 0.0
            : flReference;
        
        if (flOpacity - epsilon < flTestValue)
        {
            discard;
        }
        
        if (isMsaa)
        {
            // Sharp anti-aliased edge using shader derivatives
            float flAlphaTestAA = saturate( (flOpacity - flReference) / ClampToPositive( fwidth(flOpacity) ) + 0.5 );
            float flAlphaTestAA_Amount = min(1.0, length( fwidth(UVs) ) * 4.0);

            // todo: lower this based on distance
            float flAntiAliasAlphaBlend = mix(flAlphaTestAA_Amount, 1.0, flEdgeStrength);

            // this value will determine the number of msaa samples covered by this fragment
            flOpacity = mix(flOpacity, flAlphaTestAA, flAntiAliasAlphaBlend);
        }

        return flOpacity;
    #endif
    }

    float AlphaTestAntiAliasing(float flOpacity, vec2 UVs, float flReference)
    {
        return AlphaTestAntiAliasing(flOpacity, UVs, flReference, g_flAntiAliasedEdgeStrength);
    }
#endif

// Render modes
#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_BumpMap 0
#define renderMode_Tangents 0
#define renderMode_Normals 0
#define renderMode_BumpNormals 0
#define renderMode_Roughness 0
#define renderMode_Metalness 0
#define renderMode_Height 0
#define renderMode_ExtraParams 0

#define renderMode_Tint 0
#define renderMode_FoliageParams 0
#define renderMode_VertexColor 0
#define renderMode_TerrainBlend 0

#define renderMode_UvDensity 0
#define renderMode_LightmapUvDensity 0
#define renderMode_MipmapUsage 0

bool HandleMaterialRenderModes(inout vec4 outputColor, MaterialProperties_t mat)
{
    switch (g_iRenderMode)
    {
        case renderMode_FullBright:
            outputColor.rgb = CalculateFullbrightLighting(mat.Albedo, mat.Normal, mat.ViewDir);
            outputColor.rgb = SrgbLinearToGamma(outputColor.rgb);
            return true;
        case renderMode_Color:
            outputColor = vec4(SrgbLinearToGamma(mat.Albedo), 1.0);
            return true;
        case renderMode_BumpMap:
            outputColor = vec4(PackToColor(mat.NormalMap), 1.0);
            return true;
        case renderMode_Tangents:
            outputColor = vec4(PackToColor(mat.Tangent), 1.0);
            return true;
        case renderMode_Normals:
            outputColor = vec4(PackToColor(mat.GeometricNormal), 1.0);
            return true;
        case renderMode_BumpNormals:
            outputColor = vec4(PackToColor(mat.Normal), 1.0);
            return true;
        case renderMode_Roughness:
            {
                #if ((F_ANISOTROPIC_GLOSS == 1) || (F_HIGH_QUALITY_GLOSS == 1))
                    outputColor.rgb = vec3(mat.Roughness.xy, 0.0);
                #else
                    outputColor.rgb = mat.IsometricRoughness.xxx;
                #endif

                return true;
            }
        case renderMode_Metalness:
            outputColor.rgb = mat.Metalness.xxx;
            return true;
        case renderMode_Height:
            outputColor.rgb = mat.Height.xxx;
            return true;
        case renderMode_ExtraParams:
            outputColor.rgb = vec3(max(mat.RetroReflectivity, mat.WaterEdgeBlend), mat.WaterColumnDepth, mat.SSSMask);
            return true;
        default:
            return false;
    }
}

bool HandleUVRenderModes(inout vec4 outputColor, MaterialProperties_t mat, sampler2D representativeTexture, vec2 flUVs)
{
    if (g_iRenderMode == renderMode_UvDensity || g_iRenderMode == renderMode_LightmapUvDensity)
    {
        outputColor.rgb = SrgbLinearToGamma(mat.Albedo);
        vec2 uv = g_iRenderMode == renderMode_UvDensity ? flUVs : mat.LightmapUv.xy;

        ivec2 vDims = g_iRenderMode == renderMode_LightmapUvDensity
            ? ivec2(8096)
            : textureSize(representativeTexture, 0);

        uint testVal = ((uv.x < 0) != (uv.y < 0)) ? 0 : 1;
        uvec2 vUVInPixels = uvec2(abs(uv) * vDims.xy);
        if (((vUVInPixels.x + vUVInPixels.y) & 1) == testVal)
        {
            outputColor.rgb *= 0.6;
        }

        uvec2 vUVIn16Pixels = vUVInPixels / 16;
        if (((vUVIn16Pixels.x + vUVIn16Pixels.y) & 1) == testVal)
        {
            outputColor.rgb *= 0.5;
        }

        return true;
    }
    else if (g_iRenderMode == renderMode_MipmapUsage)
    {
        outputColor.rgb = SrgbLinearToGamma(mat.Albedo);

        ivec2 vTexDimensions = textureSize(representativeTexture, 0);
        float flMipLevel = textureQueryLod(representativeTexture, flUVs).y;
        float flMipLevels = log2(max(vTexDimensions.x, vTexDimensions.y));

        uint testVal = ((flUVs.x < 0) != (flUVs.y < 0)) ? 0 : 1;
        uvec2 vUVInPixels = uvec2(abs(flUVs) * vTexDimensions.xy);
        uvec2 vUVIn16Pixels = vUVInPixels / uint(8 * round(1 + flMipLevel));

        float fIntensity = (((vUVIn16Pixels.x + vUVIn16Pixels.y) & 1) == testVal) ? .75 : .25f;
        outputColor.rgb = mix(outputColor.rgb, vec3(1.0, flMipLevel / flMipLevels, 0.0), fIntensity);

        return true;
    }
    return false;
}

#endif
