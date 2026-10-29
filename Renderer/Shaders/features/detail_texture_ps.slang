#version 460

#ifndef VRF_DETAIL_TEXTURE_PS_INCLUDE
#define VRF_DETAIL_TEXTURE_PS_INCLUDE

// Detail texture layer over the albedo and normal map, for shaders that have F_DETAIL_TEXTURE.
// The vertex shader writes vDetailTexCoords.

uniform float g_flDetailBlendFactor = 1.0;
uniform float g_flDetailBlendToFull = 0.0;
uniform float g_flDetailNormalStrength = 1.0;
in vec2 vDetailTexCoords;

uniform sampler2D g_tDetailMask;
uniform sampler2D g_tDetail;
uniform sampler2D g_tNormalDetail;

#define MOD2X_MUL 1.9922
#define DETAIL_CONST 0.9961

// F_DETAIL_TEXTURE: 1 = mod2x, 2 = overlay, 3 = normals, 4 = overlay and normals.
// alwaysColorAndNormal applies the overlay and the normals for every F_DETAIL_TEXTURE mode but 0.
// distanceFade fades the detail mask out between 224 and 256 units from the camera.
// overlayGamma22 blends the overlay in pow 1/2.2 space instead of sRGB.
void ApplyDetailTextureWithOptions(inout vec3 Albedo, inout vec3 NormalMap, vec2 detailMaskCoords, bool alwaysColorAndNormal, bool distanceFade, bool overlayGamma22)
{
    if (F_DETAIL_TEXTURE == 0)
    {
        return;
    }

    bool colorMod2x = (F_DETAIL_TEXTURE == 1) && !alwaysColorAndNormal;
    bool colorOverlay = (F_DETAIL_TEXTURE == 2) || (F_DETAIL_TEXTURE == 4) || alwaysColorAndNormal;
    bool normals = (F_DETAIL_TEXTURE == 3) || (F_DETAIL_TEXTURE == 4) || alwaysColorAndNormal;

    float detailMask = texture(g_tDetailMask, detailMaskCoords).x;
    detailMask = g_flDetailBlendFactor * max(detailMask, g_flDetailBlendToFull);

    if (distanceFade)
    {
        float cameraDistance = length(g_vCameraPositionWs - vFragPosition);
        detailMask *= saturate((256.0 - cameraDistance) / 32.0);
    }

    if (colorMod2x)
    {
        vec3 DetailTexture = texture(g_tDetail, vDetailTexCoords).rgb * MOD2X_MUL;
        Albedo *= mix(vec3(1.0), DetailTexture, detailMask);
    }
    else if (colorOverlay)
    {
        vec3 DetailTexture = DETAIL_CONST * texture(g_tDetail, vDetailTexCoords).rgb;

        vec3 linearAlbedo = overlayGamma22 ? pow(Albedo, vec3(1.0 / 2.2)) : SrgbLinearToGamma(Albedo);
        vec3 overlayScreen = 1.0 - (1.0 - DetailTexture) * (1.0 - linearAlbedo) * 2.0;
        vec3 overlayMul = DetailTexture * linearAlbedo * 2.0;

        vec3 linearBlendedOverlay = mix(overlayMul, overlayScreen, greaterThanEqual(linearAlbedo, vec3(0.5)));
        vec3 gammaBlendedOverlay = overlayGamma22 ? pow(linearBlendedOverlay, vec3(2.2)) : SrgbGammaToLinear(linearBlendedOverlay);

        Albedo = mix(Albedo, gammaBlendedOverlay, detailMask);
    }

    if (normals)
    {
        vec3 DetailNormal = DecodeHemiOctahedronNormal(texture(g_tNormalDetail, vDetailTexCoords).xy);
        DetailNormal = mix(vec3(0, 0, 1), DetailNormal, detailMask * g_flDetailNormalStrength);
        NormalMap = NormalMap * DetailNormal.z + vec3(NormalMap.z * DetailNormal.z * DetailNormal.xy, 0.0);
    }
}

void ApplyDetailTexture(inout vec3 Albedo, inout vec3 NormalMap, vec2 detailMaskCoords)
{
    ApplyDetailTextureWithOptions(Albedo, NormalMap, detailMaskCoords, false, false, false);
}

#endif
