#version 460

// A decal projected onto world geometry.

// Takes its normal basis from the decal projection instead of the surface it lands on.
#define F_DECAL_SPACE_NORMALS 0

// Not implemented: F_RENDER_ON_TRANSLUCENT, a separately lit pass for decals that land
// on translucent surfaces.
