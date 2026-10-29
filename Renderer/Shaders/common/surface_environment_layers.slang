#version 460

#ifndef VRF_SURFACE_ENVIRONMENT_LAYERS_INCLUDE
#define VRF_SURFACE_ENVIRONMENT_LAYERS_INCLUDE

// Layered environment materials, where each layer is a color, a normal and roughness, and a packed texture.
// Include after surface_base_ps.slang.

#include "citadel.slang"
#include "planar_projection.slang"

// Every UV here is driven by a (scale, rotation in degrees, translate u, translate v) vector.
vec2 ApplyUvTransform(vec2 texCoord, vec4 scaleRotationTranslation)
{
    float scale = scaleRotationTranslation.x;
    vec2 offset = texCoord + scaleRotationTranslation.zw / ((scale == 0.0) ? 1.0 : scale);
    return scale * RotateVector2D(offset, scaleRotationTranslation.y);
}

// A layer's UVs and the tangent frame its normal map is resolved against. Layers on the interpolated UVs use
// the mesh frame; projected ones have to derive theirs from screen derivatives.
struct LayerMapping_t
{
    vec2 TexCoord;
    vec3 Tangent;
    vec3 Bitangent;
};

LayerMapping_t GetMeshMapping(vec2 texCoord)
{
    LayerMapping_t mapping;
    mapping.TexCoord = texCoord;
    mapping.Tangent = vTangentOut.xyz;
    mapping.Bitangent = GetBitangent(vNormalOut, vTangentOut);
    return mapping;
}

// Mikkelsen's screen space tangent frame, for UVs that aren't the mesh's own.
LayerMapping_t GetDerivedMapping(vec2 texCoord, vec3 normal)
{
    vec3 positionDdx = dFdxFine(vFragPosition);
    vec3 positionDdy = dFdyFine(vFragPosition);

    vec3 sigmaX = positionDdx - normal * dot(positionDdx, normal);
    vec3 sigmaY = positionDdy - normal * dot(positionDdy, normal);
    float handedness = (dot(positionDdy, cross(normal, positionDdx)) < 0.0) ? -1.0 : 1.0;

    vec2 texCoordDdx = dFdxFine(texCoord);
    vec2 texCoordDdy = dFdyFine(texCoord);

    float area = dot(texCoordDdx, vec2(texCoordDdy.y, -texCoordDdy.x));
    float winding = (area < 0.0) ? -1.0 : 1.0;

    vec2 projection = vec2(texCoordDdy.y, -texCoordDdx.y) * winding;
    vec3 tangent = sigmaX * projection.x + sigmaY * projection.y;

    if (abs(area) > 0.0)
    {
        tangent = normalize(tangent);
    }

    LayerMapping_t mapping;
    mapping.TexCoord = texCoord;
    mapping.Tangent = tangent;
    mapping.Bitangent = cross(normal, tangent) * (winding * handedness);
    return mapping;
}

// Layers only ever sample the dominant plane. When the median plane wins outright, its
// derivatives are swapped in but the dominant plane's coordinate is kept, so there is only
// ever one set of texels to fetch.
LayerMapping_t GetBiPlanarMapping(vec3 normal, float tiling)
{
    PlanarAxes_t axes = GetPlanarAxes(normal);
    vec2 texCoord = GetPlanarTexCoord(vFragPosition, axes.Major) / ((tiling == 0.0) ? 1.0 : tiling);
    return GetDerivedMapping(texCoord, normal);
}

// "0=Unmasked, 1=Masked, 2=Inverted"
float GetOverlayTintMask(int mode, float tintMask)
{
    return (mode == 0) ? 1.0 : ((mode == 2) ? (1.0 - tintMask) : tintMask);
}

struct Layer_t
{
    vec3 Albedo;
    vec3 NormalOffset; // world space surface gradient, subtracted from the geometric normal
    float Roughness;
    float Metalness;
    float TintMask;
    float SelfIllum;
};

// Hemi-octahedron normal as a surface gradient, so layers and overlays sum before one normalize.
vec2 GetSurfaceGradient(vec2 hemiOct)
{
    vec3 normalTs = DecodeHemiOctahedronNormal(hemiOct);
    vec3 absNormal = abs(normalTs);
    return -normalTs.xy / max(absNormal.z, 0.0078125 * max(absNormal.x, absNormal.y));
}

float ApplyContrastBrightness(float value, vec2 contrastBrightness)
{
    return saturate(((value - 0.5) + contrastBrightness.y) * contrastBrightness.x + 0.5);
}

Layer_t SampleLayer(
    sampler2D colorTexture, sampler2D normalRoughnessTexture, sampler2D packedTexture,
    LayerMapping_t mapping,
    mat4 albedoColorCorrect, mat4 colorTint,
    vec2 roughnessContrastBrightness, vec2 tintMaskContrastBrightness, vec2 metalnessContrastBrightness,
    float normalStrength, float selfIllumScale,
    bool maskColorTint, bool maskVertexColorTint, bool modelTint, float vertexColorStrength)
{
    vec4 color = texture(colorTexture, mapping.TexCoord);
    vec4 normalRoughness = texture(normalRoughnessTexture, mapping.TexCoord);
    vec4 packedTexel = texture(packedTexture, mapping.TexCoord);

    Layer_t layer;
    layer.TintMask = ApplyContrastBrightness(color.a, tintMaskContrastBrightness);
    layer.Roughness = ApplyContrastBrightness(normalRoughness.b, roughnessContrastBrightness);
    layer.Metalness = ApplyContrastBrightness(packedTexel.g, metalnessContrastBrightness);
    layer.SelfIllum = packedTexel.r * selfIllumScale;

    vec2 gradient = GetSurfaceGradient(normalRoughness.rg) * normalStrength;
    layer.NormalOffset = mapping.Tangent * gradient.x + mapping.Bitangent * gradient.y;

    vec3 corrected = (vec4(color.rgb, 1.0) * albedoColorCorrect).rgb;
    vec3 tinted = (vec4(corrected, 1.0) * colorTint).rgb;
    vec3 albedo = mix(corrected, tinted, maskColorTint ? layer.TintMask : 1.0);

    float vertexColorAmount = modelTint
        ? ((maskVertexColorTint ? layer.TintMask : 1.0) * vertexColorStrength)
        : 0.0;
    layer.Albedo = mix(albedo, albedo * vVertexColorOut.rgb, vertexColorAmount);

    return layer;
}

// Where along the vertex paint gradient this layer starts showing through. The reveal mask
// is first stretched around its midpoint by the offset, then squeezed into the band the
// softness leaves free at either end.
float GetRevealThreshold(float revealMask, bool invert, float offset, float softness)
{
    float reveal = invert ? (1.0 - revealMask) : revealMask;

    float halfOffset = offset * 0.5;
    float mid = 0.5 + halfOffset;
    float remapped = (reveal < 0.5)
        ? saturate(reveal * 2.0) * mid
        : saturate((reveal - 0.5) * 2.0) * (0.5 - halfOffset) + mid;

    return saturate(clamp(remapped, 1e-4, 0.9999)) * (1.0 - 2.0 * softness) + softness;
}

// Per-layer strength of the seam decoration around a reveal edge. x decorates layer 1,
// y this layer, z layer 3.
vec3 GetBorderAmount(float blend, float threshold, vec2 width, vec2 softness, vec3 layerAmount)
{
    bool below = blend < threshold;

    float edgeSoftness = below ? softness.x : softness.y;
    float outer = below ? max(1e-4, threshold - width.x) : min(0.9999, threshold + width.y);
    float inner = below ? max(1e-4, threshold - 0.5 * width.x) : min(0.9999, threshold + 0.5 * width.y);

    return layerAmount * smoothstep(mix(inner, outer, edgeSoftness), mix(inner, threshold, edgeSoftness), blend);
}

vec3 ApplyBorderTint(vec3 albedo, vec4 tint, float amount, int blendMode)
{
    float weight = amount * tint.a;

    // Mod2x reads the tint as a factor around its 0.5 midpoint, the other modes as a colour.
    vec3 tintColor = (blendMode == 1) ? tint.rgb : SrgbGammaToLinear(tint.rgb);

    if (blendMode == 0) // Multiply
    {
        return albedo * mix(vec3(1.0), tintColor, weight);
    }
    else if (blendMode == 1) // Mod2x
    {
        return albedo * mix(vec3(1.0), tintColor * 1.9921569, weight);
    }
    else if (blendMode == 2) // Blend
    {
        return mix(albedo, tintColor, weight);
    }

    return albedo;
}

// The reveal threshold of a layer, from its reveal mask sampled with the UVs the mask is driven by
float SampleRevealThreshold(sampler2D revealMaskTexture, vec2 texCoord, vec4 scaleRotationTranslation, bool invert, float offset, float softness)
{
    float revealMask = texture(revealMaskTexture, ApplyUvTransform(texCoord, scaleRotationTranslation)).r;

    return GetRevealThreshold(revealMask, invert, offset, softness);
}

void ApplyBorderToLayer(inout Layer_t layer, vec4 tint, int blendMode, float roughness, float roughnessStrength, float amount)
{
    layer.Albedo = ApplyBorderTint(layer.Albedo, tint, amount, blendMode);
    layer.Roughness = mix(layer.Roughness, roughness, roughnessStrength * amount);
}

// Lays the overlay over a layer, weighed by the layer's tint mask
void ApplyOverlayToLayer(inout Layer_t layer, vec3 factor, vec3 normalOffset, float overlayRoughness, int tintMaskMode, float strength, float normalStrength, float roughnessStrength)
{
    float mask = GetOverlayTintMask(tintMaskMode, layer.TintMask);
    layer.Albedo = mix(layer.Albedo, layer.Albedo * factor, mask * strength);
    layer.NormalOffset += normalOffset * (normalStrength * mask);
    layer.Roughness = saturate(layer.Roughness + overlayRoughness * roughnessStrength * mask);
}

// The material of the composited layer. The layers accumulate world space surface gradients, so the
// tangent space normal is recovered from the total rather than blended per layer.
void ResolveLayerMaterial(inout MaterialProperties_t mat, Layer_t layer, vec3 illumColor)
{
    mat.Albedo = layer.Albedo;
    mat.IllumColor = illumColor;
    mat.Metalness = layer.Metalness;
    mat.RoughnessTex = vec2(layer.Roughness);

    vec2 gradient = vec2(dot(layer.NormalOffset, vTangentOut.xyz), dot(layer.NormalOffset, GetBitangent(vNormalOut, vTangentOut)));
    mat.NormalMap = normalize(vec3(-gradient, 1.0));
    mat.Normal = normalize(mat.GeometricNormal - layer.NormalOffset);

    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    ResolveDiffuseSpecularCitadel(mat);
}

#endif
