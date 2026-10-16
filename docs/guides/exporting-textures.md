# Exporting Textures

Source 2 Viewer can preview and export textures from any Source 2 game to standard image formats.

## Finding Textures

- Textures are `.vtex_c` files, usually in `materials/` folders inside VPK archives
- Use <kbd>Ctrl</kbd>+<kbd>F</kbd> to search for textures by name
- Textures are also referenced by materials (`.vmat_c`). Opening a material shows which textures it uses

## Texture Viewer

Double-click a `.vtex_c` to open the texture viewer. Features:

- View individual channels: RGB, Red, Green, Blue, Alpha
- Toggle between mip levels to see different resolutions
- For cubemaps, set **Projection type** to **None** to browse individual faces
- For texture arrays, browse individual slices
- HDR textures are shown gamma encoded, so values above 1.0 clip

![Texture viewer with channel controls](./images/exporting-textures-viewer.png)

## Exporting

- Right-click a `.vtex_c` and select **Decompile & export** to write a `.vtex` compile config with the image next to it, at the texture's path below the output folder you pick. HDR textures write `.exr`, others `.png`; textures stored as JPEG, PNG or WebP keep that format
- Or open the texture and use the save button in the viewer toolbar to save just the image as **PNG**, **JPG**, or **EXR** (for HDR textures)

For batch export:

- Right-click a folder containing textures → **Decompile & export**, pick which file types to export in the dialog, then choose an output folder. Textures are written at their path in the game (`materials/...`) below it, or below the addon content folder if the output folder is inside one
- Use the CLI for large-scale extraction:

```sh
Source2Viewer-CLI -i "pak01_dir.vpk" -o "exported/" -d \
    --vpk_extensions "vtex_c" \
    --vpk_filepath "materials/models/heroes/"
```

Texture images of a decompiled material are written at the paths the `.vmat` refers to, so the material finds them again. Decompiling a material or model with **Include dependencies** checked also decompiles the textures it uses from the same package. Pass `--decompile_dependencies` for the same in the CLI. Images are named after the source image a texture was compiled from. When several textures compiled from one source image would write it, only the one with the highest resolution is written, and every material points at it.

## Texture Types

Source 2 uses several texture types you'll encounter:

- **Color/Albedo** (`_color`): The base color/diffuse texture
- **Normal map** (`_normal`): Surface detail for lighting. These appear purple/blue in the viewer
- **Roughness/Metalness**: PBR material properties, often packed into channels of a single texture
- **Mask textures**: Various masks packed into RGBA channels (detail mask, self-illum, specular, etc.)
- **AO** (`_ao`): Ambient occlusion

::: tip
Normal maps and mask textures may look wrong if you view only the RGB channels. Switch to individual channel view to understand what data each channel contains.
:::

## HDR and Cubemap Textures

- HDR textures (used for skyboxes and environment probes) are stored in higher precision formats
- Cubemap textures have 6 faces. Set **Projection type** to **None** to browse them
- HDR textures can be exported as EXR to preserve the full dynamic range, or as PNG/JPG, which clip values above 1.0
