#version 460

#ifndef VRF_PBR_FEATURES_INCLUDE
#define VRF_PBR_FEATURES_INCLUDE

// Translucency
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_ADVANCED_TRANSLUCENCY 0 // second translucency texture combined into the opacity
#define F_ADDITIVE_BLEND 0

// Vertex animation
#define F_JITTER_VERTICES 0 // 0="No", 1="World Space", 2="Object Space"

// Texturing
#define F_DETAIL 0
#define F_ENABLE_TEXTURE_TRANSFORMS 0
#define F_SECONDARY_UV 0
#define F_VERTEX_COLOR 0
#define F_PAINT_VERTEX_COLORS 0

// Shading
#define F_SELF_ILLUM 0
#define F_UNLIT 0

// todo: support npr lighting
#define F_USE_NPR_LIGHTING 0

#define F_SHEEN 0
#define F_GLASS 0 // transmission through the albedo, not the edge fresnel glass elsewhere

#if (F_GLASS == 1)
    #define GLASS_IS_TRANSMISSIVE
#endif

// Not implemented: NPR lighting itself and its outline/rim/transmissive companions,
// F_CLOAK, F_COSMIC_VEIL, F_USE_STATUS_EFFECTS_PROXY, F_MATERIAL_VARIANT,
// F_OVERRIDE_BLOOM_AMOUNT.

// The colour alpha is the opacity whenever the material is cut out or see through,
// which is what pushes metalness onto its own texture. Unlit shades nothing, so it
// carries neither metalness nor occlusion.
#if ((F_ALPHA_TEST == 1) || (F_TRANSLUCENT == 1)) && (F_UNLIT == 0)
    #define METALNESS_TEXTURE 1
#else
    #define METALNESS_TEXTURE 0
#endif

// Translucency runs the alpha through the view angle fade, and unlit alpha test tests
// against that faded value rather than the raw texture alpha.
#if (F_TRANSLUCENT == 1) || ((F_UNLIT == 1) && (F_ALPHA_TEST == 1))
    #define FADED_OPACITY 1
#else
    #define FADED_OPACITY 0
#endif

#endif
