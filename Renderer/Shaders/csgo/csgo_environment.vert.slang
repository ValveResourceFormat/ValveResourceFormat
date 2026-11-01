#version 460

#include "../common/utils.slang"
#include "../common/instancing.slang"
#include "../common/animation.slang"
#include "../common/shared_combos.slang"
#include "csgo_environment_features.slang"
#include "../common/surface_height_layers_vs.slang"

void main()
{
    WriteHeightLayerVertex();

    vColorBlendValues.a = clamp(vColorBlendValues.a, 0.001, 1.0);
}
