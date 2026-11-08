# Source 1 and GoldSrc

Source 1 and GoldSrc games open in the GUI viewer, which converts their files to Source 2 resources in memory. The CLI does not open them and the library is not published.

## Supported

### Source 1

- Mounting from `gameinfo.txt`:
  - search paths
  - `|gameinfo_path|`, `|all_source_engine_paths|` and `|appid_N|`
  - wildcard folders
  - VPK v1 and v2
  - `update`, `_dlcN` and `platform` folders
  - the map pakfile
- BSP v17 to v23 (Bloodlines, INFRA, Tactical Intervention, Dota 2 before Reborn) and Strata v25:
  - L4D2 lump header
  - Dark Messiah maps
  - LZMA lumps
  - `_l_N.lmp` lump files
  - displacements
  - overlays and water overlays
  - world and brush entity collision, with clips and water
  - static props v4 to v13
  - visibility from the node tree and PVS
  - per-face cubemaps
  - light entities
  - projected textures
  - `point_spotlight` and `beam_spotlight` beams and halos
  - animated light styles of `light` and `light_spot`, as dynamic lights
  - picking the faces of one brush with one material
- MDL v44 to v49 with VVD, VTX, PHY and ANI:
  - include models
  - skins and bodygroups
  - sequences, blends and pose parameters
  - procedural bones
  - jiggle bones
  - collision per solid
  - flexes and flex rules
  - bone flex drivers
  - bone controllers, as pose parameters
  - ragdoll constraints, as physics joints
  - eyes and eyelids that follow the camera
- Scenes from `scenes/scenes.image`, read but not played
- VTF 7.0 to 7.6
- VMT:
  - `#base`, `#include` and patch
  - fallbacks and conditions
  - proxies, evaluated once
  - constant-rate texture scrolls and animated textures, run over time
  - `$envmaplightscale` and `$envmaplightscaleminmax` on LightmappedGeneric
- Shaders:
  - LightmappedGeneric
  - WorldVertexTransition
  - Lightmapped_4WayBlend
  - VertexLitGeneric
  - UnlitGeneric and UnlitTwoTexture
  - Modulate
  - MonitorScreen
  - WorldTwoTextureBlend
  - Water (cheap), with flow maps and their color layer
  - Refract
  - SolidEnergy, with flow maps and detail layers
  - Eyes and Teeth
  - Infected
  - Sprite
  - Cable
  - DecalModulate
  - detail sprites
- Real-time sun shadows on the world and static props where the bake stores sun amounts (CS:GO)
- Static prop vertex lighting (`sp_N.vhv`, `sp_hdr_N.vhv`)
- PCF particles
- Entity classes from each game's FGD files, and Hammer tool visuals
- Sounds:
  - WAV (PCM, Microsoft ADPCM)
  - MP3
  - game sound scripts
  - soundscapes
  - animation event sounds
- Surface properties and footstep sounds
- Bullet impact decals
- `.nav` meshes (v15, v16)
- Sprite animation by `framerate`

### GoldSrc

- Mounting from `liblist.gam`:
  - `fallback_dir`
  - `_addon`, `_hd` and `_downloads` folders
- BSP v30, including Blue Shift:
  - WAD3 textures
  - render modes
  - animated and random textures
  - the sky
  - tool textures
  - `infodecal` decals
  - visibility
  - collision
- MDL v10:
  - `T.mdl` textures
  - sequence groups
  - chrome, additive and masked textures
  - bone controllers
  - render modes of the entities that place them, with additive models unlit
- SPR v2, animated by `framerate`
- WAD files, browsed like a package
- Entity classes for Half-Life and Counter-Strike
- Sounds:
  - WAV
  - animation event sounds
- Surface properties from `sound/materials.txt` and footstep sounds

### Export

- Textures, materials, models and maps export like Source 2 resources: PNG, glTF and decompiled vmat, vmdl and vmap with the models, materials and textures they draw, and for maps the tools materials of brushes; the cube map and light probe textures are left out, as Hammer bakes them again. Each cube map becomes a combined light probe volume with a box fitted to its room, nested by priority, below one volume that covers the world
- Export with dependencies saves the file and the game files its conversion reads into a folder laid out like the game, with a `gameinfo.txt` or `liblist.gam`, including the sounds and scripts that entities, soundscapes and animation events name, and the models and icons that stand in for entities

## Not supported

### Source 1

- BSP:
  - before v17
  - Vindictus
  - console maps
- Static props:
  - screen-size fades
  - Black Mesa vertex lighting
- Models:
  - MDL before v44, and v50 to v53
  - VTX other than v7
  - VVC
  - `.phz`
  - ragdoll joint friction, and the limit of the narrower swing axis
  - collision shapes with open surfaces, or too many points to split
  - most IK rules
  - jiggle boing
- Scenes:
  - playback
  - text `.vcd` files
- VTF:
  - 7.6 deflate compression
  - console textures
  - volume textures (first slice only)
- Materials:
  - proxies other than constant-rate scrolls and animated textures
  - entity-driven proxies
  - water reflection and refraction
  - lightmap light on the color flow layer of water
  - Portal 2 ignoring `$flow_timescale`
  - `$seamless_base` and `$seamless_detail`
  - tree sway
  - malformed VMT files that the game still reads
- Shaders:
  - SolidEnergy view-angle opacity, fizzler vortices and power-up
  - Portal shaders
  - WindowImposter
  - screenspace_general
  - CS:GO character and weapon shaders
  - SpriteCard
  - SplineRope
  - Grass
  - ShatteredGlass
  - Core
  - EyeBall
  - VolumeClouds
  - Black Mesa shaders
  - TF2 item effects
- Entities:
  - spotlights that rotate, toggle or follow a parent
  - `color_correction` and `color_correction_volume`
  - shadows of lights with animated styles
  - sprite Play Once and start frame
  - VGUI screens
- Impact particle effects and flesh decals
- Sounds:
  - `$gender` entries
  - DSP presets
  - soundscape positions and `env_soundscape_proxy`
- Mounting:
  - folders of languages other than English
  - GMA addons

### Export

- Decompiled materials, GoldSrc ones too, are written for the shaders of Counter-Strike 2. They keep the textures, blend mode, layers, self illumination, detail, roughness and metalness. CS:GO character materials use the Counter-Strike 2 character shader. Material proxies, environment map reflections and shader specific parameters are left out
- Decompiled maps leave out particles
- Export with dependencies leaves out sounds that entity classes play without naming them, such as the default sounds of doors

### GoldSrc

- BSP v29
- WAD2 and WAD4
- Console and alpha MDL versions
- Triggered, spray and random-scale decals
- Impact sounds per surface
- Studio render mode 1
- Animated light styles
- Render fx that change an entity over time, and the glow shell

## Credits

Formats and behaviour were cross-checked against these projects:

- [SourceUtils](https://github.com/Metapyziks/SourceUtils)
- [Crowbar](https://github.com/ZeqMacaw/Crowbar)
- [SourceIO](https://github.com/REDxEYE/SourceIO)
- [bspsrc](https://github.com/StrataSource/bspsrc)
- [Strata Source wiki](https://github.com/StrataSource/Wiki)
- [srctools](https://github.com/TeamSpen210/srctools)
- [sourcepp](https://github.com/craftablescience/sourcepp)
