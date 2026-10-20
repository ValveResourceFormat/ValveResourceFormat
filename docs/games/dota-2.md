---
description: Extract Dota 2 hero models, cosmetic items, particles, icons, music and maps with Source 2 Viewer, and export heroes with animations to glTF for Blender.
---

# Extract Dota 2 Hero Models, Cosmetics and Files

Dota 2 stores its heroes, cosmetic items, particles, sounds and maps in VPK archives. Source 2 Viewer opens these archives and exports their contents to formats you can use in Blender, image editors and audio players. This page shows where Dota 2 keeps each kind of asset. The general steps are in the [guides](#see-also).

Guides written for older tools often use folder names and file formats that no longer apply. Dota 2 files are compiled: their extensions end in `_c` (such as `.vmdl_c` or `.vtex_c`), and they need Source 2 Viewer to decompile them.

## Where the Files Are

Dota 2 is installed in `steamapps\common\dota 2 beta` in your Steam library. The game files are in `game\dota`.

| What                            | Where                                       |
| ------------------------------- | ------------------------------------------- |
| Heroes, items, particles, sounds | `game\dota\pak01_dir.vpk`                  |
| The main map                    | `game\dota\maps\dota.vpk`                   |
| Workshop content                | `steamapps\workshop\content\570\<item id>\` |

Open `pak01_dir.vpk`, not the numbered `pak01_000.vpk`, `pak01_001.vpk` files next to it. The `_dir` file is the index, and Source 2 Viewer reads the numbered files from it. You can also find Dota 2 in the **Explorer** tab, which lists installed games and their workshop content. See [How to Open and Extract VPK Files](../guides/vpk-management.md).

## What's Inside pak01_dir.vpk

| Asset               | Path inside the VPK                                   | Example                                                     |
| ------------------- | ----------------------------------------------------- | ----------------------------------------------------------- |
| Heroes              | `models/heroes/<hero>/`                               | `models/heroes/axe/axe.vmdl_c`                              |
| Cosmetic items      | `models/items/<hero>/<item>/`                         | `models/items/axe/aghsbp_2021_axe_belt/`                    |
| Couriers            | `models/courier/`                                     |                                                             |
| Hero particles      | `particles/units/heroes/hero_<hero>/`                 | `particles/units/heroes/hero_axe/axe_armor.vpcf_c`          |
| Cosmetic particles  | `particles/econ/items/<hero>/`                        | `particles/econ/items/axe/`                                 |
| Hero icons          | `panorama/images/heroes/icons/`                       | `panorama/images/heroes/icons/npc_dota_hero_axe_png.vtex_c` |
| Hero portraits      | `panorama/images/heroes/`                             | `panorama/images/heroes/npc_dota_hero_axe_png.vtex_c`       |
| Item icons          | `panorama/images/econ/items/<hero>/`                  |                                                             |
| Loading screens     | `panorama/images/loadingscreens/`, `panorama/images/econ/loading_screens/` |                                        |
| Voice lines         | `sounds/vo/<hero>/`                                   | `sounds/vo/axe/`                                            |
| Announcers          | `sounds/vo/announcer/`, `sounds/vo/announcer_dlc_<name>/` | `sounds/vo/announcer_dlc_axe/`                          |
| Music packs         | `sounds/music/<pack>/`                                | `sounds/music/valve_dota_001/`                              |
| Item definitions    | `scripts/items/items_game.txt`                        |                                                             |
| Item names          | `resource/localization/items_english.txt`             |                                                             |
| Hero names          | `resource/localization/dota_english.txt`              |                                                             |

## Hero Names

Hero folders use internal names, which are sometimes old names of the hero. A few examples:

| In the game     | Folder name  |
| --------------- | ------------ |
| Shadow Fiend    | `nevermore`  |
| Clockwerk       | `rattletrap` |
| Nature's Prophet | `furion`    |
| Io              | `wisp`       |
| Timbersaw       | `shredder`   |

The same name is used in `models/heroes/`, `sounds/vo/` and the icon file names. To look up any other hero, search `resource/localization/dota_english.txt` for its name: each hero has a `npc_dota_hero_<name>__name_alias` line with its internal name.

## Hero Models and Their Parts

A hero folder holds the hero's body and its default items as separate models. For example, `models/heroes/axe/` has `axe.vmdl_c` for the body and `axe_armor.vmdl_c`, `axe_belt.vmdl_c`, `axe_hair.vmdl_c`, `axe_ponytail.vmdl_c` and `axe_weapon.vmdl_c` for the parts he wears.

Double-click a model to preview it with its animations. To use it in Blender, right-click it and select **Decompile & export**, then choose glTF. The hero's animations are included in the same file. See [Exporting Models](../guides/exporting-models.md).

Some animation names start with `@` or `@@`, such as `@run` next to `run`. They are separate animations and export under the same names.

Hero materials use a Dota-specific shader with rim light and specular masks that standard glTF materials can't hold. The export keeps the colour, normal, metalness and specular textures, so heroes look flatter in Blender than in the game. See [Known Limitations](../guides/importing-into-blender.md#known-limitations).

Export the body and each part you want. Every part is its own model with its own skeleton, so the parts don't follow the hero's animations by themselves after import. The bone names match the hero's skeleton, so in Blender you can point each part's **Armature** modifier at the hero's armature and delete the part's own armature. See [Importing into Blender](../guides/importing-into-blender.md).

## Cosmetic Items

Cosmetic items are in `models/items/<hero>/`, with one folder per item. Like the default parts, each item is a separate model that you export and attach in the same way.

To find which folder belongs to an item you know from the game, open `scripts/items/items_game.txt`. Each item entry has:

- `name`: the item's name, such as "Carnage of the Endless Hunt - Belt"
- `item_slot`: where the hero wears it, such as `belt`
- `model_player`: the path to its model, such as `models/items/axe/aghsbp_2021_axe_belt/aghsbp_2021_axe_belt.vmdl`
- `item_name`: a `#` token that `resource/localization/items_english.txt` turns into the name shown in the game

`items_game.txt` is very large. Use <kbd>Ctrl</kbd>+<kbd>F</kbd> with **File Contents (Case Sensitive)** to find which file mentions a name, or export the file and search it in a text editor.

## Particles

Hero ability effects are in `particles/units/heroes/hero_<hero>/`, and effects of cosmetic items are in `particles/econ/items/<hero>/`. Double-click a `.vpcf_c` file to preview the effect. **Decompile & export** saves it as a `.vpcf` text file.

## Icons and Loading Screens

Hero icons are in `panorama/images/heroes/icons/`, larger portraits are in `panorama/images/heroes/`, and item icons are in `panorama/images/econ/items/`. Loading screens are in `panorama/images/loadingscreens/` and `panorama/images/econ/loading_screens/`. These are textures that you can export to PNG. See [Exporting Textures](../guides/exporting-textures.md).

## Voice Lines and Music

Each hero's voice lines are in `sounds/vo/<hero>/`, using the internal hero name. Music packs are in `sounds/music/`, one folder per pack, such as `valve_dota_001` or `deadmau5_01`.

Right-click a sound or a whole folder and select **Decompile & export** to save it as WAV or MP3. See [Exporting Sounds](../guides/exporting-sounds.md).

## Maps

The main map is `game\dota\maps\dota.vpk`. Open it and find `maps/dota.vmap_c` inside. The other VPK files in `game\dota\maps\` hold other maps and variants of the main map.

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
