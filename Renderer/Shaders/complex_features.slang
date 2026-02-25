#version 460

// Common features
#define F_LAYERS 0 // csgo_lightmappedgeneric
#define F_BLEND 0 // blend, vr_simple, vr_standard, vr_substance_painter, vr_xen_foliage
#define F_PAINT_VERTEX_COLORS 0 // GameVfx_csgo_static_overlay (citycrawl_building_anim, csgo_complex, csgo_depth_only, csgo_glass, csgo_simple, csgo_simple_2way_blend, csgo_static_overlay, generic, global_lit_boardspray, global_lit_erosion, global_lit_simple, liquid_fx, pbr)
#define F_SECONDARY_UV 0 // csgo_complex, csgo_simple_3layer_parallax, depth_only_bindless, pbr, vr_complex
#define F_FORCE_UV2 0 // csgo_vertexlitgeneric
#define F_DETAIL_TEXTURE 0 // csgo_character, csgo_complex, csgo_projected_decals, csgo_vertexlitgeneric, csgo_weapon, vr_complex, vr_xen_foliage
#define F_FOLIAGE_ANIMATION 0 // csgo_foliage, depth_only, vr_complex, vr_depth_only, vr_depth_only_nonbindless

#if ((GameVfx_csgo_foliage != 0) || ((GameVfx_vr_complex != 0) && (F_FOLIAGE_ANIMATION > 0)))
    #define foliage_vfx_common
#endif

#if (GameVfx_vr_standard != 0) && (F_BLEND > 0)
    #define vr_standard_vfx_blend
#endif
