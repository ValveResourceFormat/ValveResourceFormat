#version 460

#include "common/utils.slang"
#include "common/features.slang"

// Set for the pass after the translucent layer, which decals the translucent surfaces in front of the opaque scene
#define D_TRANSLUCENT_SCENE_DEPTH 0

#define ENABLE_LPV_BLENDING

// Dual source: the destination becomes outputColor + destination * outputMultiplier, one channel at a time,
// which is the same shape as every decal blend mode, so any mix of them stacks in order in one draw
layout(location = 0, index = 0) out vec4 outputColor;
layout(location = 0, index = 1) out vec4 outputMultiplier;

vec3 vFragPosition;

struct ProjectedDecal_t
{
    // Maps world space into the box, centred on the origin. Each row is a box axis over its squared length.
    mat3x4 WorldToDecal;
    uint MaterialIndex;
    uint Flags;
    uint Tint; // RGBA8, sRGB
    float PlaceTime;
};

struct DecalTexture_t
{
    vec2 Scale; // how much of the layer the decal's part covers
    uint Layer; // the layer in the low 16 bits, then the last mip level the texture has in four
    uint Offset; // where that part starts, in texels: x in the low 16 bits, y in the high
};

const uint DECAL_FLIP_U = 1u << 0;
const uint DECAL_HIDDEN = 1u << 1; // on an entity that is not drawn

struct ProjectedDecalMaterialData_t
{
    DecalTexture_t Color;
    DecalTexture_t Normal;
    DecalTexture_t Occlusion;
    DecalTexture_t Height;
    vec4 Fade;
    vec4 Parallax;
    vec4 Emissive;
    uint Flags;
    float Roughness;
    float _padding0;
    float _padding1;
};

struct ProjectedDecalMaterial_t
{
    DecalTexture_t Color;
    DecalTexture_t Normal;
    DecalTexture_t Occlusion;
    DecalTexture_t Height;
    vec4 Fade; // cutoff bias, cutoff scale, inverse depth fade, inverse alpha cutoff softness
    vec4 Parallax; // height scale, min steps, max steps, LOD threshold
    vec4 Emissive; // rgb tint, a additive amount
    float Roughness;

    bool HasOcclusionMap;
    uint F_BLEND_MODE; // 0 = translucent, 1 = mod2x, 2 = emissive, 3 = liquid
    bool F_ALPHA_MODE;
    bool F_CUTOFF_ANGLE;
    bool F_NORMAL_MAP;
    bool F_PARALLAX;
    bool F_SPECULAR_DIRECT;
    bool F_TRIPLANAR_MAPPING;
    bool F_BLOOD_AGING;
};

bool IsBitSet(uint bits, uint bit)
{
    return (bits & (1u << bit)) != 0u;
}

ProjectedDecalMaterial_t UnpackDecalMaterial(ProjectedDecalMaterialData_t data)
{
    ProjectedDecalMaterial_t material;
    material.Color = data.Color;
    material.Normal = data.Normal;
    material.Occlusion = data.Occlusion;
    material.Height = data.Height;
    material.Fade = data.Fade;
    material.Parallax = data.Parallax;
    material.Emissive = data.Emissive;
    material.Roughness = data.Roughness;

    material.HasOcclusionMap = IsBitSet(data.Flags, 7u);
    material.F_BLEND_MODE = data.Flags & 3u;
    material.F_ALPHA_MODE = IsBitSet(data.Flags, 2u);
    material.F_CUTOFF_ANGLE = IsBitSet(data.Flags, 3u);
    material.F_NORMAL_MAP = IsBitSet(data.Flags, 4u);
    material.F_PARALLAX = IsBitSet(data.Flags, 5u);
    material.F_SPECULAR_DIRECT = IsBitSet(data.Flags, 6u);
    material.F_TRIPLANAR_MAPPING = IsBitSet(data.Flags, 8u);
    material.F_BLOOD_AGING = IsBitSet(data.Flags, 9u);

    return material;
}

layout(std430, binding = 6) readonly buffer ProjectedDecalBuffer
{
    ProjectedDecal_t g_ProjectedDecals[];
};

layout(std430, binding = 7) readonly buffer ProjectedDecalMaterialBuffer
{
    ProjectedDecalMaterialData_t g_ProjectedDecalMaterials[];
};

uniform uint g_nDecalTileBase;
uniform uint g_nDecalBinBase;
uniform uint g_nDecalCullWords;
uniform uint g_nDecalCount;

uniform sampler2DArray g_tDecalColor;
uniform sampler2DArray g_tDecalNormal; // rg = normal, b = roughness
uniform sampler2DArray g_tDecalOcclusion; // r = occlusion, g = metalness
uniform sampler2DArray g_tDecalHeight;

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"
#include "common/depth.slang"

#if (D_TRANSLUCENT_SCENE_DEPTH == 1)
    uniform sampler2D g_tTranslucentSceneDepth; // reserved
#endif

#include "common/fullbright.slang"
#include "common/texturing.slang"
#include "common/fog.slang"
#include "common/light_cull.slang"

#define VRF_OBJECT_DATA_INCLUDE
#include "common/instancing_base.slang"

uniform uint g_nLightProbeIndex;

ObjectDataStandard GetObjectLightingData()
{
    return ObjectDataStandard(g_nLightProbeIndex, 0u, 0u, 0u, uvec4(0u));
}

// Must be last
#include "common/lighting.slang"

float ReadReceiverDepth(ivec2 pixel)
{
#if (D_TRANSLUCENT_SCENE_DEPTH == 1)
    return texelFetch(g_tTranslucentSceneDepth, pixel, 0).x;
#else
    return texelFetch(g_tSceneDepth, pixel, 0).x;
#endif
}

vec3 GetScenePosition(vec2 fragCoord)
{
    ivec2 pixel = clamp(ivec2(fragCoord), ivec2(0), ivec2(g_vViewportSize) - 1);
    return GetWorldPositionFromDepthValue(ReadReceiverDepth(pixel), GetCameraRay(fragCoord));
}

vec2 GetDecalTextureTexels(sampler2DArray textureArray, DecalTexture_t decalTexture)
{
    return decalTexture.Scale * vec2(textureSize(textureArray, 0).xy);
}

vec4 SampleDecalTexture(sampler2DArray textureArray, DecalTexture_t decalTexture, vec2 texCoord, vec2 texCoordDdx, vec2 texCoordDdy)
{
    vec2 layerSize = vec2(textureSize(textureArray, 0).xy);
    vec2 texels = decalTexture.Scale * layerSize;

    vec2 texelsDdx = texCoordDdx * texels;
    vec2 texelsDdy = texCoordDdy * texels;
    float lod = 0.5 * log2(max(max(dot(texelsDdx, texelsDdx), dot(texelsDdy, texelsDdy)), 1e-8));

    // The layer has more mip levels than the texture brought, and what lies past them is not the decal's
    float lastMip = float((decalTexture.Layer >> 16u) & 0xFu);

    if (lod > lastMip)
    {
        float shrink = exp2(lastMip - lod);
        texCoordDdx *= shrink;
        texCoordDdy *= shrink;
        lod = lastMip;
    }

    // Keep the bilinear taps of the coarsest mip sampled inside the decal's part of the layer
    vec2 halfTexel = min(0.5 * exp2(ceil(max(lod, 0.0))) / texels, vec2(0.5));
    texCoord = clamp(texCoord, halfTexel, 1.0 - halfTexel);

    vec2 offset = vec2(decalTexture.Offset & 0xFFFFu, decalTexture.Offset >> 16u) / layerSize;
    vec3 coords = vec3(offset + texCoord * decalTexture.Scale, float(decalTexture.Layer & 0xFFFFu));

    return textureGrad(textureArray, coords, texCoordDdx * decalTexture.Scale, texCoordDdy * decalTexture.Scale);
}

// Blood spreads from its centre as it lands, then dries from its thin edges inward until only the thickest
// of it is left. Fading it out dries it the rest of the way. Returns the opacity, how dry the blood is, and
// where its normal and occlusion are read, which sag toward the ground while it is wet.
float AgeBlood(float age, float fadeOpacity, vec4 color, vec3 decalCoords, vec3 axisX, vec3 axisY, out vec2 agedCoord, out float dry)
{
    age = max(age, 0.0);

    float alpha = color.a;
    bool isAging = age < 10.0 || fadeOpacity < 0.98;

    float thickness = saturate(color.r * alpha) * (1.0 - length(decalCoords - 0.5));
    float sag = thickness * (isAging ? (saturate(sqrt(age * 0.1)) - 0.5) * -60.0 : -60.0);

    vec3 aged = ((decalCoords + vec3(-axisX.z, axisY.z, 0.0) * sag) - 0.5) * (1.0 + sag * 0.02) + 0.5;
    agedCoord = aged.xy;

    if (!isAging)
    {
        dry = 1.0;
        return pow2(saturate((alpha - 0.86) * 6.25));
    }

    float reveal = saturate(age * (1.0 / 0.12) + (pow(alpha, 0.2) - 1.0) * 24.0 - length(vec2(0.5) - aged.xy) * 2.0);
    float revealed = mix(saturate((alpha - 1.0 + reveal) / (reveal + 0.01)), alpha, reveal);

    dry = smoothstep(0.0, 1.0, saturate((age - 2.0) * 0.125 - alpha + 1.0)) * (1.0 + (1.0 - fadeOpacity) * 0.2);

    return pow2(saturate((revealed - saturate(dry * 0.86)) / (1.01 - saturate(dry * 0.85))));
}

vec4 UnpackDecalTint(uint tint)
{
    vec4 color = vec4(uvec4(tint & 0xFFu, (tint >> 8u) & 0xFFu, (tint >> 16u) & 0xFFu, tint >> 24u)) / 255.0;
    return vec4(SrgbGammaToLinear(color.rgb), color.a);
}

// Gradients here come from the depth buffer rather than dFdx, which is undefined inside the decal loop
vec2 ApplyParallax(ProjectedDecalMaterial_t material, vec2 texCoord, vec2 texCoordDdx, vec2 texCoordDdy,
    vec3 positionWs, vec3 geometricNormal, vec3 axisX, vec3 axisY, vec3 axisZ)
{
    // Sampling uses halved gradients, the footprint is measured on the full ones
    vec2 fullDdx = texCoordDdx * 2.0;
    vec2 fullDdy = texCoordDdy * 2.0;

    vec2 textureTexels = GetDecalTextureTexels(g_tDecalColor, material.Color);
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
        sampledHeight = SampleDecalTexture(g_tDecalHeight, material.Height, currentTexCoord, fullDdx, fullDdy).x;

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
    uint tileWord = g_nDecalTileBase + (((tile.y * g_nLightTileCols) + tile.x) * g_nDecalCullWords);
    uint tileBits = 0u;

    for (uint word = 0u; word < g_nDecalCullWords; word++)
    {
        tileBits |= g_CullBits[tileWord + word] & GetCullLiveMask(word, g_nDecalCount);
    }

    if (tileBits == 0u)
    {
        discard;
    }

#if (D_TRANSLUCENT_SCENE_DEPTH == 1)
    // The translucent depth starts out as the opaque one, and the pass before the translucent layer
    // already decaled wherever nothing was drawn nearer
    if (ReadReceiverDepth(ivec2(fragCoord)) <= texelFetch(g_tSceneDepth, ivec2(fragCoord), 0).x)
    {
        discard;
    }
#endif

    vec3 positionWs = GetScenePosition(fragCoord);
    CullMasks_t masks = MakeCullMasks(GetCullLookupCached(positionWs), g_nDecalTileBase, g_nDecalBinBase, g_nDecalCullWords);

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

    // Each layer scales everything under it and adds its own light: the destination by keepDestination,
    // the merged lit surface by litWeight and the emissive light by itself, which keeps them all in order
    vec3 keepDestination = vec3(1.0);
    vec3 litWeight = vec3(0.0);
    vec3 emissiveColor = vec3(0.0);

    for (uint word = 0u; word < g_nDecalCullWords; word++)
    {
        uint bits = GetCullMaskWord(masks, word) & GetCullLiveMask(word, g_nDecalCount);

        while (bits != 0u)
        {
            uint decalIndex = (word * 32u) + uint(findLSB(bits));
            bits &= bits - 1u;

            ProjectedDecal_t decal = g_ProjectedDecals[decalIndex];

            if ((decal.Flags & DECAL_HIDDEN) != 0u)
            {
                continue;
            }

            ProjectedDecalMaterial_t material = UnpackDecalMaterial(g_ProjectedDecalMaterials[decal.MaterialIndex]);

            uint blendMode = material.F_BLEND_MODE;

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


            // A triplanar decal has no single depth axis to fade along, it fades toward every box face instead
            float fade = material.F_TRIPLANAR_MAPPING || material.F_BLOOD_AGING ? 1.0 : saturate(decalCoords.z * material.Fade.z);

            if (material.F_CUTOFF_ANGLE && !material.F_BLOOD_AGING)
            {
                float cutoff = (dot(geometricNormal, normalize(axisZ)) - material.Fade.x) * material.Fade.y;

                if (cutoff < 0.0)
                {
                    continue;
                }

                fade = min(cutoff, 1.0);
            }

            vec2 texCoord = decalCoords.xy;

            if (material.F_PARALLAX)
            {
                texCoord = ApplyParallax(material, texCoord, texCoordDdx, texCoordDdy, positionWs, geometricNormal, axisX, axisY, axisZ);
            }

            if ((decal.Flags & DECAL_FLIP_U) != 0u)
            {
                texCoord.x = 1.0 - texCoord.x;
            }

            vec4 tint = UnpackDecalTint(decal.Tint);
            vec4 color;

            if (material.F_TRIPLANAR_MAPPING)
            {
                vec3 coords = decalCoords;

                if ((decal.Flags & DECAL_FLIP_U) != 0u)
                {
                    coords.xz = 1.0 - coords.xz;
                }

                vec3 coordsDdx = vec3(texCoordDdx, dot(positionDdx, axisZ) * 0.5);
                vec3 coordsDdy = vec3(texCoordDdy, dot(positionDdy, axisZ) * 0.5);

                // Each box axis projects the texture, weighted by how squarely the surface faces it
                vec3 facing = vec3(dot(geometricNormal, normalize(axisX)), dot(geometricNormal, normalize(axisY)), dot(geometricNormal, normalize(axisZ)));
                vec3 weights = facing * facing;

                color = SampleDecalTexture(g_tDecalColor, material.Color, coords.zy, coordsDdx.zy, coordsDdy.zy) * weights.x
                    + SampleDecalTexture(g_tDecalColor, material.Color, vec2(coords.x, decalCoords.z), coordsDdx.xz, coordsDdy.xz) * weights.y
                    + SampleDecalTexture(g_tDecalColor, material.Color, coords.xy, coordsDdx.xy, coordsDdy.xy) * weights.z;

                vec3 toFace = min(decalCoords, 1.0 - decalCoords);
                color.a *= saturate(8.0 * min(toFace.x, min(toFace.y, toFace.z)));
            }
            else
            {
                color = SampleDecalTexture(g_tDecalColor, material.Color, texCoord, texCoordDdx, texCoordDdy);
            }

            float opacity = 0.0;
            float bloodDry = 0.0;

            if (material.F_BLOOD_AGING)
            {
                if (color.a < 1.0 / 255.0)
                {
                    continue;
                }

                vec2 agedCoord;
                opacity = AgeBlood(g_flTime - decal.PlaceTime, tint.a, color, decalCoords, axisX, axisY, agedCoord, bloodDry);

                texCoord = (decal.Flags & DECAL_FLIP_U) != 0u ? vec2(1.0 - agedCoord.x, agedCoord.y) : agedCoord;
            }
            else
            {
                opacity = material.F_ALPHA_MODE
                    ? min((color.a * fade + tint.a - 1.0) * material.Fade.w, 1.0)
                    : tint.a * color.a * fade;
            }

            if (opacity < 1.0 / 255.0)
            {
                continue;
            }

            vec3 albedo = tint.rgb * color.rgb;

            if (blendMode == 1u || blendMode == 3u)
            {
                // Mod2x doubles, so mid grey leaves what is under it unchanged
                vec3 keep = mix(vec3(1.0), blendMode == 1u ? 2.0 * albedo : albedo, opacity);

                keepDestination *= keep;
                litWeight *= keep;
                emissiveColor *= keep;
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

            if (material.F_NORMAL_MAP)
            {
                vec4 normalTexel = SampleDecalTexture(g_tDecalNormal, material.Normal, texCoord, texCoordDdx, texCoordDdy);
                float octX = normalTexel.x + normalTexel.y - (256.0 / 255.0);
                float octY = normalTexel.x - normalTexel.y;
                vec3 normalTs = normalize(vec3(octX, octY, 1.0 - abs(octX) - abs(octY)));

                // The tangent frame follows the decal axes, so the normal map reads the same on whatever it lands on
                vec3 tangent = normalize(cross(geometricNormal, -axisY));
                vec3 bitangent = normalize(cross(tangent, geometricNormal));

                layerNormal = normalize(tangent * normalTs.x - bitangent * normalTs.y + geometricNormal * normalTs.z);
                layerRoughness = normalTexel.b;
            }

            if (material.F_BLOOD_AGING)
            {
                // Thin blood lies flat on the surface, and all of it dulls as it dries
                layerNormal = normalize(mix(geometricNormal, layerNormal, saturate((opacity - 0.75) * 4.0)));
                layerRoughness = mix(layerRoughness, 0.6, bloodDry);
            }

            vec2 occlusionMetalness = material.HasOcclusionMap
                ? SampleDecalTexture(g_tDecalOcclusion, material.Occlusion, texCoord, texCoordDdx, texCoordDdy).xy
                : vec2(1.0, 0.0);

            vec3 layerDiffuse = material.F_SPECULAR_DIRECT ? albedo * (1.0 - occlusionMetalness.y) : albedo;
            vec3 layerSpecular = material.F_SPECULAR_DIRECT ? mix(vec3(0.04), albedo, occlusionMetalness.y) : vec3(0.0);

            float keep = 1.0 - opacity;

            coverage = coverage * keep + opacity;
            diffuseColor = diffuseColor * keep + layerDiffuse * opacity;
            specularColor = specularColor * keep + layerSpecular * opacity;
            specularMask = specularMask * keep + (material.F_SPECULAR_DIRECT ? opacity : 0.0);
            normal = normal * keep + layerNormal * opacity;
            roughness = roughness * keep + layerRoughness * opacity;
            occlusion = occlusion * keep + occlusionMetalness.x * opacity;

            keepDestination *= keep;
            emissiveColor *= keep;
            litWeight = litWeight * keep + opacity;
        }
    }

    if (litWeight == vec3(0.0) && keepDestination == vec3(1.0) && emissiveColor == vec3(0.0))
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

    outputColor = vec4(litColor * litWeight + emissiveColor, 0.0);
    outputMultiplier = vec4(keepDestination, 1.0);
}
