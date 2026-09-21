# VmdlExtractor

> **Source 2 `.vmdl_c` → fully-compilable `.vmdl` decompiler with byte-perfect ModelDoc round-trip.**
>
> Decompiler Source 2 `.vmdl_c` в готовый к компиляции `.vmdl` c побайтово-точным round-trip через ModelDoc.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)](#requirements)
[![Round-trip](https://img.shields.io/badge/ModelDoc_round--trip-0_diff_lines-brightgreen)](#round-trip-fidelity)

---

## English

### What it is

`VmdlExtractor` is a command-line tool that extracts Source 2 models from Dota 2 (Valve) VPK packages or compiled `.vmdl_c` files and emits a **fully-reconstructed, ModelDoc-compatible `.vmdl` source** together with all its dependencies (`.vmesh_c`, `.vmorf_c`, `.vphys_c`, `.vanim_c`, `.vseq_c`, DMX meshes, etc.).

It is built on top of [ValveResourceFormat (VRF)](https://github.com/ValveResourceFormat/ValveResourceFormat) but applies a long post-processing pipeline so the result is the file ModelDoc would produce if you opened the model inside the Source 2 Workshop Tools, clicked *Save*, and clicked *Build*.

The extracted output opens in ModelDoc without errors, compiles cleanly through `resourcecompiler.exe`, and — most importantly — produces a **0-line diff** against a file that has been round-tripped through ModelDoc's Save.

### Why it exists

Vanilla VRF output is a *decompilation*, not a *source that recompiles*. Opening a raw VRF `.vmdl` in ModelDoc surfaces dozens of schema-migration warnings, missing defaults, dangling resource references, broken AutoLayer links, and subtly malformed animation metadata. ModelDoc rewrites all of these on Save, producing a huge cosmetic diff that obscures any real authored changes.

`VmdlExtractor` closes that gap end-to-end so the extracted source is already in ModelDoc's canonical form.

### Feature matrix

| Area | Behaviour |
|---|---|
| **Skeleton** | Merged from `.vmdl_c` primary skeleton + `.vmesh_c` `m_skeleton` fallback |
| **Hitboxes** | `HitboxSetList` reconstructed; `Hitbox` duplicates auto-suffixed (`Root0_JNT`, `Root0_JNT1`, ...) like ModelDoc does |
| **Attachments** | `AttachmentList` with full translation/rotation/offset fields |
| **Flex controllers** | Merged from the model and external `.vmorf_c` files |
| **Physics** | `PhysicsShapeList` + additional `.vphys_c` transplant (ragdoll, cloth, collision hulls) |
| **Animations** | Full `AnimFile` nodes with `framerate`, `start_frame`, `end_frame`, `activity_name`, `activity_weight`, `weight_list`, `extract_motion`, `additional_anim_files`, `reverse` injected from `.vseq_c` metadata |
| **Anim events** | `AE_CL_CREATE_PARTICLE_EFFECT_CFG` receives `aggregate`/`tags` defaults; `AE_CL_PLAYSOUND` receives `tags`; `AE_CL_SUPPRESS_EVENTS_WITH_TAG` has its mistyped `tag = resource:"X"` rewritten to `tag = "X"` |
| **DMX meshes** | `jointIndex = -1` → `0` with `weight = 0` patch (eliminates root-bone stretching) |
| **Include models** | `m_refAnimIncludeModels` decompiled recursively so the include tree is complete |
| **Motion patch** | `extract_motion` neutralised where VRF's output would produce a 10× speed bug |
| **Activity modifiers** | `ActivityModifier` child nodes injected so compile preserves `items_game.txt` modifier bindings (optional `--prune-modifiers` for custom addons) |
| **Schema header** | Bumped from `modeldoc28` / `modeldoc32` to `modeldoc41` (current) |
| **Numeric format** | Float arrays (`angles`, `origin`, `hitbox_*`) round-tripped to ModelDoc's shortest-roundtrip decimal form |
| **Optional auto-build** | `--build` runs `resourcecompiler.exe`, transplants the original MRPH + PHYS binary blocks from the source `.vmdl_c`, and produces a drop-in replacement without ever opening the Workshop Tools |

### Round-trip fidelity

Verified on **Shadow Fiend Arcana** from Dota 2 `7.28c`:

```
Source (ours):                          144,221 bytes
After ModelDoc "Save":                  147,305 bytes   (+3 KB of default blocks ModelDoc appends)
Round-trip diff (Compare-Object):       0 lines
Recompile via resourcecompiler.exe:     OK: 1 compiled, 0 failed, 0 skipped
```

The diff went from **1186 lines → 0 lines** over a focused fidelity pass — see `Program.cs` `InjectModelDocDefaults()` for the full transformation chain.

### Requirements

- **OS:** Windows 10/11 (x64)
- **Runtime:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Source game:** Dota 2 install with VPK files
- **Target game:** Dota 2 + Workshop Tools (for `resourcecompiler.exe`), optional
- **NuGet:** [`ValveResourceFormat`](https://www.nuget.org/packages/ValveResourceFormat) 19.1.x, [`ValvePak`](https://www.nuget.org/packages/ValvePak) 4.0.x — restored automatically on first build

### Build

```powershell
git clone https://github.com/<your-user>/VmdlExtractor.git
cd VmdlExtractor
dotnet build -c Release
```

The executable is written to `bin/Release/net10.0/VmdlExtractor.exe`.

### Usage

#### Extract one hero from a VPK

```powershell
.\bin\Release\net10.0\VmdlExtractor.exe `
    -i "D:\Games\Dota 2 7.28c\game\dota\pak01_dir.vpk" `
    -f "models/heroes/shadow_fiend/shadow_fiend_arcana.vmdl_c" `
    -o ".\out"
```

#### Extract everything under a directory prefix

```powershell
.\bin\Release\net10.0\VmdlExtractor.exe `
    -i "D:\Games\Dota 2 7.28c\game\dota\pak01_dir.vpk" `
    -p "models/heroes/shadow_fiend" `
    -o ".\out"
```

#### Full pipeline: extract → copy to addon → compile → transplant PHYS+MRPH

```powershell
.\bin\Release\net10.0\VmdlExtractor.exe `
    -i "D:\Games\Dota 2 7.28c\game\dota\pak01_dir.vpk" `
    -p "models/heroes/shadow_fiend" `
    -o ".\out" `
    --copy-to "E:\Steam\steamapps\common\dota 2 beta\content\dota_addons\mymod\models\heroes\shadow_fiend" `
    --copy-from "models/heroes/shadow_fiend/shadow_fiend_arcana" `
    --build -v
```

This single command produces a ready-to-use model in your custom game addon. **Do not open ModelDoc afterwards** — its *Save & Build* button overwrites the MRPH/PHYS transplant with a plain re-compile.

#### CLI reference

Run `VmdlExtractor.exe --help` for the full list. Short version:

| Flag | Purpose |
|---|---|
| `-i`, `--input` | VPK file, game-extracted folder, or single `.vmdl_c` |
| `-o`, `--output` | Output root directory |
| `-f`, `--file` | Extract exactly ONE file by its in-VPK path (wins over `-p`) |
| `-p`, `--vpk-prefix` | Limit VPK scan to a directory prefix |
| `-g`, `--game-root` | (folder/file mode) base for resolving external references |
| `--copy-to` | Copy extracted files into a `content/<addon>/…` path |
| `--build` | Auto-run `resourcecompiler.exe` + motion patch + PHYS/MRPH transplant |
| `--no-sanitize` | Skip the post-process sanitizer (debugging only) |
| `--prune-modifiers` | Hard-disable sequences whose modifiers are not in `activity_modifier_weights.txt` |
| `-v`, `--verbose` | Per-file diagnostics + merge summary |

### How it works

1. **VRF decompile** — `ValveResourceFormat.IO.ContentFile` turns the `.vmdl_c` into a first-pass `.vmdl` source plus sub-files.
2. **Skeleton / Hitbox / Attachment / Flex / Physics merge** — these classes live in different places inside the compiled resource; `VmdlExtractor` normalises them into a single ModelDoc-layout tree.
3. **Resource-ref sanitize** — strips empty references, rewrites the broken `tag = resource:"X"` form in `AE_CL_SUPPRESS_EVENTS_WITH_TAG`, removes dangling `AutoLayer` targets.
4. **ModelDoc default injection** (`InjectModelDocDefaults` in `Program.cs`):
    - `InjectAnimFileSchemaDefaults`, `InjectAnimEventEndFrame`
    - `InjectParticleEventKeysDefaults` (particle + PLAYSOUND)
    - `InjectExtractMotionDefaults`, `InjectRenderMeshFileImportFilter`
    - `InjectWeightListDefaults`, `InjectAnimationListDefaults`, `InjectHitboxDefaults`
    - `NormalizeFloatPrecision` (float arrays → ModelDoc's shortest-roundtrip form)
    - `StripEmptyBoneMarkupChildren`
    - `DisambiguateDuplicateHitboxNames` (auto-suffixes `name1`, `name2`, …)
    - `UpgradeSchemaHeader` (→ modeldoc41)
5. **Include-model recursion** — every `m_refAnimIncludeModels` entry is resolved and decompiled so the include tree is complete.
6. **Optional auto-build** (`--build`):
    - Runs `resourcecompiler.exe` on the produced `.vmdl`.
    - Patches `extract_motion` frame rate to fix a 10× speed regression.
    - Transplants MRPH (morph) and full PHYS (ragdoll + cloth) binary blocks from the original `.vmdl_c` into the recompiled `.vmdl_c` so the block set matches Valve's at the semantic level.

### Project layout

```
VmdlExtractor/
├── Program.cs               Main pipeline (4.4k lines)
├── BlockTransplant.cs       MRPH / PHYS binary block splice
├── FolderFileLoader.cs      Folder-mode asset resolver
├── VmdlExtractor.csproj     .NET 10 SDK project
└── Diag/                    Ad-hoc diagnostic tools (excluded from main build)
```

### Acknowledgements

- [ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat) — the heavy-lifting Source 2 decompiler this tool stands on.
- Valve — for shipping the Workshop Tools.

### License

Apache License 2.0 — see [LICENSE](LICENSE).

---

## Русский

### Что это

`VmdlExtractor` — консольная утилита, которая вытаскивает модели Source 2 из VPK-пакетов Dota 2 или из скомпилированных `.vmdl_c` и выдаёт **полностью восстановленный, ModelDoc-совместимый исходник `.vmdl`** вместе со всеми его зависимостями (`.vmesh_c`, `.vmorf_c`, `.vphys_c`, `.vanim_c`, `.vseq_c`, DMX-меши и т.д.).

Под капотом используется [ValveResourceFormat (VRF)](https://github.com/ValveResourceFormat/ValveResourceFormat), поверх которого идёт длинный post-процессинг, после которого результат идентичен тому, что выдал бы ModelDoc, если бы вы открыли модель в Source 2 Workshop Tools, нажали *Save*, и *Build*.

Экстрагированный вывод открывается в ModelDoc без ошибок, чисто компилируется через `resourcecompiler.exe` и — главное — даёт **0 строк разницы** по сравнению с файлом, прошедшим через ModelDoc-ный Save.

### Зачем

Сырой вывод VRF — это *decompilation*, а не *source который перекомпилируется*. При открытии «чистого» `.vmdl` от VRF в ModelDoc всплывают десятки warning'ов о схема-миграции, пропущенные defaults, висящие resource-references, битые AutoLayer'ы и хитро-сломанная анимационная метадата. ModelDoc всё это переписывает на Save, создавая огромный косметический diff, который затмевает любые реально внесённые правки.

`VmdlExtractor` закрывает этот разрыв целиком — и экстрагированный исходник уже в канонической форме ModelDoc'а.

### Что именно делается

| Область | Поведение |
|---|---|
| **Скелет** | Мёрдж `.vmdl_c` primary skeleton + fallback на `m_skeleton` из `.vmesh_c` |
| **Hitbox'ы** | `HitboxSetList` восстановлен; дубликаты `Hitbox.name` авто-суффиксируются (`Root0_JNT`, `Root0_JNT1`...) как это делает ModelDoc |
| **Attachments** | `AttachmentList` с полными полями translation/rotation/offset |
| **Flex controllers** | Мёрдж из модели и внешних `.vmorf_c` |
| **Физика** | `PhysicsShapeList` + трансплантация дополнительных `.vphys_c` (ragdoll, cloth, collision hulls) |
| **Анимации** | Полноценные `AnimFile` с `framerate`, `start_frame`, `end_frame`, `activity_name`, `activity_weight`, `weight_list`, `extract_motion`, `additional_anim_files`, `reverse` инжектятся из метаданных `.vseq_c` |
| **Anim events** | `AE_CL_CREATE_PARTICLE_EFFECT_CFG` получает дефолты `aggregate`/`tags`; `AE_CL_PLAYSOUND` — `tags`; у `AE_CL_SUPPRESS_EVENTS_WITH_TAG` кривое `tag = resource:"X"` переписывается в `tag = "X"` |
| **DMX меши** | `jointIndex = -1` → `0` c `weight = 0` (убирает «растяжку» вершин к root-bone) |
| **Include-модели** | `m_refAnimIncludeModels` декомпилируется рекурсивно |
| **Motion-патч** | `extract_motion` нейтрализуется там, где вывод VRF вызвал бы баг 10× speed |
| **Activity-модификаторы** | Инжект `ActivityModifier` child-nodes (опционально `--prune-modifiers` для кастомных аддонов без `items_game.txt`) |
| **Schema header** | Апгрейд с `modeldoc28`/`modeldoc32` до `modeldoc41` (актуальная схема) |
| **Numeric format** | Float-массивы (`angles`, `origin`, `hitbox_*`) переформатируются в shortest-roundtrip-представление ModelDoc'а |
| **Опциональный auto-build** | `--build` запускает `resourcecompiler.exe`, трансплантирует оригинальные MRPH + PHYS binary-блоки из исходного `.vmdl_c`, и выдаёт drop-in replacement — без открытия Workshop Tools |

### Round-trip fidelity

Проверено на **Shadow Fiend Arcana** из Dota 2 `7.28c`:

```
Наш source:                          144 221 bytes
После ModelDoc "Save":               147 305 bytes   (+3 КБ default-блоков, которые дописывает MD)
Round-trip diff (Compare-Object):    0 строк
Recompile через resourcecompiler:    OK: 1 compiled, 0 failed, 0 skipped
```

Diff сократился с **1186 строк → 0 строк** за счёт фокусного fidelity-прохода — см. `Program.cs` `InjectModelDocDefaults()` для полной цепочки трансформаций.

### Требования

- **ОС:** Windows 10/11 (x64)
- **Runtime:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Source-игра:** установленная Dota 2 с VPK
- **Target-игра:** Dota 2 + Workshop Tools (для `resourcecompiler.exe`), опционально
- **NuGet:** [`ValveResourceFormat`](https://www.nuget.org/packages/ValveResourceFormat) 19.1.x, [`ValvePak`](https://www.nuget.org/packages/ValvePak) 4.0.x — качаются автоматически при первом билде

### Сборка

```powershell
git clone https://github.com/crsvdd/VmdlExtractor.git
cd VmdlExtractor
dotnet build -c Release
```

Бинарник — `bin/Release/net10.0/VmdlExtractor.exe`.

### Использование

#### Извлечь одного героя из VPK

```powershell
.\bin\Release\net10.0\VmdlExtractor.exe `
    -i "D:\Games\Dota 2 7.28c\game\dota\pak01_dir.vpk" `
    -f "models/heroes/shadow_fiend/shadow_fiend_arcana.vmdl_c" `
    -o ".\out"
```

#### Извлечь всё по префиксу

```powershell
.\bin\Release\net10.0\VmdlExtractor.exe `
    -i "D:\Games\Dota 2 7.28c\game\dota\pak01_dir.vpk" `
    -p "models/heroes/shadow_fiend" `
    -o ".\out"
```

#### Полный пайплайн: экстракт → копия в аддон → компиляция → PHYS+MRPH transplant

```powershell
.\bin\Release\net10.0\VmdlExtractor.exe `
    -i "D:\Games\Dota 2 7.28c\game\dota\pak01_dir.vpk" `
    -p "models/heroes/shadow_fiend" `
    -o ".\out" `
    --copy-to "E:\Steam\steamapps\common\dota 2 beta\content\dota_addons\mymod\models\heroes\shadow_fiend" `
    --copy-from "models/heroes/shadow_fiend/shadow_fiend_arcana" `
    --build -v
```

Одна команда — и модель готова к использованию в custom-аддоне. **Не открывайте ModelDoc после этого** — его *Save & Build* затрёт MRPH/PHYS-трансплант обычной перекомпиляцией.

### Архитектура / Pipeline

1. **VRF-декомпил** — `ValveResourceFormat.IO.ContentFile` превращает `.vmdl_c` в первичный `.vmdl` + sub-files.
2. **Skeleton / Hitbox / Attachment / Flex / Physics merge** — эти классы лежат в разных местах скомпилированного ресурса; `VmdlExtractor` нормализует их в единое ModelDoc-дерево.
3. **Resource-ref sanitize** — убирает пустые references, чинит сломанный `tag = resource:"X"` в `AE_CL_SUPPRESS_EVENTS_WITH_TAG`, удаляет висящие AutoLayer targets.
4. **ModelDoc default injection** (`InjectModelDocDefaults` в `Program.cs`):
    - `InjectAnimFileSchemaDefaults`, `InjectAnimEventEndFrame`
    - `InjectParticleEventKeysDefaults` (particle + PLAYSOUND)
    - `InjectExtractMotionDefaults`, `InjectRenderMeshFileImportFilter`
    - `InjectWeightListDefaults`, `InjectAnimationListDefaults`, `InjectHitboxDefaults`
    - `NormalizeFloatPrecision` (float-массивы → shortest-roundtrip-форма MD)
    - `StripEmptyBoneMarkupChildren`
    - `DisambiguateDuplicateHitboxNames` (авто-суффикс `name1`, `name2`, ...)
    - `UpgradeSchemaHeader` (→ modeldoc41)
5. **Include-model рекурсия** — каждая запись в `m_refAnimIncludeModels` резолвится и декомпилируется.
6. **Опциональный auto-build** (`--build`): прогон `resourcecompiler.exe` → motion-патч частоты кадров → MRPH+PHYS-трансплант оригинальных блоков.

### Благодарности

- [ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat) — базовый Source 2 decompiler, на котором всё это построено.
- Valve — за Workshop Tools.

### Лицензия

Apache License 2.0 — см. [LICENSE](LICENSE).
