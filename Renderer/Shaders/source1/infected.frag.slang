#version 460

#include "infected_features.slang"
#include "common_ps.slang"
#include "common_envmap_ps.slang"
#include "common_model_lighting_ps.slang"

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;

#if (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    in vec4 vBakedLightOut;
#endif

// The variation mask. Blue picks the skin gradient above half and the clothing gradient below it, alpha is the
// index into the gradient and, squared, the grey level. A quarter of the texture holds the other masks, chosen by
// the sheet index.
uniform sampler2D g_tColor; // Sampler(UserConfig)
uniform sampler2D g_tGradient; // SrgbRead(true)
uniform sampler2D g_tDetail; // Sampler(UserConfig)
uniform sampler2D g_tBurnDetail; // SrgbRead(true)

// A material without variation has an ordinary color texture, with the phong mask in its alpha
uniform int F_DISABLE_VARIATION;
uniform int F_BURNING;
uniform int g_nSheetIndex;
uniform float g_flSkinTintGradient;
uniform float g_flColorTintGradient;
uniform vec4 g_vBloodMaskRange = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec3 g_vBloodColor = vec3(1.0, 0.0, 0.0);
uniform float g_flDefaultPhongExponent = 5.0;
uniform float g_flSkinPhongExponent = 5.0;
uniform float g_flBloodPhongExponent = 5.0;
uniform float g_flBurnStrength;
uniform vec4 g_vPhongFresnelRanges = vec4(0.0, 0.5, 1.0, 0.0);
uniform vec3 g_vPhongTint = vec3(1.0);
uniform float g_flPhongBoost = 1.0;

void main()
{
    vec3 normal = GetSource1SurfaceNormal(vNormalOut, vFragPosition);
    vec3 eyeDirection = normalize(g_vCameraPositionWs - vFragPosition);

    vec4 baseTexel = texture(g_tColor, vTexCoordOut);

    vec3 albedo = SrgbGammaToLinear(baseTexel.rgb);
    float specularMask = baseTexel.a;
    float specularExponent = g_flDefaultPhongExponent;

    if (F_DISABLE_VARIATION == 0)
    {
        vec2 sheetCorner = vec2(float(g_nSheetIndex / 2), float(g_nSheetIndex % 2)) * 0.5;
        vec4 maskTexel = texture(g_tColor, vTexCoordOut * 0.5 + sheetCorner);

        float skinWeight = saturate(baseTexel.b * 2.0 - 1.0);
        float clothingWeight = saturate(1.0 - baseTexel.b * 2.0);
        float detailMask = saturate(maskTexel.r * 2.0 - 1.0);
        float bloodValue = saturate(maskTexel.g * 2.0 - 1.0);
        specularMask = saturate(1.0 - maskTexel.r * 2.0);

        float gradientRows = float(textureSize(g_tGradient, 0).y);
        float skinRow = (g_flSkinTintGradient + 0.5) / gradientRows;
        float clothingRow = (g_flColorTintGradient + 0.5) / gradientRows;
        vec3 skinColor = texture(g_tGradient, vec2(baseTexel.a, skinRow)).rgb;
        vec3 clothingColor = texture(g_tGradient, vec2(baseTexel.a, clothingRow)).rgb;

        float grey = baseTexel.a * baseTexel.a;
        albedo = mix(vec3(grey), skinColor, skinWeight);
        albedo = mix(albedo, clothingColor, clothingWeight);

        if (F_DETAIL != 0)
        {
            // The detail coordinates are read transposed
            vec2 detailCoord = vTexCoordOut.yx * g_flDetailScale;
            vec3 detailColor = 2.0 * grey * texture(g_tDetail, detailCoord).rgb;
            albedo = mix(albedo, detailColor, detailMask);
        }

        float bloodRange = max(g_vBloodMaskRange.y - g_vBloodMaskRange.x, 0.0001);
        float bloodFraction = saturate((bloodValue - g_vBloodMaskRange.x) / bloodRange);
        float bloodMask = saturate(bloodFraction * bloodFraction * (3.0 - 2.0 * bloodFraction));

        albedo *= mix(vec3(1.0), g_vBloodColor, bloodMask);

        specularExponent = mix(g_flDefaultPhongExponent, g_flSkinPhongExponent, skinWeight);
        specularExponent = mix(specularExponent, g_flBloodPhongExponent, bloodMask);

        if (F_BURNING != 0)
        {
            float burnScale = g_flBurnStrength * 2.0;
            float burnThreshold = skinWeight * burnScale + 1.0;
            vec4 burnTexel = texture(g_tBurnDetail, vTexCoordOut * g_flDetailScale);
            float burnAmount = saturate(burnTexel.a + burnScale * 2.0 - burnThreshold);
            albedo = mix(albedo, burnTexel.rgb, burnAmount);
        }
    }

    float fresnel = GetSource1FresnelRanges(normal, eyeDirection, g_vPhongFresnelRanges);

    vec3 specularLight = vec3(0.0);
    vec3 rimLight = vec3(0.0);

#if (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    vec3 diffuseLight = GetSource1BakedVertexLight(vBakedLightOut, vFragPosition, normal);
    vec3 unusedDiffuse = vec3(0.0);
    AddSource1SceneLights(unusedDiffuse, specularLight, rimLight, vFragPosition, normal, eyeDirection, specularExponent, fresnel);
#else
    vec3 diffuseLight = GetSource1Ambient(normal);
    AddSource1SceneLights(diffuseLight, specularLight, rimLight, vFragPosition, normal, eyeDirection, specularExponent, fresnel);
    diffuseLight = GetSource1FullbrightFallback(diffuseLight, normal);
#endif

    vec3 color = albedo * vVertexColorOut.rgb * diffuseLight;

    if (F_PHONG != 0)
    {
        color += specularLight * specularMask * fresnel * g_flPhongBoost * g_vPhongTint;
    }

    ApplyFog(color, vFragPosition);

    float alpha = GetSource1Opacity(1.0, vVertexColorOut.a);
    outputColor = vec4(color, ApplySource1AlphaTest(alpha));
}
