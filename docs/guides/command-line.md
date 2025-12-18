# Command-line utility

While Source 2 Viewer is a GUI application for Windows, there is also a command-line utility available for all Windows, Linux, and macOS.

The binary name is `Source2Viewer-CLI`.

## Command-line options

Option                        | Description
----------------------------- | -----------
| **Input**                   | |
`--input` (or `-i`)           | Input file to be processed. With no additional arguments, a summary of the input(s) will be displayed.
`--recursive`                 | If specified and given input is a folder, all sub directories will be scanned too.
`--recursive_vpk`             | If specified along with `--recursive`, will also recurse into VPK archives.
`--vpk_extensions` (or `-e`)  | File extension(s) filter, example: "vcss_c,vjs_c,vxml_c".
`--vpk_filepath` (or `-f`)    | File path filter(s), supports comma-separated values. Example: "panorama/,sounds/" or "scripts/items/items_game.txt".
`--vpk_cache`                 | Use cached VPK manifest to keep track of updates. Only changed files will be written to disk.
`--vpk_verify`                | Verify checksums and signatures.
| **Output**                  | |
`--output` (or `-o`)          | Output path to write to. If input is a folder (or a VPK), this should be a folder.
`--all` (or `-a`)             | Print the content of each resource block in the file.
`--block` (or `-b`)           | Print the content of a specific block, example: DATA, RERL, REDI, NTRO.
`--vpk_decompile` (or `-d`)   | Decompile supported resource files.
`--texture_decode_flags`      | Decompile textures with specified decode flags. Options: "none", "auto", "foceldr". Default: "auto".
`--vpk_list` (or `-l`)        | Lists all resources in given VPK. File extension and path filters apply.
`--vpk_dir`                   | Print a list of files in given VPK and information about them.
| **Type specific export**    | |
`--gltf_export_format`        | Exports meshes/models in given glTF format. Must be either 'gltf' or 'glb'.
`--gltf_export_materials`     | Whether to export materials during glTF exports.
`--gltf_export_animations`    | Whether to export animations during glTF exports.
`--gltf_animation_list`       | Comma-separated list of animations to include in glTF export, example: "idle,dropped". Requires `--gltf_export_animations`. By default includes all animations.
`--gltf_textures_adapt`       | Whether to perform any glTF spec adaptations on textures (e.g. split metallic map).
`--gltf_export_extras`        | Export additional Mesh properties into glTF extras
`--tools_asset_info_short`    | Whether to print only file paths for tools_asset_info files.
| **Other**                   | |
`--threads`                   | If higher than 1, files will be processed concurrently.
`--version`                   | Show version information.
`--help`                      | Show help information.

There are also `--stats` related options (for collecting statistics and testing exports) primarily intended for VRF developers. You can pass `--input "steam"` to automatically scan all Steam library folders for Source 2 files. See the `--help` output for details.

### Cached VPK Manifest
When using `--vpk_cache`, a `.manifest.txt` file is created alongside the VPK to track file versions. This allows incremental exports where only changed files are written. The cache is automatically invalidated if the decompiler version changes.

## Examples

### List all files in a VPK

Use `--vpk_dir` to also print file metadata.

```powershell
./Source2Viewer-CLI.exe -i "core/pak01_dir.vpk" --vpk_list
```

### Export the entire VPK as is

```powershell
./Source2Viewer-CLI.exe -i "core/pak01_dir.vpk" --output "pak01_exported"
```

### Export only specific folders from a VPK

Export only the "panorama/layout" folder:

```powershell
./Source2Viewer-CLI.exe -i "core/pak01_dir.vpk" --output "pak01_exported" --vpk_filepath "panorama/layout"
```

### Decompile and export Panorama files

Decompile and export all Panorama files to a folder named "exported":

```powershell
./Source2Viewer-CLI.exe -i "core/pak01_dir.vpk" -e "vjs_c,vxml_c,vcss_c" -o "exported" -d
```

### Print resource blocks

Print resource blocks for a specific file (similar to resourceinfo.exe in Source 2). Use `--block DATA` to only print a specific block:

```powershell
./Source2Viewer-CLI.exe -i "file.vtex_c" --all
```

### Decompile a specific file

```powershell
./Source2Viewer-CLI.exe -i "file.vtex_c" -o exported.png
```

### Export a model to glTF with specific animations

Export a model with only specific animations included:

```powershell
./Source2Viewer-CLI.exe -i "model.vmdl_c" -o "output.glb" -d --gltf_export_format glb --gltf_export_animations --gltf_animation_list "idle,walk,run"
```

### Scan Steam libraries for statistics

```powershell
./Source2Viewer-CLI.exe -i "steam" --stats
```

### Decompile all shaders
```powershell
./Source2Viewer-CLI.exe -i "<game>/shaders_vulkan_dir.vpk" --vpk_decompile --vpk_extensions "vcs" --output "."
```

## Argument Stability

Command-line arguments and their behavior may change in future releases. We do not guarantee stability of the CLI interface. If you are writing scripts that depend on specific arguments or output formats, be prepared to update them when upgrading to newer versions.

[The source code is available here.](https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/CLI/Decompiler.cs)
