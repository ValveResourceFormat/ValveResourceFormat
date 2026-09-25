#version 460

// Common features
#define F_LAYERS 0 // csgo_lightmappedgeneric
#define F_PAINT_VERTEX_COLORS 0 // GameVfx_csgo_static_overlay (citycrawl_building_anim, csgo_complex, csgo_depth_only, csgo_glass, csgo_simple, csgo_simple_2way_blend, csgo_static_overlay, generic, global_lit_boardspray, global_lit_erosion, global_lit_simple, liquid_fx, pbr)
#define F_SECONDARY_UV 0 // csgo_complex, csgo_simple_3layer_parallax, depth_only_bindless, pbr, vr_complex
#define F_FORCE_UV2 0 // csgo_vertexlitgeneric
uniform int F_DETAIL_TEXTURE; // csgo_character, csgo_complex, csgo_projected_decals, csgo_vertexlitgeneric, csgo_weapon, vr_complex, vr_xen_foliage
#define F_FOLIAGE_ANIMATION 0 // csgo_foliage, depth_only, vr_complex, vr_depth_only, vr_depth_only_nonbindless
#define F_ENABLE_ADJUSTMENTS 0 // csgo_character

// Detail modes are picked at runtime, so only the shaders that support detail textures pay for them
#if (GameVfx_csgo_complex != 0) || (GameVfx_csgo_vertexlitgeneric != 0) || (GameVfx_csgo_weapon != 0) || (GameVfx_vr_complex != 0) || (GameVfx_vr_xen_foliage != 0)
    #define detail_texture_vfx_common
#endif

#define CS2_CHARACTER_ADJUSTMENTS ((GameVfx_csgo_character != 0) && (F_ENABLE_ADJUSTMENTS == 1))

#if ((GameVfx_csgo_foliage != 0) || ((GameVfx_vr_complex != 0) && (F_FOLIAGE_ANIMATION > 0)))
    #define foliage_vfx_common
#endif
