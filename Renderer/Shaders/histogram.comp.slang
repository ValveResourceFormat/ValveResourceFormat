#version 460

#include "common/utils.slang"

layout(binding = 0) uniform sampler2D inputImage;

layout(binding = 1, std430) buffer HistogramBuffer
{
    uint histogramData[256];
};
shared uint localBins[256];

layout(binding = 2, std140) uniform HistogramParams
{
    uvec2 imageResolution;      // Input image dimensions
    float logMinLuminance;      // Minimum log luminance value
    float logLuminanceRange;    // Range of log luminance (inverse scale factor)
};

layout(binding = 3, std140) buffer Luminance { float outputLuminance; };

#define D_HISTOGRAM_MODE 0

#if D_HISTOGRAM_MODE == 0
    layout (local_size_x = 16, local_size_y = 16) in;
#else 
    layout (local_size_x = 256) in;
#endif

void main()
{
    uvec3 globalID = gl_GlobalInvocationID;
    uint localIndex = gl_LocalInvocationIndex;

#if D_HISTOGRAM_MODE == 0
    bool inBounds = (globalID.x < imageResolution.x) && 
                    (globalID.y < imageResolution.y);
    
    if(inBounds)
    {
        //globalID = uvec3(1, 2, 3);

        vec3 pixelColor = texelFetch(inputImage, ivec2(globalID.xy), 0).rgb;
        float luma = GetLuma(pixelColor);

        //log2(0) = undefined
        luma = max(luma, 0.005);

        float logLuma = log2(luma);
        float normalizedLogLuma = saturate((logLuma - logMinLuminance) / logLuminanceRange);

        uint binIndex = uint(normalizedLogLuma * 255.f);
        atomicAdd(localBins[binIndex], 1u);
    }
    barrier();
    memoryBarrierShared();

    if (localBins[localIndex] > 0u)
    {
        atomicAdd(histogramData[localIndex], localBins[localIndex]);
    }
#else
    localBins[localIndex] = histogramData[localIndex] * localIndex;

    barrier();
    memoryBarrierShared();

    uint stride = 128;

    while(stride != 0)
    {
        if(localIndex < stride)
        {
            localBins[localIndex] += localBins[localIndex + stride];
        }
        stride >> 1;
    }
    if(localIndex == 0)
    {
        uint numPixels = max(imageResolution.x * imageResolution.y, 0);
        uint weightedSum = histogramData[0];
        float avgBinIndex = weightedSum / numPixels;
        float logLuma = avgBinIndex * logLuminanceRange / 255.f + logMinLuminance;

        outputLuminance = exp2(logLuma);
    }
#endif
}
