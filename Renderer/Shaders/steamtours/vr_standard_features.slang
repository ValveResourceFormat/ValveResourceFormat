#version 460

#define FOG_GRADIENT_TEXTURE 1
#define SPECULAR_BLINN_PHONG 1
#define ENV_SPECULAR_SIMPLE 1
#define INDIRECT_LIGHT_TEXTURE (F_INDIRECT_TEXTURES == 1)
#define LIGHT_GEOMETRIC_NORMAL_GATE 1

// MODES
#define F_UNLIT 0
// BLENDING
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_GLASS 0
// TEXTURING
#define F_TINT_MASK 0
#define F_VERTEX_COLOR 0
#define F_SELF_ILLUM 0
#define F_BLEND 0
#define F_BLEND_NORMALS 0
// SHADING
#define F_TRANSMISSIVE_SUNLIGHT 0
#define F_TRANSMISSIVE_THICKNESS 0
#define F_SPECULAR 0
#define F_SPECULAR_CUBE_MAP 0
#define F_SPECULAR_CUBE_MAP_PROJECTION 0 // 0 is the plain reflection, 1 follows the projection of the environment map
#define F_HIGH_QUALITY_GLOSS 0
#define F_METALNESS_TEXTURE 0
// LIGHTING
#define F_INDIRECT_TEXTURES 0
#define F_INDIRECT_TEXTURES_UV2 0 // the occlusion texture is mapped by the second UV set