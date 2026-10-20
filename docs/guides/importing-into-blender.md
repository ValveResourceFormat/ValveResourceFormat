---
description: Import Source 2 models, animated characters and maps into Blender through Source 2 Viewer's glTF export, and fix bone spheres, frame rate and material issues.
---

# Importing Models and Maps into Blender

Source 2 Viewer exports models and maps to glTF 2.0, which Blender imports without any addons. This page covers getting those exports into Blender: props and weapons, characters with their animations, Dota 2 cosmetics, and whole maps.

## Exporting from Source 2 Viewer

1. Find the `.vmdl_c` model or `.vmap_c` map in the file tree. See [Exporting Models](./exporting-models.md#finding-models) and [Exporting Maps](./exporting-maps.md#finding-maps) for where they are in each game
2. Right-click it and select **Decompile & export**
3. In the save dialog, pick **glTF** as the file type
4. Choose a save location and click Save

Prefer **glTF** over **glTF Binary** (`.glb`). A `.glb` file is limited to 2 GB by the glTF spec, which large characters and maps can exceed.

What the export includes when you use the GUI:

- Geometry, UV maps, materials and textures, written as separate PNG files next to the `.gltf`
- The skeleton, if the model has one
- All of the model's animations
- Morph targets, such as eye blinks on some CS2 agents

Metalness, roughness and ambient occlusion are packed into one standard glTF texture (red is ambient occlusion, green is roughness, blue is metalness). It looks orange or red in an image viewer, which is expected.

Collision is written to a separate `<name>_physics.gltf` file. You don't need to import it to use the model.

### Exporting from the command line

The command-line utility does not export animations or materials unless you ask for them. Use `--gltf_textures_adapt` to export materials with the same texture packing as the GUI, and `--gltf_export_animations` to include animations:

```sh
Source2Viewer-CLI -i "model.vmdl_c" -o "output.gltf" -d \
    --gltf_export_format gltf \
    --gltf_textures_adapt \
    --gltf_export_animations
```

See the [command-line utility guide](./command-line.md) for all options.

## Before Importing

glTF stores animation time in seconds. If your Blender scene's frame rate doesn't match the animation, clips look stretched or squashed in time. Set the scene's frame rate (for example 30 fps) before importing a model with animations.

## Importing

1. Open Blender
2. Go to **File → Import → glTF 2.0 (.gltf/.glb)**
3. Select the exported `.gltf` file

Source 2 Viewer exports in metres with Y up, which is what the glTF importer expects, so models come in at the right size and orientation. Don't rescale them on import. For example, a CS2 agent is about 2 metres tall.

Textures that are self-illuminated in the game export as emissive textures, so they glow in Blender.

### Bones look like big spheres in Blender

If the armature is covered in large blobs, change these options in the import dialog under **Bones & Skin**:

- Enable **Disable Bone Shape**, or
- Lower **Bone Shape Scale**; the right value depends on the model, roughly 0.2 to 0.75, or
- Set **Bone Dir** to **Temperance**, which points bones at their children and draws no spheres. Animations still play correctly.

Blender remembers the last used import settings, and they can be saved as an operator preset. After import, the armature's **Viewport Display** can also be set to Stick or Wire.

glTF joints have no length or direction, so Blender guesses them. The default **Bone Dir** points each bone along its local +Y axis, while Source 2 bones run along local X, and draws every bone as a sphere sized at 5% of the armature's smallest dimension. IK target, root motion, cloth and helper bones get long guessed tails that stretch the armature, which makes all the spheres large. A glTF file cannot choose import options, so this has to be changed in Blender.

## Characters and Animations

Animations are part of the model export. You don't need to export animation sets separately and copy keyframes between armatures. Exporting the character model gives you all of its clips, including shared animations:

- CS2 agents in `agents/models/` include the shared player animations; `ctm_sas` has about 2000 clips. The models in `characters/models/` are placeholders, so use `agents/models/` instead
- Deadlock heroes include the clips from the hero's `clips/` folder
- Dota 2 heroes include their own animations

A character with thousands of clips makes a large file that can be slow to import. If you only need a few animations, export them by name with the command line. `--gltf_animation_list` turns on animation export by itself:

```sh
Source2Viewer-CLI -i "model.vmdl_c" -o "output.gltf" -d \
    --gltf_export_format gltf \
    --gltf_textures_adapt \
    --gltf_animation_list "idle,walk,run"
```

See [Exporting Animations](./exporting-models.md#exporting-animations) for how additive animations and keyframes are exported.

## Dota 2 Cosmetics

Dota 2 cosmetic items are separate models in `models/items/<hero>/`, and they don't follow the hero on their own. Each item comes with its own armature. To make items move with the hero:

1. Export the hero and each item to glTF
2. Import all of them into the same Blender scene
3. For each item, set the **Object** of its Armature modifier to the hero's armature. The bone names match, so the item follows the hero's animations
4. Delete the item's own armature

Which items belong to which hero is listed in `scripts/items/items_game.txt`.

## Materials and Textures

Source 2 materials are converted to standard glTF materials, so some game-specific shading is lost. How transparency is exported:

- Alpha-tested materials are exported with glTF alpha mode `MASK`, using the material's alpha test reference as the cutoff
- Translucent materials and glass are exported with alpha mode `BLEND`
- Everything else is opaque
- Materials that render back faces in the game are exported as double-sided

Foliage meshes carry vertex colours that the game uses as foliage animation data, not as colour. If foliage shows up red in Blender, remove or bypass the **Color Attribute** node in its material.

### Blended ground materials show one texture

Ground and terrain in maps often use blend materials that mix two or more textures. glTF has no blended materials, so only one texture comes through. The blend weights are kept as an extra texture coordinate set.

To rebuild a blend by hand, export the map with the [command-line utility](./command-line.md) using `--gltf_export_materials` without `--gltf_textures_adapt`, which writes every texture the material uses.

### Normal maps look black

Exported normal maps can have an alpha channel of 0, and many image editors hide the colour where alpha is 0, so the image looks black or empty. The colour data is still there. To save a copy without alpha, open the texture in Source 2 Viewer, set **Channels** to **RGB (Opaque)**, and click **Save to disk…**. See [Exporting Textures](./exporting-textures.md) for more on the texture viewer.

## Maps

Exporting a `.vmap_c` map to glTF gives you a single scene with the map geometry, the props and models placed in it, their materials and textures, and the sun. See [Exporting Maps](./exporting-maps.md#export-to-gltf) for the export steps.

Maps are large. A typical CS2 competitive map exports hundreds of texture files, and big maps take a while to export and import. Use **glTF** rather than **glTF Binary** for maps to stay clear of the 2 GB limit.

Ground that blends several textures comes in with one of them. See [Blended ground materials show one texture](#blended-ground-materials-show-one-texture).

## Known Limitations

- Only the highest level of detail (LOD0) of each model is exported
- Cloth simulation is not exported, so cloth such as coats and capes doesn't move as it does in the game
- Collision is in a separate `_physics` file, not in the main model
- Map exports don't include the skybox, baked lighting, point or spot lights, game logic, or navigation meshes
- Game-specific shader effects are reduced to standard glTF materials
- Source 2 Viewer only exports. It cannot turn your Blender changes back into game files
