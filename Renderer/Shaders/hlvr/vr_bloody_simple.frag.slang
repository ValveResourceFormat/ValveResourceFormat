#version 460

// TODO: with F_SUPPORTS_DECALS the game blends a blood layer (color, normal, roughness, self illum) where a per-object decal
// blend mask has coverage. The mask is written at runtime, so a static render has no blood and matches this body.
#include "vr_simple_markable.frag.slang"
