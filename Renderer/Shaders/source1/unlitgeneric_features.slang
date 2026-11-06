#version 460

#define F_VERTEX_COLOR 0
#define F_VERTEX_ALPHA 0
// D_BAKED_LIGHTING_FROM_VERTEX_STREAM takes the diffuse light baked per vertex in the second color stream, instead of the ambient cube and the sun
#include "../common/shared_combos.slang"

#define SOURCE1_DETAIL_SRGB_ALL_MODES 1

// Set by the shader that multiplies a second texture into the first
#ifndef SOURCE1_TWO_TEXTURE
#define SOURCE1_TWO_TEXTURE 0
#endif

// Set by the shader that adjusts the contrast, saturation and tint of the color of two textures
#ifndef SOURCE1_MONITOR_SCREEN
#define SOURCE1_MONITOR_SCREEN 0
#endif
