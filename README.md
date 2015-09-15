# Valve Resource Format [<img src="https://travis-ci.org/SteamDatabase/ValveResourceFormat.svg?branch=master" align="right">](https://travis-ci.org/SteamDatabase/ValveResourceFormat)
Valve's Source 2 resource file format *(also known as Stupid Valve Format)* parser and decompiler.

### Decompiler usage
```
Usage: decompiler <path>
  Path can be a single file, a list of files, or a directory.
```

## Resource types
Dota 2 lists supported asset types in a `sdkassettypes.txt` file.
This file contains information about each type (name, icon, extension, etc.).
`m_CompilerIdentifier` string is stored in every resource file indicating which file format it is.

### Known resource types
The plan is to support every listed type in this library, but right now that is not the case.

Ext | Name | Friendly Name | Resource Module | Compiler Identifier
--- | ---- | ------------- | --------------- | -------------------
vanim | animation_asset | Animation | animationsystem | CompileAnimation
vagrp | animgroup_asset | Animation Group | animationsystem | CompileAnimGroup
vseq | sequence_asset | Sequence Group | animationsystem | CompileSequence
vpcf | particle_asset | Particle System | particles | CompileParticle *(KV3)*
vmat | material_asset | Material | materialsystem2 | CompileMaterial
vmks | sheet_asset | Sheet | rendersystemdx9 | :worried:
vmesh | mesh_asset | Mesh | meshsystem | CompileRenderMesh *(KV3)*
vtex | texture_asset | Compiled Texture | rendersystemdx9 | CompileTexture
vmdl | model_asset | Model | worldrenderer | CompileModel
vphys | physics_collision | Physics Collision Mesh | vphysics2 | CompileVPhysXData
vsnd | sound_asset | Sound | soundsystem | CompileSound
vmorf | morphset_asset | MorphSet | meshsystem | CompileMorph
vrman | resourcemanifest_asset | ResourceManifest | *None* | CompileResourceManifest
vwrld | world_asset | World | worldrenderer | CompileWorld
vwnod | worldnode_asset | WorldNode | worldrenderer | CompileWorldNode
vvis | worldvis_asset | WorldVisibility | worldrenderer | CompileMapVisibility
vents | entitylump_asset | EntityLump | worldrenderer | CompileEntityLump
vsurf | surface_properties | Surface Properties | vphysics2 | CompileSurfaceProperties
vsndevts | sound_event_script | Sound Event Script | soundsystem | CompileSoundEventScript
vsndstck | sound_stack_script | Sound Stack Script | soundsystem | CompileSoundStackScript
vfont | bitmap_font | Bitmap Font | *None* | CompileFont
vrmap | resource_remap_table | Resource Remap Table | worldrenderer | CompileResourceRemapTable
vcss | panorama_style | Panorama Style | *None* | CompilePanorama
vxml | panorama_layout | Panorama Layout | *None* | CompilePanorama
vpdi | panorama_dynamic_images | Panorama Dynamic Images | *None* | CompilePanorama
vjs | panorama_script | Panorama Script | *None* | CompilePanorama
vpsf | particle_snapshot | Particle Snapshot | *None* | CompilePsf
vmap | map_asset | Map | worldrenderer | CompileMap
