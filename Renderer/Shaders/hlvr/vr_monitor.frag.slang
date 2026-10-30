#version 460

// F_SPECULAR_CUBE_MAP is not ported, no material uses it. F_VERTEX_COLOR only reaches an instance color the pixel shader does not read.

#define F_TEXTURE_ANIMATION 0
#define F_TEXTURE_ANIMATION_MODE 0
#define F_ADDITIVE_BLEND_OVER_ALPHA 0

#define MONITOR_GLASS_HIGHLIGHT 1

#include "vr_monitor.slang"
