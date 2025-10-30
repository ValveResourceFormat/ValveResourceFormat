# Source 2 Viewer - VPK File Viewer & Asset Extractor for Source 2 Games

**Source 2 Viewer** (also known as **S2V**) is a free, open-source tool that allows you to browse VPK archives, view, extract, and decompile Source 2 assets, including maps, models, materials, textures, sounds, and particles from Counter-Strike 2, Dota 2, Half-Life: Alyx, Deadlock, and more. The project is reverse-engineered without access to an official Source 2 SDK and is not affiliated with Valve Software.

[**Download Source 2 Viewer**](https://s2v.app/) | [View on GitHub](https://github.com/ValveResourceFormat/ValveResourceFormat)

---

## What is Source 2 Viewer?

Source 2 Viewer is a modern, actively maintained tool for browsing, extracting, and viewing assets from Valve's Source 2 engine games. Whether you're a game modder, map creator, texture artist, or simply curious about how your favorite Source 2 games work, Source 2 Viewer provides everything you need to explore game files.

### Key Features

- **3D Rendering** - Interactive viewer for maps, models, physics, and particles with PBR shaders, animation playback
- **Texture Tools** - Channel isolation, cubemap/HDR support, normal map visualization, SVG rasterization, and export capabilities
- **VPK Management** - Browse and extract archives with advanced search, deleted file recovery, and archive creation
- **Export Systems** - glTF 2.0 compatibility for maps, models, physics, and animations with batch export functionality
- **Decompilers** - Maps, models, and materials conversion to editable formats for Hammer editor and ModelDoc
- **Audio Player** - Built-in player supporting WAV, MP3, and Source 2 formats with waveform visualization
- **Shader Analysis** - Inspect and convert Source 2 shader files with bytecode conversion from SPIRV
- **Additional Formats** - Navigation meshes, particle systems, keyvalues (KV1/KV2/KV3), closed captions, and hex viewer
- **Game Explorer** - Automatically detects VPK files from all your installed Steam games
- **Free & Open Source** - MIT licensed, community-driven development

---

## What is ValveResourceFormat?

ValveResourceFormat (VRF) is the underlying library that powers Source 2 Viewer. It's a comprehensive Source 2 resource file format parser, decompiler, and exporter. The project consists of three main components:

1. **Library** - Public API for parsing and decompiling Source 2 resources
2. **GUI Viewer (Source 2 Viewer)** - User-friendly interface for browsing VPK archives and viewing assets
3. **CLI Decompiler** - Command-line tool for batch processing and automation

Developers can integrate ValveResourceFormat into their own projects via [NuGet](https://www.nuget.org/packages/ValveResourceFormat/) to programmatically work with Source 2 game files.

### Related Libraries

- **[ValvePak](https://www.nuget.org/packages/ValvePak/)** - VPK archive reading library for .NET
- **[ValveKeyValue](https://www.nuget.org/packages/ValveKeyValue/)** - KeyValues format support library (KV1/KV2/KV3)

---

## Supported Games

Source 2 Viewer supports **all Source 2 games**, including:

- **Counter-Strike 2**
- **Dota 2**
- **Deadlock**
- **Half-Life: Alyx**
- **SteamVR**
- **Aperture Desk Job**
- **Artifact**
- **Dota Underlords**
- **The Lab**

Supports Source 2 games updated through current release versions. [See complete game list on SteamDB.](https://steamdb.info/tech/Engine/Source2/)

Note: s&box is partially supported, but we do not provide full support for it. s&box uses a heavily modified version of Source 2 that doesn't quite align with Valve's version of the engine, which can cause compatibility issues.

## Use Cases

Source 2 Viewer is used by:

- **Game Modders** - Extract and modify game assets for custom mods
- **Map Creators** - Study official maps and extract assets for custom map projects
- **Texture Artists** - Extract textures and materials for reference or modification
- **3D Artists** - Export models for use in other 3D software
- **Content Creators** - Extract assets for videos, thumbnails, and promotional content
- **Game Researchers** - Analyze game file structures and reverse-engineer formats
- **Students & Educators** - Learn about game development and asset pipelines
- **Enthusiasts** - Explore and appreciate the artistry behind Source 2 games

---

## Frequently Asked Questions

### Does Source 2 Viewer work on Mac and Linux?
Source 2 Viewer is currently a .NET Winforms Forms application, as a result it only works on Windows. However you can run it using Wine.
The command-line utility is completely cross-platform.

### How do I open VPK files?
Simply launch Source 2 Viewer and use the Game Explorer to automatically detect installed games, or use File > Open to manually browse for .vpk files.

### Can I extract textures and convert them to PNG/JPG?
Yes, Source 2 Viewer can preview .vtex files and export them to common image formats.

### Can I extract files from Counter-Strike 2 (CS2)?
Yes, Source 2 Viewer fully supports CS2, including models, textures, materials, maps, and sounds.

### Is Source 2 Viewer still being updated?
Yes, Source 2 Viewer is actively maintained and regularly updated to support new Source 2 games and file format changes.

### How do I extract models for use in Blender or other 3D software?
Source 2 Viewer can export .vmdl, .vmesh, .vwrld, .vwnod, and .vmap files into a standard glTF format which is supported by a lot of software including Blender.

### Does Source 2 use BSP files like Source 1?
No, Source 2 does not use the traditional BSP (Binary Space Partitioning) format. Source 1 games used .bsp files for compiled maps with BSP tree structures for rendering optimization. Source 2 instead uses mesh-based maps. This represents a fundamental shift in how Source 2 handles level geometry and rendering.

---

## Alternative to GCFScape

**Source 2 Viewer is the modern alternative to GCFScape** for working with Valve package files.

GCFScape was a popular utility created by Nem for extracting and browsing Valve package files (GCF, VPK) from Source 1 games. It was part of a suite of utilities known as Nem's Tools, which also included VTFEdit (texture editor), VTFLib (texture library), and BSPSource (map decompiler). However, these tools are now abandoned by their developer. While they may still work for older Source 1 files, there is no longer an official download available, and they do not support modern Source 2 file formats.

- **Active Development** - Regular updates and bug fixes
- **Source 2 Support** - Full support for modern Source 2 file formats
- **Game Explorer** - Automatically detects VPK files from all installed Steam games
- **Asset Previewing** - View assets directly without extraction
- **Decompilation** - Convert compiled resources to readable formats

---

## Community & Support

### Get Help
- **Discord** - Join the [SteamDB Discord](https://steamdb.info/discord/) and visit the #source2-viewer channel
- **GitHub Issues** - [Report bugs or request features](https://github.com/ValveResourceFormat/ValveResourceFormat/issues)

### Contribute
Source 2 Viewer is open-source and welcomes contributions! Whether you're a developer, designer, or documentation writer, check out the [Contributing Guide](https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/CONTRIBUTING.md).

### Links
- **Website** - [s2v.app](https://s2v.app/)
- **GitHub** - [ValveResourceFormat/ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)
- **NuGet Package** - [ValveResourceFormat](https://www.nuget.org/packages/ValveResourceFormat/)
- **License** - [MIT License](https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/LICENSE)
