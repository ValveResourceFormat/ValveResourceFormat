#version 460

#include "common/utils.slang"
#include "common/features.slang"

// The pass covers the whole screen, so the probe volume is found per pixel
#define ENABLE_LPV_BLENDING

layout(location = 0) out vec4 outputColor;

// Not a varying: the shared lighting code reads the surface position from here, and this pass only
// learns it from the depth buffer
vec3 vFragPosition;

struct ProjectedDecal_t
{
    // Maps world space into the box, centred on the origin. Each row is a box axis over its squared length.
    mat3x4 WorldToDecal;
    uint MaterialIndex;
    uint Flags;
    uint Tint; // RGBA8, linear
    uint _padding;
};

struct ProjectedDecalMaterial_t
{
    // Smaller textures sit in the corner of an array layer sized for the largest one:
    // xy scale into their part of it, z is the layer
    vec4 ColorScaleLayer;
    vec4 NormalScaleLayer;
    vec4 OcclusionScaleLayer;
    vec4 HeightScaleLayer;
    vec4 Fade; // cutoff bias, cutoff scale, inverse depth fade, inverse alpha cutoff softness
    vec4 Parallax; // height scale, min steps, max steps, LOD threshold
    vec4 Emissive; // rgb tint, a additive amount
    uint Flags;
    float Roughness;
    float _padding0;
    float _padding1;
};

const uint DECAL_FLIP_U = 1u;
const uint DECAL_HIDDEN = 2u; // on an entity that is not drawn

const uint DECAL_BLEND_MODE_MASK = 3u; // 0 = translucent, 1 = mod2x, 2 = emissive, 3 = liquid
const uint DECAL_ALPHA_CUTOFF = 4u;
const uint DECAL_CUTOFF_ANGLE = 8u;
const uint DECAL_NORMAL_MAP = 16u;
const uint DECAL_PARALLAX = 32u;
const uint DECAL_SPECULAR = 64u;
const uint DECAL_OCCLUSION_MAP = 128u;

layout(std430, binding = 15) readonly buffer ProjectedDecalBuffer
{
    ProjectedDecal_t g_ProjectedDecals[];
};

layout(std430, binding = 16) readonly buffer ProjectedDecalMaterialBuffer
{
    ProjectedDecalMaterial_t g_ProjectedDecalMaterials[];
};

uniform uint uDecalTileBase;
uniform uint uDecalBinBase;
uniform uint uDecalCullWords;
uniform uint uDecalCount;

// 0 draws lit and emissive decals, 1 the multiplying ones, which need a blend of their own
uniform uint uDecalPass;

uniform sampler2DArray uDecalColor;
uniform sampler2DArray uDecalNormal; // rg = normal, b = roughness
uniform sampler2DArray uDecalOcclusion; // r = occlusion, g = metalness
uniform sampler2DArray uDecalHeight;

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"
#include "common/depth.slang"

#include "common/fullbright.slang"
#include "common/texturing.slang"
#include "common/fog.slang"
#include "common/light_cull.slang"

// Must be last
#include "common/lighting.slang"

// The world position the resolved scene depth holds under a pixel
vec3 GetScenePosition(vec2 fragCoord)
{
    vec2 ndc = fragCoord * g_vInvViewportSize * 2.0 - 1.0;
    vec4 viewRay = g_matProjectionToWorld * vec4(ndc, 1.0, 1.0);
    vec3 cameraRay = transpose(mat3(g_matWorldToView)) * (viewRay.xyz / viewRay.w);

    ivec2 pixel = clamp(ivec2(fragCoord), ivec2(0), ivec2(g_vViewportSize) - 1);
    return GetWorldPositionFromDepth(pixel, cameraRay);
}

vec4 SampleDecalTexture(sampler2DArray decalTexture, vec4 scaleLayer, vec2 texCoord, vec2 texCoordDdx, vec2 texCoordDdy)
{
    // Keep bilinear taps off the unused part of the layer
    vec2 halfTexel = 0.5 / (scaleLayer.xy * vec2(textureSize(decalTexture, 0).xy));
    texCoord = clamp(texCoord, halfTexel, 1.0 - halfTexel);

    return textureGrad(decalTexture, vec3(texCoord * scaleLayer.xy, scaleLayer.z), texCoordDdx * scaleLayer.xy, texCoordDdy * scaleLayer.xy);
}

vec4 UnpackDecalTint(uint tint)
{
    return vec4(uvec4(tint & 0xFFu, (tint >> 8u) & 0xFFu, (tint >> 16u) & 0xFFu, tint >> 24u)) / 255.0;
}

// Gradients here come from the depth buffer rather than dFdx, which is undefined inside the decal loop
vec2 ApplyParallax(ProjectedDecalMaterial_t material, vec2 texCoord, vec2 texCoordDdx, vec2 texCoordDdy,
    vec3 positionWs, vec3 geometricNormal, vec3 axisX, vec3 axisY, vec3 axisZ)
{
    // Sampling uses halved gradients, the footprint is measured on the full ones
    vec2 fullDdx = texCoordDdx * 2.0;
    vec2 fullDdy = texCoordDdy * 2.0;

    vec2 textureTexels = material.ColorScaleLayer.xy * vec2(textureSize(uDecalColor, 0).xy);
    vec2 texelsDdx = fullDdx * textureTexels;
    vec2 texelsDdy = fullDdy * textureTexels;
    vec2 texelFootprint = texelsDdx * texelsDdx + texelsDdy * texelsDdy;
    float lod = max(0.5 * log2(max(texelFootprint.x, texelFootprint.y)), 0.0);

    float lodThreshold = material.Parallax.w;

    if (lod > lodThreshold)
    {
        return texCoord;
    }

    vec3 cameraToPosition = positionWs - g_vCameraPositionWs;
    float depthAlongDecal = dot(cameraToPosition, -axisZ);

    // Fewer steps where the view meets the surface head on
    float facing = saturate(dot(-g_vCameraDirWs, geometricNormal));
    int stepCount = max(int(mix(material.Parallax.z, material.Parallax.y, facing)), 1);
    float stepSize = 1.0 / float(stepCount);

    vec2 parallaxRange = vec2(dot(cameraToPosition, -axisX), dot(cameraToPosition, axisY)) / depthAlongDecal * material.Parallax.x;
    vec2 parallaxStep = parallaxRange * stepSize;

    vec2 currentTexCoord = texCoord;
    float layerHeight = 1.0;
    float previousHeight = 1.0;
    float sampledHeight = 0.0;

    for (int i = 0; i < stepCount; i++)
    {
        sampledHeight = SampleDecalTexture(uDecalHeight, material.HeightScaleLayer, currentTexCoord, fullDdx, fullDdy).x;

        if (sampledHeight > layerHeight)
        {
            break;
        }

        previousHeight = sampledHeight;
        currentTexCoord -= parallaxStep;
        layerHeight -= stepSize;
    }

    // Intersect the height profile between the last two samples
    float afterHit = layerHeight - sampledHeight;
    float layerBefore = layerHeight + stepSize;
    float beforeHit = layerBefore - previousHeight;
    float denominator = beforeHit - afterHit;
    float hitHeight = denominator == 0.0 ? 0.0 : (layerHeight * beforeHit - layerBefore * afterHit) / denominator;

    vec2 parallaxTexCoord = texCoord - parallaxRange * (1.0 - hitHeight);

    // Fade the offset out over the last mip before it is skipped
    if (lod > lodThreshold - 1.0)
    {
        parallaxTexCoord = mix(parallaxTexCoord, texCoord, fract(lod));
    }

    return parallaxTexCoord;
}

void main()
{
    vec2 fragCoord = gl_FragCoord.xy;

    // A tile no decal reaches ends here, before the depth is read
    uvec2 tile = min(uvec2(fragCoord) >> uvec2(g_nLightTileShift), uvec2(g_nLightTileCols, g_nLightTileRows) - 1u);
    uint tileWord = uDecalTileBase + (((tile.y * g_nLightTileCols) + tile.x) * uDecalCullWords);
    uint tileBits = 0u;

    for (uint word = 0u; word < uDecalCullWords; word++)
    {
        tileBits |= g_CullBits[tileWord + word] & GetCullLiveMask(word, uDecalCount);
    }

    if (tileBits == 0u)
    {
        discard;
    }

    vec3 positionWs = GetScenePosition(fragCoord);
    CullMasks_t masks = MakeCullMasks(GetCullLookupCached(positionWs), uDecalTileBase, uDecalBinBase, uDecalCullWords);

    // Position derivatives from whichever neighbour lies on the same surface, only once a decal needs them
    bool hasSurface = false;
    vec3 positionDdx = vec3(0.0);
    vec3 positionDdy = vec3(0.0);
    vec3 geometricNormal = vec3(0.0, 0.0, 1.0);

    // Translucent decals merge into one surface that is lit once, each value weighted by the coverage
    // left above it, as layering them one by one would
    float coverage = 0.0;
    vec3 diffuseColor = vec3(0.0);
    vec3 specularColor = vec3(0.0);
    float specularMask = 0.0;
    vec3 normal = vec3(0.0);
    float roughness = 0.0;
    float occlusion = 0.0;

    // The output is added to the destination scaled by keepDestination, with the merged surface
    // weighted by litWeight: that keeps emissive decals in order around the translucent ones
    float keepDestination = 1.0;
    float litWeight = 0.0;
    vec3 emissiveColor = vec3(0.0);

    vec3 multiplier = vec3(1.0);

    for (uint word = 0u; word < uDecalCullWords; word++)
    {
        uint bits = GetCullMaskWord(masks, word) & GetCullLiveMask(word, uDecalCount);

        while (bits != 0u)
        {
            uint decalIndex = (word * 32u) + uint(findLSB(bits));
            bits &= bits - 1u;

            ProjectedDecal_t decal = g_ProjectedDecals[decalIndex];

            if ((decal.Flags & DECAL_HIDDEN) != 0u)
            {
                continue;
            }

            ProjectedDecalMaterial_t material = g_ProjectedDecalMaterials[decal.MaterialIndex];

            uint blendMode = material.Flags & DECAL_BLEND_MODE_MASK;
            bool isMultiply = blendMode == 1u || blendMode == 3u;

            if (isMultiply != (uDecalPass == 1u))
            {
                continue;
            }

            vec3 boxPosition = (vec4(positionWs, 1.0) * decal.WorldToDecal) + 0.5;
            vec3 decalCoords = vec3(boxPosition.x, 1.0 - boxPosition.y, boxPosition.z);

            if (any(lessThan(decalCoords, vec3(0.0))) || any(greaterThan(decalCoords, vec3(1.0))))
            {
                continue;
            }

            if (!hasSurface)
            {
                vec3 toLeft = positionWs - GetScenePosition(fragCoord - vec2(1.0, 0.0));
                vec3 toRight = GetScenePosition(fragCoord + vec2(1.0, 0.0)) - positionWs;
                vec3 toBelow = positionWs - GetScenePosition(fragCoord - vec2(0.0, 1.0));
                vec3 toAbove = GetScenePosition(fragCoord + vec2(0.0, 1.0)) - positionWs;

                positionDdx = dot(toLeft, toLeft) < dot(toRight, toRight) ? toLeft : toRight;
                positionDdy = dot(toBelow, toBelow) < dot(toAbove, toAbove) ? toBelow : toAbove;
                geometricNormal = normalize(cross(positionDdx, positionDdy));

                hasSurface = true;
            }

            vec3 axisX = decal.WorldToDecal[0].xyz;
            vec3 axisY = decal.WorldToDecal[1].xyz;
            vec3 axisZ = decal.WorldToDecal[2].xyz;

            vec2 texCoordDdx = vec2(dot(positionDdx, axisX), dot(positionDdx, -axisY)) * 0.5;
            vec2 texCoordDdy = vec2(dot(positionDdy, axisX), dot(positionDdy, -axisY)) * 0.5;

            float fade = saturate(decalCoords.z * material.Fade.z);

            if ((material.Flags & DECAL_CUTOFF_ANGLE) != 0u)
            {
                float cutoff = (dot(geometricNormal, normalize(axisZ)) - material.Fade.x) * material.Fade.y;

                if (cutoff < 0.0)
                {
                    continue;
                }

                // Fading by angle replaces fading by depth
                fade = min(cutoff, 1.0);
            }

            vec2 texCoord = decalCoords.xy;

            if ((material.Flags & DECAL_PARALLAX) != 0u)
            {
                texCoord = ApplyParallax(material, texCoord, texCoordDdx, texCoordDdy, positionWs, geometricNormal, axisX, axisY, axisZ);
            }

            if ((decal.Flags & DECAL_FLIP_U) != 0u)
            {
                texCoord.x = 1.0 - texCoord.x;
            }

            vec4 tint = UnpackDecalTint(decal.Tint);
            vec4 color = SampleDecalTexture(uDecalColor, material.ColorScaleLayer, texCoord, texCoordDdx, texCoordDdy);

            float opacity = (material.Flags & DECAL_ALPHA_CUTOFF) != 0u
                ? min((color.a * fade + tint.a - 1.0) * material.Fade.w, 1.0)
                : tint.a * color.a * fade;

            if (opacity < 1.0 / 255.0)
            {
                continue;
            }

            vec3 albedo = tint.rgb * color.rgb;

            if (blendMode == 1u)
            {
                multiplier *= mix(vec3(1.0), 2.0 * albedo, opacity);
                continue;
            }

            if (blendMode == 3u)
            {
                multiplier *= albedo * opacity + vec3(1.0 - opacity);
                continue;
            }

            if (blendMode == 2u)
            {
                float keep = mix(1.0 - opacity, 1.0, material.Emissive.a);

                keepDestination *= keep;
                litWeight *= keep;
                emissiveColor = emissiveColor * keep + material.Emissive.rgb * albedo * opacity;
                continue;
            }

            vec3 layerNormal = geometricNormal;
            float layerRoughness = material.Roughness;

            if ((material.Flags & DECAL_NORMAL_MAP) != 0u)
            {
                vec4 normalTexel = SampleDecalTexture(uDecalNormal, material.NormalScaleLayer, texCoord, texCoordDdx, texCoordDdy);
                float octX = normalTexel.x + normalTexel.y - (256.0 / 255.0);
                float octY = normalTexel.x - normalTexel.y;
                vec3 normalTs = normalize(vec3(octX, octY, 1.0 - abs(octX) - abs(octY)));

                // The tangent frame follows the decal axes, so the normal map reads the same on whatever it lands on
                vec3 tangent = normalize(cross(geometricNormal, -axisY));
                vec3 bitangent = normalize(cross(tangent, geometricNormal));

                layerNormal = normalize(tangent * normalTs.x - bitangent * normalTs.y + geometricNormal * normalTs.z);
                layerRoughness = normalTexel.b;
            }

            vec2 occlusionMetalness = (material.Flags & DECAL_OCCLUSION_MAP) != 0u
                ? SampleDecalTexture(uDecalOcclusion, material.OcclusionScaleLayer, texCoord, texCoordDdx, texCoordDdy).xy
                : vec2(1.0, 0.0);

            bool isSpecular = (material.Flags & DECAL_SPECULAR) != 0u;
            vec3 layerDiffuse = isSpecular ? albedo * (1.0 - occlusionMetalness.y) : albedo;
            vec3 layerSpecular = isSpecular ? mix(vec3(0.04), albedo, occlusionMetalness.y) : vec3(0.0);

            float keep = 1.0 - opacity;

            coverage = coverage * keep + opacity;
            diffuseColor = diffuseColor * keep + layerDiffuse * opacity;
            specularColor = specularColor * keep + layerSpecular * opacity;
            specularMask = specularMask * keep + (isSpecular ? opacity : 0.0);
            normal = normal * keep + layerNormal * opacity;
            roughness = roughness * keep + layerRoughness * opacity;
            occlusion = occlusion * keep + occlusionMetalness.x * opacity;

            keepDestination *= keep;
            emissiveColor *= keep;
            litWeight = litWeight * keep + opacity;
        }
    }

    if (uDecalPass == 1u)
    {
        if (multiplier == vec3(1.0))
        {
            discard;
        }

        outputColor = vec4(multiplier, 1.0);
        return;
    }

    if (litWeight <= 0.0 && keepDestination >= 1.0 && emissiveColor == vec3(0.0))
    {
        discard;
    }

    vec3 litColor = vec3(0.0);

    if (coverage > 0.0)
    {
        vFragPosition = positionWs;

        vec3 tangent = normalize(cross(geometricNormal, abs(geometricNormal.z) < 0.9 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0)));

        MaterialProperties_t mat;
        InitProperties(mat, geometricNormal, positionWs, tangent, cross(geometricNormal, tangent));

        float inverseCoverage = 1.0 / coverage;

        mat.DiffuseColor = diffuseColor * inverseCoverage;
        mat.Albedo = mat.DiffuseColor;
        mat.SpecularColor = specularColor * inverseCoverage;
        mat.Normal = normalize(normal);
        mat.AmbientNormal = mat.Normal;
        mat.AmbientGeometricNormal = mat.GeometricNormal;
        mat.RoughnessTex = vec2(roughness * inverseCoverage);
        mat.Roughness = mat.RoughnessTex;
        mat.AmbientOcclusion = occlusion * inverseCoverage;
        mat.DiffuseAO = vec3(mat.AmbientOcclusion);
        mat.SpecularAO = mat.AmbientOcclusion;
        mat.Opacity = coverage;

        LightingTerms_t lighting = CalculateLighting(mat);

        // Occlusion darkens the indirect light and the specular, not the direct diffuse
        litColor = mat.DiffuseColor * (lighting.DiffuseDirect + lighting.DiffuseIndirect * mat.AmbientOcclusion)
            + lighting.SpecularDirect * mat.AmbientOcclusion * (specularMask * inverseCoverage);

        ApplyFog(litColor, positionWs);
    }

    outputColor = vec4(litColor * litWeight + emissiveColor, keepDestination);
}
