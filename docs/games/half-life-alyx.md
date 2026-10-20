---
description: "Extract Half-Life: Alyx character, creature and weapon models, voice lines, music and maps with Source 2 Viewer, and export them to glTF for Blender."
---

# Extract Half-Life: Alyx Models, Sounds and Maps

Half-Life: Alyx stores its models, textures, sounds and maps in VPK archives. Source 2 Viewer opens these archives and exports their contents to formats you can use in Blender, image editors and audio players. This page shows where the game keeps each kind of asset. The general steps are in the [guides](#see-also).

Half-Life: Alyx files are compiled: their extensions end in `_c` (such as `.vmdl_c` or `.vsnd_c`), and they need Source 2 Viewer to decompile them. Guides written for Source 1 Half-Life games or older tools use paths and formats that don't apply here.

## Where the Files Are

Half-Life: Alyx is installed in `steamapps\common\Half-Life Alyx` in your Steam library. The game files are in `game\hlvr`.

| What                      | Where                                                  |
| ------------------------- | ------------------------------------------------------ |
| Models, textures, sounds  | `game\hlvr\pak01_dir.vpk`                              |
| Maps                      | `game\hlvr\maps\<map>.vpk`, such as `a1_intro_world.vpk` |
| Workshop addons           | `steamapps\workshop\content\546560\`                   |

Open `pak01_dir.vpk`, not the numbered `pak01_000.vpk`, `pak01_001.vpk` files next to it. The `_dir` file is the index, and Source 2 Viewer reads the numbered files from it. You can also find the game and its workshop addons in the **Explorer** tab. See [How to Open and Extract VPK Files](../guides/vpk-management.md).

## What's Inside pak01_dir.vpk

| Asset       | Path inside the VPK           | Example                                          |
| ----------- | ----------------------------- | ------------------------------------------------ |
| Characters  | `models/characters/<name>/`   | `models/characters/alyx/alyx.vmdl_c`             |
| Creatures   | `models/creatures/<name>/`    | `models/creatures/headcrab_classic/`             |
| Weapons     | `models/weapons/<name>/`      | `models/weapons/vr_alyxgun/vr_alyxgun.vmdl_c`    |
| Props       | `models/props/` and other `models/props_*` folders |                             |
| Voice lines | `sounds/vo/<character>/`      | `sounds/vo/alyx/`, `sounds/vo/russell/`          |
| Music       | `sounds/music/places/`, `sounds/music/ambient/` |                                |

## Characters, Creatures and Weapons

- **Characters** are in `models/characters/`, such as `alyx`, `eli`, `gman`, `larry`, `olga` and the Combine soldiers (`combine_grunt`, `combine_soldier`, `combine_soldier_captain`).
- **Creatures** are in `models/creatures/`, such as `headcrab_classic`, `antlion`, `barnacle`, `strider`, `vortigaunt` and `zombie_classic`.
- **Weapons** are in `models/weapons/`, such as `vr_alyxgun`, `vr_shotgun` and `vr_irifle`.

Double-click a model to preview it with its animations. Right-click it and select **Decompile & export**, then choose glTF to use it in Blender. Animations are included in the same file. See [Exporting Models](../guides/exporting-models.md).

## Voice Lines and Music

Voice lines are in `sounds/vo/`, one folder per character, such as `alyx`, `russell`, `eli`, `gman`, `vortigaunt` and `combine`.

Music is in `sounds/music/places/` and `sounds/music/ambient/`.

Right-click a sound or a whole folder and select **Decompile & export** to save it as WAV or MP3. See [Exporting Sounds](../guides/exporting-sounds.md).

## Maps

Each level is its own VPK in `game\hlvr\maps\`, such as `a1_intro_world.vpk`, `a2_hideout.vpk` and `a5_vault.vpk`. Open the map's VPK and find `maps/<map>.vmap_c` inside it.

Most levels also have a `_commentary` VPK, such as `a2_hideout_commentary.vpk`, for the developer commentary mode. These are small and hold only a few entities, not the level itself, so open the VPK without the suffix to get the level.

Maps can be exported to glTF for Blender or decompiled to `.vmap` for Hammer. See [Exporting Maps](../guides/exporting-maps.md).

## Workshop Addons

Steam downloads workshop addons to `steamapps\workshop\content\546560\`, one folder per item. They show up in the **Explorer** tab, and their models and materials load the textures they need from the game. See [Game Search Paths](../guides/game-search-paths.md).

## See Also

- [Getting Started](../guides/getting-started.md)
- [How to Open and Extract VPK Files](../guides/vpk-management.md)
- [Exporting Models](../guides/exporting-models.md)
- [Importing into Blender](../guides/importing-into-blender.md)
- [Exporting Maps](../guides/exporting-maps.md)
- [Exporting Sounds](../guides/exporting-sounds.md)
- [Game Search Paths](../guides/game-search-paths.md)
