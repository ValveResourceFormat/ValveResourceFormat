#version 460
//? #include "LightingConstants.glsl"
//? #include "features.glsl"
//? #include "utils.glsl"

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

float AdjustRoughnessByGeometricNormal( float roughness, vec3 geometricNormal )
{
    float geometricRoughnessFactor = CalculateGeometricRoughnessFactor(geometricNormal);

    return max(roughness, geometricRoughnessFactor);
}

vec2 AdjustRoughnessByGeometricNormal( vec2 roughness, vec3 geometricNormal )
{
    float geometricRoughnessFactor = CalculateGeometricRoughnessFactor(geometricNormal);

    return max(roughness, vec2(geometricRoughnessFactor));
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

#if defined(VEC2_ROUGHNESS)
    vec2 Roughness;
    vec2 RoughnessTex;
#else
    float Roughness;
    float RoughnessTex;
#endif

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

    vec3 AmbientGeometricNormal; // Indirect geometric normal
    vec3 AmbientNormal; // Indirect normal


#if defined(NEED_CURVATURE)
    float Curvature;
#endif

#if (F_ANISOTROPIC_GLOSS == 1)
    vec3 AnisotropicTangent;
    vec3 AnisotropicBitangent;
#endif
    //int NumDynamicLights;
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

#if defined(VEC2_ROUGHNESS)
    mat.Roughness = vec2(0.0);
    mat.RoughnessTex = vec2(0.0);
#else
    mat.Roughness = 0.0;
    mat.RoughnessTex = 0.0;
#endif
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

    mat.AmbientGeometricNormal = vec3(0.0); // Indirect geometric normal
    mat.AmbientNormal = vec3(0.0); // Indirect normal
#if defined(NEED_CURVATURE)// || renderMode_Curvature
    #if F_USE_PER_VERTEX_CURVATURE == 1
        mat.Curvature = flPerVertexCurvature;
    #else
        mat.Curvature = pow( GetCurvature(mat.GeometricNormal, mat.PositionWS), 0.333 );
    #endif
#endif

#if (F_ANISOTROPIC_GLOSS == 1)
    mat.AnisotropicTangent = vec3(0.0);
    mat.AnisotropicBitangent = vec3(0.0);
#endif
    //prop.NumDynamicLights = 0;

    if (F_RENDER_BACKFACES == 1 && F_DONT_FLIP_BACKFACE_NORMALS == 0)
    {
        // when rendering backfaces, they invert normal so it appears front-facing
        mat.GeometricNormal *= gl_FrontFacing ? 1.0 : -1.0;
    }
}


#if defined(vr_skin_vfx) || defined(vr_xen_foliage_vfx)
#define DIFFUSE_AO_COLOR_BLEED
#endif

#if defined(DIFFUSE_AO_COLOR_BLEED)

uniform vec4 g_vAmbientOcclusionColorBleed = vec4(0.4, 0.14902, 0.129412, 0.0);

void SetDiffuseColorBleed(inout MaterialProperties_t mat)
{
    vec3 vAmbientOcclusionExponent = vec3(1.0) - SrgbGammaToLinear(g_vAmbientOcclusionColorBleed.rgb);
#if (F_SSS_MASK == 1)
    vAmbientOcclusionExponent = mix(vec3(1.0), vAmbientOcclusionExponent, mat.SSSMask);
#endif
    mat.DiffuseAO = pow(mat.DiffuseAO, vAmbientOcclusionExponent);
}

#endif

float GetIsoRoughness(float Roughness)
{
    return Roughness;
}

float GetIsoRoughness(vec2 Roughness)
{
    return dot(Roughness, vec2(0.5));
}


//-------------------------------------------------------------------------
//                              NORMALS
//-------------------------------------------------------------------------

// Prevent over-interpolation of vertex normals. Introduced in The Lab renderer
vec3 SwitchCentroidNormal(vec3 vNormalWs, vec3 vCentroidNormalWs)
{
    return ( dot(vNormalWs, vNormalWs) >= 1.01 ) ? vCentroidNormalWs : vNormalWs;
}


// Unpack HemiOct normal map
vec3 DecodeNormal(vec4 bumpNormal)
{
    //Reconstruct the tangent vector from the map
#if (HemiOctIsoRoughness_RG_B == 1)
    vec2 temp = vec2(bumpNormal.x + bumpNormal.y - 1.003922, bumpNormal.x - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#else
    //vec2 temp = vec2(bumpNormal.w, bumpNormal.y) * 2 - 1;
    //vec3 tangentNormal = vec3(temp, sqrt(1 - temp.x * temp.x - temp.y * temp.y));
    vec2 temp = vec2(bumpNormal.w + bumpNormal.y - 1.003922, bumpNormal.w - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#endif

    // This is free, it gets compiled into the TS->WS matrix mul
    tangentNormal.y = -tangentNormal.y;

    return tangentNormal;
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
        vec3 bentNormalTexel = DecodeNormal( texture(g_tBentNormal, texCoords) );
        mat.AmbientGeometricNormal = calculateWorldNormal(bentNormalTexel, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

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
        vec3 DetailNormal = DecodeNormal(texture(g_tNormalDetail, vDetailTexCoords));
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
    uniform vec4 g_vEdgeColor;
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
    uniform vec4 g_flSheenTintColor = vec4(1.0);

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

        if (F_DECAL_BLEND_MODE == 0)
        {
            return mix(albedo, decalTex.rgb, decalTex.a);
        }
        else
        {
            return albedo * decalTex.rgb;
        }
    }
#endif
