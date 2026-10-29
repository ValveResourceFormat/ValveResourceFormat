#version 460

#include "csgo_character_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

#if (F_ENABLE_ADJUSTMENTS == 1)
#include "../features/detail_texture_vs.slang"
#endif

#include "../features/csgo_character_eyes_vs.slang"

void main()
{
    WriteSurfaceVertex(true);

#if (F_ENABLE_ADJUSTMENTS == 1)
    WriteDetailTexCoords();
#endif

#if (F_EYEBALLS == 1)
    eyeInterpolator = GetCharacterEyeInterpolator(vPOSITION);
#endif
}
