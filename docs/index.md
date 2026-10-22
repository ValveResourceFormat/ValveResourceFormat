---
title: "Source 2 Viewer Docs: Open & Extract VPK Files, Export Models"
titleTemplate: false
description: "Guides and FAQ for Source 2 Viewer: open and extract VPK files, export models, maps, textures and sounds from CS2, Dota 2, Deadlock and Half-Life: Alyx."
---

# Source 2 Viewer Documentation

**Source 2 Viewer** (also known as **S2V**) is a free, open-source tool that allows you to browse VPK archives, view, extract, and decompile Source 2 assets, including maps, models, materials, textures, sounds, and particles from Counter-Strike 2, Dota 2, Half-Life: Alyx, Deadlock, and more. The project is reverse-engineered without access to an official Source 2 SDK and is not affiliated with Valve Software.

[**Download Source 2 Viewer**](https://s2v.app/) | [View on GitHub](https://github.com/ValveResourceFormat/ValveResourceFormat)

To get started, see [Getting Started](guides/getting-started.md).

## What is Source 2 Viewer?

Source 2 Viewer is a modern, actively maintained tool for browsing, extracting, and viewing assets from Valve's Source 2 engine games. Whether you're a game modder, map creator, texture artist, or simply curious about how your favorite Source 2 games work, Source 2 Viewer provides everything you need to explore game files.

### Key Features

- **3D Rendering** - Interactive viewer for maps, models, physics, and particles with PBR shaders, animation playback
- **Texture Tools** - Channel isolation, cubemap/HDR support, normal map visualization, SVG rasterization, and export capabilities
- **VPK Management** - Browse and extract archives with advanced search, deleted file recovery, and creating and editing archives
- **Export Systems** - glTF 2.0 compatibility for maps, models, physics, and animations with batch export functionality
- **Decompilers** - Maps, models, and materials conversion to editable formats for Hammer editor and ModelDoc
- **Audio Player** - Built-in player supporting WAV, MP3, and Source 2 formats with waveform visualization
- **Shader Analysis** - Inspect and convert Source 2 shader files with bytecode conversion from SPIRV
- **Additional Formats** - Navigation meshes, particle systems, keyvalues (KV1/KV2/KV3), closed captions, and hex viewer
- **Game Explorer** - Automatically detects VPK files from all your installed Steam games
- **Free & Open Source** - MIT licensed, community-driven development

## What is ValveResourceFormat?

ValveResourceFormat (VRF) is the library behind Source 2 Viewer and its [command-line utility](guides/command-line.md). It parses, decompiles and exports Source 2 files, and developers can use it in their own projects from [NuGet](https://www.nuget.org/packages/ValveResourceFormat/), together with its OpenGL renderer, [ValveResourceFormat.Renderer](https://www.nuget.org/packages/ValveResourceFormat.Renderer/).

### Related Libraries

- **[ValvePak](https://www.nuget.org/packages/ValvePak/)** - VPK archive reading library for .NET
- **[ValveKeyValue](https://www.nuget.org/packages/ValveKeyValue/)** - KeyValues1 and KeyValues3 text library for .NET
- **[Datamodel.NET](https://www.nuget.org/packages/KeyValues2/)** - Datamodel Exchange (DMX) and KeyValues2 library for .NET

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

### Production Use in s&box

ValveResourceFormat is used in production by **[s&box](https://sbox.game/)** (a Source 2 game platform by Facepunch Studios). Despite having access to the Source 2 source engine code, Facepunch uses VRF on their backend to scan and extract information from assets.

In their [asset system](https://sbox.game/news/asset-system#asset-information), VRF is used to:

- **Automatically scan uploaded assets** without requiring republishing
- **Extract and verify metadata** from models and materials
- **[Retroactively tag and sort assets](https://sbox.game/news/march2022)** in their marketplace

This demonstrates VRF's reliability: even developers with official Source 2 SDK access choose VRF for programmatic asset processing.

## Crediting Source 2 Viewer

If you ship our binaries or use parts of our code, the [MIT license](https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/LICENSE) requires you to keep the copyright notice with it.

Source 2 Viewer is the result of years of reverse engineering by many contributors. If it helped anywhere in your project, even if you only ran it once to export some assets, please mention it with a link. If your project is substantially built on it, link it in the app itself too, not just the readme.

## Frequently Asked Questions

### Does Source 2 Viewer work on Mac and Linux?

Source 2 Viewer is currently a .NET WinForms application, as a result it only works on Windows. However you can run it using Wine.
The command-line utility is completely cross-platform.

If it crashes on startup under Wine or Proton with a `stack overflow` error, check whether `DOTNET_BUNDLE_EXTRACT_BASE_DIR` is set in your environment. Wine passes it to the app as a Linux path, which the .NET host fails to create. Create the directory it points to, for example `mkdir -p ~/.cache/dotnet_bundle_extract`.

### How do I open VPK files?

Simply launch Source 2 Viewer and use the Game Explorer to automatically detect installed games, or use File > Open to manually browse for .vpk files. See [Getting Started](./guides/getting-started.md) and [VPK Management](./guides/vpk-management.md).

Under Wine, the Explorer only finds games installed by a Windows Steam in the same Wine prefix, so games from a native Linux or macOS Steam are not listed. Open their packages with File > Open instead, from your Steam library on the `Z:` drive. Files opened from a game's folder still load the textures and models they use from that game, and **Add to Bookmarks** keeps a package in the Explorer for next time.

### Why does a file fail to open after a game update?

Games change their file formats when they update, and the stable release of Source 2 Viewer can lag behind. Files that fail to load show an error tab, which says when the file may use a newer format and whether a newer build is available.

Try the latest development build, as [Getting the Latest Dev Build](./guides/reporting-issues.md#getting-the-latest-dev-build) shows. If the error persists on the latest build, [report the issue](./guides/reporting-issues.md) and paste the whole error text, which includes your version.

### Can I extract textures and convert them to PNG/JPG?

Yes, Source 2 Viewer can preview .vtex files and export them to common image formats. See [Exporting Textures](./guides/exporting-textures.md).

### Is Source 2 Viewer still being updated?

Yes, Source 2 Viewer is actively maintained and regularly updated to support new Source 2 games and file format changes.

### Why is Source 2 Viewer not opening?

If you double click the executable and nothing happens, the most likely cause is that your Windows is missing CET (Control-flow Enforcement Technology) support. Please update your Windows.

### How do I extract models for use in Blender or other 3D software?

Source 2 Viewer can export models, meshes, maps, physics shapes, animation clips and navigation meshes into a standard glTF format which is supported by a lot of software including Blender. See [Exporting Models](./guides/exporting-models.md) and [Exporting Maps](./guides/exporting-maps.md).

### Does Source 2 Viewer support Source 1 games like Team Fortress 2 or Half-Life 2?

No, Source 2 Viewer only supports Source 2 games. For Source 1 and GoldSrc games, check out **[VPKEdit](https://github.com/craftablescience/VPKEdit)**. It's basically Source 2 Viewer but for older games on Source and GoldSource engines.

Source 2 does not use the Source 1 BSP (Binary Space Partitioning) format.

## Alternative to GCFScape

**Source 2 Viewer is the modern alternative to GCFScape** for working with Valve package files.

GCFScape was a popular utility created by Nem for extracting and browsing Valve package files (GCF, VPK) from Source 1 games. It was part of a suite of utilities known as Nem's Tools, which also included VTFEdit (texture editor), VTFLib (texture library), and BSPSource (map decompiler). However, these tools are now abandoned by their developer. While they may still work for older Source 1 files, there is no longer an official download available, and they do not support modern Source 2 file formats.

- **Active Development** - Regular updates and bug fixes
- **Source 2 Support** - Full support for modern Source 2 file formats
- **Game Explorer** - Automatically detects VPK files from all installed Steam games
- **Asset Previewing** - View assets directly without extraction
- **Decompilation** - Convert compiled resources to readable formats

## Community & Support

### Get Help

- **Discord** - [Join our Discord](https://discord.gg/s9QQ7Wg7r4) and visit the #source2-viewer channel
- **GitHub Issues** - [Report bugs or request features](./guides/reporting-issues.md)

### Contribute

Source 2 Viewer is open-source and welcomes contributions! Whether you're a developer, designer, or documentation writer, check out the [Contributing Guide](https://github.com/ValveResourceFormat/ValveResourceFormat#contributing).

### Links

- **Website** - [s2v.app](https://s2v.app/)
- **GitHub** - [ValveResourceFormat/ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)
- **NuGet Package** - [ValveResourceFormat](https://www.nuget.org/packages/ValveResourceFormat/)
- **License** - [MIT License](https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/LICENSE)
