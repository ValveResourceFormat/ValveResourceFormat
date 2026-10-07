#version 460

#include "common/utils.slang"
#include "common/features.slang"

// Set for the pass after the translucent layer, which decals the translucent surfaces in front of the opaque scene
#define D_TRANSLUCENT_SCENE_DEPTH 0

// The pass covers the whole screen, so the probe volume is found per pixel
#define ENABLE_LPV_BLENDING

// Dual source: the destination becomes outputColor + destination * outputMultiplier, one channel at a time,
// which is the same shape as every decal blend mode, so any mix of them stacks in order in one draw
layout(location = 0, index = 0) out vec4 outputColor;
layout(location = 0, index = 1) out vec4 outputMultiplier;

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
    float PlaceTime; // g_flTime when the decal was added
};

// Where a decal finds one of its textures. A texture sits in the corner of an array layer sized for the
// largest one, and the decal may use only a part of it, as when its texture is an atlas of many decals.
struct DecalTexture_t
{
    vec2 Scale; // how much of the layer the decal's part covers
    uint Layer; // the layer in the low 16 bits, then the last mip level the texture has in four
    uint Offset; // where that part starts, in texels: x in the low 16 bits, y in the high
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
    uint Flags;
    float Roughness;
    float _padding0;
    float _padding1;
};

const uint DECAL_FLIP_U = 1u << 0;
const uint DECAL_HIDDEN = 1u << 1; // on an entity that is not drawn

const uint DECAL_BLEND_MODE_MASK = 3u; // 0 = translucent, 1 = mod2x, 2 = emissive, 3 = liquid
const uint DECAL_ALPHA_CUTOFF = 1u << 2;
const uint DECAL_CUTOFF_ANGLE = 1u << 3;
const uint DECAL_NORMAL_MAP = 1u << 4;
const uint DECAL_PARALLAX = 1u << 5;
const uint DECAL_SPECULAR = 1u << 6;
const uint DECAL_OCCLUSION_MAP = 1u << 7;
const uint DECAL_TRIPLANAR = 1u << 8;
const uint DECAL_BLOOD_AGING = 1u << 9;

layout(std430, binding = 6) readonly buffer ProjectedDecalBuffer
{
    ProjectedDecal_t g_ProjectedDecals[];
};

layout(std430, binding = 7) readonly buffer ProjectedDecalMaterialBuffer
{
    ProjectedDecalMaterial_t g_ProjectedDecalMaterials[];
};

uniform uint uDecalTileBase;
uniform uint uDecalBinBase;
uniform uint uDecalCullWords;
uniform uint uDecalCount;

uniform sampler2DArray uDecalColor;
uniform sampler2DArray uDecalNormal; // rg = normal, b = roughness
uniform sampler2DArray uDecalOcclusion; // r = occlusion, g = metalness
uniform sampler2DArray uDecalHeight;

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

// The pass belongs to no scene node, so it answers for the object data the lighting code would look up.
// Only pixels no probe volume contains read the volume from here.
#define VRF_OBJECT_DATA_INCLUDE
#include "common/instancing_base.slang"

uniform uint uLightProbeIndex = 0u;

ObjectDataStandard GetObjectLightingData()
{
    return ObjectDataStandard(uLightProbeIndex, 0u, 0u, 0u, uvec4(0u));
}

// Must be last
#include "common/lighting.slang"

// The nearest surface this pass decals under a pixel
float ReadReceiverDepth(ivec2 pixel)
{
#if (D_TRANSLUCENT_SCENE_DEPTH == 1)
    return texelFetch(g_tTranslucentSceneDepth, pixel, 0).x;
#else
    return texelFetch(g_tSceneDepth, pixel, 0).x;
#endif
}

// The world position of that surface
vec3 GetScenePosition(vec2 fragCoord)
{
    vec2 ndc = fragCoord * g_vInvViewportSize * 2.0 - 1.0;
    vec4 viewRay = g_matProjectionToWorld * vec4(ndc, 1.0, 1.0);
    vec3 cameraRay = transpose(mat3(g_matWorldToView)) * (viewRay.xyz / viewRay.w);

    ivec2 pixel = clamp(ivec2(fragCoord), ivec2(0), ivec2(g_vViewportSize) - 1);
    return GetWorldPositionFromDepthValue(ReadReceiverDepth(pixel), cameraRay);
}

// The size in texels of the part of a texture a decal uses
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
    return vec4(uvec4(tint & 0xFFu, (tint >> 8u) & 0xFFu, (tint >> 16u) & 0xFFu, tint >> 24u)) / 255.0;
}

// Gradients here come from the depth buffer rather than dFdx, which is undefined inside the decal loop
vec2 ApplyParallax(ProjectedDecalMaterial_t material, vec2 texCoord, vec2 texCoordDdx, vec2 texCoordDdy,
    vec3 positionWs, vec3 geometricNormal, vec3 axisX, vec3 axisY, vec3 axisZ)
{
    // Sampling uses halved gradients, the footprint is measured on the full ones
    vec2 fullDdx = texCoordDdx * 2.0;
    vec2 fullDdy = texCoordDdy * 2.0;

    vec2 textureTexels = GetDecalTextureTexels(uDecalColor, material.Color);
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
        sampledHeight = SampleDecalTexture(uDecalHeight, material.Height, currentTexCoord, fullDdx, fullDdy).x;

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

#if (D_TRANSLUCENT_SCENE_DEPTH == 1)
    // The translucent depth starts out as the opaque one, and the pass before the translucent layer
    // already decaled wherever nothing was drawn nearer
    if (ReadReceiverDepth(ivec2(fragCoord)) <= texelFetch(g_tSceneDepth, ivec2(fragCoord), 0).x)
    {
        discard;
    }
#endif

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

    // Each layer scales everything under it and adds its own light: the destination by keepDestination,
    // the merged lit surface by litWeight and the emissive light by itself, which keeps them all in order
    vec3 keepDestination = vec3(1.0);
    vec3 litWeight = vec3(0.0);
    vec3 emissiveColor = vec3(0.0);

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

            bool isTriplanar = (material.Flags & DECAL_TRIPLANAR) != 0u;
            bool isBloodAging = (material.Flags & DECAL_BLOOD_AGING) != 0u;

            // A triplanar decal has no single depth axis to fade along, it fades toward every box face instead
            // Aging blood fades by drying, along neither the depth nor the angle
            float fade = isTriplanar || isBloodAging ? 1.0 : saturate(decalCoords.z * material.Fade.z);

            if ((material.Flags & DECAL_CUTOFF_ANGLE) != 0u && !isBloodAging)
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
            vec4 color;

            if (isTriplanar)
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

                color = SampleDecalTexture(uDecalColor, material.Color, coords.zy, coordsDdx.zy, coordsDdy.zy) * weights.x
                    + SampleDecalTexture(uDecalColor, material.Color, vec2(coords.x, decalCoords.z), coordsDdx.xz, coordsDdy.xz) * weights.y
                    + SampleDecalTexture(uDecalColor, material.Color, coords.xy, coordsDdx.xy, coordsDdy.xy) * weights.z;

                vec3 toFace = min(decalCoords, 1.0 - decalCoords);
                color.a *= saturate(8.0 * min(toFace.x, min(toFace.y, toFace.z)));
            }
            else
            {
                color = SampleDecalTexture(uDecalColor, material.Color, texCoord, texCoordDdx, texCoordDdy);
            }

            float opacity = 0.0;
            float bloodDry = 0.0;

            if (isBloodAging)
            {
                if (color.a < 1.0 / 255.0)
                {
                    continue;
                }

                vec2 agedCoord;
                opacity = AgeBlood(g_flTime - decal.PlaceTime, tint.a, color, decalCoords, axisX, axisY, agedCoord, bloodDry);

                // Only the color stays where it landed
                texCoord = (decal.Flags & DECAL_FLIP_U) != 0u ? vec2(1.0 - agedCoord.x, agedCoord.y) : agedCoord;
            }
            else
            {
                opacity = (material.Flags & DECAL_ALPHA_CUTOFF) != 0u
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

            if ((material.Flags & DECAL_NORMAL_MAP) != 0u)
            {
                vec4 normalTexel = SampleDecalTexture(uDecalNormal, material.Normal, texCoord, texCoordDdx, texCoordDdy);
                float octX = normalTexel.x + normalTexel.y - (256.0 / 255.0);
                float octY = normalTexel.x - normalTexel.y;
                vec3 normalTs = normalize(vec3(octX, octY, 1.0 - abs(octX) - abs(octY)));

                // The tangent frame follows the decal axes, so the normal map reads the same on whatever it lands on
                vec3 tangent = normalize(cross(geometricNormal, -axisY));
                vec3 bitangent = normalize(cross(tangent, geometricNormal));

                layerNormal = normalize(tangent * normalTs.x - bitangent * normalTs.y + geometricNormal * normalTs.z);
                layerRoughness = normalTexel.b;
            }

            if (isBloodAging)
            {
                // Thin blood lies flat on the surface, and all of it dulls as it dries
                layerNormal = normalize(mix(geometricNormal, layerNormal, saturate((opacity - 0.75) * 4.0)));
                layerRoughness = mix(layerRoughness, 0.6, bloodDry);
            }

            vec2 occlusionMetalness = (material.Flags & DECAL_OCCLUSION_MAP) != 0u
                ? SampleDecalTexture(uDecalOcclusion, material.Occlusion, texCoord, texCoordDdx, texCoordDdy).xy
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
