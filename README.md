# Source2Viewer - d2pfx

Source2Viewer - d2pfx is a modded build of [Source 2 Viewer](https://github.com/ValveResourceFormat/ValveResourceFormat) (ValveResourceFormat), made for the [d2pfx](https://h6rd.github.io/Dota2PornFxWeb/) community.

It adds a custom VMDL extraction option to the package viewer's right-click menu, built on top of [VmdlExtractor](https://github.com/fvckmindself/VmdlExtractor) by [fvckmindself](https://github.com/fvckmindself).

It also adds a custom VMAT extraction option for `.vmat_c` materials. The option writes every texture the material uses next to the `.vmat` and points the `.vmat` at them by their path inside your addon (for example `j0nathan550/materials/razor_arcana_head_color.png`), so the material compiles without any red, missing textures. Cubemaps are stitched into one 4:3 cross `.png`, the layout Dota 2's own cubemap sources use, instead of six separate face images. Each custom option only appears when the selection contains files of its type.

It also adds an "Export character assets (items_game.txt)" option to the right-click menu of Dota 2's `pak01_dir.vpk`. It reads `scripts/items/items_game.txt` and the hero scripts from the package every time it opens, so it always matches the current game. Pick a hero, an item set or the item and style for each loadout slot, and see the result in a live preview. Heroes with a persona switch their whole loadout when the persona is picked. The export then collects everything the hero and items use: models, materials, textures, particles, panorama icons and the hero's `game_sounds` and `game_sounds_vo` sound event files, following every resource's references. The sounds themselves are only exported when you ask for them, otherwise the sound event files keep pointing at the ones in the game. It writes everything into one folder laid out like the package. Models go through the custom VMDL extractor and materials through the custom VMAT exporter, with texture paths relative to the export folder. Everything else goes through the built-in decompiler.

With "Replace default assets" on, the export also writes the chosen look over the hero's default files, so it shows without the items equipped. The arcana or persona model is written as the hero's base model (e.g. `models/heroes/earthshaker/earthshaker.vmdl`) with the chosen style's skin as its default. Chosen items and their refits are written over the default items' models. Particles the items swap in are written over the ones they replace, and particles the items create are added to the models as model particles. Particles every hero uses, like the blink dagger or stun effects, are only replaced when you also tick "Also shared particles".

## Credits

- [Source 2 Viewer / ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat) and its original authors and contributors, whose work this fork is built entirely on top of.
- [fvckmindself](https://github.com/fvckmindself) for [VmdlExtractor](https://github.com/fvckmindself/VmdlExtractor), the custom VMDL extraction pipeline this fork integrates.
- [J0nathan550](https://github.com/J0nathan550) for this fork.

## License

Contents of this repository are available under the [MIT license](LICENSE), except for the `Tests/Files` folder, which contains files that have likely come from Valve's games.

Third party code and assets used by this project are listed in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).

This project is not affiliated with Valve Software. Source 2 is a trademark and/or registered trademark of Valve Corporation.
