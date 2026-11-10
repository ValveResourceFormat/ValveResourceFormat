#version 460

#include "common/luminance_histogram.slang"

layout (local_size_x = 256) in;

// Global histogram buffer (256 bins), filled by the MSAA resolve
layout(binding = 15, std430) buffer HistogramBuffer
{
    uint histogram[256];
};

// Shared histogram used inside a workgroup
shared uint localHistogram[256];

uniform int g_nTotalPixels;

layout(binding = 11, std140) buffer Luminance { float outputLuminance; };

void main()
{
    uint bin = gl_LocalInvocationIndex;

    // sum up weighted bin values
    localHistogram[bin] = histogram[bin] * bin;
    memoryBarrierShared();
    barrier();
    for (uint stride = 128; stride > 0; stride >>= 1)
    {
        if (bin < stride)
        {
            localHistogram[bin] += localHistogram[bin + stride];
        }
        memoryBarrierShared();
        barrier();
    }

    if(bin == 0)
    {
        uint weightedSum = localHistogram[0];
        float averageBin = g_nTotalPixels > 0 ? float(weightedSum) / float(g_nTotalPixels) : 0.0;
        outputLuminance = HistogramBinToLuminance(averageBin);
    }
}
