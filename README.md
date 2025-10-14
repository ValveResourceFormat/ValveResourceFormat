<h1 align="center"><img src="./Misc/Icons/source2viewer.png" width="64" align="center"> Source 2 Viewer</h1>

<p align="center">
    <a href="https://github.com/ValveResourceFormat/ValveResourceFormat/actions"><img alt="GitHub Workflow Status" src="https://img.shields.io/github/actions/workflow/status/ValveResourceFormat/ValveResourceFormat/build.yml?logo=github&style=for-the-badge&branch=master"></a>
    <a href="https://www.nuget.org/packages/ValveResourceFormat/"><img src="https://img.shields.io/nuget/v/ValveResourceFormat.svg?logo=nuget&style=for-the-badge"></a>
    <a href="https://app.codecov.io/gh/ValveResourceFormat/ValveResourceFormat"><img src="https://img.shields.io/codecov/c/github/ValveResourceFormat/ValveResourceFormat/master?logo=codecov&logoColor=ffffff&style=for-the-badge"></a>
</p>

*\* The library component of Source 2 Viewer is called ValveResourceFormat (VRF).*

Valve's Source 2 resource file format parser, decompiler, and exporter.
Source 2 files usually end with `_c`, for example `.vmdl_c`.

This repository is split into three components:
- **CLI Decompiler** - File data viewer, decompiler and a playground for testing new formats and features.
- **GUI Viewer** - A vpk archive viewer and extractor. Also supports viewing resources such as sounds, textures, models, maps, and much more.
- **Library** - Provides public API to parse resource files and some helpers.

⚒ [View the official website for downloads](https://valveresourceformat.github.io/).
ℹ️ [View the library documentation here](https://s2v.app/ValveResourceFormat/api/ValveResourceFormat.html).

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
Ext           | Name                              | Support
------------- | --------------------------------- | -------
vagrp         | Animation Group                   | 👍
vanim         | Animation                         | 👍
vanmgrph      | Animation Graph                   | 👍
vcd           | Choreo                            | 👍
vcdlist       | Choreo Scene File Data            | 👍
vcompmat      | Composite Material                | 👍
vcss          | Panorama Style                    | 👍
vdata         | Data                              | 👍
vents         | EntityLump                        | 👍
vjs           | Panorama Script                   | 👍
vmap          | Map                               | 👍
vmat          | Material                          | 👍
vmdl          | Model                             | 👍
vmesh         | Mesh                              | 👍
vmix          | VMix                              | 👍
vmorf         | MorphSet                          | 👍
vnmclip       | NmClip                            | 👍
vnmgrph       | NmGraph                           | 👍
vnmskel       | NmSkeleton                        | 👍
vnmvar        | NmGraph Variation                 | 👍
vpcf          | Particle System                   | 👍
vpdi          | Panorama Dynamic Images           | No
vphys         | Physics Collision Mesh            | 👍
vpost         | Postprocessing Settings           | 👍
vpram         | Processing Graph Instance         | 👍
vpsf          | Particle Snapshot                 | 👍
vpulse        | Pulse Graph Definition            | 👍
vrman         | ResourceManifest                  | 👍
vrmap         | Resource Remap Table              | No
vrr           | Response Rules                    | 👍
vseq          | Sequence Group                    | No
vsmart        | Smart Prop                        | Partially
vsnap         | Particle Snapshot                 | 👍
vsnd          | Sound                             | 👍
vsndevts      | Sound Event Script                | 👍
vsndstck      | Sound Stack Script                | 👍
vsurf         | Surface Properties                | No
vsvg          | Panorama Vector Graphic           | 👍
vtex          | Compiled Texture                  | 👍
vts           | Panorama TypeScript               | 👍
vvis          | World Visibility                  | No
vwnod         | World Node                        | 👍
vwrld         | World                             | 👍
vxml          | Panorama Layout                   | 👍
&nbsp;        | &nbsp;                            | &nbsp;
csgoitem      | CSGO Item                         | 👍
econitem      | CSGO Economy Item                 | 👍
herolist      | Dota Hero List                    | 👍
item          | Artifact Item                     | 👍
vdpn          | Dota Patch Notes                  | 👍
vdvn          | Dota Visual Novels                | 👍
&nbsp;        | &nbsp;                            | &nbsp;
bin           | Tools Asset Info                  | 👍 Handled by `ToolsAssetInfo`
dat           | Closed Captions                   | 👍 Handled by `ClosedCaptions`
vcs           | Compiled Shader                   | 👍 Handled by `CompiledShader`
vdacdefs      | DAC Game Defs Data                | No
vfe           | Flex Scene File                   | 👍 Handled by `FlexSceneFile`
vfont         | Bitmap Font                       | 👍 Decrypts `VFONT1`, supported in Source 1 and Source 2.
vpk           | Pak (package)                     | 👍 Handled by [ValvePak](https://github.com/ValveResourceFormat/ValvePak)

## List of supported magics
Magic        | Description
------------ | ------------
`0x03564B56` | VKV\x03 - First binary keyvalues 3 encoding with custom block compression
`0x4B563301` | KV3\x01 - Binary keyvalues 3 (version 1)
`0x4B563302` | KV3\x02 - Binary keyvalues 3 (version 2)
`0x4B563303` | KV3\x03 - Binary keyvalues 3 (version 3)
`0x4B563304` | KV3\x04 - Binary keyvalues 3 (version 4)
`0x4B563305` | KV3\x05 - Binary keyvalues 3 (version 5)
`0x564B4256` | VBKV - binary keyvalues 1 (handled by ValveKeyvalue)
`0x55AA1234` | VPK - valve package (handled by ValvePak)
`0x44434356` | VCCD - closed captions
`0xC4CCACE8` | tools asset info
`0xC4CCACE9` | tools asset info (newer version)
`0x32736376` | vcs2 - compiled shader
`0x31415926` | murmurhash2 seed used by StringToken
`0xEDABCDEF` | murmurhash64 seed used to encode resource IDs
`VFONT1`     | "encrypted" font file
`0x00564645` | VFE - flex scene file

# CLI

## Command-line options

Option                        | Description
----------------------------- | -----------
| **Input**                   | |
`--input` (or `-i`)           | Input file to be processed. With no additional arguments, a summary of the input(s) will be displayed.
`--recursive`                 | If specified and given input is a folder, all sub directories will be scanned too.
`--recursive_vpk`             | If specified along with `--recursive`, will also recurse into VPK archives.
`--vpk_extensions` (or `-e`)  | File extension(s) filter, example: "vcss_c,vjs_c,vxml_c".
`--vpk_filepath` (or `-f`)    | File path filter, example: "panorama\\" or "scripts/items/items_game.txt".
`--vpk_cache`                 | Use cached VPK manifest to keep track of updates. Only changed files will be written to disk.
`--vpk_verify`                | Verify checksums and signatures.
| **Output**                  | |
`--output` (or `-o`)          | Output path to write to. If input is a folder (or a VPK), this should be a folder.
`--all` (or `-a`)             | Print the content of each resource block in the file.
`--block` (or `-b`)           | Print the content of a specific block, example: DATA, RERL, REDI, NTRO.
`--vpk_decompile` (or `-d`)   | Decompile supported resource files.
`--vpk_list` (or `-l`)        | Lists all resources in given VPK. File extension and path filters apply.
`--vpk_dir`                   | Print a list of files in given VPK and information about them.
| **Type specific export**    | |
`--gltf_export_format`        | Exports meshes/models in given glTF format. Must be either 'gltf' or 'glb'.
`--gltf_export_materials`     | Whether to export materials during glTF exports.
`--gltf_export_animations`    | Whether to export animations during glTF exports.
`--gltf_textures_adapt`       | Whether to perform any glTF spec adaptations on textures (e.g. split metallic map).
`--gltf_export_extras`        | Export additional Mesh properties into glTF extras
`--tools_asset_info_short`    | Whether to print only file paths for tools_asset_info files.
| **Other**                   | |
`--threads`                   | If higher than 1, files will be processed concurrently.
`--version`                   | Show version information.
`--help`                      | Show help information.

There are also `--stats` related options, but they are not listed here as they are not relevant to most users.

### Examples:

```powershell
# List all files in the vpk
# Use `--vpk_dir` to also print file metadata
./Source2Viewer-CLI.exe -i "core/pak01_dir.vpk" --vpk_list

# Export the entire vpk as is
./Source2Viewer-CLI.exe -i "core/pak01_dir.vpk" --output "pak01_exported"

# Export only the "panorama/layout" folder
./Source2Viewer-CLI.exe -i "core/pak01_dir.vpk" --output "pak01_exported" --vpk_filepath "panorama/layout"

# Decompile and export all Panorama files to a folder named "exported"
./Source2Viewer-CLI.exe -i "core/pak01_dir.vpk" -e "vjs_c,vxml_c,vcss_c" -o "exported" -d

# Print resource blocks for a specific file similar to resourceinfo.exe in Source 2
# Use `--block DATA` to only print a specific block
./Source2Viewer-CLI.exe -i "file.vtex_c" --all

# Decompile a specific file on disk
./Source2Viewer-CLI.exe -i "file.vtex_c" -o exported.png
```

# GUI

Source 2 Viewer keeps its settings in `%LocalAppData%/Source2Viewer/settings.vdf`.

## GUI Keybinds

### General

| Key | Action |
| --- | ------ |
| <kbd>Ctrl-O</kbd> | Open a new file |
| <kbd>Ctrl-F</kbd> | Open find window (package or text viewer) |
| <kbd>Ctrl-W</kbd> | Close current tab |
| <kbd>Ctrl-Q</kbd> | Close all tabs |
| <kbd>Ctrl-E</kbd> | Close all tabs to the right of the current one |
| <kbd>Ctrl-R</kbd> or <kbd>F5</kbd> | Reload current tab |

### Map/model viewer

### Camera
| Key | Action |
| --- | ------ |
| Hold <kbd>LMB</kbd> | Rotate the camera |
| <kbd>ShiftLeft+LMB</kbd> | Pan the camera |
| <kbd>AltLeft+LMB</kbd> | Move camera forward or backwards |
| <kbd>W</kbd><kbd>A</kbd><kbd>S</kbd><kbd>D</kbd>  | Move the camera |
| <kbd>Q</kbd> | Move the camera up |
| <kbd>Z</kbd> | Move the camera down |
| Hold <kbd>Ctrl</kbd> and <kbd>LMB</kbd> | Move the light position (in model viewer) |

#### Objects
| Key | Action |
| --- | ------ |
| Click <kbd>LMB</kbd> | Pick an object |
| <kbd>Ctrl+LMB</kbd> | Toggle object selection |
| Double click <kbd>LMB</kbd> | Open entity info dialog |
| Double click <kbd>Ctrl+LMB</kbd> | Open object in a new tab |
| <kbd>Delete</kbd> | Temporarily hide the selected object (toggle world layers to restore) |

#### Other
| Key | Action |
| --- | ------ |
| <kbd>F11</kbd> | Enter/exit fullscreen |
| <kbd>Ctrl-C</kbd> | Copy viewport screenshot to clipboard |

### Texture viewer

| Key | Action |
| --- | ------ |
| Hold <kbd>LMB</kbd> | Move around the picture |
| Arrow keys | Move around the picture |
| <kbd>Ctrl-0</kbd> | Reset zoom |
| <kbd>Ctrl</kbd> and <kbd>+</kbd> | Zoom in |
| <kbd>Ctrl</kbd> and <kbd>-</kbd> | Zoom out |
| <kbd>Ctrl-C</kbd> | Copy current texture to clipboard |
| <kbd>Ctrl-S</kbd> | Save current texture to disk |
| <kbd>F11</kbd> | Enter/exit fullscreen |

# Misc

## License

Contents of this repository are available under [MIT license](LICENSE), except for `Tests/Files` folder contains files which have likely come from Valve's games.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).
