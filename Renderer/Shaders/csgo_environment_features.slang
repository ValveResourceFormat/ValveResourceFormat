#version 460

// Blend specific features
#if (GameVfx_csgo_environment_blend != 0)

    // Blend Effects
    #define F_BLEND_EFFECTS_2 0 // csgo_environment_blend
    #define F_BLEND_EFFECTS_3 0 // csgo_environment_blend

    #define F_SHARED_COLOR_OVERLAY 0 // csgo_environment_blend
    #define F_ENABLE_LAYER_3 0 // csgo_environment_blend
    #define F_DEPTH_BIAS 0 // csgo_complex, csgo_depth_only, csgo_environment_blend, csgo_vertexlitgeneric
#endif

#define F_DETAIL_NORMAL 0 // csgo_environment, csgo_environment_blend, vr_standard
#define F_ALPHA_TEST 0 // csgo_environment, csgo_environment_blend (and many others via complex_features)
