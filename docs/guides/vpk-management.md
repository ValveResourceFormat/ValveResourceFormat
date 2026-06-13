# VPK Management

Source 2 Viewer provides tools for browsing, extracting, and creating VPK (Valve Pak) archives. VPK is the package format used by Source 2 games to store their assets.

## Browsing VPK Archives

- Open any `.vpk` file via File > Open or the Explorer
- The directory file (e.g., `pak01_dir.vpk`) acts as an index for all content archives (`pak01_000.vpk`, `pak01_001.vpk`, etc.)
- You only need to open the `_dir.vpk` file. Source 2 Viewer reads the content archives automatically
- The file tree shows the full directory structure inside the VPK

## Extracting Files

There are several ways to extract files from a VPK:

### Extract individual files

- Right-click a file → **Export as is** (raw format) or **Decompile & Export** (converted format)

### Extract folders

- Right-click a folder → choose export option to extract everything inside

### Extract entire VPK

- Right-click the root of the file tree → export to extract the complete archive

### Using the CLI

For large or automated extractions:

```sh
Source2Viewer-CLI -i "pak01_dir.vpk" -o "output_folder/"
```

Add `-d` to decompile files during extraction:

```sh
Source2Viewer-CLI -i "pak01_dir.vpk" -o "output_folder/" -d
```

## Creating VPK Archives

Source 2 Viewer can create new VPK archives:

1. Go to **File → Create VPK from folder**
2. A new tab opens with an empty VPK. Right-click to add files and folders from disk, create virtual folders, or remove entries
3. When ready, right-click and select **Save VPK to disk** to write a single `.vpk` file

::: warning
The VPK creation UI is very rudimentary.
:::

## Recovering Deleted Files

VPK archives can contain remnants of deleted files. Valve keeps empty space in VPK content archives to reduce Steam differential update sizes, so that overall content doesn't shift between updates. When files are removed or replaced, the old data may still exist in the content archives even though it's no longer referenced by the directory file.

Source 2 Viewer can discover and recover these deleted files:

1. Open a VPK archive
2. Right-click the root node in the file tree and select **Recover deleted files**
3. Recovered files appear in the tree, though they may lack their original file paths

::: warning
Recovered files may be incomplete, corrupted, or from old game versions. They lack original path information, so identifying them requires inspecting their contents.
:::

This feature is useful for game researchers studying how assets have changed across game updates.

## VPK File Format

VPK archives consist of:

- A **directory file** (`pak01_dir.vpk`) containing the file index with paths, sizes, and offsets
- One or more **content archives** (`pak01_000.vpk`, `pak01_001.vpk`, etc.) containing the actual file data

Source 2 Viewer supports all VPK versions, including both Source 1 and Source 2 formats. See the [VPK file format](<https://developer.valvesoftware.com/wiki/VPK_(file_format)>) page on the Valve Developer Community wiki for more details.

For programmatic VPK access, see the [ValvePak](https://www.nuget.org/packages/ValvePak/) NuGet library.
