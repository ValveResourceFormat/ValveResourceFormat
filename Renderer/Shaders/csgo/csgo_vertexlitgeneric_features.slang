#version 460

// BLENDING
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
// TEXTURING
#define F_TINT_MASK 0
#define F_SELF_ILLUM 0
#define F_FORCE_UV2 0
#define F_DECAL_TEXTURE 0
uniform int F_DETAIL_TEXTURE;
// SHADING
#define F_SPECULAR_DIRECT 0
#define F_SPECULAR_INDIRECT 0

// The decal is read from the second UV set
#define F_SECONDARY_UV ((F_FORCE_UV2 == 1) || (F_DECAL_TEXTURE == 1))
