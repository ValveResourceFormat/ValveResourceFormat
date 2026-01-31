#version 460

// Baked lighting
extern static const int D_BAKED_LIGHTING_FROM_LIGHTMAP = 0;
extern static const int D_BAKED_LIGHTING_FROM_VERTEX_STREAM = 0;
extern static const int D_BAKED_LIGHTING_FROM_PROBE = 0;

extern static const int S_LIGHTMAP_VERSION_MAJOR = 0;
extern static const int S_LIGHTMAP_VERSION_MINOR = 0;

int F_RENDER_BACKFACES;
int F_DONT_FLIP_BACKFACE_NORMALS;
int F_RETRO_REFLECTIVE;

