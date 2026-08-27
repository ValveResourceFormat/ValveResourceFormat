# Format Support and Export Fidelity

Source 2 Viewer (and the ValveResourceFormat library) can view, dump, decompile, and export
compiled Source 2 resources. This page describes what you get out of each format, and just
as importantly, what you do not: which data is lost, and why.

If a limitation listed here has since been fixed, please [open an issue](./reporting-issues.md) or a pull request.

## How to Read This Page

VRF produces four kinds of output: **viewing** in the GUI, **text dumps** (nearly every
resource gets at least this, since most compiled data is KeyValues3 or NTRO-encoded and
those dump generically even for types with no dedicated code), **decompiling to source**
assets that Valve's tools can load and recompile (`.vmdl`, `.vmap`, `.vmat`, ...), and
**exporting to interchange** formats (glTF, PNG, EXR, WAV, ...).

When data does not survive, the reason falls into one of three categories:

| Category                  | Meaning                                                                                                                                                        |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Not in compiled files** | Valve's compiler discarded it; no tool can recover it, at best VRF reconstructs an approximation heuristically.                                                |
| **Not implemented**       | The data is in the file, but VRF does not parse or re-emit it yet; [contributions](https://github.com/ValveResourceFormat/ValveResourceFormat/issues) welcome. |
| **Format limitation**     | VRF parses it, but the output format cannot represent it.                                                                                                      |

Decompiled `.vmdl` and `.vmap` files are expected to load and recompile in ModelDoc and
Hammer; the sections below list exactly what the recompiled asset will be missing. There
is also basic support for writing VPKs and re-serializing resources back to their binary
format. Parsing network/demo formats such as `.dem` is out of scope
([#1122](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/1122)).

## Support Matrix

**View** is what the GUI shows beyond raw bytes. **Dump** is text output: "yes" means a
dedicated dump, "generic" means only the generic KV3/NTRO tree dump described above.
**Decompiles / exports to** is what file-level extraction ("Decompile & Export") produces;
a "-" means no dedicated decompile exists, extraction still writes the text dump described
in the Dump column.

### Resource Formats

| Ext      | Name                       | View                          | Dump    | Decompiles / exports to                                                                                      |
| -------- | -------------------------- | ----------------------------- | ------- | ------------------------------------------------------------------------------------------------------------ |
| vagrp    | Animation Group            | text                          | generic | - (legacy, animation data now lives inside vmdl)                                                             |
| valst    | Action List                | text                          | generic | -                                                                                                            |
| vanim    | Animation                  | text                          | generic | - (legacy, animation data now lives inside vmdl)                                                             |
| vanmgrph | Animation Graph            | graph                         | yes     | editable graph document                                                                                      |
| vcd      | Choreo Scene               | text                          | generic | KV3 text (no dedicated parsing; vcdlist scenes are separate)                                                 |
| vcdlist  | Choreo Scene File Data     | text                          | yes     | per-scene `.vcd` as KV3 text (see [Choreo](#choreo-captions-and-ui))                                         |
| vcompmat | Composite Material         | text                          | generic | KV3 text (no compositing preview)                                                                            |
| vcss     | Panorama Style             | text                          | yes     | `.css` (prettified minified text)                                                                            |
| vdata    | Data                       | text (3D for CS2 bomb damage) | generic | KV3 text                                                                                                     |
| vdpn     | Dota Patch Notes           | text                          | generic | KV3 text                                                                                                     |
| vdvn     | Dota Visual Novels         | text                          | generic | KV3 text                                                                                                     |
| vents    | Entity Lump                | graph, text                   | yes     | entity dump; glTF/GLB; included in map exports                                                               |
| vjs      | Panorama Script            | text                          | yes     | `.js` (byte-exact; see [Panorama](#choreo-captions-and-ui))                                                  |
| vmap     | Map                        | 3D                            | yes     | `.vmap` (Hammer); glTF/GLB                                                                                   |
| vmat     | Material                   | 3D                            | yes     | `.vmat` + unpacked texture inputs                                                                            |
| vmdl     | Model                      | 3D                            | yes     | `.vmdl` + DMX; glTF/GLB                                                                                      |
| vmesh    | Mesh                       | 3D                            | yes     | glTF/GLB; also handled via vmdl                                                                              |
| vmix     | VMix (DSP graph)           | text                          | generic | KV3 text (graph is not interpreted)                                                                          |
| vmks     | Sheet                      | text                          | generic | -                                                                                                            |
| vmorf    | Morph Set                  | via model                     | yes     | used by vmdl/glTF export                                                                                     |
| vnmclip  | NmClip (Animgraph 2 clip)  | 3D playback                   | yes     | clip document + DMX; glTF/GLB                                                                                |
| vnmgraph | NmGraph (Animgraph 2)      | graph                         | generic | editable graph document                                                                                      |
| vnmikrig | NmIKRig                    | text                          | generic | -                                                                                                            |
| vnmskel  | NmSkeleton                 | 3D                            | generic | skeleton document + DMX                                                                                      |
| vnmvar   | NmGraph Variation          | text                          | generic | - (variations are carried by the base graph's document)                                                      |
| vpcf     | Particle System            | 3D                            | yes     | `.vpcf` (exact round-trip for KV3-era files)                                                                 |
| vpdi     | Panorama Dynamic Images    | text                          | yes     | - (manifest of referenced vtex/vsvg images; holds no pixel data)                                             |
| vphys    | Physics Collision          | 3D                            | yes     | geometry into vmdl/vmap; glTF                                                                                |
| vpost    | Postprocessing Settings    | LUT image; applied in 3D      | yes     | `.vpost` + LUT `.raw`                                                                                        |
| vpram    | Processing Graph Instance  | text                          | generic | -                                                                                                            |
| vpsf     | Particle Snapshot (legacy) | text                          | generic | -                                                                                                            |
| vpulse   | Pulse Graph                | graph                         | generic | - (graph view only)                                                                                          |
| vrman    | Resource Manifest          | text                          | yes     | KV3 text                                                                                                     |
| vrmap    | Resource Remap Table       | text                          | generic | -                                                                                                            |
| vrr      | Response Rules             | text                          | yes     | original rules script (byte-exact)                                                                           |
| vseq     | Sequence Group             | text                          | generic | -                                                                                                            |
| vsmart   | Smart Prop                 | 3D (partial)                  | yes     | KV3 text                                                                                                     |
| vsnap    | Particle Snapshot          | 3D                            | yes     | `.vsnap`                                                                                                     |
| vsnd     | Sound                      | audio                         | yes     | `.wav` / `.mp3` + phonemes `.txt` + `.vsnd` KV3 for newer sounds                                             |
| vsndevts | Sound Event Script         | text                          | generic | KV3 text (lossless)                                                                                          |
| vsndstck | Sound Stack Script         | text                          | yes     | script text                                                                                                  |
| vsurf    | Surface Properties         | text                          | generic | -                                                                                                            |
| vsvg     | Panorama Vector Graphic    | image                         | yes     | `.svg` (byte-exact)                                                                                          |
| vtex     | Texture                    | image                         | yes     | `.png` / `.exr` (stored JPEG/PNG/WebP passes through as-is); `.mks` for sprite sheets; `.vtex` config for 2D |
| vts      | Panorama TypeScript        | text                          | yes     | `.js` (compiled JS, byte-exact)                                                                              |
| vvis     | World Visibility           | 3D                            | yes     | -                                                                                                            |
| vwnod    | World Node                 | 3D                            | yes     | handled via vmap/glTF export                                                                                 |
| vwrld    | World                      | 3D                            | yes     | `.vmap` (Hammer); glTF/GLB                                                                                   |
| vxml     | Panorama Layout            | text                          | yes     | `.xml` (structural decompile)                                                                                |
| econitem | Economy Item               | text                          | generic | KV3 text                                                                                                     |
| herolist | Dota Hero List             | text                          | yes     | plaintext KV1 (verbatim)                                                                                     |
| item     | Artifact Item              | text                          | yes     | plaintext KV1 (verbatim)                                                                                     |
| shader   | s&box Shader               | text                          | yes     | `.shader` source                                                                                             |
| vdacdefs | DAC Game Defs Data         | text                          | generic | KV3 text (no ResourceType; unknown-file fallback)                                                            |

### Other Formats

| Format                             | Support                                                                                                                                                                                                       |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| vpk (Valve Pak)                    | Browse, extract, create, and edit, handled by [ValvePak](https://github.com/ValveResourceFormat/ValvePak)                                                                                                     |
| vcs (compiled shader)              | Combo/parameter browser; `.vfx` reconstruction; per-stage source for GL and Vulkan platforms (see [Shaders](#shaders-vcs))                                                                                    |
| SPIR-V `.spv`                      | Decompiled via SPIRV-Cross to HLSL (or GLSL), same as Vulkan vcs stages                                                                                                                                       |
| dat (VCCD closed captions)         | Grid view; export to `.txt` KV1 (caption keys are lost, see [below](#choreo-captions-and-ui))                                                                                                                 |
| bin (tools asset info)             | Text dump of the per-asset dependency and search data (an embedded KV3 segment in newer files is parsed but not dumped)                                                                                       |
| vfont                              | Decrypted to the original TTF/OTF (CLI only)                                                                                                                                                                  |
| uifont (CS:GO/CS2 UI font package) | Embedded fonts decrypted and extracted exactly (CLI only)                                                                                                                                                     |
| vfe (flex scene file)              | Text view; export to a `.txt` dump                                                                                                                                                                            |
| nav (navigation mesh)              | 3D view; export to `.glb`                                                                                                                                                                                     |
| gnv (Dota grid navigation)         | Text info dump                                                                                                                                                                                                |
| bvcd (binary choreo)               | Parsed as part of vcdlist                                                                                                                                                                                     |
| DMX / KeyValues2                   | Normalized text dump                                                                                                                                                                                          |
| VBKV / binary KeyValues1           | Text dump                                                                                                                                                                                                     |
| Binary KeyValues3 (all versions)   | Text dump; backbone of the generic fallback above; writing supported                                                                                                                                          |
| Binary resource writing            | Basic re-serialization of resources back to the compiled binary format (`Resource.Serialize`), for a subset of block types (binary KV3, plaintext, Panorama, external reference lists, and raw binary blocks) |

## Models (vmdl)

See the [exporting models guide](./exporting-models.md) for the workflow.

Decompiling produces a `.vmdl` plus DMX files for meshes, physics shapes, and animations,
loadable in ModelDoc. Reconstructed: render meshes with all vertex streams, skeleton,
attachments, bodygroups, LOD groups, hitbox sets, material groups (skins), static collision
shapes, bone constraints, breakable pieces, embedded sequences with events/layers/root
motion, Animgraph 2 clips and references, and a wide range of game data blocks (prop_data,
particle attachments, and many more) passed through verbatim.

What a recompiled model will be missing:

| What                                      | Why                          | Details                                                                                                                                                                                                                                                                                                                                                               |
| ----------------------------------------- | ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| IK chains                                 | Not implemented              | `ikdata`/`m_IKChains` is parsed but never written back as vmdl IK chain nodes. [#1267](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/1267)                                                                                                                                                                                                        |
| Cloth simulation                          | Partly not in compiled files | The compiled `FeModel` cloth data is not parsed, and some of the authored cloth attributes do not survive compilation in recoverable form, so recompiled models will not simulate. [#653](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/653)                                                                                                      |
| Ragdoll joints                            | Not implemented              | Physics constraints (`m_constraints2`) are not parsed anywhere; only the collision shapes survive.                                                                                                                                                                                                                                                                    |
| Face flexes (morphs)                      | Not implemented              | Neither the morph vertex deltas nor the flex controllers and flex rules that drive them (the expressions visible in ModelDoc, stereo left/right splits included) are written into the reconstructed model. Only flex controller animation channels in sequences are exported, with nothing to drive. VRF parses all of this data for rendering, so it is recoverable. |
| Animations from external animation groups | Not implemented              | Only embedded sequences and Animgraph 2 clips get DMX files; sequences in referenced `vagrp` files are skipped. Animations from referenced include-models are not written either, but their `AnimIncludeModel` references are kept, so they come back if those models are decompiled too.                                                                             |
| Additional external physics files         | Not implemented              | Only the first `m_refPhysicsData` reference is extracted; shapes from further files are dropped.                                                                                                                                                                                                                                                                      |
| Blend sequences (blend spaces)            | Not implemented              | Multi-reference blend sequences collapse to their first referenced animation.                                                                                                                                                                                                                                                                                         |
| Vertical root motion                      | Intentional                  | The Z component of root motion is zeroed on export, matching how the engine applies movement to the visible body.                                                                                                                                                                                                                                                     |
| Extra skin materials                      | Not implemented              | If a material group lists more materials than the default group, the extras are silently dropped.                                                                                                                                                                                                                                                                     |

### glTF Export

Models and maps export to glTF 2.0 (`.glb`/`.gltf`) with meshes, up to 8 bone weights per
vertex, morph targets, skeletal/additive/morph animations, PBR-approximated materials with
ORM repacking, and collision shapes as a separate `<name>_physics` file. For a single
model the physics file is only written when the collision data is embedded in the model; a
model that references an external `.vphys_c` exports without one (map exports handle both).

| What                       | Why               | Details                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| -------------------------- | ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Lower LODs                 | Intentional       | Only the highest-detail LOD is exported. [#535](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/535)                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Source 2 material features | Format limitation | Materials are flattened to glTF PBR metallic-roughness. Layered blend materials export only one layer ([#1161](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/1161)), cloth sheen has no accurate glTF equivalent ([#576](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/576)), vertex-color-driven shaders like foliage do not translate ([#1180](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/1180)). Every exported material also carries the original shader name and parameters in a `vmat` extras block for custom tooling. |
| Flex controllers           | Format limitation | glTF has no flex rig; controller-driven morph animation is baked into per-frame morph weights instead.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Morph normal deltas        | Not implemented   | Only position-delta morph bundles are decoded; `NormalWrinkle` bundles are skipped, so morph targets carry position deltas only.                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Additive animations        | Format limitation | glTF has no additive concept; clips are composed over the bind pose, or written as delta tracks flagged via a VRF-specific extras convention that generic viewers will play back incorrectly.                                                                                                                                                                                                                                                                                                                                                                                                |
| Hitboxes                   | Format limitation | glTF has no collision volume concept; hitboxes are not exported.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Cloth simulation           | Not implemented   | There is no cloth solver; procedural cloth bones are rigidly pinned to their anchor bones.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Physical properties        | Format limitation | The physics export carries visualization geometry and materials, plus each shape's surface property and collision tags as node extras; the physical values themselves (mass, friction, joints) are not represented.                                                                                                                                                                                                                                                                                                                                                                          |

::: warning
Exports at or above 2 GB cannot be written as a single `.glb`; use `.gltf` instead, which
chunks buffers.
:::

## Maps (vmap)

See the [exporting maps guide](./exporting-maps.md) for the workflow.

Decompiling a compiled map (`vmap_c`/`vwrld_c` and its lumps) produces a `.vmap` loadable in
Hammer. Reconstructed: all entities across every entity lump (including `point_template`
children, deduplicated) with full entity I/O connections, world render geometry welded back
into editable per-material Hammer meshes (near-coplanar triangle pairs merged back into
quads) with real
per-face texture projection, static props with their placement properties, aggregate props
split back into individual entities, world layers, overlays, and per-surface-property
physics geometry for collision that has no matching render mesh.

Data that was already destroyed by the map compiler, and therefore cannot come back:

| What                          | Details                                                                                                                                                              |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Prefab instances              | The compiler flattens prefabs into the world; nothing marks their boundaries.                                                                                        |
| Groups and selection sets     | Only a numeric `hammeruniqueid` lineage survives; VRF rebuilds nesting from it, but the original names and folder structure are gone.                                |
| Overlay projection parameters | Compiled maps store only the final projected triangles. VRF reconstructs overlays by geometric back-projection, which is an approximation of the original placement. |
| Per-face lightmap scale       | Not stored; all faces come back with default luxel density.                                                                                                          |
| Mesh subdivision levels       | Only the final tessellated mesh is stored; subdivided surfaces come back flat (non-subdivided).                                                                      |
| Per-light "No Shadow" flag    | Not preserved in compiled CS2 maps.                                                                                                                                  |

On cubemap and light probe entities, VRF also intentionally renames the baked
`cubemaptexture`, `lightprobetexture` and `array_index` keys with a `vrf_stripped_` prefix:
the baked data is present in the compiled map, but referencing it can crash newer engine
branches, and it is regenerated on recompile anyway.

Not implemented yet:

| What                         | Details                                                                                                                                                                                                                       |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| External reference meshes    | Models welded into Hammer geometry (world meshes and aggregates) use only their embedded meshes, so render meshes living in a separate `.vmesh_c` are dropped from that geometry. Props that reference models are unaffected. |
| Clutter                      | Procedural scatter objects are ignored (also invisible in the viewer).                                                                                                                                                        |
| Physics spheres and capsules | Only physics hulls and meshes are turned into Hammer geometry.                                                                                                                                                                |
| 3D skybox bundling           | `skybox_reference` keeps its properties, but the referenced skybox map is not decompiled and bundled automatically (same for glTF export, [#967](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/967)).     |

For glTF map exports, additionally: only `light_environment` is exported as a light (point
and spot lights are not), entities carrying no model at all (logic, cameras, point
entities) are skipped (trigger volumes do carry a physics model in compiled maps, so their
collision lands in the companion physics file), baked lighting (lightmaps, probes) is not
exported, and a placed prop that names an animation via its entity properties exports only
that one, while props naming none export their full animation set.

## Materials (vmat)

`.vmat` reconstruction recovers the shader name, all int/float/vector parameters (including
`F_` feature choices), attributes, the `SubrectDefinition` tool attribute, and dynamic expressions decompiled from
bytecode back to readable expression text.

Compiled textures are unpacked back into the original input maps (color, normal, roughness,
masks, ...) using the channel processor metadata in the compiled shader when it is
available. Without the shader file, a built-in table covers the most common shaders; for
anything else the textures are extracted as raw RGBA dumps under guessed names. Unpacking is
skipped for cubemaps, texture arrays, volume textures, and HDR textures; those are written
out as complete decoded images (per face or slice) instead of channel maps.

## Shaders (vcs)

Compiled shader files parse fully across all known versions: programs, static/dynamic combos,
constraints and rules, parameters with UI metadata, texture channel processors, and render
state, which is enough to reconstruct a structural `.vfx` source. Readable per-stage code
depends on the platform:

| Platform                       | Result                                                                                                                                                              |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| PCGL / Mobile GL               | The compiled GLSL text is stored in the file and shown as-is.                                                                                                       |
| Vulkan (desktop, iOS, Android) | SPIR-V is decompiled via SPIRV-Cross to HLSL (or GLSL), with resource names recovered from reflection data. A faithful reconstruction, but not the original source. |
| PC D3D (DXBC/DXIL)             | Not implemented; only the shader hash is shown (useful for RenderDoc lookups); bytecode can be exported.                                                            |

The original artist-authored HLSL is not in the compiled files in any form: comments, exact
control flow, and names outside reflection metadata are permanently gone, for any tool.

## Textures (vtex)

See the [exporting textures guide](./exporting-textures.md) for the workflow.

All commonly shipped pixel formats decode.
Compile-time transforms (YCoCg, hemi-octahedral normals, DXT5nm, normal Z-reconstruction)
are reversed on export. LDR textures export as PNG and HDR as EXR, except textures that
store a whole JPEG/PNG/WebP file, which are copied out byte-exact instead. Sprite sheets
reconstruct a compilable `.mks` plus per-frame images; LDR cubemaps export one image per
face, HDR cubemaps combine into a single equirectangular map; arrays and volumes export
per slice.

| What                                       | Why                   | Details                                                                                                                                                                                                                                                                                                                                             |
| ------------------------------------------ | --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Original source images                     | Not in compiled files | Block compression (BC1-7, ETC2) is lossy at compile time; exports faithfully reflect the compiled data, not the artist's source.                                                                                                                                                                                                                    |
| Mip chain                                  | Format limitation     | File extraction always uses the largest mip only; the texture viewer can save the mip, face, or slice it is currently showing, and per-mip access exists in the library API. HL:Alyx's per-mip roughness packing therefore has no single-file preserving export path. [#936](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/936) |
| `.vtex` config for cubemaps/arrays/volumes | Not implemented       | Only flat 2D textures get a reconstructed `.vtex` compile config; other shapes extract images only. [#856](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/856)                                                                                                                                                                   |
| Clamp/LOD flags in reconstructed `.vtex`   | Not implemented       | `SUGGEST_CLAMP*` and no-LOD flags are parsed but not written into the regenerated config.                                                                                                                                                                                                                                                           |
| Transform detection without edit info      | Not implemented       | Which compile-time transform to reverse is detected from the resource's edit info block; files stripped of it export still-encoded pixels without warning.                                                                                                                                                                                          |

## Animation

Legacy embedded sequences decode through all common per-bone compression types (unknown
decoder types are skipped silently), and Animgraph 2 clips (`vnmclip`) decode fully:
compressed poses, 3D root motion (position plus yaw; the root track's pitch and roll are
dropped), float curves, events, and secondary skeleton tracks.
Both feed viewer playback, glTF export, and DMX reconstruction, including retargeting of
clips authored on a different skeleton.

Animation _graphs_ are decompiled to editable documents and visualized, for both Animgraph 1
and Animgraph 2, but are never executed: the viewer plays explicitly selected clips, not
what the graph's state machine would choose. Graph-driven behavior (IK solving, bone
constraint evaluation such as constraint-driven eyelid morphs
[#756](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/756)) does not
affect playback or exports. `vnmikrig` files have no dedicated support beyond the generic
dump.

Two details are unrecoverable from compiled clips: the original additive-base authoring
choice, and float curve tangents (curves are baked to per-frame samples; reconstruction
emits linear knots that reproduce the values exactly but not the authored spline). The reconstructed clip
document's `m_sourceFilename` is derived from the compiled resource's own path; the
authored content path survives in the resource edit info's input dependencies, but is not
read back yet ([#1024](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/1024)).

## Physics (vphys)

All four collision shape types (sphere, capsule, hull, mesh) parse, render, and export to
glTF as visualization geometry. Decompiled `.vmdl` files carry all four; `.vmap` decompiles
carry only hulls and meshes. Hitboxes fully round-trip into decompiled models.

Not parsed anywhere: ragdoll joints/constraints (`m_constraints2`) and the `FeModel`
cloth/softbody block; both are visible only in the raw text dump. Surface properties are
resolved by name only; their physical values (friction, density, sounds) are not consumed
or exported. Text-dumping the PHYS block of gigabyte-class maps can run out of memory; dump
the block to a file via the CLI instead
([#840](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/840)).

## Particles (vpcf)

Particle systems round-trip exactly: extraction re-emits the original stored KV3 tree, not
VRF's upgraded in-memory version, so decompiled `.vpcf` files match the compiled source
(pre-KV3 NTRO-era files are re-encoded as KV3 text instead). The viewer simulates a large
subset of particle functions and lists any unsupported functions per system in red instead
of failing.

Particle snapshots (`vsnap`) preview and extract. Bone name streams survive, but skinning
streams are written into the extracted `.vsnap` as empty streams: their values show in the
text dump but are not emitted.

## Audio (vsnd)

See the [exporting sounds guide](./exporting-sounds.md) for the workflow.

Extraction is a bit-exact remux of the stored stream, never a re-encode: WAV (PCM/ADPCM)
gets its RIFF header synthesized from stored data, MP3 bytes are copied raw. Newer sounds
also get their voice-container definition written alongside as a `.vsnd` KV3 file. Lip-sync
phoneme data extracts to a companion `.txt` that the compiler picks back up. Sound event
scripts dump losslessly as KV3 text; stack scripts dump each stack's stored text, though
duplicate stack names collapse to the last one. VMix DSP graphs are dumped but not
interpreted.

## Choreo, Captions, and UI

- **Choreo** (`vcdlist`): scenes, actors, events, and flex curves are fully parsed. Scenes
  are emitted as KV3 `.vcd`.
- **Closed captions**: caption text is recovered in full, but the compiled file stores only
  a CRC32 hash of each caption key, so exports are keyed by hash. The original key strings
  are not in the file.
- **Panorama**: layouts (`vxml`) get a real structural decompile back to XML, and vector
  graphics (`vsvg`) are stored as plain SVG and extract byte-exact. Styles (`vcss`)
  extract as text: minified ones are pretty-printed heuristically (their embedded source
  map is not used yet), non-minified ones come out verbatim. Scripts extract byte-exact: a
  `vjs` usually stores the authored JavaScript as-is, while a `vts` stores the TypeScript
  compiler's JavaScript output, so the original TypeScript source is gone. Dynamic image
  manifests (`vpdi`) dump their listing; the pixels live in the separate `vtex`/`vsvg`
  resources they reference.
- **Pulse graphs** (`vpulse`): reconstructed from bytecode into a readable node graph in the
  GUI, including control flow; there is no textual source export.

## Per-Game Notes

- **Counter-Strike 2**: weapon skin composite materials (`vcompmat`) only dump as KV3, with
  no compositing preview. Smart props render partially (nested smart prop references are not
  expanded, [#590](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/590)),
  and very high-poly smart-prop-deformed meshes can decompile with missing faces
  ([#874](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/874)).
- **Half-Life: Alyx**: its custom per-mip roughness normal map packing has no preserving
  export path ([#936](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/936)).
  Model-rendering particle operators used by some HLA effects are not implemented in the
  viewer ([#716](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/716)).
- **Dota 2**: the uncompiled VGrass format found in map VPKs is entirely unparsed
  ([#83](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/83)). Hero list,
  patch notes, visual novel, and econ item resources are text dumps only.
- **Deadlock**: viewer shading is a work in progress; environment blend materials in
  particular do not render their texture layers correctly yet
  ([#1092](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/1092)).
- **s&box**: only the Vulkan shader container is supported, with `.shader` source
  reconstruction; the older DXBC container is not read. Managed resources extract as
  plaintext.
- **Source 1 leftovers**: `vfont` (VFONT1), `uifont` packages, VBKV, closed captions, and
  flex scene `.vfe` files are supported even where they appear in Source 1 games. Source 1
  `.nav` meshes are not supported
  ([#989](https://github.com/ValveResourceFormat/ValveResourceFormat/issues/989)).

## Appendix: Recognized Magics

The magic numbers (file signatures) recognized by VRF. **Magic** is the value as a
little-endian 32-bit integer, the way it appears in code; **File bytes** is the same
signature as the byte sequence you see at the start of the file in a hex editor.

| Magic        | File bytes          | ASCII     | Description                                                     |
| ------------ | ------------------- | --------- | --------------------------------------------------------------- |
| `0x03564B56` | `56 4B 56 03`       | `VKV\x03` | first binary KeyValues 3 encoding with custom block compression |
| `0x4B563301` | `01 33 56 4B`       | `KV3\x01` | binary KeyValues 3 (version 1)                                  |
| `0x4B563302` | `02 33 56 4B`       | `KV3\x02` | binary KeyValues 3 (version 2)                                  |
| `0x4B563303` | `03 33 56 4B`       | `KV3\x03` | binary KeyValues 3 (version 3)                                  |
| `0x4B563304` | `04 33 56 4B`       | `KV3\x04` | binary KeyValues 3 (version 4)                                  |
| `0x4B563305` | `05 33 56 4B`       | `KV3\x05` | binary KeyValues 3 (version 5)                                  |
| `0x564B4256` | `56 42 4B 56`       | `VBKV`    | binary KeyValues 1                                              |
| `0x55AA1234` | `34 12 AA 55`       | -         | VPK - Valve package (handled by ValvePak)                       |
| `0x44434356` | `56 43 43 44`       | `VCCD`    | closed captions                                                 |
| `0xC4CCACE8` | `E8 AC CC C4`       | -         | tools asset info                                                |
| `0xC4CCACE9` | `E9 AC CC C4`       | -         | tools asset info (newer version)                                |
| `0x32736376` | `76 63 73 32`       | `vcs2`    | compiled shader                                                 |
| `0x07230203` | `03 02 23 07`       | -         | SPIR-V bytecode                                                 |
| `0x414D5A4C` | `4C 5A 4D 41`       | `LZMA`    | LZMA compression marker (compiled shaders, choreo data)         |
| `0x64637662` | `62 76 63 64`       | `bvcd`    | binary choreo scene                                             |
| `0xFEEDFACE` | `CE FA ED FE`       | -         | navigation mesh                                                 |
| `0xFADEBEAD` | `AD BE DE FA`       | -         | grid navigation                                                 |
| `0x31415926` | -                   | -         | murmurhash2 seed used by StringToken (not a file signature)     |
| -            | `56 46 4F 4E 54 31` | `VFONT1`  | "encrypted" font file (signature is at the end of the file)     |
| `0x00564645` | `45 46 56 00`       | `EFV\0`   | flex scene file                                                 |

Standard formats (PNG, JPEG, GIF, WAV/RIFF, WebP, MP3, SVG) are also detected by their
usual signatures or extensions.
