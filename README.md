<h1 align="center">VRF / Valve Resource Format</h1>

<p align="center">
    <a href="https://github.com/ValveResourceFormat/ValveResourceFormat/actions">
        <img alt="GitHub Workflow Status" src="https://img.shields.io/github/actions/workflow/status/ValveResourceFormat/ValveResourceFormat/build.yml?logo=github&style=for-the-badge&branch=master">
    </a>
    <a href="https://www.nuget.org/packages/ValveResourceFormat/">
        <img src="https://img.shields.io/nuget/v/ValveResourceFormat.svg?logo=nuget&style=for-the-badge">
    </a>
    <a href="https://app.codecov.io/gh/ValveResourceFormat/ValveResourceFormat">
        <img src="https://img.shields.io/codecov/c/github/ValveResourceFormat/ValveResourceFormat/master?logo=codecov&logoColor=ffffff&style=for-the-badge">
    </a>
</p>

Valve's Source 2 resource file format parser, decompiler, and exporter.
Source 2 files usually files end with `_c`, for example `.vmdl_c`.

This repository is split into three components:
- **CLI Decompiler** - File data viewer, decompiler and a playground for testing new formats and features.
- **GUI Viewer** - A vpk archive viewer and extractor. Also supports viewing resources such as sounds, textures, models, maps, and much more.
- **Library** - Provides public API to parse resource files and some helpers.

⚒ [View the official website for downloads](https://valveresourceformat.github.io/).

## Join our Discord

[![Join our Discord](https://discord.com/api/guilds/467730051622764565/embed.png?style=banner2)](https://steamdb.info/discord/)

## Eye catchy screenshots
<table>
	<tr>
		<td><img src="https://valveresourceformat.github.io/static/screen_map.png"></td>
		<td><img src="https://valveresourceformat.github.io/static/screen_texture.png"></td>
	</tr>
	<tr>
		<td><img src="https://valveresourceformat.github.io/static/screen_package.png"></td>
		<td><img src="https://valveresourceformat.github.io/static/screen_cli.png"></td>
	</tr>
</table>

## What's supported?
- Model viewer and decompiler
- Map viewer and decompiler
- Sound player
- VPK viewer which supports opening and exporting files
- Read only VPK API
- Binary KeyValues3 parser
- NTRO support

## Why does VRF suck?

This tool is based entirely on a reverse engineered effort because Valve does not provide any documentation or Source 2 code (SDK or engine code), while the Source 1 SDK and leaked engine code are helpful, a lot of systems and formats have changed.

The code contained in this repository is based on countless hours of reverse engineering Source 2 games and not all intricate details have been figured out.

If you are interested in helping, take a look at the open issues.

Not all formats are 100% supported, some parameters are still unknown and not fully understood.

## Supported resource types
Ext      | Name                    | Support
-------- | ----------------------- | -------
vanim    | Animation               | 👍
vagrp    | Animation Group         | 👍
vanmgrph | Animation Graph         | No
vseq     | Sequence Group          | No
vpcf     | Particle System         | 👍
vmat     | Material                | 👍
vmks     | Sheet                   | No
vmesh    | Mesh                    | 👍
vtex     | Compiled Texture        | 👍
vmdl     | Model                   | 👍
vphys    | Physics Collision Mesh  | 👍
vsnd     | Sound                   | 👍
vmorf    | MorphSet                | No
vrman    | ResourceManifest        | 👍
vwrld    | World                   | 👍
vwnod    | WorldNode               | 👍
vvis     | WorldVisibility         | No
vents    | EntityLump              | 👍
vsurf    | Surface Properties      | No
vsndevts | Sound Event Script      | 👍
vsndstck | Sound Stack Script      | 👍
vpost    | Postprocessing Settings | 👍
vrmap    | Resource Remap Table    | No
vcss     | Panorama Style          | 👍
vxml     | Panorama Layout         | 👍
vpdi     | Panorama Dynamic Images | No
vjs      | Panorama Script         | 👍
vsvg     | Panorama Vector Graphic | 👍
vsnap    | Particle Snapshot       | 👍
~~vpsf~~ | ~~Particle Snapshot~~   | No
vmap     | Map                     | 👍
vrr      | Response rules          | 👍
&nbsp;   | &nbsp;                  | &nbsp;
vpk      | Pak (package)           | 👍 Handled by [ValvePak](https://github.com/ValveResourceFormat/ValvePak)
vcs      | Compiled Shader         | 👍 Partially supported by `CompiledShader`
vfont    | Bitmap Font             | 👍 Decrypts `VFONT1`, supported in Source 1 and Source 2.
dat      | Closed Captions         | 👍 Handled by `ClosedCaptions`
bin      | Tools Asset Info        | 👍 Partially handled by `ToolsAssetInfo`
vdpn     | Dota Patch Notes        | 👍
vdacdefs | DAC Game Defs Data      | No
vfe      | Face poser              | No
vcd      | VCD                     | No
vcdlist  | VCD list                | No

## List of supported magics
Magic        | Description
------------ | ------------
`0x03564B56` | VKV\x03 - First binary keyvalues 3 encoding with custom block compression
`0x4B563301` | KV3\x01 - Binary keyvalues 3 (version 1)
`0x4B563302` | KV3\x02 - Binary keyvalues 3 (version 2)
`0x4B563303` | KV3\x03 - Binary keyvalues 3 (version 3)
`0x4B563304` | KV3\x04 - Binary keyvalues 3 (version 4)
`0x564B4256` | VBKV - binary keyvalues 1 (handled by ValveKeyvalue)
`0x55AA1234` | VPK - valve package (handled by ValvePak)
`0x44434356` | VCCD - closed captions
`0xC4CCACE8` | tools asset info
`0xC4CCACE9` | tools asset info (newer version)
`0x32736376` | vcs2 - compiled shader
`0x31415926` | murmurhash2 seed used in various places (like entity keys)
`VFONT1`     | "encrypted" font file

## License

Contents of this repository are available under [MIT license](LICENSE), except for `Tests/Files` folder contains files which have likely come from Valve's games.
