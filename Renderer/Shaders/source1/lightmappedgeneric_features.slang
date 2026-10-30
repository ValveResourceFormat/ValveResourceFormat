#version 460

// Combos that change the vertex inputs. Everything else a VMT sets is a uniform.
#define F_VERTEX_COLOR 0
#define F_VERTEX_ALPHA 0
// $basetexture2 and $bumpmap2 blend by vertex alpha, like worldvertextransition
#define F_BASE_TEXTURE2 0
#define F_BUMPMAP2 0

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

#if (SOURCE1_BASE_TEXTURE2 == 1 || SOURCE1_BUMPMAP2 == 1)
    #define SOURCE1_BLEND_BY_VERTEX_ALPHA 1
#else
    #define SOURCE1_BLEND_BY_VERTEX_ALPHA 0
#endif

// Only the additive detail blend mode holds colors
#define SOURCE1_DETAIL_SRGB_ALL_MODES 0
