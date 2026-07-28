#version 460

#ifndef VRF_ENVIRONMENT_BLEND_FEATURES_INCLUDE
#define VRF_ENVIRONMENT_BLEND_FEATURES_INCLUDE

#define F_LAYER_COUNT 0 // 0="2 Layers", 1="3 Layers"

#define F_LAYER1_BIPLANAR 0
#define F_LAYER2_BIPLANAR 0
#define F_LAYER3_BIPLANAR 0

#define F_UV_OVERLAY 0
#define F_WORLD_OVERLAY 0 // 0="Off", 1="1 World Overlay", 2="2 World Overlays"
#define F_WORLD_OVERLAY_USE_OBJECT_SPACE 0

// Layer 3 only exists at F_LAYER_COUNT == 1, so its biplanar combo is dead without it.
#if (F_LAYER_COUNT == 1) && (F_LAYER3_BIPLANAR == 1)
    #define LAYER3_BIPLANAR 1
#else
    #define LAYER3_BIPLANAR 0
#endif

#if (F_LAYER1_BIPLANAR == 1) || (F_LAYER2_BIPLANAR == 1) || (LAYER3_BIPLANAR == 1)
    #define ANY_BIPLANAR 1
#else
    #define ANY_BIPLANAR 0
#endif

// The world overlay is projected in world space unless the material asks for object space.
#if (F_WORLD_OVERLAY > 0) && (F_WORLD_OVERLAY_USE_OBJECT_SPACE == 1)
    #define WORLD_OVERLAY_OBJECT_SPACE 1
#else
    #define WORLD_OVERLAY_OBJECT_SPACE 0
#endif

#endif
