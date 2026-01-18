#version 460

#include "common/utils.slang"

layout(binding = 0) uniform sampler2D inputImage;

// Global histogram buffer (256 bins)
layout(binding = 1, std430) buffer HistogramBuffer
{
    uint histogram[256];
};

// Shared histogram used inside a workgroup
shared uint localHistogram[256];

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
    uint bin       = gl_LocalInvocationIndex;

#if D_HISTOGRAM_MODE == 0
    if (globalID.x < imageResolution.x && globalID.y < imageResolution.y)
    {
        vec3 color = texelFetch(inputImage, ivec2(globalID.xy), 0).rgb;
        float luma = GetLuma(color);

        // log2(0) = undefined
        luma = max(luma, 0.005);

        // Convert to log space and normalize
        float logLuma = log2(luma);
        float normalizedLogLuma =
            saturate((logLuma - logMinLuminance) / logLuminanceRange);

        // Map to histogram bin
        uint destBin = uint(normalizedLogLuma * 255.0);

        // Accumulate into shared histogram
        atomicAdd(localHistogram[destBin], 1u);
    }

    // Ensure all threads finished writing
    barrier();
    memoryBarrierShared();

    // Accumulate shared histogram into global histogram
    if (localHistogram[bin] > 0u)
    {
        atomicAdd(histogram[bin], localHistogram[bin]);
    }
#else
    // convert bins to weighted, brighter pixels = more weight
    localHistogram[bin] = histogram[bin] * bin;

    barrier();
    memoryBarrierShared();

    // add all bins together
    for (uint stride = 128; stride > 0; stride >>= 1)
    {
        if (bin < stride)
        {
            localHistogram[bin] += localHistogram[bin + stride];
        }

        barrier();
        memoryBarrierShared();
    }

    // process our sum
    if(bin == 0)
    {
        uint weightedSum = localHistogram[bin];

        uint totalPixels =  max(imageResolution.x * imageResolution.y, 1u);
        float averageBin = float(weightedSum) / float(totalPixels);

        // Convert bin index back to luminance
        float logLuma = averageBin * logLuminanceRange / 255.0 + logMinLuminance;

        outputLuminance = exp2(logLuma);
    }
#endif
}
