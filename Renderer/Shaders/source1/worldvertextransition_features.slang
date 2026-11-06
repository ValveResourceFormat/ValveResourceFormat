#version 460

#include "../common/shared_combos.slang"

#define F_VERTEX_COLOR 0
#define F_VERTEX_ALPHA 0

// The two textures always blend by vertex alpha
#define SOURCE1_BASE_TEXTURE2 1
#define SOURCE1_BUMPMAP2 0
#define SOURCE1_BLEND_BY_VERTEX_ALPHA 1

#define SOURCE1_WORLD_TWO_TEXTURE_BLEND 0
#define SOURCE1_FOUR_WAY_BLEND 0

#define SOURCE1_DETAIL_SRGB_ALL_MODES 0
