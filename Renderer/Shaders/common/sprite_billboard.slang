#version 460

#ifndef VRF_SPRITE_BILLBOARD_INCLUDE
#define VRF_SPRITE_BILLBOARD_INCLUDE

// The right axis of a sprite that stays upright and turns around the up axis to face along a direction
vec3 GetUprightBillboardRight(vec3 direction)
{
    return normalize(vec3(direction.y, -direction.x, 0.0));
}

#endif
