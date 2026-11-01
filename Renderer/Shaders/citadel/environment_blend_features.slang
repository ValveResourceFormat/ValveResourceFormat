#version 460

#define F_LAYER_COUNT 0 // 0="2 Layers", 1="3 Layers"

#define F_LAYER1_BIPLANAR 0
#define F_LAYER2_BIPLANAR 0
#define F_LAYER3_BIPLANAR 0

#define F_UV_OVERLAY 0
#define F_WORLD_OVERLAY 0 // 0="Off", 1="1 World Overlay", 2="2 World Overlays"
#define F_WORLD_OVERLAY_USE_OBJECT_SPACE 0

// Writes the bloom amount of the pixel for the bloom threshold
#define F_OVERRIDE_BLOOM_AMOUNT 0
