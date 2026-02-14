#version 460

// Common features
#define F_LAYERS 0
#define F_BLEND 0
#define F_PAINT_VERTEX_COLORS 0 // GameVfx_csgo_static_overlay
#define F_SECONDARY_UV 0
#define F_FORCE_UV2 0
#define F_DETAIL_TEXTURE 0
#define F_FOLIAGE_ANIMATION 0

#if ((GameVfx_csgo_foliage != 0) || ((GameVfx_vr_complex != 0) && (F_FOLIAGE_ANIMATION > 0)))
    #define foliage_vfx_common
#endif

#if (GameVfx_vr_standard != 0) && (F_BLEND > 0)
    #define vr_standard_vfx_blend
#endif
