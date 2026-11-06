#version 460

#include "../common/shared_combos.slang"

#define F_VERTEX_COLOR 0
#define F_VERTEX_ALPHA 0
#define F_BUMPMAP2 0

// Four base textures blend by the layer weights of the vertices
#define SOURCE1_FOUR_WAY_BLEND 1
#define SOURCE1_FRESNEL_POWER 4.0
#define SOURCE1_BASE_TEXTURE2 0
#define SOURCE1_BLEND_BY_VERTEX_ALPHA 0

#if (F_BUMPMAP2 == 1)
    #define SOURCE1_BUMPMAP2 1
#else
    #define SOURCE1_BUMPMAP2 0
#endif

#define SOURCE1_WORLD_TWO_TEXTURE_BLEND 0

#define SOURCE1_DETAIL_SRGB_ALL_MODES 0
