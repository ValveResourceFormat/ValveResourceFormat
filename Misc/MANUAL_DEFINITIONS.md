There are places in VRF which define certain mappings that are required to be updated manually.
This is a list of these places so it is easier to keep track.

## Asset icons for the GUI

[GUI/AssetTypes](../GUI/AssetTypes/) folder contains png files of assets that have unique icons in the
package viewer. For example `mp3.png` will be used for files ending with `.mp3`.

These icons should all have the same size. Use [TinyPNG](https://tinypng.com/) to optimize them.

If a high resolution icon is available (from Source 2 tools), put it in [Icons/AssetTypes](../Icons/AssetTypes) folder.

## Known entity key names

Map files have entity lumps and every entity has a key value, but the keys use murmur hashes instead of strings.
To map them back to strings we have a big list of known key names which are hashed at runtime and a backwards lookup is performed.

The list is in [EntityLumpKnownKeys.cs](../ValveResourceFormat/Utils/EntityLumpKnownKeys.cs) file.
All keys must be lowercase, and the list is sorted alphabetically.

CLI Decompiler can help collecting unknown hashes by using `--stats --dump_unknown_entity_keys`.
When scanning `vents_c` files, a `unknown_keys.txt` file will be created.

This file can be used with (MurmurHashMatcher)[../Misc/MurmurHashMatcher] utility which bruteforces game files and binaries to find strings.

There is also a (VrfFgdParser)[../Misc/VrfFgdParser] tool which parses FGD files to extract all possible key names and entity icons.

## Entity icons for the map viewer

(HammerEntities.cs)[../GUI/Utils/HammerEntities.cs] contains a mapping of entity names and their Hammer icons (sprite or model).
Unfortunately different games may have different paths for these icons, so they may not always be available.

Use (VrfFgdParser)[../Misc/VrfFgdParser] to extract them.

## Material texture mappings for correct extraction

(MaterialExtract.cs)[../ValveResourceFormat/IO/MaterialExtract.cs] contains a list of shader names and which texture files are used,
along with which channels they use.

This is required for correct material extraction. In future it should be possible to automatically extract this information from shader files.
