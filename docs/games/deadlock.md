---
description: Extract Deadlock hero models with animations, voice lines, music, vdata scripts and maps with Source 2 Viewer, and export heroes to glTF for Blender.
---

# Extract Deadlock Hero Models, Animations and Files

Deadlock stores its heroes, sounds, scripts and maps in VPK archives. Source 2 Viewer opens these archives and exports their contents to formats you can use in Blender, image editors and audio players. This page shows where Deadlock keeps each kind of asset, and how to find a hero whose folder has a different name. The general steps are in the [guides](#see-also).

Deadlock files are compiled: their extensions end in `_c` (such as `.vmdl_c` or `.vnmclip_c`), and they need Source 2 Viewer to decompile them. Guides written for older tools may use steps or paths that no longer apply.

## Where the Files Are

Deadlock is installed in `steamapps\common\` in your Steam library, in a folder that may be named `Project8Staging`. The game files are in `game\citadel` inside it.

| What                            | Where                                                 |
| ------------------------------- | ----------------------------------------------------- |
| Heroes, sounds, scripts         | `game\citadel\pak01_dir.vpk`                          |
| Maps                            | `game\citadel\maps\<map>.vpk`, such as `dl_midtown.vpk` |
| Hero names in each language     | `game\citadel\resource\localization\citadel_gc_hero_names\` |

Open `pak01_dir.vpk`, not the numbered `pak01_000.vpk`, `pak01_001.vpk` files next to it, and not the VPKs in `game\core`. The `_dir` file is the index, and Source 2 Viewer reads the numbered files from it. Source 2 Viewer detects Deadlock automatically, so you can also find it in the **Explorer** tab. See [How to Open and Extract VPK Files](../guides/vpk-management.md).

On the [command line](../guides/command-line.md), `steam:1422450/game/citadel/pak01_dir.vpk` points at the VPK wherever Deadlock is installed.

## What's Inside pak01_dir.vpk

| Asset             | Path inside the VPK                                   | Example                                        |
| ----------------- | ----------------------------------------------------- | ---------------------------------------------- |
| Newer hero models | `models/heroes_wip/<hero>/`                           | `models/heroes_wip/abrams/abrams.vmdl_c`       |
| Older hero models | `models/heroes_staging/<hero>/`                       | `models/heroes_staging/chrono/chrono.vmdl_c`   |
| Hero animations   | `clips/` folders in the hero folders                  | `models/heroes_wip/abrams/clips/ability_charge.vnmclip_c` |
| Voice lines       | `sounds/vo/<hero>/`                                   | `sounds/vo/atlas/`                             |
| Announcer         | `sounds/vo/announcer/`                                |                                                |
| Ability sounds    | `sounds/abilities/<hero>/`                            | `sounds/abilities/forge/a1_turret/deploy_full.vsnd_c` |
| Sound events      | `soundevents/hero/<hero>.vsndevts_c`                  | `soundevents/hero/forge.vsndevts_c`            |
| Music             | `sounds/music/`                                       |                                                |
| Hero images       | `panorama/images/heroes/`                             | `panorama/images/heroes/archer_card_psd.vtex_c` |
| Ability particles | `particles/abilities/<hero>/`                         | `particles/abilities/abrams/`                  |
| Hero definitions  | `scripts/heroes.vdata_c`                              |                                                |
| Ability definitions | `scripts/abilities.vdata_c`                         |                                                |

## Hero Models

Hero models are in two folders:

- `models/heroes_wip/` holds the newer and reworked heroes, such as `abrams`, `ivy`, `mcginnis`, `geist` and `pocket`.
- `models/heroes_staging/` holds older models, such as `archer`, `chrono` and `hornet_v3`.

Each hero has a folder named after its internal name, which is often different from its name in the game. Heroes also move between the two folders, and some have several versions, such as `archer` and `archer_v2`, or `heroes_staging/inferno_v4` as well as `heroes_wip/inferno`. The folder with the highest version number is not always the one the game uses.

Double-click a model to preview it with its animations. To use a hero in Blender, right-click the model and select **Decompile & export**, then choose glTF. See [Exporting Models](../guides/exporting-models.md).

### Finding a Hero's Model

The game's own files say which model each hero uses, so you can look it up even after an update moves things around:

1. Open `game\citadel\resource\localization\citadel_gc_hero_names\citadel_gc_hero_names_english.txt`. This is a plain text file on disk, outside the VPK. Find the hero's name to get its `hero_` id, such as `"hero_atlas:n" "Abrams"`.
2. In `pak01_dir.vpk`, open `scripts/heroes.vdata_c` and find that id, such as `hero_atlas`. Its `m_strModelName` line has the path to the model the game uses.

Some examples from the current game files:

| In the game | Hero id       | Model                                               |
| ----------- | ------------- | --------------------------------------------------- |
| Abrams      | `hero_atlas`  | `models/heroes_wip/abrams/abrams.vmdl_c`            |
| Ivy         | `hero_tengu`  | `models/heroes_wip/ivy/ivy.vmdl_c`                  |
| McGinnis    | `hero_forge`  | `models/heroes_wip/mcginnis/mcginnis.vmdl_c`        |
| Lady Geist  | `hero_ghost`  | `models/heroes_wip/geist/geist.vmdl_c`              |
| Paradox     | `hero_chrono` | `models/heroes_staging/chrono/chrono.vmdl_c`        |
| Grey Talon  | `hero_orion`  | `models/heroes_staging/archer/archer.vmdl_c`        |
| Vindicta    | `hero_hornet` | `models/heroes_staging/hornet_v3/hornet.vmdl_c`     |

::: tip
These paths change between game updates. If one is missing, look the hero up in `heroes.vdata_c` again.
:::

### Animations

Deadlock heroes keep their animations as separate clip files (`.vnmclip_c`) in a `clips` folder. This folder is often next to the model, but not always: Ivy's model is in `models/heroes_wip/ivy/`, while her clips are in `models/heroes_staging/tengu/tengu_v2/clips/`.

You don't need to find or export the clips yourself. When you export the model with **Decompile & export**, the animations it uses are included in the same glTF file, wherever the clips are stored.

## Voice Lines and Music

Voice lines are in `sounds/vo/`, one folder per hero. These folders use the hero id, not the model folder name: Abrams is `sounds/vo/atlas/`, Ivy is `sounds/vo/tengu/`, McGinnis is `sounds/vo/forge/`, Vindicta is `sounds/vo/hornet/` and Grey Talon is `sounds/vo/orion/`. The announcer is in `sounds/vo/announcer/`.

Music is in `sounds/music/`, in folders such as `arpeggiator`, `brawl`, `match_intro` and `menu`.

Right-click a sound or a whole folder and select **Decompile & export** to save it as WAV or MP3. See [Exporting Sounds](../guides/exporting-sounds.md).

## Hero and Ability Data

Hero and ability stats are in `.vdata_c` files in `scripts/`, such as `scripts/heroes.vdata_c` and `scripts/abilities.vdata_c`. Double-click one to read it as text, or right-click it and select **Decompile & export** to save the decompiled text.

## Maps

Maps are separate VPKs in `game\citadel\maps\`, such as `dl_midtown.vpk` and `dl_hideout.vpk`. Open the map's VPK and find `maps/<map>.vmap_c` inside it.

Maps can be exported to glTF for Blender or decompiled to `.vmap` for Hammer. See [Exporting Maps](../guides/exporting-maps.md).

## See Also

- [Getting Started](../guides/getting-started.md)
- [How to Open and Extract VPK Files](../guides/vpk-management.md)
- [Exporting Models](../guides/exporting-models.md)
- [Importing into Blender](../guides/importing-into-blender.md)
- [Exporting Maps](../guides/exporting-maps.md)
- [Exporting Sounds](../guides/exporting-sounds.md)
- [Command-line utility](../guides/command-line.md)
