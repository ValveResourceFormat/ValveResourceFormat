#version 460

#ifndef VRF_SURFACE_HEIGHT_LAYERS_INCLUDE
#define VRF_SURFACE_HEIGHT_LAYERS_INCLUDE

// Material layers made of a color, a height and a normal texture, each read from the mesh UVs or projected along
// the world axes. Include after surface_base_ps.slang.

#include "planar_projection.slang"

const vec3 Forward = vec3(1.0, 0.0, 0.0);
const vec3 Left = vec3(0.0, 1.0, 0.0);
const vec3 Up = vec3(0.0, 0.0, 1.0);

const vec3 BiplanarTexCoordScale = vec3(-1.0, 1.0, -1.0) / 64.0;
const vec3 BiplanarU[3] = vec3[](-Forward, Left, -Forward);
const vec3 BiplanarV[3] = vec3[](Up, -Left, Up);

struct BiplanarFrame_t
{
    ivec2 MajorAxes;    // which two components of the scaled position the major plane reads
    vec2 MajorUv;       // the plane the surface faces most
    vec2 MajorDdx;
    vec2 MajorDdy;
    ivec2 MedianAxes;
    vec2 MedianUv;      // the second most faced plane
    vec2 MedianDdx;
    vec2 MedianDdy;
    vec2 Weights;       // major in x, median in y, summing to 1
};

// Which two components of the position a plane reads, swapped when the plane faces Y.
ivec2 BiplanarUvAxes(ivec3 axis)
{
    return (axis.x == 1) ? axis.zy : axis.yz;
}

BiplanarFrame_t GetBiplanarFrame(vec3 geometricNormal, vec3 positionWs)
{
    PlanarAxes_t planarAxes = GetPlanarAxes(geometricNormal);
    ivec2 majorAxes = BiplanarUvAxes(planarAxes.Major);
    ivec2 medianAxes = BiplanarUvAxes(planarAxes.Median);

    vec3 scaled = positionWs * BiplanarTexCoordScale;
    vec3 ddx = dFdxFine(scaled);
    vec3 ddy = dFdyFine(scaled);

    BiplanarFrame_t frame;
    frame.MajorAxes = majorAxes;
    frame.MedianAxes = medianAxes;
    frame.MajorUv = vec2(scaled[majorAxes.x], scaled[majorAxes.y]);
    frame.MajorDdx = vec2(ddx[majorAxes.x], ddx[majorAxes.y]);
    frame.MajorDdy = vec2(ddy[majorAxes.x], ddy[majorAxes.y]);
    frame.MedianUv = vec2(scaled[medianAxes.x], scaled[medianAxes.y]);
    frame.MedianDdx = vec2(ddx[medianAxes.x], ddx[medianAxes.y]);
    frame.MedianDdy = vec2(ddy[medianAxes.x], ddy[medianAxes.y]);

    frame.Weights = GetPlanarWeights(planarAxes);

    return frame;
}

vec4 SampleMaterialTexture(sampler2D materialTexture, int uvSet, vec2 meshTexCoord, vec2 scale, BiplanarFrame_t frame)
{
    if (uvSet != 0)
    {
        return texture(materialTexture, meshTexCoord);
    }

    vec4 projected = textureGrad(materialTexture, frame.MajorUv * scale,
        frame.MajorDdx * scale, frame.MajorDdy * scale) * frame.Weights.x;

    if (frame.Weights.y > 0.0)
    {
        projected += textureGrad(materialTexture, frame.MedianUv * scale,
            frame.MedianDdx * scale, frame.MedianDdy * scale) * frame.Weights.y;
    }

    return projected;
}

vec3 PlaneNormalOffset(sampler2D normalTexture, vec2 uv, vec2 ddx, vec2 ddy, ivec2 axes, float weight)
{
    vec3 decoded = DecodeHemiOctahedronNormal(textureGrad(normalTexture, uv, ddx, ddy).rg) * weight;

    return BiplanarU[axes.x] * decoded.x - BiplanarV[axes.y] * decoded.y;
}

vec3 ProjectedLayerNormal(sampler2D normalTexture, vec2 scale, BiplanarFrame_t frame,
                          vec3 geometricNormal, vec3 tangent, vec3 bitangent)
{
    vec3 offset = PlaneNormalOffset(normalTexture, frame.MajorUv * scale, frame.MajorDdx * scale,
        frame.MajorDdy * scale, frame.MajorAxes, frame.Weights.x);

    if (frame.Weights.y > 0.0)
    {
        offset += PlaneNormalOffset(normalTexture, frame.MedianUv * scale, frame.MedianDdx * scale,
            frame.MedianDdy * scale, frame.MedianAxes, frame.Weights.y);
    }

    vec3 world = normalize(offset + geometricNormal);

    return vec3(dot(world, tangent), dot(world, bitangent), dot(world, geometricNormal));
}

vec3 LayerNormal(vec2 encoded, sampler2D normalTexture, int uvSet, vec2 scale, float contrast,
                 float rotation, BiplanarFrame_t frame, vec3 geometricNormal, vec3 tangent, vec3 bitangent)
{
    vec3 layerNormal;

    if (uvSet != 0)
    {
        layerNormal = DecodeHemiOctahedronNormal(encoded);
        // Turned with its coordinates; negated because the decode's Y flip mirrors the authored angle
        layerNormal.xy = RotateVector2D(layerNormal.xy, -rotation);
    }
    else
    {
        layerNormal = ProjectedLayerNormal(normalTexture, scale, frame, geometricNormal, tangent, bitangent);
    }

    // Contrast steers the normal away from flat, or past it above 1. Applied to the finished normal,
    // not the encoded pair, so flat is Up.
    return normalize(mix(Up, layerNormal, contrast));
}

// A detail texture follows its layer's UV set unless it names one of its own.
int DetailUvSet(int detailUvSet, int layerUvSet)
{
    return (detailUvSet == -1) ? layerUvSet : detailUvSet;
}

// The layer's detail normal, folded in after its own contrast. Same fold the layers use on each
// other: add the two and take flat back out, so each keeps its deviation from flat.
vec3 ApplyDetailNormal(vec3 layerNormal, sampler2D detailTexture, int detailUvSet, vec2 meshTexCoord,
                       vec2 scale, float contrast, float rotation, BiplanarFrame_t frame,
                       vec3 geometricNormal, vec3 tangent, vec3 bitangent)
{
    vec3 detail;

    if (detailUvSet != 0)
    {
        vec2 encoded = texture(detailTexture, meshTexCoord).rg;

        detail = LayerNormal(encoded, detailTexture, detailUvSet, scale, contrast, rotation, frame,
            geometricNormal, tangent, bitangent);
    }
    else
    {
        vec3 projected = ProjectedLayerNormal(detailTexture, scale, frame, geometricNormal, tangent, bitangent);

        detail = normalize(mix(Up, projected, contrast));
    }

    return normalize(layerNormal + detail - Up);
}

// Hue-preserving recolor: keep the layer's luminance, take the tint's hue, and clamp so a dark
// tint cannot brighten the layer. Used by the model tint and by the border's Colorize mode.
vec3 ColorizeTint(vec3 albedo, vec3 tint, float amount)
{
    vec3 tintNorm = normalize(max(tint, vec3(0.001)));
    float luma = GetLuma(albedo);
    float tinted = min(luma / GetLuma(tintNorm), 3.0 * luma * Max3(tint));

    return saturate(mix(albedo, tintNorm * tinted, amount));
}

struct HeightLayer_t
{
    vec3 Albedo;
    float Occlusion;
    vec4 Height;        // height in r, tint mask in g, alpha test occlusion in b, metalness in a
    vec2 EncodedNormal;
    float TintMask;
    float Roughness;
};

// Samples a layer and tints it. The model tint is the color in rgb and how much of it applies in a.
HeightLayer_t SampleHeightLayer(
    sampler2D colorTexture, sampler2D heightTexture, sampler2D normalTexture,
    int uvSet, vec2 meshTexCoord, vec2 texCoordScale, BiplanarFrame_t biplanar,
    float tintMaskContrast, float tintMaskBrightness, float roughnessContrast, float roughnessBrightness,
    bool metalness, mat4 textureAdjust, mat4 textureColorAdjust, int colorCorrectionMode,
    vec4 modelTint, bool modelTintEnabled)
{
    vec4 color = SampleMaterialTexture(colorTexture, uvSet, meshTexCoord, texCoordScale, biplanar);
    vec4 height = SampleMaterialTexture(heightTexture, uvSet, meshTexCoord, texCoordScale, biplanar);
    vec4 normal = SampleMaterialTexture(normalTexture, uvSet, meshTexCoord, texCoordScale, biplanar);

    HeightLayer_t layer;
    layer.TintMask = ContrastBrightness(height.g, tintMaskContrast, tintMaskBrightness);
    layer.Roughness = ContrastBrightness(normal.b, roughnessContrast, roughnessBrightness);
    layer.Height = height;
    layer.Height.a = metalness ? height.a : 0.0;
    layer.EncodedNormal = normal.rg;
    layer.Occlusion = color.a;

    // The color matrices carry an offset that applies to a w of 1, not to the occlusion in the alpha
    vec4 opaqueColor = vec4(color.rgb, 1.0);
    vec3 corrected = (colorCorrectionMode == 1) ? (opaqueColor * textureAdjust).rgb : color.rgb;
    vec3 tinted = (opaqueColor * textureColorAdjust).rgb;
    vec3 albedo = mix(corrected, tinted, layer.TintMask);

    layer.Albedo = ColorizeTint(albedo, modelTint.rgb, (modelTint.a * layer.TintMask) * float(modelTintEnabled));

    return layer;
}

// Remaps a layer's occlusion by its levels, as the material stores them. The lowest is negated, the exponent is a
// power of two that reaches the authored fraction, and the highest is flipped around two.
float LayerOcclusion(float occlusion, vec3 authoredLevels)
{
    float lowest = -authoredLevels.x;
    float exponent = -1.4427 * log(max(0.0001, 1.0 - authoredLevels.y));
    float highest = 2.0 - authoredLevels.z;

    return saturate(mix(lowest, highest, pow(max(occlusion, 0.0), exponent)));
}

// The material of the composited layers, up to where wetness takes over
void InitHeightMaterial(inout MaterialProperties_t mat, vec3 albedo, float occlusion,
                        vec3 normalTs, float roughness, float height, float metalness)
{
    mat.Albedo = albedo;
    mat.AmbientOcclusion = occlusion;

    mat.NormalMap = normalTs;
    mat.RoughnessTex = vec2(roughness);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.Height = height;
    mat.Metalness = metalness;

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;
}

// The rest of the material, once wetness has had its say over the roughness and the reflectance
void FinishHeightMaterial(inout MaterialProperties_t mat, float reflectance)
{
    AdjustRoughnessByGeometricNormal(mat);

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;
    mat.SpecularColor = mix(vec3(reflectance), mat.Albedo, mat.Metalness);

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;
}

#endif
