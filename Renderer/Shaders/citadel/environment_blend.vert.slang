#version 460

#include "environment_blend_features.slang"

#define PAINTED_TINT 0
#define OBJECT_SPACE_POSITION (F_WORLD_OVERLAY_USE_OBJECT_SPACE == 1)

#include "environment_vertex.slang"
