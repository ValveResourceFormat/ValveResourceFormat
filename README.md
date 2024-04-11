<h1 align="center"><img src="./Misc/Icons/source2viewer.png" width="64" align="center"> Source 2 Viewer</h1>

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

*\* The library component of Source 2 Viewer is called ValveResourceFormat (VRF).*

Valve's Source 2 resource file format parser, decompiler, and exporter.
Source 2 files usually end with `_c`, for example `.vmdl_c`.

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
- VPK viewer which supports opening and exporting files
- Creating new vpk archives
- Model viewer and decompiler to glTF and modeldoc
- Map viewer and decompiler to glTF and vmap
- Material decompiler to vmat
- Sound player
- Binary KeyValues3 parser
- NTRO support

## Limitations

This tool is based entirely on a reverse engineered effort because Valve does not provide any documentation or Source 2 code (SDK or engine code), while the Source 1 SDK and leaked engine code are helpful, a lot of systems and formats have changed.

The code contained in this repository is based on countless hours of reverse engineering Source 2 games and not all intricate details have been figured out.

If you are interested in helping, take a look at the open issues and join our Discord.

Not all formats are 100% supported, some parameters are still unknown and not fully understood.

## Supported resource types
Ext      | Name                    | Support
-------- | ----------------------- | -------
vagrp    | Animation Group         | 👍
vanim    | Animation               | 👍
vanmgrph | Animation Graph         | No
vcompmat | Composite Material      | No
vcss     | Panorama Style          | 👍
vdata    | Data                    | 👍
vents    | EntityLump              | 👍
vjs      | Panorama Script         | 👍
vmap     | Map                     | 👍
vmat     | Material                | 👍
vmdl     | Model                   | 👍
vmesh    | Mesh                    | 👍
vmorf    | MorphSet                | 👍
vpcf     | Particle System         | 👍
vpdi     | Panorama Dynamic Images | No
vphys    | Physics Collision Mesh  | 👍
vpost    | Postprocessing Settings | 👍
vpsf     | Particle Snapshot       | No
vpulse   | Pulse Graph Definition  | No
vrman    | ResourceManifest        | 👍
vrmap    | Resource Remap Table    | No
vrr      | Response rules          | 👍
vrr      | Response Rules          | No
vseq     | Sequence Group          | No
vsmart   | Smart Prop              | Partially
vsnap    | Particle Snapshot       | 👍
vsnd     | Sound                   | 👍
vsndevts | Sound Event Script      | 👍
vsndstck | Sound Stack Script      | 👍
vsurf    | Surface Properties      | No
vsvg     | Panorama Vector Graphic | 👍
vtex     | Compiled Texture        | 👍
vts      | Panorama TypeScript     | 👍
vvis     | WorldVisibility         | No
vwnod    | WorldNode               | 👍
vwrld    | World                   | 👍
vxml     | Panorama Layout         | 👍
&nbsp;   | &nbsp;                  | &nbsp;
vpk      | Pak (package)           | 👍 Handled by [ValvePak](https://github.com/ValveResourceFormat/ValvePak)
vcs      | Compiled Shader         | 👍 Handled by `CompiledShader`
vfont    | Bitmap Font             | 👍 Decrypts `VFONT1`, supported in Source 1 and Source 2.
dat      | Closed Captions         | 👍 Handled by `ClosedCaptions`
bin      | Tools Asset Info        | 👍 Handled by `ToolsAssetInfo`
vdpn     | Dota Patch Notes        | 👍
vdacdefs | DAC Game Defs Data      | No
vfe      | Flex Scene File         | 👍 Handled by `FlexSceneFile`
vcd      | VCD                     | No
vcdlist  | VCD list                | 👍

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
`0x00564645` | VFE - flex scene file

## License

Contents of this repository are available under [MIT license](LICENSE), except for `Tests/Files` folder contains files which have likely come from Valve's games.
