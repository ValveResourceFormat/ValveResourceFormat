#version 460

// Ground truth ambient occlusion in three compute passes: D_PASS 0 filters the depth into a chain of mips,
// 1 integrates the horizons of each pixel, 2 denoises the result along the depth edges.
#include "../common/view_constants.slang"
#include "../common/utils.slang"

layout(local_size_x = 8, local_size_y = 8) in;

#define D_PASS 0

#define SLICE_COUNT 3
#define STEPS_PER_SLICE 3
#define DEPTH_MIP_COUNT 5
#define EFFECT_RADIUS 64.0
#define FALLOFF_RANGE 0.615
#define THIN_OCCLUDER_COMPENSATION 0.5
#define DEPTH_MIP_SAMPLING_OFFSET 3.3
#define RADIUS_MULTIPLIER 1.457
#define SAMPLE_DISTRIBUTION_POWER 2.0
#define PIXEL_TOO_CLOSE_THRESHOLD 1.3
#define OCCLUSION_TERM_SCALE 1.5

// Scales a texture coordinate to the view space xy of a point at depth 1
#define g_vNdcToViewMul vec2(2.0 / g_matViewToProjection[0][0], 2.0 / g_matViewToProjection[1][1])
#define g_vNdcToViewAdd vec2(-1.0 / g_matViewToProjection[0][0], -1.0 / g_matViewToProjection[1][1])

uniform vec2 g_vPixelSize;
uniform float g_flDenoiseBlurBeta;
uniform bool g_bFinalPass;

// The multiplier and offset that turn a distance into the weight of a sample, fading out over the falloff range
vec2 GetFalloffMulAdd(float effectRadius)
{
    float falloffRange = FALLOFF_RANGE * effectRadius;
    float falloffFrom = effectRadius * (1.0 - FALLOFF_RANGE);

    return vec2(-1.0 / falloffRange, falloffFrom / falloffRange + 1.0);
}

#if (D_PASS == 0)

uniform sampler2D g_tSourceDepth;

layout(binding = 0, r32f) uniform writeonly image2D g_tWorkingDepthMip0;
layout(binding = 1, r32f) uniform writeonly image2D g_tWorkingDepthMip1;
layout(binding = 2, r32f) uniform writeonly image2D g_tWorkingDepthMip2;
layout(binding = 3, r32f) uniform writeonly image2D g_tWorkingDepthMip3;
layout(binding = 4, r32f) uniform writeonly image2D g_tWorkingDepthMip4;

shared float g_SharedDepth[8][8];

// Distance along the view axis of a window space depth
float GetViewDepth(float windowDepth)
{
    float normalizedDepth = RemapValClamped(windowDepth, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0);
    float inverseDepth = fma(normalizedDepth, g_vInvProjRow3.z, g_vInvProjRow3.w);

    return clamp(abs(1.0 / inverseDepth), 0.0, 65504.0);
}

// Averages four depths, weighing the ones far behind the nearest less
float FilterDepths(vec4 depths, float falloffMul, float falloffAdd)
{
    float maxDepth = max(max(depths.x, depths.y), max(depths.z, depths.w));
    vec4 weights = saturate((maxDepth - depths) * falloffMul + falloffAdd);

    return dot(weights, depths) / dot(weights, vec4(1.0));
}

// Filters the 2x2 block of shared depths that starts at a tile position, with the given stride between them
float FilterSharedDepths(uvec2 local, uint stride, float falloffMul, float falloffAdd)
{
    return FilterDepths(vec4(
        g_SharedDepth[local.x][local.y], g_SharedDepth[local.x + stride][local.y],
        g_SharedDepth[local.x][local.y + stride], g_SharedDepth[local.x + stride][local.y + stride]), falloffMul, falloffAdd);
}

void main()
{
    float effectRadius = 0.75 * EFFECT_RADIUS * RADIUS_MULTIPLIER;
    vec2 falloff = GetFalloffMulAdd(effectRadius);
    float falloffMul = falloff.x;
    float falloffAdd = falloff.y;

    ivec2 sourceMax = textureSize(g_tSourceDepth, 0) - 1;
    ivec2 base = ivec2(gl_GlobalInvocationID.xy) * 2;

    vec4 depths = vec4(
        GetViewDepth(texelFetch(g_tSourceDepth, min(base, sourceMax), 0).x),
        GetViewDepth(texelFetch(g_tSourceDepth, min(base + ivec2(1, 0), sourceMax), 0).x),
        GetViewDepth(texelFetch(g_tSourceDepth, min(base + ivec2(0, 1), sourceMax), 0).x),
        GetViewDepth(texelFetch(g_tSourceDepth, min(base + ivec2(1, 1), sourceMax), 0).x));

    imageStore(g_tWorkingDepthMip0, base, vec4(depths.x));
    imageStore(g_tWorkingDepthMip0, base + ivec2(1, 0), vec4(depths.y));
    imageStore(g_tWorkingDepthMip0, base + ivec2(0, 1), vec4(depths.z));
    imageStore(g_tWorkingDepthMip0, base + ivec2(1, 1), vec4(depths.w));

    uvec2 local = gl_LocalInvocationID.xy;

    float mip1 = FilterDepths(depths, falloffMul, falloffAdd);
    imageStore(g_tWorkingDepthMip1, ivec2(gl_GlobalInvocationID.xy), vec4(mip1));
    g_SharedDepth[local.x][local.y] = mip1;

    barrier();
    memoryBarrierShared();

    if (all(equal(local % uvec2(2u), uvec2(0u))))
    {
        float mip2 = FilterSharedDepths(local, 1u, falloffMul, falloffAdd);
        imageStore(g_tWorkingDepthMip2, ivec2(gl_GlobalInvocationID.xy / 2u), vec4(mip2));
        g_SharedDepth[local.x][local.y] = mip2;
    }

    barrier();
    memoryBarrierShared();

    if (all(equal(local % uvec2(4u), uvec2(0u))))
    {
        float mip3 = FilterSharedDepths(local, 2u, falloffMul, falloffAdd);
        imageStore(g_tWorkingDepthMip3, ivec2(gl_GlobalInvocationID.xy / 4u), vec4(mip3));
        g_SharedDepth[local.x][local.y] = mip3;
    }

    barrier();
    memoryBarrierShared();

    if (all(equal(local % uvec2(8u), uvec2(0u))))
    {
        float mip4 = FilterSharedDepths(local, 4u, falloffMul, falloffAdd);
        imageStore(g_tWorkingDepthMip4, ivec2(gl_GlobalInvocationID.xy / 8u), vec4(mip4));
    }
}

#elif (D_PASS == 1)

uniform sampler2D g_tWorkingDepth; // Point filtered, with the mips of pass 0

layout(binding = 5, r32f) uniform writeonly image2D g_tAoTerm;
layout(binding = 6, rgba16f) uniform writeonly image2D g_tEdges;

// Position in a space with x to the right, y up and z away from the camera
vec3 GetViewPosition(vec2 uv, float viewDepth)
{
    return vec3((uv * g_vNdcToViewMul + g_vNdcToViewAdd) * viewDepth, viewDepth);
}

// Position in the Hilbert curve of a 64x64 tile
uint HilbertIndex(uint posX, uint posY)
{
    uint index = 0u;

    for (uint level = 32u; level > 0u; level /= 2u)
    {
        uint regionX = (posX & level) > 0u ? 1u : 0u;
        uint regionY = (posY & level) > 0u ? 1u : 0u;
        index += level * level * ((3u * regionX) ^ regionY);

        if (regionY == 0u)
        {
            if (regionX == 1u)
            {
                posX = 63u - posX;
                posY = 63u - posY;
            }

            uint swapped = posX;
            posX = posY;
            posY = swapped;
        }
    }

    return index;
}

// Per pixel noise for the slice rotation and the step offsets
vec2 GetSpatioTemporalNoise(uvec2 pixel)
{
    uint index = HilbertIndex(pixel.x % 64u, pixel.y % 64u);

    return fract(vec2(0.5) + vec2(0.75487766624669276, 0.569840290998) * float(index));
}

// The depths left, right, up and down of a pixel
vec4 GetNeighborDepths(ivec2 pixel)
{
    ivec2 maxPixel = textureSize(g_tWorkingDepth, 0) - 1;

    return vec4(
        texelFetch(g_tWorkingDepth, clamp(pixel + ivec2(-1, 0), ivec2(0), maxPixel), 0).x,
        texelFetch(g_tWorkingDepth, clamp(pixel + ivec2(1, 0), ivec2(0), maxPixel), 0).x,
        texelFetch(g_tWorkingDepth, clamp(pixel + ivec2(0, 1), ivec2(0), maxPixel), 0).x,
        texelFetch(g_tWorkingDepth, clamp(pixel + ivec2(0, -1), ivec2(0), maxPixel), 0).x);
}

// The surface normal from the depths around a pixel, taking the side of each axis closer in depth
vec3 GetViewNormal(ivec2 pixel, vec4 neighborDepths, vec3 center, float centerDepth)
{
    float depthLeft = neighborDepths.x;
    float depthRight = neighborDepths.y;
    float depthUp = neighborDepths.z;
    float depthDown = neighborDepths.w;

    vec2 uv = (vec2(pixel) + 0.5) * g_vPixelSize;

    vec3 horizontal = abs(depthLeft - centerDepth) < abs(depthRight - centerDepth)
        ? center - GetViewPosition(uv - vec2(g_vPixelSize.x, 0.0), depthLeft)
        : GetViewPosition(uv + vec2(g_vPixelSize.x, 0.0), depthRight) - center;

    vec3 vertical = abs(depthDown - centerDepth) < abs(depthUp - centerDepth)
        ? center - GetViewPosition(uv - vec2(0.0, g_vPixelSize.y), depthDown)
        : GetViewPosition(uv + vec2(0.0, g_vPixelSize.y), depthUp) - center;

    return normalize(cross(vertical, horizontal));
}

// Quantized to the depth steps the edges are found by
vec4 GetDepthEdges(vec4 neighborDepths, float centerDepth)
{
    vec4 edges = neighborDepths - centerDepth;

    float slopeHorizontal = (edges.y - edges.x) * 0.5;
    float slopeVertical = (edges.w - edges.z) * 0.5;
    vec4 slopeAdjusted = edges + vec4(slopeHorizontal, -slopeHorizontal, slopeVertical, -slopeVertical);

    edges = min(abs(edges), abs(slopeAdjusted));

    return round(saturate(saturate(vec4(1.25) - edges / (centerDepth * 0.011)) * 2.9)) / 3.0;
}

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = textureSize(g_tWorkingDepth, 0);

    if (any(greaterThanEqual(pixel, size)))
    {
        return;
    }

    float centerDepth = texelFetch(g_tWorkingDepth, pixel, 0).x;

    vec4 neighborDepths = GetNeighborDepths(pixel);

    imageStore(g_tEdges, pixel, GetDepthEdges(neighborDepths, centerDepth));

    vec2 noise = GetSpatioTemporalNoise(uvec2(pixel));
    float noiseSlice = noise.x;
    float noiseSample = noise.y;

    // Keeps the center out of its own depth
    float pixelDepth = centerDepth * 0.9992;
    vec2 screenUv = (vec2(pixel) + 0.5) * g_vPixelSize;
    vec3 pixelPosition = GetViewPosition(screenUv, pixelDepth);
    vec3 viewVec = normalize(-pixelPosition);

    vec3 viewNormal = GetViewNormal(pixel, neighborDepths, pixelPosition, centerDepth);

    if (dot(viewNormal, viewVec) < 0.0)
    {
        viewNormal = -viewNormal;
    }

    float effectRadius = EFFECT_RADIUS * RADIUS_MULTIPLIER;
    vec2 falloff = GetFalloffMulAdd(effectRadius);
    float falloffMul = falloff.x;
    float falloffAdd = falloff.y;

    float thinFactor = 1.0 + THIN_OCCLUDER_COMPENSATION;

    float screenspaceRadius = effectRadius / (pixelDepth * g_vNdcToViewMul.x * g_vPixelSize.x);
    float minStep = PIXEL_TOO_CLOSE_THRESHOLD / screenspaceRadius;

    // Fades the occlusion out when the radius is only a few pixels, where it would only add noise
    float visibility = saturate((10.0 - screenspaceRadius) / 100.0) * 0.5;

    for (int slice = 0; slice < SLICE_COUNT; slice++)
    {
        float phi = (float(slice) + noiseSlice) * (PI / float(SLICE_COUNT));
        vec2 omega = vec2(cos(phi), sin(phi)) * screenspaceRadius;
        vec3 directionVec = vec3(cos(phi), sin(phi), 0.0);

        vec3 orthoDirectionVec = directionVec - viewVec * dot(directionVec, viewVec);
        vec3 axisVec = normalize(cross(orthoDirectionVec, viewVec));

        vec3 projectedNormalVec = viewNormal - axisVec * dot(viewNormal, axisVec);
        float signNorm = sign(dot(orthoDirectionVec, projectedNormalVec));
        float projectedNormalVecLength = length(projectedNormalVec);
        float cosNorm = saturate(dot(projectedNormalVec, viewVec) / projectedNormalVecLength);
        float normalAngle = signNorm * acos(cosNorm);

        float lowHorizonCos0 = cos(normalAngle + PI * 0.5);
        float lowHorizonCos1 = cos(normalAngle - PI * 0.5);
        float horizonCos0 = lowHorizonCos0;
        float horizonCos1 = lowHorizonCos1;

        for (int step = 0; step < STEPS_PER_SLICE; step++)
        {
            float stepBaseNoise = float(slice + step * STEPS_PER_SLICE) * 0.6180339887498948482;
            float stepNoise = fract(noiseSample + stepBaseNoise);

            float s = (float(step) + stepNoise) / float(STEPS_PER_SLICE);
            s = pow(s, SAMPLE_DISTRIBUTION_POWER) + minStep;

            vec2 sampleOffset = s * omega;
            float sampleOffsetLength = length(sampleOffset);
            float mipLevel = clamp(log2(sampleOffsetLength) - DEPTH_MIP_SAMPLING_OFFSET, 0.0, float(DEPTH_MIP_COUNT));

            sampleOffset = round(sampleOffset) * g_vPixelSize;

            vec2 sampleUv0 = screenUv + sampleOffset;
            vec2 sampleUv1 = screenUv - sampleOffset;

            vec3 sampleDelta0 = GetViewPosition(sampleUv0, textureLod(g_tWorkingDepth, sampleUv0, mipLevel).x) - pixelPosition;
            vec3 sampleDelta1 = GetViewPosition(sampleUv1, textureLod(g_tWorkingDepth, sampleUv1, mipLevel).x) - pixelPosition;

            float sampleDist0 = length(vec3(sampleDelta0.xy, sampleDelta0.z * thinFactor));
            float sampleDist1 = length(vec3(sampleDelta1.xy, sampleDelta1.z * thinFactor));

            float sampleHorizonCos0 = dot(sampleDelta0 / length(sampleDelta0), viewVec);
            float sampleHorizonCos1 = dot(sampleDelta1 / length(sampleDelta1), viewVec);

            float weight0 = saturate(sampleDist0 * falloffMul + falloffAdd);
            float weight1 = saturate(sampleDist1 * falloffMul + falloffAdd);

            horizonCos0 = max(horizonCos0, mix(lowHorizonCos0, sampleHorizonCos0, weight0));
            horizonCos1 = max(horizonCos1, mix(lowHorizonCos1, sampleHorizonCos1, weight1));
        }

        projectedNormalVecLength = mix(projectedNormalVecLength, 1.0, 0.05);

        float h0 = -acos(clamp(horizonCos1, -1.0, 1.0));
        float h1 = acos(clamp(horizonCos0, -1.0, 1.0));

        float iarc0 = (cosNorm + 2.0 * h0 * sin(normalAngle) - cos(2.0 * h0 - normalAngle)) * 0.25;
        float iarc1 = (cosNorm + 2.0 * h1 * sin(normalAngle) - cos(2.0 * h1 - normalAngle)) * 0.25;

        visibility += projectedNormalVecLength * (iarc0 + iarc1);
    }

    visibility /= float(SLICE_COUNT);
    visibility = max(0.03, visibility);

    imageStore(g_tAoTerm, pixel, vec4(saturate(visibility / OCCLUSION_TERM_SCALE)));
}

#else

uniform sampler2D g_tAoTerm;
uniform sampler2D g_tEdges;

layout(binding = 7, r32f) uniform writeonly image2D g_tFinalAo;

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    ivec2 maxPixel = textureSize(g_tAoTerm, 0) - 1;

    if (any(greaterThan(pixel, maxPixel)))
    {
        return;
    }

    ivec2 left = clamp(pixel + ivec2(-1, 0), ivec2(0), maxPixel);
    ivec2 right = clamp(pixel + ivec2(1, 0), ivec2(0), maxPixel);
    ivec2 top = clamp(pixel + ivec2(0, 1), ivec2(0), maxPixel);
    ivec2 bottom = clamp(pixel + ivec2(0, -1), ivec2(0), maxPixel);

    vec4 edgesC = texelFetch(g_tEdges, pixel, 0);
    vec4 edgesL = texelFetch(g_tEdges, left, 0);
    vec4 edgesR = texelFetch(g_tEdges, right, 0);
    vec4 edgesT = texelFetch(g_tEdges, top, 0);
    vec4 edgesB = texelFetch(g_tEdges, bottom, 0);

    // The edge between two pixels is not found identically from both sides
    edgesC *= vec4(edgesL.y, edgesR.x, edgesT.w, edgesB.z);

    // Lets a pixel boxed in by edges on all sides blur a little, so it does not stay noisy
    float edginess = saturate(1.5 - dot(edgesC, vec4(1.0))) / 3.0;
    edgesC = saturate(edgesC + edginess);

    const float diagonalWeight = 0.85 * 0.5;

    float weightTL = diagonalWeight * (edgesC.x * edgesL.z + edgesC.z * edgesT.x);
    float weightTR = diagonalWeight * (edgesC.z * edgesT.y + edgesC.y * edgesR.z);
    float weightBL = diagonalWeight * (edgesC.w * edgesB.x + edgesC.x * edgesL.w);
    float weightBR = diagonalWeight * (edgesC.y * edgesR.w + edgesC.w * edgesB.y);

    float aoC = texelFetch(g_tAoTerm, pixel, 0).x;
    float aoL = texelFetch(g_tAoTerm, left, 0).x;
    float aoR = texelFetch(g_tAoTerm, right, 0).x;
    float aoT = texelFetch(g_tAoTerm, top, 0).x;
    float aoB = texelFetch(g_tAoTerm, bottom, 0).x;
    float aoTL = texelFetch(g_tAoTerm, clamp(pixel + ivec2(-1, 1), ivec2(0), maxPixel), 0).x;
    float aoTR = texelFetch(g_tAoTerm, clamp(pixel + ivec2(1, 1), ivec2(0), maxPixel), 0).x;
    float aoBL = texelFetch(g_tAoTerm, clamp(pixel + ivec2(-1, -1), ivec2(0), maxPixel), 0).x;
    float aoBR = texelFetch(g_tAoTerm, clamp(pixel + ivec2(1, -1), ivec2(0), maxPixel), 0).x;

    float sumWeight = g_flDenoiseBlurBeta;
    float sum = aoC * g_flDenoiseBlurBeta;

    sum += edgesC.x * aoL;
    sumWeight += edgesC.x;
    sum += edgesC.y * aoR;
    sumWeight += edgesC.y;
    sum += edgesC.z * aoT;
    sumWeight += edgesC.z;
    sum += edgesC.w * aoB;
    sumWeight += edgesC.w;

    sum += weightTL * aoTL;
    sumWeight += weightTL;
    sum += weightTR * aoTR;
    sumWeight += weightTR;
    sum += weightBL * aoBL;
    sumWeight += weightBL;
    sum += weightBR * aoBR;
    sumWeight += weightBR;

    float denoised = sum / sumWeight;

    // Only the last pass scales the term back from the range the passes keep it in
    imageStore(g_tFinalAo, pixel, vec4(g_bFinalPass ? saturate(denoised * OCCLUSION_TERM_SCALE) : denoised));
}

#endif
