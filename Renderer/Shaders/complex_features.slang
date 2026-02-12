#version 460

import common.features;

// Common features (overridden by config module)
public extern static const int F_LAYERS = 0;
public extern static const int F_BLEND = 0;
public extern static const int F_PAINT_VERTEX_COLORS = 0;
public extern static const int F_SECONDARY_UV = 0;
public extern static const int F_FORCE_UV2 = 0;
public extern static const int F_DETAIL_TEXTURE = 0;
public extern static const int F_FOLIAGE_ANIMATION = 0;

// Variant names (overridden by config module via export static const int xxx_vfx=1)
public extern static const int csgo_foliage_vfx = 0;
public extern static const int csgo_complex_vfx = 0;
public extern static const int vr_simple_vfx = 0;
public extern static const int csgo_simple_vfx = 0;
public extern static const int vr_simple_2way_blend_vfx = 0;
public extern static const int csgo_simple_2way_blend_vfx = 0;
public extern static const int steampal_2way_blend_mask_vfx = 0;
public extern static const int vr_glass_vfx = 0;
public extern static const int vr_glass_markable_vfx = 0;
public extern static const int csgo_glass_vfx = 0;
public extern static const int csgo_lightmappedgeneric_vfx = 0;
public extern static const int csgo_vertexlitgeneric_vfx = 0;
public extern static const int vr_static_overlay_vfx = 0;
public extern static const int csgo_static_overlay_vfx = 0;
public extern static const int csgo_character_vfx = 0;
public extern static const int csgo_weapon_vfx = 0;
public extern static const int vr_standard_vfx = 0;
public extern static const int vr_skin_vfx = 0;
public extern static const int generic_vfx = 0;
public extern static const int pbr_vfx = 0;
public extern static const int vr_unlit_vfx = 0;
public extern static const int unlit_vfx = 0;
public extern static const int csgo_unlitgeneric_vfx = 0;
public extern static const int csgo_decalmodulate_vfx = 0;
public extern static const int crystal_vfx = 0;
public extern static const int vr_eyeball_vfx = 0;
public extern static const int vr_energy_field_vfx = 0;
public extern static const int environment_blend_vfx = 0;
public extern static const int vr_simple_2way_parallax_vfx = 0;
public extern static const int vr_simple_3way_parallax_vfx = 0;
public extern static const int vr_simple_blend_to_triplanar_vfx = 0;
public extern static const int vr_simple_blend_to_xen_membrane_vfx = 0;

// Derived group booleans
public static const bool foliage_vfx_common = (csgo_foliage_vfx != 0) || (vr_complex_vfx != 0 && F_FOLIAGE_ANIMATION > 0);

public static const bool vr_standard_vfx_blend = (vr_standard_vfx != 0) && (F_BLEND > 0);

public static const bool complex_vfx_common = (vr_complex_vfx != 0) || (csgo_complex_vfx != 0);
public static const bool simple_vfx_common = (vr_simple_vfx != 0) || (csgo_simple_vfx != 0);
public static const bool simple_blend_common = (vr_simple_2way_blend_vfx != 0) || (csgo_simple_2way_blend_vfx != 0) || (steampal_2way_blend_mask_vfx != 0);
public static const bool glass_vfx_common = (vr_glass_vfx != 0) || (vr_glass_markable_vfx != 0) || (csgo_glass_vfx != 0);
public static const bool csgo_generic_vfx_common = (csgo_lightmappedgeneric_vfx != 0) || (csgo_vertexlitgeneric_vfx != 0);
public static const bool static_overlay_vfx_common = (vr_static_overlay_vfx != 0) || (csgo_static_overlay_vfx != 0);

public static const bool simple_2way_blend_vfx = (vr_simple_2way_blend_vfx != 0) || (csgo_simple_2way_blend_vfx != 0);
public static const bool vr_blend_vfx_common = (vr_simple_2way_blend_vfx != 0) || (vr_simple_2way_parallax_vfx != 0) || (vr_simple_3way_parallax_vfx != 0) || (vr_simple_blend_to_triplanar_vfx != 0) || (vr_simple_blend_to_xen_membrane_vfx != 0);
