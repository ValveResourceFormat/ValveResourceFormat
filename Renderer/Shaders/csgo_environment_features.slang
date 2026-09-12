#version 460

#define F_SECONDARY_AO 0 // csgo_environment, csgo_environment_blend (not yet implemented)

// Blend specific features
#if (GameVfx_csgo_environment_blend != 0)

    // Blend Effects, per layer. F_BORDER_ROUGHNESS_n and F_BORDER_BLEND_MODE_n are uniforms
    // rather than combos, matching the compiled shader.
    #define F_BLEND_EFFECTS_2 0 // csgo_environment_blend, csgo_static_overlay
    #define F_BLEND_EFFECTS_3 0 // csgo_environment_blend

    #define F_SHARED_COLOR_OVERLAY 0 // csgo_environment_blend
    #define F_ENABLE_LAYER_3 0 // csgo_environment_blend
    #define F_DEPTH_BIAS 0 // csgo_complex, csgo_depth_only, csgo_environment_blend, csgo_vertexlitgeneric
#endif

#define F_DETAIL_NORMAL 0 // csgo_environment, csgo_environment_blend, vr_standard
#define F_ALPHA_TEST 0 // csgo_environment, csgo_environment_blend (and many others via complex_features)
