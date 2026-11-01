# Source2Viewer-CLI for agents

Recipes to read game files. For all options, read `docs/guides/command-line.md`, not `--help`.

- Binary: `CLI/bin/Release/Source2Viewer-CLI.exe`, or `CLI/bin/Debug/` after a Debug build. A build replaces the exe, so if a build can run at the same time, use a copy of the `bin/<config>` folder.
- `-i steam:<appid>/game/<mod>/pak01_dir.vpk` finds the Steam install: 730 `csgo`, 570 `dota`, 1422450 `citadel`, 546560 `hlvr`. Give the `_dir.vpk`, not a numbered chunk.
- Informational lines (search paths, preloaded VPKs) go to stderr. Send stderr to a file and read the content from stdout.
- Exit codes: 0 success, 1 bad arguments or no file matched, 2 a file failed to process.
- `-f` and `-e` take comma-separated lists (`-f "panorama/,sounds/"`, `-e "vjs_c,vxml_c"`).
- `-i` also takes a loose compiled file. With `-o`, the CLI decompiles it without `-d` (`-i x.vtex_c -o x.png`).

## Find files

```powershell
& $cli -i $vpk -l -f "*chicken*" -e vmdl_c       # -f matches the start of the path. Use * for a substring
& $cli -i $vpk --vpk_search "C_OP_RenderSprites" -e vpcf_c   # files whose contents contain the text
& $cli -i $vpk --vpk_search "C_OP_Render(Sprites|Trails)" --vpk_search_options regex   # also "case", "word", or "hex" for bytes
& $cli -i <game>/maps --recursive_vpk -l -e vents_c          # folder input looks inside VPKs only with --recursive_vpk
```

`No files matched the given filters` almost always means that `-f` was not the start of the path. List the paths with `-l` and a `*` pattern first.

## Read one file

```powershell
& $cli -i $vpk -f materials/x/y.vmat_c -o -       # decompiled text on stdout, after a "--- <path>" line
& $cli -i $vpk -f materials/x/y.vmat_c -b DATA    # raw resource block (KV3 for most types)
& $cli -i "vpk:steam:730/game/csgo/pak01_dir.vpk:scripts/items/items_game.txt" -o -   # Copy URL link from the viewer
```

- `-o -` prints text formats. For an image or a model, use `-d -o <folder>`.
- `-d` requires `-o`. Use `-o -` to print.
- `-b` requires a block name (`DATA`, `RERL,RED2`). `-a` prints all blocks.
- To find values without a search of the full text, flatten it to one `path = value` line per value: `& $cli -i $vpk -f $f -o - 2>$null | Select-Object -Skip 1 | & $cli --kv_flatten -i -`. `--kv_flatten` reads only a text file or stdin.
- A file outside the game folder (a copy or a depot download) loads its references only with `--game steam:730/game/csgo`, or the folder that contains `gameinfo.gi`.

## Export many files

```powershell
& $cli -i $vpk -e vpcf_c -f particles/ -d -o $out -q --threads 8
& $cli -i <game>/maps/de_dust2.vpk -e vents_c -o -            # all entities of a map
& $cli -i $vpk -f models/x/y.vmdl_c -o $out/y.glb --gltf_export_format glb --gltf_export_animations --gltf_export_materials
```

- `-q` prints only errors and the summary. Without `-d`, the CLI writes VPK files compiled, as stored.
- `--gltf_animation_list "idle,run"` limits the animations. All `--gltf_*` options require `--gltf_export_format`.
- A VPK inside a VPK (skybox prefabs in map VPKs, maps in workshop VPKs) is only opened by `-b`/`-a` and the shader options. For anything else, extract it first with `-e vpk -o <folder>`.

## Shaders

`shaders_vulkan_dir.vpk` and `shaders_pc_dir.vpk` are next to `pak01_dir.vpk`. Each shader is split into `<name>_<platform>_<model>_{features,vs,ps,...}.vcs`. The model is `50` or `60` and changes with the game, so list the files first.

```powershell
& $cli -i $shaders -l -f "shaders/vfx/csgo_environment_"
& $cli -i $shaders -f shaders/vfx/csgo_environment_vulkan_50_ps.vcs --shader_list_combos -q
& $cli -i $shaders -f shaders/vfx/csgo_environment_vulkan_50_ps.vcs --shader_combo "D_BAKED_LIGHTING_FROM_VERTEX_STREAM" -q
& $cli -i $vpk -f materials/x/y.vmat_c --shader_list_combos -q   # only the variants that a material selects
& $cli -i $vpk -f materials/x/y.vmat_c --shader_combo "" -q       # decompile the material's variant
& $cli -i $shaders -f "shaders/vfx/csgo_environment_vulkan_" -d -o $out   # reconstruct the .vfx
```

- With the shader options, `-q` prints only their output.
- "Static combo ... was not compiled" means a combo rule excludes it. Select a row from `--shader_list_combos`.
- A combo name without a value means `=1`. Combos you do not give stay at their minimum.
- `.vfx` reconstruction works only from the shader VPK. A loose `.vcs` prints a summary.

## One action per run

Each run accepts only one of `-o`, `-l`, `--vpk_search`, `--vpk_verify`, `--vpk_create`, `-b`/`-a`, `--test`, `--kv_flatten` and the shader options. To list and then print, run the CLI twice.
