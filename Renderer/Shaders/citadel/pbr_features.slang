#version 460

// Translucency
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_ADVANCED_TRANSLUCENCY 0 // second translucency texture combined into the opacity
#define F_ADDITIVE_BLEND 0

// Keeps the vertex normal as it is on back faces, so both sides of a card shade like the front
#define F_USE_FRONT_FACE_NORMALS_FOR_BACK_FACES 0

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

// Writes the bloom amount of the pixel for the bloom threshold, opaque materials only
#define F_OVERRIDE_BLOOM_AMOUNT 0

// Not implemented: the NPR outline and stepped direct specular,
// F_CLOAK, F_COSMIC_VEIL, F_USE_STATUS_EFFECTS_PROXY, F_MATERIAL_VARIANT.
