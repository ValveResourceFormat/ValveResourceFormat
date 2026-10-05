# Command-line utility

While Source 2 Viewer is a GUI application for Windows, there is also a command-line utility available for all of Windows, Linux, and macOS.

The binary name is `Source2Viewer-CLI`.

## Command-line options

| Option                       | Description                                                                                                                                                                                                                                                                                                      |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Input**                    |                                                                                                                                                                                                                                                                                                                  |
| `--input` (or `-i`)          | Input file or folder to be processed, multiple can be comma-separated (not with `--output`). With no other options, a summary of the input(s) is printed.                                                                                                                                                        |
| `--recursive`                | If the input is a folder, also scan its subfolders.                                                                                                                                                                                                                                                              |
| `--recursive_vpk`            | If the input is a folder, also process files inside of VPK archives in it.                                                                                                                                                                                                                                       |
| `--vpk_extensions` (or `-e`) | File extension(s) filter, example: `"vcss_c,vjs_c,vxml_c"`.                                                                                                                                                                                                                                                      |
| `--vpk_filepath` (or `-f`)   | File path filter(s), matching the start of the path inside the VPK or relative to the input folder (case-insensitive), or the full path when using `*` and `?` wildcards. Example: `"panorama/,sounds/"` or `"*/entities/*"`.                                                                                    |
| `--vpk_cache`                | Use a cached VPK manifest to keep track of updates, only changed files are written to disk. Requires `--output`.                                                                                                                                                                                                 |
| `--vpk_verify`               | Verify checksums and signatures of the given VPK, or of every VPK in the given folder.                                                                                                                                                                                                                           |
| **Output**                   |                                                                                                                                                                                                                                                                                                                  |
| `--output` (or `-o`)         | Output path to write to. Treated as a folder when it is an existing folder, ends with a path separator, or has no file extension, otherwise it names the file to write, which requires the input to be a single file or the filters to match one file. Use `-` to print decompiled files to the console instead. |
| `--all` (or `-a`)            | Print the content of each resource block in the file.                                                                                                                                                                                                                                                            |
| `--block` (or `-b`)          | Print the content of specific block(s), example: `"DATA"` or `"RERL,RED2"`.                                                                                                                                                                                                                                      |
| `--vpk_decompile` (or `-d`)  | Decompile supported resource files. Requires `--output`.                                                                                                                                                                                                                                                         |
| `--texture_decode_flags`     | Decompile textures with the specified decode flags, example: `"none"`, `"auto"` (default), `"ForceLDR"`. Requires `--output`.                                                                                                                                                                                    |
| `--vpk_list` (or `-l`)       | List all files in the given VPK or folder. File extension and path filters apply.                                                                                                                                                                                                                                |
| `--vpk_dir`                  | Same as `--vpk_list`, but also print the archive index, offset and metadata size of each file.                                                                                                                                                                                                                   |
| **Type specific export**     |                                                                                                                                                                                                                                                                                                                  |
| `--gltf_export_format`       | Export meshes and models in the given glTF format, `"gltf"` or `"glb"`. Implies `--vpk_decompile`.                                                                                                                                                                                                               |
| `--gltf_export_materials`    | Export materials during glTF exports.                                                                                                                                                                                                                                                                            |
| `--gltf_export_animations`   | Export model animations during glTF exports.                                                                                                                                                                                                                                                                     |
| `--gltf_mesh_list`           | Meshes to include in the glTF, example: `"mesh1,mesh2"`. By default all meshes are included.                                                                                                                                                                                                                     |
| `--gltf_animation_list`      | Animations to include in the glTF, example: `"idle,dropped"`. Implies `--gltf_export_animations`. By default all animations are included.                                                                                                                                                                        |
| `--gltf_textures_adapt`      | Perform glTF spec adaptations on exported textures (e.g. split metallic map). Implies `--gltf_export_materials`.                                                                                                                                                                                                 |
| `--gltf_export_extras`       | Export additional mesh properties into glTF extras.                                                                                                                                                                                                                                                              |
| `--gltf_compose_additive`    | Compose additive animations over the bind pose instead of exporting their delta tracks.                                                                                                                                                                                                                          |
| `--shader_list_combos`       | List every compiled variant of a shader with its combo values and bytecode hash. For a material, only the variants of its shader that the material selects.                                                                                                                                                      |
| `--shader_combo`             | Decompile the shader variant matching these combo values, example: `"S_ALPHA_TEST=1,D_BLEND_WEIGHT_COUNT=4"`. A bare name means `=1`, omitted combos stay at their minimum. For a material, the static combos it selects are used.                                                                               |
| `--tools_asset_info_short`   | Print only file paths for tools_asset_info files.                                                                                                                                                                                                                                                                |
| **Other**                    |                                                                                                                                                                                                                                                                                                                  |
| `--threads`                  | If higher than 1, files are processed concurrently. Only used with `--output` or `--stats`.                                                                                                                                                                                                                      |
| `--quiet` (or `-q`)          | When writing to `--output`, only print errors and a summary. With the shader options, only print their output.                                                                                                                                                                                                   |
| `--game`                     | Path to a `gameinfo.gi` file, or the folder containing it, to load game search paths from. Useful when the input file is not located inside a game folder.                                                                                                                                                       |
| `--version`                  | Show version information.                                                                                                                                                                                                                                                                                        |
| `--help`                     | Show help information.                                                                                                                                                                                                                                                                                           |

There are also `--stats` related options (for collecting statistics and testing exports) primarily intended for VRF developers. You can pass `--input "steam"` to automatically scan all Steam library folders for Source 2 files. See the `--help` output for details.

### Exit codes

The exit code is `0` on success, `1` for invalid arguments, and `2` when any file failed to process. Errors for individual files are printed to stderr and appended to `exceptions.txt` in the current working directory.

### Good to know

- Only one of `--output`, `--vpk_list` (or `--vpk_dir`), `--vpk_verify`, `--block` (or `--all`), `--stats`, and the shader options can be used at a time.
- Pass the `_dir.vpk` of a multi-chunk package (`pak01_dir.vpk`), not one of the numbered `pak01_000.vpk` chunks.
- Use `--vpk_list` to find the exact path of a file, then filter on it with `--vpk_filepath`. The filter matches the start of the path, so `models/chicken/` works but `chicken` does not, use `*chicken*` instead.
- For VPK input, `--output` without `--vpk_decompile` writes the compiled files as they are stored. A single compiled file on disk is decompiled whenever `--output` is given.
- Decompiling or exporting one resource can write several files: a model also writes its meshes and animation clips next to it, and glTF export writes textures as separate images and physics as a separate `_physics` file.
- `--output -` prints each decompiled file to stdout after a `--- <path>` line, without the additional files, while everything else is printed to stderr. It is meant for text formats.
- References to other files (materials, meshes, textures) are resolved by finding `gameinfo.gi` in a parent folder of the input. A file copied out of the game folder will be missing them unless you pass `--game`.
- `--block` output starts with a summary of the file (type, external references, block list) before the block contents.
- Folder input does not look inside VPK files unless `--recursive_vpk` is given. This is the easiest way to process all maps of a game, for example `-i <game>/maps --recursive_vpk -e vents_c`.
- VPKs inside other VPKs (such as 3D skybox prefabs inside map VPKs, or map VPKs inside workshop items) are only processed by `--stats`. Otherwise they are treated as regular files, so extract them first with `--vpk_extensions vpk --output <folder>` and run the CLI on the extracted files.
- Compiled shaders are split into multiple files (`<name>_<platform>_<model>_features.vcs`, `_vs.vcs`, `_ps.vcs`, ...). Decompiling to `.vfx` only works from the shader VPK, which writes one `.vfx` per shader from the highest shader model among the matched files. A loose `.vcs` file only prints a summary.
- Loading dependencies prints the game search paths it mounts and the files it fails to load to stderr. `--quiet` hides everything but the warnings.

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

### Print the entities of a map

```powershell
./Source2Viewer-CLI.exe -i "<game>/maps/de_dust2.vpk" --vpk_extensions "vents_c" -o -
```

Or decompile the entities of every map:

```powershell
./Source2Viewer-CLI.exe -i "<game>/maps" --recursive_vpk --vpk_extensions "vents_c" -d -o "entities" --quiet
```

### Decompile a specific file

```powershell
./Source2Viewer-CLI.exe -i "file.vtex_c" -o exported.png
```

Decompile a single file from a VPK to a specific path:

```powershell
./Source2Viewer-CLI.exe -i "<game>/pak01_dir.vpk" --vpk_filepath "models/chicken/chicken.vmdl_c" -d -o "chicken/chicken.vmdl"
```

### Export a model to glTF with specific animations

Export a model with only specific animations included:

```powershell
./Source2Viewer-CLI.exe -i "model.vmdl_c" -o "output.glb" -d --gltf_export_format glb --gltf_export_animations --gltf_animation_list "idle,walk,run"
```

### Scan Steam libraries for statistics

```powershell
./Source2Viewer-CLI.exe -i "steam" --stats --recursive --recursive_vpk --threads 8
```

### Decompile all shaders

```powershell
./Source2Viewer-CLI.exe -i "<game>/shaders_vulkan_dir.vpk" --vpk_decompile --vpk_extensions "vcs" --output "."
```

Or a single shader:

```powershell
./Source2Viewer-CLI.exe -i "<game>/shaders_vulkan_dir.vpk" --vpk_filepath "shaders/vfx/csgo_environment_vulkan_" -d -o "shaders"
```

### Decompile one variant of a shader

A shader is compiled once per combination of its static (`S_`) and dynamic (`D_`) combos. List the variants that exist, then decompile one of them:

```powershell
./Source2Viewer-CLI.exe -i "<game>/shaders_vulkan_dir.vpk" -f "shaders/vfx/csgo_environment_vulkan_60_vs.vcs" --shader_list_combos
./Source2Viewer-CLI.exe -i "<game>/shaders_vulkan_dir.vpk" -f "shaders/vfx/csgo_environment_vulkan_60_vs.vcs" --shader_combo "S_DETAIL_NORMAL,D_COMPRESSED_NORMALS_AND_TANGENTS"
```

Pass a material together with one of these options to decompile the variants of its shader that the material uses. Without them, materials are processed as usual. Dynamic combos can still be set with `--shader_combo`, or left empty:

```powershell
./Source2Viewer-CLI.exe -i "<game>/pak01_dir.vpk" -f "materials/models/chicken/chick_yellow.vmat_c" --shader_combo ""
```

## Argument Stability

Command-line arguments and their behavior may change in future releases. We do not guarantee stability of the CLI interface. If you are writing scripts that depend on specific arguments or output formats, be prepared to update them when upgrading to newer versions.

[The source code is available here.](https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/CLI/Decompiler.cs)
