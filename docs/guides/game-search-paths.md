# Game Search Paths

Most game files depend on other files. A model needs its materials, a material needs its textures, and a map needs all of the models placed in it. Source 2 Viewer finds these files the same way the game does, by looking through the game's folders and VPK archives. These folders and archives are called game search paths.

When the files can't be found, models and materials show up without textures, and maps are missing props.

## Files From Installed Games

Usually there is nothing to set up. When you open a file from a game's folder, Source 2 Viewer works out which game it belongs to and looks for its dependencies in that game's files. This includes everything you open from the **Explorer** tab.

This works for:

- **Game files**, such as `Counter-Strike Global Offensive/game/csgo/maps/de_dust2.vpk`. The game's folders are listed in its `gameinfo.gi` file.
- **Workshop items**, which Steam stores in `steamapps/workshop/content/<appid>/`. Their dependencies are loaded from the game they belong to if it is installed, even when it is in a different Steam library.
- **Addons you are working on** in Workshop Tools, such as `csgo_addons/my_addon/`. The addon's own files take priority over the game's files.
- **Official community maps** in Counter-Strike 2. Their content in `csgo_community_addons` is loaded along with the map.
- **Addon dependencies**, in games that support them, such as SteamVR Home. Workshop items and addons that an addon depends on are loaded too, as long as they are installed.

## Files Outside of a Game

Files that are not inside a game folder, such as a VPK you downloaded or files extracted to another folder, aren't linked to any game. To load their textures and other dependencies, add the game in **Settings** under **Game content search paths**:

- **Add .vpk or gameinfo.gi**: pick the game's `gameinfo.gi`. This is the recommended option, as it adds everything the game uses. For Counter-Strike 2, this is `Counter-Strike Global Offensive/game/csgo/gameinfo.gi`. You can also pick a single VPK to add only that archive.
- **Add folder**: adds the files in that folder, but not the contents of VPK archives inside it. Add those with the button above.

Changes apply to files you open afterwards, so reopen any files that are already open.

::: warning
The paths in settings are used for every file you open, whichever game it is from. If you add paths from several games, files can be loaded from the wrong game. Remove paths you no longer need.

If a file in the list has been moved or deleted, other paths in the list may stop working too. Remove it from the list.
:::

## Command Line

The [command-line utility](./command-line.md) finds the game in the same way. For files outside of a game folder, pass the game's `gameinfo.gi`, or the folder that contains it, with `--game`:

```powershell
./Source2Viewer-CLI.exe -i "chicken.vmdl_c" -o "chicken.glb" --gltf_export_format glb --gltf_export_materials --game "steam:730/game/csgo"
```

`steam:730/` stands for the folder where Steam app 730 (Counter-Strike 2) is installed, so the same command works on any computer that has the game.

## Troubleshooting

The **Console** tab in Source 2 Viewer shows which game was found and which files couldn't be loaded. The command-line utility prints the same messages.

- No `Found "<game>"` message: the game wasn't detected. Add its `gameinfo.gi` in settings, or pass `--game` on the command line.
- `Failed to load "<file>"` messages: the game was found, but those files aren't in it. Check that the game is fully installed and up to date. For workshop items, also check that any items they depend on are installed.
