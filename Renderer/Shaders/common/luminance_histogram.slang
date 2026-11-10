#version 460

#ifndef VRF_LUMINANCE_HISTOGRAM_INCLUDE
#define VRF_LUMINANCE_HISTOGRAM_INCLUDE

#include "utils.slang"

// The auto exposure histogram has 256 bins spread evenly over the log2 luminance range
const float HISTOGRAM_LOG_MIN_LUMINANCE = -8.0;
const float HISTOGRAM_LOG_LUMINANCE_RANGE = 13.0;

uint LuminanceToHistogramBin(float luminance)
{
    // log2(0) is undefined
    float logLuminance = log2(max(luminance, 0.005));
    float normalizedLogLuminance = saturate((logLuminance - HISTOGRAM_LOG_MIN_LUMINANCE) / HISTOGRAM_LOG_LUMINANCE_RANGE);
    return uint(normalizedLogLuminance * 255.0);
}

float HistogramBinToLuminance(float bin)
{
    float logLuminance = bin * (HISTOGRAM_LOG_LUMINANCE_RANGE / 255.0) + HISTOGRAM_LOG_MIN_LUMINANCE;
    return exp2(logLuminance);
}

#endif
