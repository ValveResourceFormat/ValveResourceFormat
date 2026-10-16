# Command-line utility

While Source 2 Viewer is a GUI application for Windows, there is also a command-line utility available for all of Windows, Linux, and macOS.

The binary name is `Source2Viewer-CLI`.

## Command-line options

| Option                           | Description                                                                                                                                                                                                                                                                                                      |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Input**                        |                                                                                                                                                                                                                                                                                                                  |
| `--input` (or `-i`)              | Input file or folder to be processed, multiple can be comma-separated (not with `--output`). Accepts paths relative to an installed Steam app such as `"steam:730/game/csgo"`, and `"vpk:"` links copied in Source 2 Viewer. With no other options, a summary of the input(s) is printed.                        |
| `--recursive`                    | If the input is a folder, also scan its subfolders.                                                                                                                                                                                                                                                              |
| `--recursive_vpk`                | If the input is a folder, also process files inside of VPK archives in it.                                                                                                                                                                                                                                       |
| `--vpk_extensions` (or `-e`)     | File extension(s) filter, example: `"vcss_c,vjs_c,vxml_c"`.                                                                                                                                                                                                                                                      |
| `--vpk_filepath` (or `-f`)       | File path filter(s), matching the start of the path inside the VPK or relative to the input folder (case-insensitive), or the full path when using `*` and `?` wildcards. Example: `"panorama/,sounds/"` or `"*/entities/*"`.                                                                                    |
| `--vpk_cache`                    | Only export the files of a VPK that changed since the last export to the same output, see [cached VPK manifest](#cached-vpk-manifest). Requires `--output`.                                                                                                                                                      |
| `--vpk_verify`                   | Verify checksums and signatures of the given VPK, or of every VPK in the given folder.                                                                                                                                                                                                                           |
| `--vpk_create`                   | Pack all files in the input folder and its subfolders into a VPK at the given path, example: `"pak01_dir.vpk"`. File paths are lowercased, and file extension and path filters apply.                                                                                                                            |
| `--vpk_create_chunk_size`        | When using `--vpk_create`, split file data into chunk files of this many megabytes (1 to 1024) next to the VPK, which must be named `"*_dir.vpk"`. By default everything is written into a single file.                                                                                                          |
| **Output**                       |                                                                                                                                                                                                                                                                                                                  |
| `--output` (or `-o`)             | Output path to write to, see [output paths](#output-paths). Treated as a folder when it is an existing folder, ends with a path separator, or has no file extension, otherwise it names the file to write, which requires one input file or one match. Use `-` to print decompiled files to the console instead. |
| `--all` (or `-a`)                | Print the content of each resource block in the file.                                                                                                                                                                                                                                                            |
| `--block` (or `-b`)              | Print the content of specific block(s), example: `"DATA"` or `"RERL,RED2"`.                                                                                                                                                                                                                                      |
| `--vpk_decompile` (or `-d`)      | Decompile supported resource files. Requires `--output`.                                                                                                                                                                                                                                                         |
| `--decompile_dependencies`       | When decompiling, also decompile the files the input references (materials, textures, models, animation clips, skeletons, ...) from the same package or addon, each at its own path. Requires `--output`.                                                                                                        |
| `--skip_existing`                | Keep files that already exist in the output instead of overwriting them. Requires `--output`.                                                                                                                                                                                                                    |
| `--texture_decode_flags`         | Decompile textures with the specified decode flags, example: `"none"`, `"auto"` (default), `"ForceLDR"`. Requires `--output`.                                                                                                                                                                                    |
| `--skip_softbody_reconstruction` | Skip reconstructing soft-body (cloth) physics when decompiling models.                                                                                                                                                                                                                                           |
| `--vpk_list` (or `-l`)           | List all files in the given VPK or folder. File extension and path filters apply.                                                                                                                                                                                                                                |
| `--vpk_dir`                      | Same as `--vpk_list`, but also print the archive index, offset and metadata size of each file.                                                                                                                                                                                                                   |
| `--kv_flatten`                   | Print a KeyValues1 or KeyValues3 text file as one `path = value` line per value, for comparing two versions of a file with a line diff. Use `-i -` to read from stdin.                                                                                                                                           |
| **Type specific export**         |                                                                                                                                                                                                                                                                                                                  |
| `--gltf_export_format`           | Export meshes and models in the given glTF format, `"gltf"` or `"glb"`. Implies `--vpk_decompile`.                                                                                                                                                                                                               |
| `--gltf_export_materials`        | Export materials during glTF exports.                                                                                                                                                                                                                                                                            |
| `--gltf_export_animations`       | Export model animations during glTF exports.                                                                                                                                                                                                                                                                     |
| `--gltf_mesh_list`               | Meshes to include in the glTF, example: `"mesh1,mesh2"`. By default all meshes are included.                                                                                                                                                                                                                     |
| `--gltf_animation_list`          | Animations to include in the glTF, example: `"idle,dropped"`. Implies `--gltf_export_animations`. By default all animations are included.                                                                                                                                                                        |
| `--gltf_textures_adapt`          | Perform glTF spec adaptations on exported textures (e.g. split metallic map). Implies `--gltf_export_materials`.                                                                                                                                                                                                 |
| `--gltf_export_extras`           | Export additional mesh properties into glTF extras.                                                                                                                                                                                                                                                              |
| `--gltf_compose_additive`        | Compose additive animations over the bind pose instead of exporting their delta tracks.                                                                                                                                                                                                                          |
| `--shader_list_combos`           | List every compiled variant of a shader with its combo values and bytecode hash. For a material, only the variants of its shader that the material selects.                                                                                                                                                      |
| `--shader_combo`                 | Decompile the shader variant matching these combo values, example: `"S_ALPHA_TEST=1,D_BLEND_WEIGHT_COUNT=4"`. A bare name means `=1`, omitted combos stay at their minimum. For a material, the static combos it selects are used.                                                                               |
| `--tools_asset_info_short`       | Print only file paths for tools_asset_info files.                                                                                                                                                                                                                                                                |
| **Other**                        |                                                                                                                                                                                                                                                                                                                  |
| `--quiet` (or `-q`)              | When writing to `--output` or `--vpk_create`, only print errors and a summary. With the shader options, only print their output.                                                                                                                                                                                 |
| `--game`                         | Path to a `gameinfo.gi` file, or the folder containing it, to load game search paths from, such as `"steam:730/game/csgo"`. Useful when the input file is not located inside a game folder. See [game search paths](./game-search-paths.md).                                                                     |
| `--version`                      | Show version information.                                                                                                                                                                                                                                                                                        |
| `--help`                         | Show help information.                                                                                                                                                                                                                                                                                           |

There are also `--test` and `--test_*` options for running every file through the decompile code paths, primarily intended for VRF developers. You can pass `--input "steam"` to automatically scan all Steam library folders for Source 2 files. See the `--help` output for details.

### Exit codes

The exit code is `0` on success, `1` for invalid arguments, and `2` when any file failed to process. Errors for individual files are printed to stderr and appended to `exceptions.txt` in the current working directory.

### Good to know

- Only one of `--output`, `--vpk_list` (or `--vpk_dir`), `--vpk_verify`, `--vpk_create`, `--block` (or `--all`), `--test`, `--kv_flatten`, and the shader options can be used at a time.
- Paths starting with `steam:<appid>/` are relative to the installation folder of that Steam app, so `-i steam:730/game/csgo/pak01_dir.vpk` works on any computer with Counter-Strike 2 installed. **Copy URL** in Source 2 Viewer copies `vpk:` links in this form, which can be passed to `--input` to process the file or folder they point to.
- Pass the `_dir.vpk` of a multi-chunk package (`pak01_dir.vpk`), not one of the numbered `pak01_000.vpk` chunks.
- Use `--vpk_list` to find the exact path of a file, then filter on it with `--vpk_filepath`. The filter matches the start of the path, so `models/chicken/` works but `chicken` does not, use `*chicken*` instead.
- For VPK input, `--output` without `--vpk_decompile` writes the compiled files as they are stored. A single compiled file on disk is decompiled whenever `--output` is given.
- Decompiling or exporting one resource can write several files: a model also writes its meshes and animation clips, and glTF export writes textures as separate images and physics as a separate `_physics` file next to the `.gltf`. See [output paths](#output-paths).
- `--output -` prints each decompiled file to stdout after a `--- <path>` line, without the additional files, while everything else is printed to stderr. It is meant for text formats.
- References to other files (materials, meshes, textures) are loaded from the game the input belongs to. A file copied out of the game folder will be missing them unless you pass `--game` (see [game search paths](./game-search-paths.md)).
- `--block` output starts with a summary of the file (type, external references, block list) before the block contents.
- Folder input does not look inside VPK files unless `--recursive_vpk` is given. This is the easiest way to process all maps of a game, for example `-i <game>/maps --recursive_vpk -e vents_c`. Files from VPKs are written at their path inside the VPK, without a folder for the VPK.
- VPKs inside other VPKs (such as 3D skybox prefabs inside map VPKs, or map VPKs inside workshop items) are only processed by `--test`. Otherwise they are treated as regular files, so extract them first with `--vpk_extensions vpk --output <folder>` and run the CLI on the extracted files.
- Compiled shaders are split into multiple files (`<name>_<platform>_<model>_features.vcs`, `_vs.vcs`, `_ps.vcs`, ...). Decompiling to `.vfx` only works from the shader VPK, which writes one `.vfx` per shader from the highest shader model among the matched files. A loose `.vcs` file only prints a summary.
- All `--gltf_*` options require `--gltf_export_format`.
- `--output` and `--test` process files on every processor core. Set the `DOTNET_PROCESSOR_COUNT` environment variable to use fewer.

### Output paths

Decompiled files are written to `<root>/<content path>`, where the content path is the path the file has inside the game or addon (`models/props/crate.vmdl`, `materials/props/crate_color.png`). Files referenced by a decompiled file are written at the paths it references them by, so models, materials and maps find their meshes, textures and sub-maps again.

- The content path of a file from a VPK is its path inside the VPK.
- The content path of a file on disk is the path below the `game/<mod>` or `game/<game>_addons/<addon>` folder it is in, otherwise the path from its first top-level asset folder such as `models`, `materials` or `sounds`. A file with neither is written by its bare name at the root, with a warning.
- Animation clips and skeletons are written next to the authored source path they record, which can be outside the model's folder.
- The root is the `--output` folder. If it, or any folder above it, is an addon or mod root (`content/<game>_addons/<name>`, `game/<game>_addons/<name>`, a `game/<mod>` with a `gameinfo.gi`, or a `content/<mod>` whose `game/<mod>` has one), that folder is the root instead. Otherwise trailing folders of `--output` that match the start of the folder all input files share are dropped, so `-o out/models/props` for `models/props/crate.vmdl` writes to `out/models/props/crate.vmdl`.
- With `--output` naming a file, that file is the decompiled input itself. The root is resolved from its folder with the same rules, and every other file is written at its content path under that root.
- Folder input and `--recursive_vpk` write every file at its own content path, not relative to the input folder.
- Two files that resolve to the same path are written once when their bytes are identical. When the content differs, one of them gets a numbered name next to the other, which one can change between runs, and only the file that references it directly is updated. A file that references two different files at the same path keeps its references, with a warning. Texture images are named after the source image they were compiled from, so images at the same path are one source compiled twice: only the one with the highest resolution is written.
- Resources the compiler generated from another resource, named `<resource>+<name>` such as the non-additive version of an additive clip or a graph variation, are not written when that resource is decompiled too.
- Paths that would leave the root, such as `..` or absolute paths, are placed under the root instead, with a warning.
- Existing files are overwritten unless `--skip_existing` is given.
- Decompiling a map writes `maps/<map>_d.vmap` next to the `maps/<map>/` folder, so recompiling it does not replace the original map. Pieces that exist only because the map was compiled (the world, world nodes, resource manifest, visibility, world physics, entity lumps `.vents`, and the baked lightmaps, cubemaps and light probes) are not written.
- glTF export does not use content paths for its references. With a folder `--output` the `.gltf` or `.glb` is written at the resource's content path below the root with its extension changed; with a file `--output` it is written to that file. Its buffers and textures go next to it, and `--skip_existing` keeps an existing `.gltf` or `.glb`.

### Cached VPK Manifest

When using `--vpk_cache`, a `<vpk name>.manifest.txt` file in the output root records the checksum of every file of the VPK that exported. The next export to the same output skips the files whose checksum did not change. A file that failed to export is not recorded, so it is retried. The manifest is ignored when the decompiler version, the VPK path, or the decompile and glTF options differ from the export that wrote it.

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

### Create a VPK from a folder

Pack compiled map files into a map VPK. The input folder contains the files at the paths they have inside of the VPK, such as `maps/de_example/world.vwrld_c`:

```powershell
./Source2Viewer-CLI.exe -i "de_example" --vpk_create "maps/de_example.vpk"
```

Large packages can be split into 100 MB chunk files (`pak01_000.vpk`, `pak01_001.vpk`, ...) next to the directory file:

```powershell
./Source2Viewer-CLI.exe -i "my_addon" --vpk_create "pak01_dir.vpk" --vpk_create_chunk_size 100
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

### Decompile a model with its dependencies

Also decompile the materials, textures and animation files of the model that come from the same VPK, each at its content path:

```powershell
./Source2Viewer-CLI.exe -i "<game>/pak01_dir.vpk" -f "models/chicken/chicken.vmdl_c" -d -o "my_addon" --decompile_dependencies
```

### Print a file copied from Source 2 Viewer

Use **Copy URL** on a file in Source 2 Viewer, and pass the copied link as the input:

```powershell
./Source2Viewer-CLI.exe -i "vpk:steam:730/game/csgo/pak01_dir.vpk:scripts/items/items_game.txt" -o -
```

### Compare two versions of a KeyValues file

`--kv_flatten` prints every value on its own line with its full path, such as `items["5316"].attributes["volatile container"].value = 1`, so a line diff shows each changed value with its context. Array elements are keyed by the first of their `m_strPropertyName`, `m_name`, `name`, `m_strName`, `m_Name` or `_class` members, otherwise by index. Repeated keys and array elements get a `#2`, `#3`, ... suffix, except for KeyValues1 blocks split into several with the same name, which are merged. KeyValues1 conditionals are evaluated for the current platform, and `#base`/`#include` lines are printed but not followed.

```bash
git show HEAD~1:scripts/abilities.vdata | ./Source2Viewer-CLI --kv_flatten -i - > old.txt
./Source2Viewer-CLI --kv_flatten -i scripts/abilities.vdata > new.txt
diff old.txt new.txt
```

### Decompile a specific file

```powershell
./Source2Viewer-CLI.exe -i "file.vtex_c" -o exported.png
```

Decompile a single file from a VPK to a specific path. Its meshes, animation clips and other files are written at their content paths under the folder of `-o`:

```powershell
./Source2Viewer-CLI.exe -i "<game>/pak01_dir.vpk" --vpk_filepath "models/chicken/chicken.vmdl_c" -d -o "chicken/chicken.vmdl"
```

### Export a model to glTF with specific animations

Export a model with only specific animations included:

```powershell
./Source2Viewer-CLI.exe -i "model.vmdl_c" -o "output.glb" -d --gltf_export_format glb --gltf_export_animations --gltf_animation_list "idle,walk,run"
```

### Test all files in Steam libraries

```powershell
./Source2Viewer-CLI.exe -i "steam" --test --recursive --recursive_vpk
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
