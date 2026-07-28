#version 460

#ifndef VRF_CITADEL_OVERLAY_FEATURES_INCLUDE
#define VRF_CITADEL_OVERLAY_FEATURES_INCLUDE

// A decal projected onto world geometry.

// Takes its normal basis from the decal projection instead of the surface it lands on.
#define F_DECAL_SPACE_NORMALS 0

// Render state only: whether the overlay contributes to the occlusion buffer.
#define F_WRITE_OCCLUSION 0

// Not implemented: F_RENDER_ON_TRANSLUCENT, a separately lit pass for decals that land
// on translucent surfaces.

#endif
