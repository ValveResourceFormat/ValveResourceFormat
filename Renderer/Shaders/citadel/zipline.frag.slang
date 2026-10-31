#version 460

#include "zipline_features.slang"

// Translucent surfaces do not write the G-buffer flag that accepts it
#if (F_TRANSLUCENT == 0)
    #define SCREEN_SPACE_AO 1
#endif

#include "../projected_decals_surface.slang"
