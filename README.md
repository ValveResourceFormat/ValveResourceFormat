<h1 align="center">VRF / Valve Resource Format</h1>

<p align="center">
    <a href="https://github.com/SteamDatabase/ValveResourceFormat/actions">
        <img alt="GitHub Workflow Status" src="https://img.shields.io/github/workflow/status/SteamDatabase/ValveResourceFormat/CI?logo=github&style=for-the-badge&logoColor=fff">
    </a>
    <a href="https://ci.appveyor.com/project/xPaw/ValveResourceFormat">
        <img src="https://img.shields.io/appveyor/ci/xPaw/valveresourceformat/master.svg?label=AppVeyor&logo=appveyor&style=for-the-badge&logoColor=fff">
    </a>
    <a href="https://www.nuget.org/packages/ValveResourceFormat/">
        <img src="https://img.shields.io/nuget/v/ValveResourceFormat.svg?label=NuGet&logo=nuget&style=for-the-badge&logoColor=fff&colorB=4c1">
    </a>
    <a href="https://coveralls.io/github/SteamDatabase/ValveResourceFormat">
        <img src="https://img.shields.io/coveralls/SteamDatabase/ValveResourceFormat.svg?label=Tests&logo=coveralls&style=for-the-badge&logoColor=fff">
    </a>
</p>

Valve's Source 2 resource file format parser, decompiler, and exporter.
Source 2 files usually files end with `_c`, for example `.vmdl_c`.

This repository is split into three components:
- **CLI Decompiler** - File data viewer, decompiler and a playground for testing new formats and features.
- **GUI Viewer** - A vpk archive viewer and extractor. Also supports viewing resources such as sounds, textures, models, maps, and much more.
- **Library** - Provides public API to parse resource files and some helpers.

⚒ [You can download latest unstable build from AppVeyor](https://ci.appveyor.com/project/xPaw/valveresourceformat/branch/master/artifacts).

## Chat

[![](https://discordapp.com/api/guilds/250160069549883392/embed.png?style=banner2)](https://discord.gg/3XPWU2c)

**Prefer IRC?** Join [<u>#steamdb-vrf on chat.freenode.net</u>](https://demo.thelounge.chat/?join=%23steamdb-vrf)

## Eye catchy screenshots
<table>
	<tr>
		<td><img src="https://raw.githubusercontent.com/SteamDatabase/ValveResourceFormat/gh-pages/static/screen_map.png"></td>
		<td><img src="https://raw.githubusercontent.com/SteamDatabase/ValveResourceFormat/gh-pages/static/screen_texture.png"></td>
	</tr>
	<tr>
		<td><img src="https://raw.githubusercontent.com/SteamDatabase/ValveResourceFormat/gh-pages/static/screen_package.png"></td>
		<td><img src="https://raw.githubusercontent.com/SteamDatabase/ValveResourceFormat/gh-pages/static/screen_cli.png"></td>
	</tr>
</table>

## What's supported?
- Model viewer
- Map viewer
- Sound player
- VPK viewer which supports opening and exporting files
- Read only VPK API
- Binary KeyValues3 parser
- NTRO support

## Why does VRF suck?

This tool is based entirely on a reverse engineered effort because Valve does not provide any documentation or Source 2 code (SDK or engine code), while the Source 1 SDK and leaked engine code are helpful, a lot of systems and formats have changed.

The code contained in this repository is based on countless hours of reverse engineering Source 2 games and not all intricate details have been figured out.

If you are interested in helping, take a look at the open issues.

## Supported resource types
Ext      | Name                    | Support
-------- | ----------------------- | -------
vanim    | Animation               | 👍
vagrp    | Animation Group         | 👍
vseq     | Sequence Group          | No
vpcf     | Particle System         | 👍 NTRO, KV3
vmat     | Material                | 👍 NTRO
vmks     | Sheet                   | No
vmesh    | Mesh                    | 👍
vtex     | Compiled Texture        | 👍 DXT1, DXT5, I8, RGBA8888, R16, RG1616, RGBA16161616, R16F, RG1616F, RGBA16161616F, R32F, RG3232F, RGB323232F, RGBA32323232F, BC6H, BC7, IA88, PNG, JPG, ETC2, ETC2_EAC, BGRA8888, ATI1N, ATI2N
vmdl     | Model                   | 👍
vphys    | Physics Collision Mesh  | No
vsnd     | Sound                   | 👍 wav, mp3
vmorf    | MorphSet                | No
vrman    | ResourceManifest        | 👍
vwrld    | World                   | 👍
vwnod    | WorldNode               | 👍
vvis     | WorldVisibility         | No
vents    | EntityLump              | 👍
vsurf    | Surface Properties      | No
vsndevts | Sound Event Script      | 👍 KV1, :-1: KV3
vsndstck | Sound Stack Script      | 👍 KV1, :-1: KV3
vrmap    | Resource Remap Table    | No
vcss     | Panorama Style          | 👍
vxml     | Panorama Layout         | 👍
vpdi     | Panorama Dynamic Images | No
vjs      | Panorama Script         | 👍
vsvg     | Panorama Vector Graphic | 👍
vpsf     | Particle Snapshot       | No
vmap     | Map                     | 👍
&nbsp;   | &nbsp;                  | &nbsp;
vpk      | Pak (package)           | 👍 Handled by [ValvePak](https://github.com/SteamDatabase/ValvePak)
vcs      | Compiled Shader         | ❓ Started work in `CompiledShader`, see #151
vfont    | Bitmap Font             | 👍 Decrypts `VFONT1`, supported in Source 1 (CS:GO) and Source 2 (Dota 2).
dat      | Closed Captions         | 👍 Handled by `ClosedCaptions`
bin      | Tools Asset Info        | 👍 Partially handled by `ToolsAssetInfo`, see #226
vdpn     | Dota Patch Notes        | No

Not all formats are 100% supported, some parameters are still unknown and not fully understood.

## License

Contents of this repository are available under [MIT license](LICENSE), except for `Tests/Files` folder contains files which have likely come from Valve's games.
