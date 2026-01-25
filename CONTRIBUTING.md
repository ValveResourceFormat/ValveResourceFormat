When contributing new code, run `dotnet format` and make sure tests pass with `dotnet test`.

If you need help or pointers, [join our Discord](https://discord.gg/s9QQ7Wg7r4)

## Library (ValveResourceFormat)

All new code should preferably go into the library component, which is shared by the GUI, CLI, and the library itself is published on NuGet for others to use.

## Tests

If you are adding support for new file formats (or new file versions),
add the smallest possible test files to the [Tests/Files](Tests/Files) folder.

If it's not a generic `_c` file, then you will have to add a test case to
actually load and test the new files.

## Shaders

If you are modifying shaders, you need to run shader validator to ensure all shaders compile successfully, as we use runtime compilation.
To perform full validation:
```sh
dotnet run --project misc/shadervalidator
```

To perform filtered validation:
```sh
dotnet run --project misc/shadervalidator "water"
```

## CLI

New file formats should preferably also be readable by the CLI,
at the very least just print information from the parsed file to console.

## Manually defined data

There are places in VRF which define certain mappings that are required to be updated manually.
This is a list of these places to make them easier to track.

### Asset icons for the GUI

[GUI/Icons/AssetTypes](GUI/Icons/AssetTypes/) folder contains png files of assets that have unique icons in the
package viewer. For example `mp3.png` will be used for files ending with `.mp3`.

These icons should all have the same size. Use [TinyPNG](https://tinypng.com/) to optimize them.

If a high resolution icon is available (from Source 2 tools), put it in [Misc/Icons/AssetTypes](Misc/Icons/AssetTypes) folder.

### Known entity key names

Map files have entity lumps and every entity has a key value, but the keys use murmur hashes instead of strings.
To map them back to strings we have a big list of known key names which are hashed at runtime and a backwards lookup is performed.

The list is in [EntityLumpKnownKeys.cs](ValveResourceFormat/Utils/EntityLumpKnownKeys.cs) file.
All keys must be lowercase, and the list is sorted alphabetically.

CLI Decompiler can help collect unknown hashes by using `--stats --dump_unknown_entity_keys`.
When scanning `vents_c` files, a `unknown_keys.txt` file will be created.

This file can be used with [MurmurHashMatcher](Misc/MurmurHashMatcher) utility which bruteforces game files and binaries to find strings.

There is also a [VrfFgdParser](Misc/VrfFgdParser) tool which parses FGD files to extract all possible key names and entity icons.

### Entity icons for the map viewer

[HammerEntities.cs](GUI/Utils/HammerEntities.cs) contains a mapping of entity names and their Hammer icons (sprite or model).
Unfortunately different games may have different paths for these icons, so they may not always be available.

Use [VrfFgdParser](Misc/VrfFgdParser) to extract them.

### Collision tags to tool texture mappings

[MapExtract.cs](ValveResourceFormat/IO/MapExtract.cs) contains various mappings of entity/collision tags to Hammer tool textures. These are used in map decompiler and to display an adequate texture in map physics viewer.

### Material texture mappings for correct extraction

[ShaderDataProvider.cs](ValveResourceFormat/IO/ShaderDataProvider.cs) contains a list of shader names and which texture files are used, along with which channels they use.

This is required for correct material extraction. These mappings are used as a fall-back when querying VCS files fails in some way.

# For VRF maintainers

## Making a new release

### With a script
1. Run `dotnet run misc/release.cs X.X`
2. `git push --follow-tags`

### Manually
1. Bump `ProjectBaseVersion` in `Directory.Build.props` file
2. Commit it with a message like `Bump version to X.X`
3. Create a signed tag: `git tag -s X.X` (or `-a` if not signed)
4. `git push --follow-tags`

### After

- Wait for CI build to publish a release on GitHub
- Login to SignPath and approve the signing
- Update release with changelog
