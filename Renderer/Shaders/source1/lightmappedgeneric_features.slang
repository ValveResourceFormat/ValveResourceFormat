#version 460

#include "../common/shared_combos.slang"

// Combos that change the vertex inputs. Everything else a VMT sets is a uniform.
#define F_VERTEX_COLOR 0
#define F_VERTEX_ALPHA 0
// $basetexture2 and $bumpmap2 blend by vertex alpha, like worldvertextransition
#define F_BASE_TEXTURE2 0
#define F_BUMPMAP2 0
// A mask texture blends the two bump maps in place of the vertex alpha
#define F_BUMPMASK 0

#if (F_BASE_TEXTURE2 == 1)
    #define SOURCE1_BASE_TEXTURE2 1
#else
    #define SOURCE1_BASE_TEXTURE2 0
#endif

#if (F_BUMPMAP2 == 1)
    #define SOURCE1_BUMPMAP2 1
#else
    #define SOURCE1_BUMPMAP2 0
#endif

#if (F_BUMPMAP2 == 1 && F_BUMPMASK == 1)
    #define SOURCE1_BUMPMASK 1
#else
    #define SOURCE1_BUMPMASK 0
#endif

#if (SOURCE1_BASE_TEXTURE2 == 1 || (SOURCE1_BUMPMAP2 == 1 && SOURCE1_BUMPMASK == 0))
    #define SOURCE1_BLEND_BY_VERTEX_ALPHA 1
#else
    #define SOURCE1_BLEND_BY_VERTEX_ALPHA 0
#endif

#define SOURCE1_FOUR_WAY_BLEND 0

// Set by the shader whose detail texture blends over the base texture by its own alpha
#ifndef SOURCE1_WORLD_TWO_TEXTURE_BLEND
#define SOURCE1_WORLD_TWO_TEXTURE_BLEND 0
#endif

// Only the additive detail blend mode holds colors
#define SOURCE1_DETAIL_SRGB_ALL_MODES 0
