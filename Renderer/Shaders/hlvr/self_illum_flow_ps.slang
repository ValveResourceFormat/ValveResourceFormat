#version 460

#ifndef VRF_SELF_ILLUM_FLOW_PS_INCLUDE
#define VRF_SELF_ILLUM_FLOW_PS_INCLUDE

// The timing and tint shared by the self illumination that flows a waveform over a mask. Include it after surface_base_ps.slang.
// The shader declares F_SELF_ILLUM_CUSTOM_FLOW_TIMER.

float GetFlowTime(float speed, float customTimer)
{
    return (F_SELF_ILLUM_CUSTOM_FLOW_TIMER == 1) ? speed * customTimer : speed * g_flTime;
}

vec3 GetSelfIllumTint(float scale, float brightness, vec3 tint)
{
    return scale * exp2(brightness) * tint;
}

#endif
