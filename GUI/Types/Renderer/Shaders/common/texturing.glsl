#version 460
//? #include "LightingConstants.glsl"
//? #include "features.glsl"
//? #include "utils.glsl"
//? #include "fullbright.glsl"

#if defined(NEED_CURVATURE) && (F_USE_PER_VERTEX_CURVATURE == 0)
    // Expensive, only used in skin shaders
    float GetCurvature(vec3 vNormal, vec3 vPositionWS)
    {
        return length(fwidth(vNormal)) / length(fwidth(vPositionWS));
    }
#endif

// Geometric roughness. Essentially just Specular Anti-Aliasing
float CalculateGeometricRoughnessFactor(vec3 geometricNormal)
{
    vec3 normalDerivX = dFdxCoarse(geometricNormal);
    vec3 normalDerivY = dFdyCoarse(geometricNormal);
    float geometricRoughnessFactor = pow(saturate(max(dot(normalDerivX, normalDerivX), dot(normalDerivY, normalDerivY))), 0.333);
    return geometricRoughnessFactor;
}

float ApplyBlendModulation(float blendFactor, float blendMask, float blendSoftness)
{
    float minb = max(0.0, blendMask - blendSoftness);
    float maxb = min(1.0, blendMask + blendSoftness);

    return smoothstep(minb, maxb, blendFactor);
}

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
    vec2 Roughness;
    float IsometricRoughness;
    vec2 RoughnessTex;
    float AmbientOcclusion;
    float Height;
    vec3 DiffuseAO; // vec3 because Diffuse AO can be tinted
    float SpecularAO;
    vec4 ExtraParams;
    float ClothMask;
    float SSSMask;

    vec3 DiffuseColor;
    vec3 SpecularColor;
    vec3 TransmissiveColor;
    vec3 IllumColor;

    vec3 LightmapUv;

    vec3 AmbientGeometricNormal; // Indirect geometric normal
    vec3 AmbientNormal; // Indirect normal

#if defined(NEED_CURVATURE)
    float Curvature;
#endif

#if defined(ANISO_ROUGHNESS)
    vec3 AnisotropicTangent;
    vec3 AnisotropicBitangent;
#endif
};

void InitProperties(out MaterialProperties_t mat, vec3 GeometricNormal)
{
    mat.PositionWS = vFragPosition;
    mat.ViewDir = normalize(g_vCameraPositionWs - vFragPosition);
    mat.GeometricNormal = normalize(GeometricNormal);
    mat.Tangent = normalize(vTangentOut);
    mat.Bitangent = normalize(vBitangentOut);

    mat.Albedo = vec3(0.0);
    mat.Opacity = 1.0;
    mat.Metalness = 0.0;
    mat.Normal = vec3(0.0);
    mat.NormalMap = vec3(0, 0, 1);

    mat.Roughness = vec2(0.0);
    mat.IsometricRoughness = 0.0;
    mat.RoughnessTex = vec2(0.0);
    mat.AmbientOcclusion = 1.0;
    mat.Height = 0.5;
    mat.DiffuseAO = vec3(1.0);
    mat.SpecularAO = 1.0;
    // r = retro reflectivity, g = misc, b = misc, a = misc/rimmask?
    mat.ExtraParams = vec4(0.0);
    mat.ClothMask = 0.0;
    mat.SSSMask = 0.0;

    mat.DiffuseColor = vec3(0.0);
    mat.SpecularColor = vec3(0.04);
    mat.TransmissiveColor = vec3(0.0);
    mat.IllumColor = vec3(0.0);

    mat.LightmapUv = vec3(0.0);

    mat.AmbientGeometricNormal = vec3(0.0); // Indirect geometric normal
    mat.AmbientNormal = vec3(0.0); // Indirect normal
#if defined(NEED_CURVATURE)// || renderMode_Curvature
    #if F_USE_PER_VERTEX_CURVATURE == 1
        mat.Curvature = flPerVertexCurvature;
    #else
        mat.Curvature = pow( GetCurvature(mat.GeometricNormal, mat.PositionWS), 0.333 );
    #endif
#endif

#if defined(ANISO_ROUGHNESS)
    mat.AnisotropicTangent = vec3(0.0);
    mat.AnisotropicBitangent = vec3(0.0);
#endif

    if (F_RENDER_BACKFACES == 1 && F_DONT_FLIP_BACKFACE_NORMALS == 0)
    {
        // when rendering backfaces, they invert normal so it appears front-facing
        mat.GeometricNormal *= gl_FrontFacing ? 1.0 : -1.0;
    }
}

void AdjustRoughnessByGeometricNormal(inout MaterialProperties_t mat)
{
    const float MIN_ROUGHNESS = 0.0525;

    float geometricRoughnessFactor = CalculateGeometricRoughnessFactor(mat.GeometricNormal);
    geometricRoughnessFactor = max(geometricRoughnessFactor, MIN_ROUGHNESS);

    vec2 geometricAdjusted = max(mat.RoughnessTex, vec2(geometricRoughnessFactor));

    mat.Roughness = geometricAdjusted;
}

#if defined(vr_skin_vfx) || defined(vr_xen_foliage_vfx)
#define DIFFUSE_AO_COLOR_BLEED
#endif

#if defined(DIFFUSE_AO_COLOR_BLEED)

uniform vec3 g_vAmbientOcclusionColorBleed = vec3(0.4, 0.14902, 0.129412); // SrgbRead(true)

void SetDiffuseColorBleed(inout MaterialProperties_t mat)
{
    vec3 vAmbientOcclusionExponent = vec3(1.0) - g_vAmbientOcclusionColorBleed.rgb;
#if (F_SSS_MASK == 1)
    vAmbientOcclusionExponent = mix(vec3(1.0), vAmbientOcclusionExponent, mat.SSSMask);
#endif
    mat.DiffuseAO = pow(mat.DiffuseAO, vAmbientOcclusionExponent);
}

#endif

//-------------------------------------------------------------------------
//                              NORMALS
//-------------------------------------------------------------------------

// Prevent over-interpolation of vertex normals. Introduced in The Lab renderer
vec3 SwitchCentroidNormal(vec3 vNormalWs, vec3 vCentroidNormalWs)
{
    return ( dot(vNormalWs, vNormalWs) >= 1.01 ) ? vCentroidNormalWs : vNormalWs;
}

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal(vec3 normalMap, vec3 normal, vec3 tangent, vec3 bitangent)
{
    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * normalMap);
}

#if (F_USE_BENT_NORMALS == 1)

    uniform sampler2D g_tBentNormal;

    void GetBentNormal(inout MaterialProperties_t mat, vec2 texCoords)
    {
        vec3 vBentNormalTs = DecodeHemiOctahedronNormal(texture(g_tBentNormal, texCoords).xy);
        mat.AmbientGeometricNormal = calculateWorldNormal(vBentNormalTs, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

        // this is how they blend in the bent normal; by re-converting the normal map to tangent space using the bent geo normal
        mat.AmbientNormal = calculateWorldNormal(mat.NormalMap, mat.AmbientGeometricNormal, mat.Tangent, mat.Bitangent);
    }
#endif

//-------------------------------------------------------------------------
//                              ALPHA TEST
//-------------------------------------------------------------------------

#if (F_ALPHA_TEST == 1) || (alphatest)

    uniform float g_flAntiAliasedEdgeStrength = 1.0;

    float AlphaTestAntiAliasing(float flOpacity, vec2 UVs)
    {
        float flAlphaTestAA = saturate( (flOpacity - g_flAlphaTestReference) / ClampToPositive( fwidth(flOpacity) ) + 0.5 );
        float flAlphaTestAA_Amount = min(1.0, length( fwidth(UVs) ) * 4.0);
        float flAntiAliasAlphaBlend = mix(1.0, flAlphaTestAA_Amount, g_flAntiAliasedEdgeStrength);
        return mix( flAlphaTestAA, flOpacity, flAntiAliasAlphaBlend );
    }
#endif

//-------------------------------------------------------------------------
//                              DETAIL TEXTURING
//-------------------------------------------------------------------------

#if (F_DETAIL_TEXTURE > 0)

// Xen foliage detail textures always has both color and normal
#define DETAIL_COLOR_MOD2X (F_DETAIL_TEXTURE == 1) && !defined(vr_xen_foliage_vfx)
#define DETAIL_COLOR_OVERLAY ((F_DETAIL_TEXTURE == 2) || (F_DETAIL_TEXTURE == 4)) || defined(vr_xen_foliage_vfx)
#define DETAIL_NORMALS ((F_DETAIL_TEXTURE == 3) || (F_DETAIL_TEXTURE == 4)) || defined(vr_xen_foliage_vfx)

uniform float g_flDetailBlendFactor = 1.0;
uniform float g_flDetailBlendToFull = 0.0;
uniform float g_flDetailNormalStrength = 1.0;
in vec2 vDetailTexCoords;

uniform sampler2D g_tDetailMask;
#if DETAIL_COLOR_MOD2X || DETAIL_COLOR_OVERLAY
    uniform sampler2D g_tDetail;
#endif
#if DETAIL_NORMALS
    uniform sampler2D g_tNormalDetail;
#endif

#define MOD2X_MUL 1.9922
#define DETAIL_CONST 0.9961

void applyDetailTexture(inout vec3 Albedo, inout vec3 NormalMap, vec2 detailMaskCoords)
{
    float detailMask = texture(g_tDetailMask, detailMaskCoords).x;
    detailMask = g_flDetailBlendFactor * max(detailMask, g_flDetailBlendToFull);

    // MOD2X
    #if (DETAIL_COLOR_MOD2X)

        vec3 DetailTexture = texture(g_tDetail, vDetailTexCoords).rgb * MOD2X_MUL;
        Albedo *= mix(vec3(1.0), DetailTexture, detailMask);

    // OVERLAY
    #elif (DETAIL_COLOR_OVERLAY)

        vec3 DetailTexture = DETAIL_CONST * texture(g_tDetail, vDetailTexCoords).rgb;

        // blend in linear space! this is actually in the code, so we're doing the right thing!
        vec3 linearAlbedo = SrgbLinearToGamma(Albedo);
        vec3 overlayScreen = 1.0 - (1.0 - DetailTexture) * (1.0 - linearAlbedo) * 2.0;
        vec3 overlayMul = DetailTexture * linearAlbedo * 2.0;

        vec3 linearBlendedOverlay = mix(overlayMul, overlayScreen, greaterThanEqual(linearAlbedo, vec3(0.5)));
        vec3 gammaBlendedOverlay = SrgbGammaToLinear(linearBlendedOverlay);

        Albedo = mix(Albedo, gammaBlendedOverlay, detailMask);

    #endif

    // NORMALS
    #if (DETAIL_NORMALS)
        vec3 DetailNormal = DecodeHemiOctahedronNormal(texture(g_tNormalDetail, vDetailTexCoords).xy);
        DetailNormal = mix(vec3(0, 0, 1), DetailNormal, detailMask * g_flDetailNormalStrength);
        // literally i dont even know
        NormalMap = NormalMap * DetailNormal.z + vec3(NormalMap.z * DetailNormal.z * DetailNormal.xy, 0.0);
    #endif
}

#endif

// GLASS
#if (F_GLASS == 1) || defined(glass_vfx_common)

    uniform bool g_bFresnel = true;
    uniform float g_flEdgeColorFalloff = 3.0;
    uniform float g_flEdgeColorMaxOpacity = 0.5;
    uniform float g_flEdgeColorThickness = 0.1;
    uniform vec3 g_vEdgeColor = vec3(0.5, 0.8, 0.5); // SrgbRead(true)
    uniform float g_flRefractScale = 0.1;

    // todo: is this right?
    // todo2: inout mat properties
    vec4 GetGlassMaterial(MaterialProperties_t mat)
    {
        float viewDotNormalInv = saturate(1.0 - (dot(mat.ViewDir, mat.Normal) - g_flEdgeColorThickness));
        float fresnel = saturate(pow(viewDotNormalInv, g_flEdgeColorFalloff)) * g_flEdgeColorMaxOpacity;
        vec4 fresnelColor = vec4(g_vEdgeColor.xyz, g_bFresnel ? fresnel : 0.0);

        return mix(vec4(mat.Albedo, mat.Opacity), fresnelColor, g_flOpacityScale);
    }
#endif

// Cloth Sheen
#if (F_CLOTH_SHADING == 1) && defined(csgo_character_vfx)

    uniform float g_flSheenScale = 0.667;
    uniform vec3 g_flSheenTintColor = vec3(1.0);

    vec3 ApplySheen(float reflectance, vec3 albedo, float clothMask)
    {
        return mix(vec3(reflectance), saturate((SrgbGammaToLinear(g_flSheenTintColor.rgb) * sqrt(albedo)) * g_flSheenScale), clothMask);
    }
#endif

// CS2 Decals
#if (F_DECAL_TEXTURE == 1)

    uniform sampler2D g_tDecal;
    #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
        uniform bool g_bUseSecondaryUvForDecal;
    #endif

    vec3 ApplyDecalTexture(vec3 albedo)
    {
    #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
        vec2 coords = (g_bUseSecondaryUvForDecal || (F_FORCE_UV2 == 1)) ? vTexCoord2.xy : vTexCoordOut.xy;
    #else
        vec2 coords = vTexCoordOut.xy;
    #endif
        vec4 decalTex = texture(g_tDecal, coords);

        return (F_DECAL_BLEND_MODE == 0)
            ? mix(albedo, decalTex.rgb, decalTex.a)
            : albedo * decalTex.rgb;
    }
#endif

// Render modes
#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_BumpMap 0
#define renderMode_Tangents 0
#define renderMode_Normals 0
#define renderMode_BumpNormals 0
#define renderMode_Occlusion 0
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
            return true;
        case renderMode_Color:
            outputColor = vec4(mat.Albedo, 1.0);
            return true;
        case renderMode_BumpMap:
            outputColor = vec4(SrgbGammaToLinear(PackToColor(mat.NormalMap)), 1.0);
            return true;
        case renderMode_Tangents:
            outputColor = vec4(SrgbGammaToLinear(PackToColor(mat.Tangent)), 1.0);
            return true;
        case renderMode_Normals:
            outputColor = vec4(SrgbGammaToLinear(PackToColor(mat.GeometricNormal)), 1.0);
            return true;
        case renderMode_BumpNormals:
            outputColor = vec4(SrgbGammaToLinear(PackToColor(mat.Normal)), 1.0);
            return true;
        case renderMode_Occlusion:
            outputColor.rgb = SrgbGammaToLinear(mat.AmbientOcclusion.xxx);
            return true;
        case renderMode_Roughness:
            {
                #if defined(ANISO_ROUGHNESS)
                    outputColor.rgb = SrgbGammaToLinear(vec3(mat.Roughness.xy, 0.0));
                #else
                    outputColor.rgb = SrgbGammaToLinear(mat.IsometricRoughness.xxx);
                #endif

                return true;
            }
        case renderMode_Metalness:
            outputColor.rgb = SrgbGammaToLinear(mat.Metalness.xxx);
            return true;
        case renderMode_Height:
            outputColor.rgb = SrgbGammaToLinear(mat.Height.xxx);
            return true;
        case renderMode_ExtraParams:
            outputColor.rgb = SrgbGammaToLinear(mat.ExtraParams.rgb);
            return true;
        default:
            return false;
    }
}

bool HandleUVRenderModes(inout vec4 outputColor, MaterialProperties_t mat, sampler2D representativeTexture, vec2 flUVs)
{
    if (g_iRenderMode == renderMode_UvDensity || g_iRenderMode == renderMode_LightmapUvDensity)
    {
        outputColor.rgb = mat.Albedo;
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
        outputColor.rgb = mat.Albedo;

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
