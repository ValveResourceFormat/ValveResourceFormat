# Valve Resource Format Renderer

OpenGL-based rendering engine for Source 2 game assets. Powers the [Source 2 Viewer](https://valveresourceformat.github.io).

## ⚠️ Breaking Changes Notice

**The primary user of this library is the [Source 2 Viewer](https://valveresourceformat.github.io).** As such, updates may contain breaking changes and backwards incompatible API changes, as the viewer does not require backwards compatibility with older library versions. Additionally, Source 2 games themselves may update and change file formats at any time, which may necessitate breaking changes in this library. **If you need to support newer file formats, you will need to update the library.** That said, we do aim to support older file formats going back to the very first Source 2 project.

## Features

- **3D Models & Maps** - Renders compiled models (`.vmdl_c`), maps (`.vwrld_c`, `.vmap_c`)
- **Physically-Based Rendering** - Full PBR pipeline with metalness/roughness workflow
- **Advanced Lighting** - Dynamic shadows, lightmaps, environment maps, light probes, and fog systems
- **Animation** - Skeletal animation, morph targets, and flex animation support
- **Particle Systems** - Particle system with many operators supported (but many more need implementation)
- **Materials & Shaders** - Material system supporting complex shader features
- **Debugging Tools** - Render modes including normals, UVs, lightmaps, and wireframe visualization
