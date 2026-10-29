#version 460

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
#define F_USE_NPR_LIGHTING 0
#define F_SHEEN 0
#define F_GLASS 0 // transmission through the albedo, not the edge fresnel glass elsewhere

// Not implemented: the NPR outline, stepped direct specular and screen space occlusion,
// F_CLOAK, F_COSMIC_VEIL, F_USE_STATUS_EFFECTS_PROXY, F_MATERIAL_VARIANT,
// F_OVERRIDE_BLOOM_AMOUNT.
