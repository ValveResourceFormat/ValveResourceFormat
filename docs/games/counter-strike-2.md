---
description: Extract Counter-Strike 2 weapon, agent and knife models, skins, stickers, sounds and maps with Source 2 Viewer, and export them to glTF for Blender.
---

# Extract Counter-Strike 2 Files, Models and Maps

Counter-Strike 2 stores its models, textures, sounds and maps in VPK archives. Source 2 Viewer opens these archives and exports their contents to formats you can use in Blender, image editors and audio players. This page shows where CS2 keeps each kind of asset. The general steps are in the [guides](#see-also).

Guides written for CS:GO or for older tools often use folder names and file formats that no longer apply. CS2 files are compiled: their extensions end in `_c` (such as `.vmdl_c` or `.vtex_c`), and they need Source 2 Viewer to decompile them.

## Where the Files Are

CS2 is installed in `steamapps\common\Counter-Strike Global Offensive` in your Steam library. The game files are in `game\csgo`.

| What                          | Where                                                     |
| ----------------------------- | --------------------------------------------------------- |
| Models, textures, sounds, UI  | `game\csgo\pak01_dir.vpk`                                 |
| Official maps                 | `game\csgo\maps\<map>.vpk`, such as `de_dust2.vpk`        |
| Workshop maps                 | `steamapps\workshop\content\730\<item id>\`               |

Open `pak01_dir.vpk`, not the numbered `pak01_000.vpk`, `pak01_001.vpk` files next to it. The `_dir` file is the index, and Source 2 Viewer reads the numbered files from it. You can also find CS2 in the **Explorer** tab, which lists installed games and their workshop content. See [How to Open and Extract VPK Files](../guides/vpk-management.md).

Press <kbd>Ctrl</kbd>+<kbd>F</kbd> to search for a file by name.

## What's Inside pak01_dir.vpk

| Asset                  | Path inside the VPK                                    | Example                                                    |
| ---------------------- | ------------------------------------------------------ | ---------------------------------------------------------- |
| Weapons                | `weapons/models/<weapon>/`                             | `weapons/models/ak47/weapon_rif_ak47.vmdl_c`               |
| Knives                 | `weapons/models/knife/<knife>/`                        | `weapons/models/knife/knife_karambit/weapon_knife_karambit.vmdl_c` |
| Agents (player models) | `agents/models/<agent>/`                               | `agents/models/ctm_sas/ctm_sas.vmdl_c`                     |
| Gloves and arms        | `agents/models/shared/arms/`                           | `agents/models/shared/arms/glove_bloodhound/glove_bloodhound.vmdl_c` |
| Props and world models | `models/`                                              |                                                            |
| Skin finishes          | `weapons/paints/<collection>/`                         | `weapons/paints/legacy/aq_ak47_cartel.vcompmat_c`          |
| Skin textures          | `materials/models/weapons/customization/paints/`       |                                                            |
| Stickers               | `stickers/<event or collection>/`                      | `stickers/cologne2026/`                                    |
| Charms (keychains)     | `weapons/keychains/`, textures in `items/assets/keychains/` |                                                       |
| Coins and pins         | `models/inventory_items/`                              | `models/inventory_items/10_year_coin.vmdl_c`               |
| Inventory icons        | `panorama/images/econ/`                                |                                                            |
| Weapon sounds          | `sounds/weapons/`                                      |                                                            |
| Player sounds          | `sounds/player/`                                       |                                                            |
| Music kits             | `sounds/music/<kit>/`                                  | `sounds/music/3kliksphilip_01/`                            |
| Voice lines            | `sounds/vo/agents/`, `sounds/vo/announcer/`            | `sounds/vo/agents/balkan/affirmative01.vsnd_c`             |
| Item definitions       | `scripts/items/items_game.txt`                         |                                                            |
| Item names             | `resource/csgo_english.txt`                            | Other languages are `resource/csgo_<language>.txt`         |

## Weapons and Knives

Each weapon has a folder in `weapons/models/` with its model and a `materials/` folder. The folder names are short (`ak47`, `awp`, `m4a1_silencer`), and the model files inside use longer names such as `weapon_rif_ak47.vmdl_c` and `weapon_snip_awp.vmdl_c`. Some weapons have their magazine as a separate model, such as `weapon_rif_ak47_mag.vmdl_c`.

Knives are in `weapons/models/knife/`, one folder per knife, such as `knife_karambit`, `knife_butterfly` and `knife_m9`.

Double-click a model to preview it, then right-click it and select **Decompile & export** to save it as glTF. See [Exporting Models](../guides/exporting-models.md).

Weapon models such as the AK-47 hold two versions of the weapon, `body_hd` (the default) and `body_legacy`, which overlap in the exported file. Delete the mesh you don't need after import. The weapon's own animations, such as `reload` and `inventory_inspect`, are included.

## Agents

Player models are in `agents/models/`, one folder per agent family, such as `ctm_sas`, `ctm_fbi`, `tm_phoenix` and `tm_balkan`. Variants of an agent are separate models in the same folder, such as `ctm_sas_variantf.vmdl_c`.

::: warning
Older guides point at `characters/models/`. The models in that folder are now only placeholders. Use the models in `agents/models/` instead.
:::

When you export an agent with **Decompile & export**, its animations are included in the same glTF file, including the shared player animations. You don't need to export animation sets separately.

Agents have no separate eye models or materials. The eyes are part of the head mesh and texture.

Gloves and bare arms are separate models in `agents/models/shared/arms/`. A glove model's own colour texture is a grey placeholder, because its look comes from its finish in `gloves/paints/`. See [Weapon Skins](#weapon-skins).

## Weapon Skins

A skin finish is a composite material: a `.vcompmat_c` file that tells the game how to combine paint textures with a weapon's base material. The finishes are in `weapons/paints/`, grouped in folders such as `legacy`, `community` and one folder per collection. Glove finishes are in `gloves/paints/`.

The textures that the finishes use are in `materials/models/weapons/customization/paints/`, and some finishes use textures from `items/assets/paintkits/`. These are `.vtex_c` textures that you can export to PNG. See [Exporting Textures](../guides/exporting-textures.md).

**Decompile & export** on a `.vcompmat_c` file saves it as a `.vcompmat` text file that lists the materials and textures it uses. Source 2 Viewer does not apply a finish to a weapon model, so it cannot export a weapon with a skin already on it.

Inventory icons of weapon skins are in `panorama/images/econ/default_generated/`.

## Item Names in items_game.txt

Files in the VPK use internal names. To find which file belongs to an item you know from the game, open these two text files:

- `scripts/items/items_game.txt` defines every item. Agents have a `model_player` line with the path to their model. Skins are listed under `paint_kits`, where `name` is the file name of the finish in `weapons/paints/`, and newer entries also have a `composite_material_path`.
- `resource/csgo_english.txt` turns the `#` tokens in `items_game.txt` into the names you see in the game. For example, a paint kit's `description_tag` of `#PaintKit_aq_p250_cartel_Tag` is "Cartel" in `csgo_english.txt`.

Both files can be read directly in Source 2 Viewer. Use <kbd>Ctrl</kbd>+<kbd>F</kbd> with **File Contents (Case Sensitive)** to find which file mentions a name.

## Stickers, Charms and Coins

- **Stickers** are materials and textures in `stickers/`, with one folder per tournament or sticker collection.
- **Charms** (keychains) are models in `weapons/keychains/`, with their textures in `items/assets/keychains/`.
- **Coins, pins and trophies** are models in `models/inventory_items/`.

## Music Kits and Voice Lines

Each music kit has a folder in `sounds/music/`, named after the artist with a number, such as `3kliksphilip_01`. The files in it are named after when they play, such as `mainmenu.vsnd_c`, `startround_01.vsnd_c`, `roundmvpanthem_01.vsnd_c` and `wonround.vsnd_c`.

Agent voice lines are in `sounds/vo/agents/`, one folder per voice, such as `balkan`, `fbihrt` and `gsg9`. These voice names don't always match the agent model folders. The announcer is in `sounds/vo/announcer/`.

Right-click a sound or a whole folder and select **Decompile & export** to save it as WAV or MP3. See [Exporting Sounds](../guides/exporting-sounds.md).

## Maps

Each official map is its own VPK in `game\csgo\maps\`, such as `de_dust2.vpk` or `de_mirage.vpk`. Open the map's VPK and find `maps/<map>.vmap_c` inside it.

Many maps also have a `_vanity` VPK, such as `de_dust2_vanity.vpk`. These are cut-down versions used as main menu backgrounds, so use the VPK without the suffix to get the whole map.

Maps can be exported to glTF for Blender or decompiled to `.vmap` for Hammer. See [Exporting Maps](../guides/exporting-maps.md).

## See Also

- [Getting Started](../guides/getting-started.md)
- [How to Open and Extract VPK Files](../guides/vpk-management.md)
- [Exporting Models](../guides/exporting-models.md)
- [Importing into Blender](../guides/importing-into-blender.md)
- [Exporting Maps](../guides/exporting-maps.md)
- [Exporting Textures](../guides/exporting-textures.md)
- [Exporting Sounds](../guides/exporting-sounds.md)
- [Command-line utility](../guides/command-line.md)
