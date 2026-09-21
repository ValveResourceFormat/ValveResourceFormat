# VmdlExtractor — Changelog

## v5.3.2 — Material-group reconstruction + activity-array fidelity

### Bug fixes

Closes three independent fidelity gaps where re-compiled `.vmdl_c` differed
from Valve's original. All three were systematic; each one affected several
production heroes. Verified across a 17-model regression sweep
(`Diag/RegressionSweep`).

#### 1. `MaterialGroupList` not reconstructed → skin/persona variants lost

VRF's `ModelExtract` does **not** regenerate the `MaterialGroupList` source
node from the compiled `m_materialGroups` array, so every skin variant
(Fall20 calavera, item-store skins, persona overlays) silently disappeared
from re-compiled hero models.

| Hero               | Variant lost                                  |
| ------------------ | --------------------------------------------- |
| `axe`              | `axe_fall20_body` (Fall20 skin)               |
| `lina`             | `lina_base_flamehair_color` + calavera        |
| `meepo`            | `meepo_fall20`                                |
| `pudge`            | `pudge2_fall20_body`                          |
| `pudge_cute`       | calavera head/body remap                      |
| `sniper`           | `sniper_calavera_body_color`                  |
| `sven`             | `sven_body` (item-store skin)                 |
| `treant_protector` | `treantprotector_fall20_body`                 |

**Fix:** new `InjectMaterialGroupList` post-extract pass reads
`m_materialGroups` straight from the compiled `.vmdl_c` `DATA` block and
emits a Valve-canonical source node:

```kv3
{
    _class = "MaterialGroupList"
    children = [
        { _class = "DefaultMaterialGroup" name = "default" remaps = [ ] },
        {
            _class = "MaterialGroup"
            name = "1"
            remaps = [
                { _class = "BaseMaterialRemap"
                  from = "materials/.../base.vmat"
                  to   = "materials/.../skin.vmat" },
                ...
            ]
        }
    ]
}
```

Class names verified against `modeldoc_utils.dll` string table. Each
variant `[N]` pairs `m_materialGroups[0].m_materials[i] → m_materialGroups[N].m_materials[i]`
positionally; identity remaps are skipped.

#### 2. Activity-modifier de-duplication corrupted weighted variants

The previous `EnrichVmdlActivityModifiers` pass passed every modifier
through a `HashSet`, collapsing legitimate duplicate entries that Valve
intentionally writes for biased weighting (Crystal Maiden's
`dplus_loadout_spawn` lists `loadout` twice on purpose, which doubles its
selection weight at runtime).

**Fix:** dedup removed; each modifier from the original `m_activityArray`
becomes its own `ActivityModifier` child node so `ModelDoc` reproduces the
exact entry count.

#### 3. Empty-name `m_activityArray` slots dropped

`if (string.IsNullOrEmpty(actName)) continue;` discarded literal
`m_name = ""` slots that Valve writes for several sequences (e.g. CM
`ward_stun` has three slots: `ACT_DOTA_DISABLED`, `wardstaff`, `""`).
The empty slot is functional — it allows the engine to roll a non-modified
variant of the activity.

**Fix:** keep empty-name entries; only `ACT_*` slots are still skipped
(those come from the primary `activity_name` field, not from modifiers).

### Sweep metrics (15-hero sample)

| Metric                            | v5.3.1 | v5.3.2 |  Δ      |
| --------------------------------- | -----: | -----: | ------: |
| Per-seq `m_activityArray` mismatches | 65     | 38     | **−42%** |
| Models with `material count` diff  | 8      | 0      | **−100%** |
| Pudge issue count                  | 23     | 8      | −65%    |
| Crystal Maiden issue count         | 14     | 4      | −71%    |
| Crystal Maiden Persona issue count | 22     | 18     | −18%    |

The 38 remaining mismatches are all `multi: True → False` (multipose flag
preservation), deferred — see *Known limitations* below.

### Build-system fix (side issue surfaced during regression work)

`VmdlExtractor.csproj` `<Compile Remove>` patterns missed `test_out_*/`
scratch directories. The .NET 10 SDK glob then picked up
`test_out_*/listheroes/obj/AssemblyInfo.cs` from leftover sub-projects,
producing `CS0017` (multiple entry points) and `CS0579` (duplicate
attribute) errors after parallel `Diag/*` builds. Globs corrected to
`test_out_*\**\*.cs` and `Diag\**\*.cs`.

### Known limitations (carried into v5.3.2)

- **`m_bMulti` flag not preserved on multipose sequences** (38 cases
  across 13 heroes). VRF emits multipose blends as plain `AnimFile` +
  duplicated `lookFrame` DMX; reconstructing `AnimBlendLayerPoseParam`
  with correct pose-parameter names requires AnimGraph (`.agrp_c`) parsing
  and is deferred. The strip introduced in v5.3.1 still neutralises the
  associated body-flip bug at runtime.
- `MRPH` / `MIDX` / `MVTX` block-count diffs and `anim`/`seq` count diffs
  are pre-existing VRF mesh/animation extraction limits, unrelated to this
  release.

### Files touched

- `Program.cs` — `InjectMaterialGroupList` (new), activity-modifier dedup
  removed, empty-name guard relaxed, `MergeSummary.MaterialGroupListInjected`
  added, version banner bumped.
- `VmdlExtractor.csproj` — SDK exclude globs corrected.
- `Diag/AseqCompare/Program.cs` — new `@data` filter mode for
  `m_materialGroups` dumps (used during root-cause analysis).

### Verified models (no regression on v5.3.1 baseline)

All 17 sweep targets compile cleanly; `material count differs : 0 models`.

## v5.3.1 — Multipose AnimAddLayer strip (in-match flipped-body fix)

### Bug fix

Fixes systematic in-match body/limb flipping during run+turn for heroes that
use Source 2 multipose turn sequences (notably **Shadow Fiend Arcana**, but the
same VRF defect also affects Pudge, Crystal Maiden, Lina, base Shadow Fiend,
and other 7.28c+ heroes whose ASEQ contains a `m_bMulti=1` `turns` /
`turns_arcana` sequence).

Symptom (before the fix):

- T-pose looks correct in ModelDoc preview ✓
- Run animation in **profile preview** plays cleanly ✓
- Run animation **in match** (where AnimGraph2 fires turn overlays) shows
  torso, shoulders, arms, and head facing in different directions ✗

### Root cause

ValveResourceFormat (VRF) cannot reconstruct Source 2 multipose blend graphs
from compiled `.vmdl_c`. For each ASEQ entry flagged `m_bMulti=1` (e.g.
`turns_arcana` for SFA, `turns` for old heroes), VRF emits a plain `AnimFile`
node pointing at a `.dmx` whose contents are a verbatim copy of the first
`@…_lookFrame_0.dmx` — a single non-zero turn pose, **not** a true multipose
blend.

Run-style sequences in the same `.vmdl` keep their original
`AnimAddLayer { anim_name = "<multi>" }` references. When ModelDoc compiles
the file, the engine treats the additive layer as a one-frame full-pose
overlay and adds that "first lookFrame" rotation onto every run frame in
match → systematic body distortion.

The bug stays invisible in the loadout/profile preview because that view
plays activities raw, without AnimGraph2 overlays.

### Fix

`InjectActivityModifierFields` now reads `m_bMulti` alongside `m_bLegacyDelta`
from each ASEQ sequence's `m_flags`. After the existing inject pass,
`StripMultiposeAddLayers` removes every
`{ _class = "AnimAddLayer" anim_name = "<multi>" }` (and the
`AnimAddPoseLayer` / `AnimAddWorldSpaceLayer` variants) whose target is in
the multipose set. Run plays cleanly without the broken overlay.

Trade-off: characters lose the small body-lean compensation while turning at
run speed. The lean is sourced from the broken multipose anyway, so removing
it is strictly better than the visible body flip.

### CLI

- **`--keep-multipose-layers`** *(new, opt-in)* — disable the strip and
  preserve `AnimAddLayer` references to multipose sequences. Use only if a
  specific model is verified to need the broken overlay (none observed in
  Dota 2 7.28c+ heroes).

### Reverted experiments (so the v5.3.1 source is clean)

- `StripSkeletonBlock` / `--strip-skeleton` / `--keep-skeleton` removed.
  Stripping the explicit `Skeleton {}` block from the `.vmdl` source did
  not change in-match behaviour and was inconclusive.
- The `delta=true` patch is back to its v5.3.0 scope: only ASEQ-flagged
  names (`turns_arcana`). The brief experiment that propagated `delta=true`
  to `@`-prefixed helpers (`@turns_arcana`, `@turns_arcana_lookFrame_*`) was
  reverted because Valve's ASEQ has `m_bLegacyDelta=0` on those entries.

### Verified models (no regression on 7.28c-era heroes)

| Model                              | `.vmdl` size | Multipose strips | Notes                       |
| ---------------------------------- | -----------: | ---------------: | --------------------------- |
| `shadow_fiend_arcana.vmdl_c` (new) |    147 741 B |     10 (`turns_arcana`) | Original repro case  |
| `shadow_fiend.vmdl_c`              |    139 808 B |     10 (`turns`)        | 7.28c base SF       |
| `pudge.vmdl_c`                     |    454 602 B |     19 (`turns`)        | 7.28c-style hero    |
| `crystal_maiden.vmdl_c`            |    260 071 B |      3 (`turns`)        | 7.28c-style hero    |
| `lina.vmdl_c`                      |    105 969 B |      2 (`turns`)        | 7.28c-style hero    |
| `sniper.vmdl_c`                    |    206 289 B |      0                  | No multipose; no-op |

The strip is deterministic and triggered only when `m_bMulti=1` sequences
exist in the source `.vmdl_c`'s ASEQ block. Models that do not declare any
multipose sequence are byte-identical to v5.3.0 output.

### Build

Self-contained Windows x64 single-file executable, .NET 10:

```
publish\VmdlExtractor-v5.3.1-win-x64.zip   ≈ 37.9 MB
└── VmdlExtractor.exe                       (single-file, AOT runtime)
└── blake3_dotnet.dll
└── libSkiaSharp.dll
└── spirv-cross.dll
└── TinyEXRNative.dll
```
