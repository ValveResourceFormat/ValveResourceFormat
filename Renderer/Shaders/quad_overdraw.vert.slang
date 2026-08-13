#version 460

#include "common/instancing.slang"
#include "common/animation.slang"

#include "common/vertex_layout.slang"
layout (location = ATTRIBUTE_POSITION) in vec3 vPOSITION;

#include "common/ViewConstants.slang"

void main(void) {
    gl_Position = g_matWorldToProjection * vec4(vec4(vPOSITION, 1.0) * CalculateSkinTransform(), 1.0);
}
