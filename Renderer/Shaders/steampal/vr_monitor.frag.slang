#version 460

// F_ADDITIVE_BLEND_OVER_ALPHA is not ported. F_VERTEX_COLOR only reaches an instance color the pixel shader does not read.

#define F_TEXTURE_ANIMATION 0
#define F_TEXTURE_ANIMATION_MODE 0
#define F_VOLUME_METER_ANIMATION 0

#define MONITOR_MODEL_UP_TANGENT 1

#include "../hlvr/vr_monitor.slang"
