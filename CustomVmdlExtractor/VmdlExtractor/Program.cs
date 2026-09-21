// VmdlExtractor v5.3 — full ModelDoc compile-pass extraction.
//
// Поверх v5.2 (strip empty resource refs + broken AutoLayer) добавлены два новых
// пост-прохода чтобы модель не только компилировалась, но и корректно рендерилась
// под анимациями:
//
//   4. Rewrite bogus `tag = resource:"X"` → `tag = "X"`.
//      Событие AE_CL_SUPPRESS_EVENTS_WITH_TAG в VRF-выводе мисс-типизирует поле
//      `tag` как resource reference, хотя это строковый идентификатор. ModelDoc
//      пытается зарегистрировать несуществующий resource → "Bad resource reference"
//      → "Tried to register an empty resource reference" → Compile Failed.
//
//   5. Patch DMX mesh jointIndices: -1 → 0 w=0.
//      VRF `ToDmxMesh` оставляет "пустые" weight-слоты с jointIndex=-1. ModelDoc
//      warning'ит ("Invalid skinning bone index :: -1") и клампит -1→root, из-за
//      чего часть вертексов паразитно тянется за root-анимацией (turns_anim и
//      прочие root-повороты). Обнуляем jointIndex=0 и jointWeight=0 — вертекс
//      больше не реагирует на root, стретчинг исчезает.

using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Datamodel;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;

namespace VmdlExtractor;

internal static class Program
{
    private const string VersionBanner =
        "VmdlExtractor v5.3.2 (MaterialGroupList reconstruct + activity-array fidelity)";

    private static readonly string[] ModelDepExts =
        [".vmesh", ".vmorf", ".vphys", ".vagrp", ".vanim", ".vmodel", ".vseq", ".vpulse"];

    // A/B-toggles для отладки in-game animation behaviour.
    // Управляются CLI-флагами:
    //   --activity-modifier-inject   — включает ActivityModifier child inject.
    //   --no-framerate-inject        — выключает framerate/start_frame/end_frame inject.
    //   --no-dedup                   — выключает дедупликацию кандидатов по (activity, modifier).
    //
    // ActivityModifier child inject ПО УМОЛЧАНИЮ ВЫКЛЮЧЕН после эмпирики:
    // ✓ ROOT FIX подтверждён байтовым diff'ом скомпилированного .vmdl_c с оригиналом
    // Valve: child-node `_class = "ActivityModifier"` с `activity_name = "<modifier>"`
    // и `activity_weight = N` ВНУТРИ `children = [...]` массива AnimFile.
    //
    // ModelDoc compile корректно мерджит этот child-node в `m_activityArray` ASEQ —
    // получаем 1:1 структуру оригинала. Альтернативные синтаксисы НЕ работают:
    //   - field-level `activity_modifiers = [...]` — игнорируется compile'ом
    //   - field-level `activities = [{name=...,weight=...}]` — игнорируется
    //
    // Поэтому: child-node injection ВКЛЮЧЁН по умолчанию, остальные injection-костыли
    // ВЫКЛЮЧЕНЫ (они нужны были как симптоматическое лечение, когда мы не знали
    // правильного синтаксиса).
    internal static bool SkipActivityModInject;          // child-node inject — ENABLED
    internal static bool SkipFramerateInject;            // framerate inject — ENABLED
    internal static bool SkipDedup = true;               // dedup hack — DISABLED (вредил корректному inject)
    internal static bool SkipDisableNonWhitelistedMods = true;  // prune hack — DISABLED
    internal static bool SkipFieldLevelModifierInject = true;   // field-level inject — DISABLED (не работает)
    internal static bool SkipExtractMotionSanitize;             // ExtractMotion neutralizer — ENABLED (фикс 10x speed)
    // Multipose-layer strip neutralizer. ENABLED by default since v5.3.1
    // (fixes systematic in-match body flipping during run+turn). Opt-out
    // via --keep-multipose-layers if a specific model relies on the broken
    // overlay (none observed in 7.28c+ heroes - Pudge/CM/Old SF/SFA all
    // exhibit identical VRF behaviour: turns.dmx === @turns_lookFrame_0.dmx).
    internal static bool SkipMultiposeLayerStrip;
    // Hardcoded whitelist (синхронизирован с scripts/activity_modifier_weights.txt
    // в Dota 2 7.28c). Используется как fallback, если файл не доступен.
    private static readonly string[] BuiltinModifierWhitelist =
        ["aggressive", "injured", "injured_aggressive", "haste"];

    internal delegate byte[]? RawReader(string compiledPath);

    // ───────────────────────────── CLI ─────────────────────────────

    private static int Main(string[] args)
    {
        string? input = null;
        string? output = null;
        string? gameRoot = null;
        string? vpkPrefix = null;
        // Single-file path inside the VPK (e.g.
        // "models/heroes/shadow_fiend/shadow_fiend_arcana.vmdl_c"). When set,
        // the extractor will only process that exact entry plus its
        // transitive dependencies, ignoring `-p` (vpk-prefix) entirely.
        string? vpkFile = null;
        string? copyTo = null;
        string? copyFrom = null;
        string? vmdlcTarget = null;
        bool autoBuild = false;
        string? resourceCompiler = null;
        // --fix-motion mode: standalone post-compile patch on an existing
        // .vmdl_c. Copies m_movementArray entries from a donor VPK so engine
        // gets correct per-anim velocity for cycle_rate scaling. Use when
        // you already compiled in ModelDoc and the recompiled output has
        // zeroed/empty movement arrays causing locomotion detach bugs.
        string? fixMotionTarget = null;
        string? donorVpkPath = null;
        string? donorVpkInner = null;
        bool verbose = false;
        // Default: flat layout matching VRF's emitted RenderMeshFile/AnimFile filename paths
        // (`models/heroes/<x>/<vmdl_basename>_<mesh>.dmx`). Use --model-folder to opt back into
        // per-model subdirectories (older behaviour; breaks ModelDoc compile).
        bool noModelFolder = true;
        bool noRawDeps = false;
        bool noSanitize = false;
        bool noIncludeDeps = false;
        // Diagnostic toggles — статические поля, читаются inject-методами напрямую.
        // Используются только для A/B-сравнения при отладке. Дефолты см. в объявлениях полей.
        var srcExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".vmdl_c" };

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-i": case "--input": input = args[++i]; break;
                case "-o": case "--output": output = args[++i]; break;
                case "-g": case "--game-root": gameRoot = args[++i]; break;
                case "-p":
                case "--vpk-prefix":
                    vpkPrefix = args[++i].Replace('\\', '/').TrimStart('/');
                    break;
                case "-f":
                case "--file":
                    // Exact single-file extraction. Wins over -p if both given.
                    vpkFile = args[++i].Replace('\\', '/').TrimStart('/');
                    break;
                case "-e":
                case "--extensions":
                    srcExts.Clear();
                    foreach (var ext in args[++i].Split(','))
                        srcExts.Add(ext.StartsWith('.') ? ext : "." + ext);
                    break;
                case "--no-model-folder": noModelFolder = true; break;  // legacy alias (now default)
                case "--model-folder": noModelFolder = false; break; // opt-in to subdir layout
                case "--no-raw-deps": noRawDeps = true; break;
                case "--no-sanitize": noSanitize = true; break;
                case "--no-include-deps": noIncludeDeps = true; break;
                case "--activity-modifier-inject": SkipActivityModInject = false; break;
                case "--no-activity-modifier-inject": SkipActivityModInject = true; break; // legacy
                case "--no-framerate-inject": SkipFramerateInject = true; break;
                case "--no-dedup": SkipDedup = true; break;
                case "--prune-modifiers": SkipDisableNonWhitelistedMods = false; break;
                case "--no-prune-modifiers": SkipDisableNonWhitelistedMods = true; break;
                case "--no-field-mod-inject": SkipFieldLevelModifierInject = true; break;
                case "--no-motion-sanitize": SkipExtractMotionSanitize = true; break;
                case "--keep-multipose-layers": SkipMultiposeLayerStrip = true; break;
                case "--copy-to": copyTo = args[++i]; break;
                case "--copy-from": copyFrom = args[++i].Replace('\\', '/').Trim('/'); break;
                case "--vmdlc-target": vmdlcTarget = args[++i]; break;
                case "--build": autoBuild = true; break;
                case "--resourcecompiler": resourceCompiler = args[++i]; break;
                case "--fix-motion": fixMotionTarget = args[++i]; break;
                case "--donor-vpk": donorVpkPath = args[++i]; break;
                case "--donor-inner": donorVpkInner = args[++i]; break;
                case "-v": case "--verbose": verbose = true; break;
                case "--version": Console.WriteLine(VersionBanner); return 0;
                case "-h": case "--help": PrintHelp(); return 0;
            }
        }

        Console.WriteLine(VersionBanner);

        // ── Standalone --fix-motion mode ─────────────────────────────────
        // Patches m_movementArray in-place on a recompiled .vmdl_c using
        // values from the donor VPK. No ModelDoc/resourcecompiler needed.
        // Fixes locomotion-detach bugs (model outruns entity at high speeds)
        // caused by ModelDoc compile zeroing motion arrays.
        if (!string.IsNullOrEmpty(fixMotionTarget))
        {
            return RunFixMotion(fixMotionTarget, donorVpkPath, donorVpkInner, verbose);
        }

        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
        {
            Console.Error.WriteLine("Error: --input and --output are required.\n");
            PrintHelp();
            return 2;
        }

        bool isVpk = input.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase) && File.Exists(input);
        bool isFile = !isVpk && File.Exists(input);
        bool isDir = Directory.Exists(input);

        if (!isVpk && !isFile && !isDir)
        {
            Console.Error.WriteLine($"Error: --input is not an existing .vpk, file or directory: {input}");
            return 2;
        }

        Directory.CreateDirectory(output);

        var rc = isVpk
            ? RunVpkMode(input, output, srcExts, vpkPrefix, vpkFile,
                         noModelFolder, noRawDeps, noSanitize, noIncludeDeps, verbose)
            : RunFolderMode(input, output, srcExts, gameRoot, isFile,
                            noModelFolder, noRawDeps, noSanitize, noIncludeDeps, verbose);

        // Auto-copy результата в указанную директорию (например, в content/dota_addons/<addon>).
        // По умолчанию (если --copy-from не указан) копируется ВСЁ содержимое output в copyTo.
        // Если указан --copy-from <relSubdir>, то копируется СОДЕРЖИМОЕ этой подпапки в copyTo
        // (без вложения самой подпапки) — удобно для подмены конкретной модели в addon.
        if (rc == 0 && !string.IsNullOrWhiteSpace(copyTo))
        {
            var srcRoot = string.IsNullOrWhiteSpace(copyFrom)
                ? output
                : Path.Combine(output, copyFrom!.Replace('/', Path.DirectorySeparatorChar));
            CopyExtractToTarget(srcRoot, copyTo, verbose);
        }

        // ✓ ROOT-FIX BYPASS: --vmdlc-target копирует ОРИГИНАЛЬНЫЙ .vmdl_c из VPK
        // прямо в game-dir addon'а. Это обходит ModelDoc compile (который теряет
        // m_activityArray модификаторы при перекомпиляции — доказано байтовым diff'ом).
        // Гарантия 100% identity с оригиналом Valve — все sequences играются как в
        // vanilla Dota 2.
        //
        // ИСПОЛЬЗОВАНИЕ:
        //   --vmdlc-target "...\game\dota_addons\witchblades"
        // Структура /models/heroes/.../*.vmdl_c сохраняется относительно VPK prefix.
        if (rc == 0 && isVpk && !string.IsNullOrWhiteSpace(vmdlcTarget))
            CopyOriginalVmdlcFromVpk(input, vpkPrefix, vmdlcTarget, verbose);

        // ✓ AUTO-BUILD: запускает resourcecompiler.exe на скопированных .vmdl и
        // применяет post-compile motion patch (заменяет per-frame m_movementArray
        // на single zero entry — фикс 10x speed для locomotion-анимаций).
        //
        // Заменяет ручной workflow "ModelDoc → Save & Build" — даёт корректный
        // .vmdl_c за один запуск VmdlExtractor.
        //
        // Авто-определяет: 
        //   - resourcecompiler.exe (через --resourcecompiler или из --copy-to пути)
        //   - game-dir (из content-dir заменой "content/" → "game/")
        if (rc == 0 && autoBuild && !string.IsNullOrWhiteSpace(copyTo))
        {
            // Pass the source VPK so AutoBuildAndPatch can transplant fidelity
            // blocks (MRPH, full PHYS, etc.) from the original compiled .vmdl_c
            // back into our newly-built one. Only valid for VPK input — for
            // folder/file inputs there's no donor to read from.
            var donorVpk = isVpk ? input : null;
            rc = AutoBuildAndPatch(copyTo, resourceCompiler, donorVpk, verbose);
        }

        return rc;
    }

    /// <summary>
    /// Auto-build pipeline: locate .vmdl files in copied content dir, run
    /// resourcecompiler.exe, then apply motion patch (zero m_movementArray for
    /// locomotion anims to fix 10x speed bug).
    /// </summary>
    /// <param name="gameArgDirOverride">
    /// Explicit "-game" directory to pass to resourcecompiler.exe. When null, falls back to the
    /// CLI's auto-detection (walks up from <paramref name="contentDir"/> for a Dota 2 install and
    /// assumes "game/dota"), which only makes sense for the standalone Dota 2 workflow. Callers
    /// targeting other Source 2 games (or the GUI, which doesn't know which game a given output
    /// folder belongs to) should always pass this explicitly.
    /// </param>
    internal static int AutoBuildAndPatch(string contentDir, string? rcOverride, string? donorVpk, bool verbose, string? gameArgDirOverride = null)
    {
        // Step 1: find resourcecompiler.exe
        string? rcPath = rcOverride;
        if (string.IsNullOrWhiteSpace(rcPath))
        {
            // Auto-detect: walk up from contentDir looking for "game/bin/win64/resourcecompiler.exe"
            // Common path: <steam>/steamapps/common/dota 2 beta/content/dota_addons/<X>/...
            //              <steam>/steamapps/common/dota 2 beta/game/bin/win64/resourcecompiler.exe
            var steamRoot = FindDotaRoot(contentDir);
            if (steamRoot != null)
                rcPath = Path.Combine(steamRoot, "game", "bin", "win64", "resourcecompiler.exe");
        }
        if (string.IsNullOrWhiteSpace(rcPath) || !File.Exists(rcPath))
        {
            Console.Error.WriteLine($"[build] resourcecompiler.exe not found: {rcPath ?? "(auto-detect failed)"}");
            Console.Error.WriteLine("[build] Use --resourcecompiler <path> to specify location.");
            return 3;
        }

        // Step 2: derive game-dir from content-dir (content/X/Y → game/X/Y)
        var gameDir = ContentToGameDir(contentDir);
        if (gameDir == null)
        {
            Console.Error.WriteLine($"[build] Cannot derive game-dir from content-dir: {contentDir}");
            return 3;
        }

        // Step 3: derive the "-game" directory passed to resourcecompiler.exe.
        string dotaGameDir;
        if (gameArgDirOverride != null)
        {
            dotaGameDir = gameArgDirOverride;
        }
        else
        {
            // It's <steam-dir>/game/dota -- independent of addon, used as content reference root.
            var dotaRoot = FindDotaRoot(contentDir);
            if (dotaRoot == null)
            {
                Console.Error.WriteLine($"[build] Cannot find Dota 2 root from: {contentDir}");
                return 3;
            }
            dotaGameDir = Path.Combine(dotaRoot, "game", "dota");
        }

        // Step 4: enumerate .vmdl files in contentDir, compile each
        var vmdlFiles = Directory.GetFiles(contentDir, "*.vmdl", SearchOption.AllDirectories);
        if (vmdlFiles.Length == 0)
        {
            Console.Error.WriteLine($"[build] No .vmdl files in: {contentDir}");
            return 3;
        }

        Console.WriteLine($"[build] resourcecompiler: {rcPath}");
        Console.WriteLine($"[build] -game: {dotaGameDir}");
        Console.WriteLine($"[build] vmdl files: {vmdlFiles.Length}");

        // Open donor VPK once (for fidelity-block transplant). Skip silently if
        // missing/inaccessible — transplant becomes a no-op then.
        Package? donorPkg = null;
        if (!string.IsNullOrEmpty(donorVpk))
        {
            try
            {
                donorPkg = new Package();
                donorPkg.Read(donorVpk);
                Console.WriteLine($"[build] donor VPK: {donorVpk}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[build] donor VPK open failed: {ex.Message}");
                donorPkg = null;
            }
        }

        // Block types we attempt to transplant from the donor when missing in
        // our compiled output. These are exactly the blocks the source-rebuild
        // path cannot fully reconstruct from .vmdl text alone:
        //   MRPH — morph (face flexes / lip-sync atlas references)
        //   PHYS — full ragdoll/cloth physics (m_pFeModel, joints)
        // Other blocks (MBUF/MIDX/MVTX/REDI/RED2) are format-migration
        // duplicates — newer Source 2 compiler emits MIDX+MVTX in place of MBUF
        // and RED2 in place of REDI; both are functionally equivalent.
        var fidelityBlocks = new[] { ValveResourceFormat.BlockType.MRPH, ValveResourceFormat.BlockType.PHYS };

        int compiled = 0, patched = 0, transplanted = 0;
        foreach (var vmdl in vmdlFiles)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = rcPath,
                ArgumentList = { "-i", vmdl, "-game", dotaGameDir },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) continue;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                Console.Error.WriteLine($"[build] FAILED to compile {Path.GetFileName(vmdl)}: exit={proc.ExitCode}");
                if (verbose) Console.Error.WriteLine(stderr);
                continue;
            }
            compiled++;
            if (verbose) Console.WriteLine($"[build] compiled: {Path.GetFileName(vmdl)}");

            // Post-compile motion patch
            var rel = Path.GetRelativePath(contentDir, vmdl);
            var compiledPath = Path.Combine(gameDir, Path.ChangeExtension(rel, ".vmdl_c"));
            if (!File.Exists(compiledPath))
            {
                if (verbose) Console.Error.WriteLine($"[build] no compiled output: {compiledPath}");
                continue;
            }

            // Motion patch FIRST — copies authoritative `m_movementArray` from
            // the donor VPK (Valve's original .vmdl_c) into our recompiled
            // resource. Required because:
            //   - SanitizeExtractMotion forces all extract_t*=false in .vmdl
            //     to dodge the 10× speed bug from VRF over-emitting
            //     ExtractMotion nodes.
            //   - With extract_t*=false, ModelDoc compile produces an empty
            //     or zero-filled `m_movementArray` for *every* anim — incl.
            //     run/walk/sprint where engine NEEDS real per-anim velocity
            //     to compute cycle_rate. Without it the locomotion anim
            //     plays at native fps regardless of hero speed → model
            //     visibly outruns its entity (treadmill detach + snap-back).
            // Fix: read original .vmdl_c from donor VPK, build name→
            // m_movementArray map, paste donor entries verbatim into our
            // compiled output. For anims absent in donor (user-added custom
            // sequences) we fall back to the legacy zero-entry behaviour.
            // Goes through VRF's `Resource.Serialize` which duplicates Mesh
            // blocks (~2.5× file bloat); benign at runtime (engine reads
            // first block of each type) and is the price for staying in
            // pure C# without a custom KV3 binary serialiser.
            string? donorVpkRel = null;
            if (donorPkg != null)
            {
                donorVpkRel = rel.Replace('\\', '/');
                if (!donorVpkRel.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
                    donorVpkRel = Path.ChangeExtension(donorVpkRel, ".vmdl_c");
            }
            int patchCount = PatchVmdlcMotionArray(compiledPath, donorPkg, donorVpkRel, verbose);
            if (patchCount > 0) patched++;

            // Fidelity transplant LAST — splice fully-formed blocks (MRPH,
            // full PHYS with cloth/joints) from the donor VPK into our
            // compiled .vmdl_c. Byte-level surgery preserves whatever motion
            // patch produced and just appends new entries with their own
            // payload, so this stage doesn't compound the bloat.
            //
            // The relative path is computed from the addon content tree
            // (`contentDir`) so it matches the VPK layout
            // (`models/heroes/X/X.vmdl_c`).
            if (donorPkg != null)
            {
                var vpkRel = rel.Replace('\\', '/');
                if (!vpkRel.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
                    vpkRel = Path.ChangeExtension(vpkRel, ".vmdl_c");
                try
                {
                    var tx = BlockTransplant.TransplantBlocksFromVpk(compiledPath, donorPkg, vpkRel, fidelityBlocks, verbose);
                    if (tx != null && tx.Added.Count > 0) transplanted++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[build] transplant failed for {Path.GetFileName(compiledPath)}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"[build] compiled: {compiled}/{vmdlFiles.Length}, motion-patched: {patched}, fidelity-transplanted: {transplanted}");
        return 0;
    }

    /// <summary>Find Dota 2 root by walking up content-dir until parent has both content/ and game/.</summary>
    private static string? FindDotaRoot(string startDir)
    {
        var d = new DirectoryInfo(startDir);
        while (d != null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "content")) &&
                Directory.Exists(Path.Combine(d.FullName, "game")))
                return d.FullName;
            d = d.Parent;
        }
        return null;
    }

    /// <summary>Convert content-dir path to corresponding game-dir path.</summary>
    private static string? ContentToGameDir(string contentDir)
    {
        // Replace "\content\" with "\game\" once
        var idx = contentDir.IndexOf("\\content\\", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return contentDir.Substring(0, idx) + "\\game\\" + contentDir.Substring(idx + "\\content\\".Length);
    }

    /// <summary>Find addon root: ".../content/dota_addons/<addon>/" — walk up until parent name is "dota_addons".</summary>
    private static string? FindAddonRoot(string startDir)
    {
        var d = new DirectoryInfo(startDir);
        while (d != null)
        {
            if (d.Parent?.Name.Equals("dota_addons", StringComparison.OrdinalIgnoreCase) == true)
                return d.FullName;
            d = d.Parent;
        }
        return null;
    }

    /// <summary>
    /// Determines whether a m_movementArray entry is the "zero motion" pattern:
    /// motionflags=64, v0=v1=angle=0, vector=[0,0,0], position=[0,0,0].
    /// Used for skip-detection: file already correctly patched, no rewrite needed.
    /// </summary>
    private static bool IsZeroMotionEntry(ValveKeyValue.KVObject entry)
    {
        try
        {
            float v0 = entry.GetFloatProperty("v0");
            float v1 = entry.GetFloatProperty("v1");
            float angle = entry.GetFloatProperty("angle");
            if (v0 != 0f || v1 != 0f || angle != 0f) return false;
            var pos = entry["position"];
            if (pos == null || pos.Count < 3) return true; // missing → considered zero
            foreach (var p in pos)
            {
                try
                {
                    float f = Convert.ToSingle(p.Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (f != 0f) return false;
                }
                catch { /* non-convertible → treat as zero */ }
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Result of patch attempt: counts how many anims actually needed patching
    /// (i.e. had per-frame motion data) vs how many were already in zero/empty state.
    /// </summary>
    private record PatchResult(int Patched, int AlreadyOk, int Empty);

    /// <summary>
    /// Standalone CLI entry: apply <see cref="PatchVmdlcMotionArray"/> on an
    /// already-compiled .vmdl_c without re-running ModelDoc/resourcecompiler.
    /// Auto-detects the inner VPK path from the target if --donor-inner not
    /// given by stripping everything before "models/" (works for Dota 2
    /// hero models in addons).
    /// </summary>
    private static int RunFixMotion(string targetVmdlc, string? donorVpk, string? innerOverride, bool verbose)
    {
        if (!File.Exists(targetVmdlc))
        {
            Console.Error.WriteLine($"[fix-motion] target .vmdl_c not found: {targetVmdlc}");
            return 2;
        }
        if (string.IsNullOrEmpty(donorVpk) || !File.Exists(donorVpk))
        {
            Console.Error.WriteLine("[fix-motion] --donor-vpk is required and must point to an existing .vpk");
            return 2;
        }

        // Auto-detect VPK-inner path: strip everything up to "models/" or
        // "particles/" etc. so e.g.
        //   E:\..\dota_addons\X\models\heroes\arc_warden\arc_warden.vmdl_c
        // becomes models/heroes/arc_warden/arc_warden.vmdl_c.
        var inner = innerOverride;
        if (string.IsNullOrEmpty(inner))
        {
            var norm = targetVmdlc.Replace('\\', '/');
            string[] roots = { "/models/", "/particles/", "/sounds/", "/materials/" };
            foreach (var r in roots)
            {
                int idx = norm.IndexOf(r, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                inner = norm[(idx + 1)..];
                break;
            }
        }
        if (string.IsNullOrEmpty(inner))
        {
            Console.Error.WriteLine("[fix-motion] could not auto-detect VPK-inner path; pass --donor-inner explicitly.");
            return 2;
        }

        Console.WriteLine($"[fix-motion] target : {targetVmdlc}");
        Console.WriteLine($"[fix-motion] donor  : {donorVpk}");
        Console.WriteLine($"[fix-motion] inner  : {inner}");

        Package donorPkg;
        try
        {
            donorPkg = new Package();
            donorPkg.Read(donorVpk);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[fix-motion] donor VPK open failed: {ex.Message}");
            return 3;
        }

        int n = PatchVmdlcMotionArray(targetVmdlc, donorPkg, inner, verbose: true);
        Console.WriteLine($"[fix-motion] anims patched: {n}");
        return 0;
    }

    /// <summary>
    /// Apply post-compile motion patch: copy `m_movementArray` from the donor
    /// VPK (Valve's original .vmdl_c) into our recompiled resource so engine
    /// gets real per-anim velocity and cycle_rate scaling works at all hero
    /// speeds. For anims missing in donor (custom user sequences) replace
    /// with single zero entry (legacy behaviour). Skips anims already in
    /// expected state — file untouched if no changes.
    /// </summary>
    private static int PatchVmdlcMotionArray(
        string vmdlcPath, Package? donorPkg,
        string? donorVpkInnerPath, bool verbose)
    {
        try
        {
            // 1. Build donor map: anim name → m_movementArray (KVValue).
            var donorMov = LoadDonorMovementMap(donorPkg, donorVpkInnerPath, verbose);

            // 2. Open compiled output.
            var srcBytes = File.ReadAllBytes(vmdlcPath);
            var memStream = new MemoryStream(srcBytes, writable: false);
            var rr = new ValveResourceFormat.Resource { FileName = Path.GetFileName(vmdlcPath) };
            rr.Read(memStream);

            var animBlock = rr.GetBlockByType(ValveResourceFormat.BlockType.ANIM);
            if (animBlock == null) return 0;

            object? animDataObj = null;
            if (animBlock is ValveResourceFormat.ResourceTypes.BinaryKV3 bkv3) animDataObj = bkv3.Data;
            else if (animBlock is ValveResourceFormat.ResourceTypes.KeyValuesOrNTRO kvOrNtro) animDataObj = kvOrNtro.Data;
            else return 0;

            ValveKeyValue.KVObject animData;
            if (animDataObj is ValveKeyValue.KVDocument doc) animData = doc.Root;
            else if (animDataObj is ValveKeyValue.KVObject kvo) animData = kvo;
            else return 0;

            if (!animData.ContainsKey("m_animArray")) return 0;
            var animArr = animData["m_animArray"];

            int patchedFromDonor = 0, patchedZero = 0, alreadyOk = 0, empty = 0, donorMissing = 0;
            foreach (var kvp in animArr)
            {
                var an = kvp.Value;
                var name = an["m_name"]?.ToString() ?? "";
                if (!an.ContainsKey("m_movementArray")) continue;
                var movArr = an["m_movementArray"];

                // 3a. Donor copy path — preferred when available.
                if (!string.IsNullOrEmpty(name) && donorMov.TryGetValue(name, out var donorMovArr))
                {
                    if (movArr != null && MovementArrayEquals(movArr, donorMovArr))
                    {
                        alreadyOk++;
                        continue;
                    }
                    if (movArr != null) movArr.Clear();
                    foreach (var de in donorMovArr) movArr!.Add(de.Value);
                    patchedFromDonor++;
                    continue;
                }

                // 3b. No donor entry — fall back to legacy zero-entry policy
                //     (custom anim added by user; safest assumption is no
                //     locomotion). Skip if already empty/zero.
                if (donorPkg != null) donorMissing++;
                if (movArr == null || movArr.Count == 0) { empty++; continue; }
                if (movArr.Count == 1 && IsZeroMotionEntry(movArr.First().Value))
                {
                    alreadyOk++;
                    continue;
                }

                int lastEnd = 0;
                foreach (var mvp in movArr)
                {
                    int ef = 0;
                    try { ef = mvp.Value.GetInt32Property("endframe"); } catch { }
                    if (ef > lastEnd) lastEnd = ef;
                }

                var zeroEntry = ValveKeyValue.KVObject.Collection();
                zeroEntry["endframe"] = lastEnd;
                zeroEntry["motionflags"] = 64;
                zeroEntry["v0"] = 0.0f;
                zeroEntry["v1"] = 0.0f;
                zeroEntry["angle"] = 0.0f;
                var vec = ValveKeyValue.KVObject.Array();
                vec.Add(0.0f); vec.Add(0.0f); vec.Add(0.0f);
                zeroEntry["vector"] = vec;
                var pos = ValveKeyValue.KVObject.Array();
                pos.Add(0.0f); pos.Add(0.0f); pos.Add(0.0f);
                zeroEntry["position"] = pos;

                movArr.Clear();
                movArr.Add(zeroEntry);
                patchedZero++;
            }

            int totalPatched = patchedFromDonor + patchedZero;
            if (totalPatched > 0)
            {
                using var ofs = File.Create(vmdlcPath);
                rr.Serialize(ofs);
                if (verbose)
                    Console.WriteLine($"  ✓ {Path.GetFileName(vmdlcPath)}: " +
                        $"donor-copy={patchedFromDonor}, zero={patchedZero}, " +
                        $"alreadyOk={alreadyOk}, empty={empty}, donorMissing={donorMissing}");
            }
            else if (verbose)
            {
                Console.WriteLine($"  ⊘ {Path.GetFileName(vmdlcPath)}: skipped " +
                    $"(alreadyOk={alreadyOk}, empty={empty}, donorMissing={donorMissing})");
            }
            return totalPatched;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[patch] failed for {Path.GetFileName(vmdlcPath)}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Read donor `.vmdl_c` from VPK and return map: anim name → m_movementArray.
    /// Returns empty dict if donor not provided or entry not found. Returned
    /// `KVValue` objects are owned by the donor's KV tree — caller must NOT
    /// mutate them; only read entries one-by-one and add into target movArr.
    /// </summary>
    private static Dictionary<string, ValveKeyValue.KVObject> LoadDonorMovementMap(
        Package? donorPkg, string? donorVpkInnerPath, bool verbose)
    {
        var map = new Dictionary<string, ValveKeyValue.KVObject>(StringComparer.OrdinalIgnoreCase);
        if (donorPkg == null || string.IsNullOrEmpty(donorVpkInnerPath)) return map;

        try
        {
            var entry = donorPkg.FindEntry(donorVpkInnerPath);
            if (entry == null)
            {
                if (verbose) Console.WriteLine($"    [motion] donor entry not in VPK: {donorVpkInnerPath}");
                return map;
            }
            donorPkg.ReadEntry(entry, out var donorBytes);

            using var ms = new MemoryStream(donorBytes, writable: false);
            var rr = new ValveResourceFormat.Resource { FileName = Path.GetFileName(donorVpkInnerPath) };
            rr.Read(ms);
            var animBlock = rr.GetBlockByType(ValveResourceFormat.BlockType.ANIM);
            if (animBlock == null) return map;

            object? animDataObj = null;
            if (animBlock is ValveResourceFormat.ResourceTypes.BinaryKV3 bkv3) animDataObj = bkv3.Data;
            else if (animBlock is ValveResourceFormat.ResourceTypes.KeyValuesOrNTRO kvOrNtro) animDataObj = kvOrNtro.Data;
            if (animDataObj == null) return map;

            ValveKeyValue.KVObject animData = animDataObj switch
            {
                ValveKeyValue.KVDocument doc => doc.Root,
                ValveKeyValue.KVObject kvo => kvo,
                _ => null!,
            };
            if (animData == null || !animData.ContainsKey("m_animArray")) return map;

            foreach (var kvp in animData["m_animArray"])
            {
                var an = kvp.Value;
                var name = an["m_name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                if (!an.ContainsKey("m_movementArray")) continue;
                // KVObject indexer returns KVObject for nested collections.
                map[name] = an["m_movementArray"];
            }
            if (verbose)
                Console.WriteLine($"    [motion] donor map: {map.Count} anim(s) from {donorVpkInnerPath}");
        }
        catch (Exception ex)
        {
            if (verbose) Console.WriteLine($"    [motion] donor read failed: {ex.Message}");
        }
        return map;
    }

    /// <summary>
    /// Compare two m_movementArray instances field-by-field. Used as a
    /// no-op short-circuit so we don't rewrite a file that already matches
    /// the donor.
    /// </summary>
    private static bool MovementArrayEquals(ValveKeyValue.KVObject a, ValveKeyValue.KVObject b)
    {
        try
        {
            if (a.Count != b.Count) return false;

            using var ea = a.GetEnumerator();
            using var eb = b.GetEnumerator();
            while (ea.MoveNext() && eb.MoveNext())
            {
                var av = ea.Current.Value;
                var bv = eb.Current.Value;
                foreach (var key in new[] { "endframe", "motionflags", "v0", "v1", "angle" })
                {
                    var ax = av.ContainsKey(key) ? av[key]?.ToString() : null;
                    var bx = bv.ContainsKey(key) ? bv[key]?.ToString() : null;
                    if (!string.Equals(ax, bx, StringComparison.Ordinal)) return false;
                }
            }
            return true;
        }
        catch { return false; }
    }

    // Копирует ВСЕ .vmdl_c из VPK (под указанным prefix'ом) в game-dir.
    // Это намеренный bypass ModelDoc compile — для случаев когда compile теряет
    // данные (как m_activityArray модификаторы). Гарантирует 100% Valve-fidelity.
    private static void CopyOriginalVmdlcFromVpk(
        string vpkPath, string? vpkPrefix, string targetDir, bool verbose)
    {
        try
        {
            using var pkg = new Package();
            pkg.Read(vpkPath);
            if (pkg.Entries == null) return;

            int copied = 0;
            string normalizedPrefix = vpkPrefix?.Replace('\\', '/').Trim('/') ?? "";
            // Берём только vmdl_c (готовые ресурсы для движка).
            if (!pkg.Entries.TryGetValue("vmdl_c", out var list)) return;
            foreach (var entry in list)
            {
                if (!string.IsNullOrEmpty(normalizedPrefix) &&
                    !entry.DirectoryName.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                pkg.ReadEntry(entry, out var bytes);
                // Имя файла внутри VPK — entry.GetFullPath() (например models/heroes/sf/sf_arcana.vmdl_c).
                var rel = entry.GetFullPath().Replace('/', Path.DirectorySeparatorChar);
                var dst = Path.Combine(targetDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.WriteAllBytes(dst, bytes);
                copied++;
                if (verbose) Console.WriteLine($"      → {rel}");
            }
            Console.WriteLine($"[vmdlc-bypass] Copied {copied} ORIGINAL .vmdl_c from VPK to: {targetDir}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[vmdlc-bypass] FAILED: {ex.Message}");
        }
    }

    // Копирует всё содержимое srcDir рекурсивно в dstDir с перезаписью.
    // Используется CLI-флагом `--copy-to` для автоматизации workflow extract → addon.
    private static void CopyExtractToTarget(string srcDir, string dstDir, bool verbose)
    {
        if (!Directory.Exists(srcDir))
        {
            Console.Error.WriteLine($"[copy] FAILED: source not found: {srcDir}");
            return;
        }
        try
        {
            int copied = 0;
            foreach (var srcFile in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(srcDir, srcFile);
                var dstFile = Path.Combine(dstDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
                File.Copy(srcFile, dstFile, overwrite: true);
                copied++;
                if (verbose) Console.WriteLine($"      → {relPath}");
            }
            Console.WriteLine($"[copy] Copied {copied} file(s) to: {dstDir}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[copy] FAILED: {ex.Message}");
        }
    }

    // ───────────────────────────── VPK mode ─────────────────────────────

    private static int RunVpkMode(
        string vpkPath, string outputRoot,
        HashSet<string> srcExts, string? vpkPrefix, string? vpkFile,
        bool noModelFolder, bool noRawDeps, bool noSanitize, bool noIncludeDeps,
        bool verbose)
    {
        Console.WriteLine($"[vpk] Opening: {vpkPath}");

        using var package = new Package();
        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
        package.Read(vpkPath);

        if (package.Entries == null) { Console.Error.WriteLine("Error: VPK has no entries."); return 1; }

        var targets = new List<PackageEntry>();

        // Single-file mode (`-f` / `--file`): look up exactly one entry by path.
        // Wins over `-p`. The path may carry an extension or be left bare; we try
        // exact match first, then fall back to appending each known source ext.
        if (!string.IsNullOrEmpty(vpkFile))
        {
            var entry = package.FindEntry(vpkFile);
            if (entry == null)
            {
                foreach (var ext in srcExts)
                {
                    entry = package.FindEntry(vpkFile + ext);
                    if (entry != null) break;
                }
            }
            if (entry == null)
            {
                Console.Error.WriteLine($"[vpk] -f/--file: entry not found: '{vpkFile}'");
                return 1;
            }
            targets.Add(entry);
            Console.WriteLine($"[vpk] Models to extract: 1 (file '{entry.GetFullPath()}')");
        }
        else
        {
            foreach (var ext in srcExts)
            {
                var key = ext.TrimStart('.');
                if (!package.Entries.TryGetValue(key, out var list)) continue;
                foreach (var entry in list)
                {
                    if (vpkPrefix != null &&
                        !entry.DirectoryName.StartsWith(vpkPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    targets.Add(entry);
                }
            }
            Console.WriteLine($"[vpk] Models to extract: {targets.Count}" +
                              (vpkPrefix != null ? $" (under '{vpkPrefix}')" : ""));
        }

        if (targets.Count == 0) return 0;

        using var fileLoader = new GameFileLoader(package, package.FileName);

        RawReader rawReader = compiledPath =>
        {
            var entry = package.FindEntry(compiledPath);
            if (entry == null) return null;
            package.ReadEntry(entry, out var bytes);
            return bytes;
        };

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var includeQueue = new Queue<string>();
        int ok = 0, fail = 0;
        var failures = new List<(string, string)>();

        for (int i = 0; i < targets.Count; i++)
        {
            var entry = targets[i];
            var inVpkPath = entry.GetFullPath();

            var visitKey = (inVpkPath.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
                            ? inVpkPath[..^2] : inVpkPath).Replace('\\', '/');
            if (!visited.Add(visitKey)) continue;

            try
            {
                package.ReadEntry(entry, out byte[] data);
                using var ms = new MemoryStream(data);
                using var resource = new Resource { FileName = inVpkPath };
                resource.Read(ms);

                var modelFolder = ComputeModelFolder(outputRoot, inVpkPath, noModelFolder);
                Directory.CreateDirectory(modelFolder);

                if (verbose) Console.WriteLine($"[{i + 1}/{targets.Count}] {inVpkPath}");
                var includes = ProcessOneModel(
                    resource, modelFolder, fileLoader, rawReader,
                    noRawDeps, noSanitize, verbose);

                if (!noIncludeDeps)
                    foreach (var inc in includes)
                        if (visited.Add(inc)) includeQueue.Enqueue(inc);

                ok++;
                if (!verbose && i % 25 == 0) Console.WriteLine($"  ... {i + 1}/{targets.Count}");
            }
            catch (Exception ex)
            {
                fail++;
                failures.Add((inVpkPath, ex.Message));
                if (verbose) Console.Error.WriteLine($"  ERROR: {inVpkPath}: {ex.Message}");
            }
        }

        if (!noIncludeDeps && includeQueue.Count > 0)
        {
            Console.WriteLine($"[vpk] Recursively decompiling {includeQueue.Count} include-dep model(s)...");
            int idx = 0;
            while (includeQueue.Count > 0)
            {
                idx++;
                var inc = includeQueue.Dequeue();
                var compiledInc = inc + "_c";
                var entry = package.FindEntry(compiledInc);
                if (entry == null)
                {
                    if (verbose) Console.Error.WriteLine($"  include-dep miss in vpk: {compiledInc}");
                    continue;
                }
                try
                {
                    package.ReadEntry(entry, out byte[] data);
                    using var ms = new MemoryStream(data);
                    using var resource = new Resource { FileName = compiledInc };
                    resource.Read(ms);

                    var modelFolder = ComputeModelFolder(outputRoot, compiledInc, noModelFolder);
                    Directory.CreateDirectory(modelFolder);

                    if (verbose) Console.WriteLine($"[inc {idx}] {compiledInc}");
                    var more = ProcessOneModel(
                        resource, modelFolder, fileLoader, rawReader,
                        noRawDeps, noSanitize, verbose);

                    foreach (var nxt in more)
                        if (visited.Add(nxt)) includeQueue.Enqueue(nxt);

                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    failures.Add((compiledInc, ex.Message));
                    if (verbose) Console.Error.WriteLine($"  ERROR: {compiledInc}: {ex.Message}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"[vpk] Extracted: {ok}, failed: {fail}");
        Console.WriteLine($"[vpk] Output: {Path.GetFullPath(outputRoot)}");
        ReportFailures(failures);
        return fail == 0 ? 0 : 1;
    }

    // ───────────────────────────── Folder/File mode ─────────────────────────────

    private static int RunFolderMode(
        string input, string outputRoot,
        HashSet<string> srcExts, string? gameRootArg,
        bool isSingleFile, bool noModelFolder, bool noRawDeps,
        bool noSanitize, bool noIncludeDeps, bool verbose)
    {
        var gameRoot = Path.GetFullPath(
            gameRootArg ?? (isSingleFile ? Path.GetDirectoryName(input)! : input));

        Console.WriteLine($"[folder] Game root: {gameRoot}");

        var targets = new List<string>();
        if (isSingleFile)
        {
            targets.Add(Path.GetFullPath(input));
        }
        else
        {
            foreach (var ext in srcExts)
                targets.AddRange(Directory.EnumerateFiles(input, "*" + ext, SearchOption.AllDirectories));
            targets = targets.Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        Console.WriteLine($"[folder] Models to extract: {targets.Count}");
        if (targets.Count == 0) return 0;

        using var fileLoader = new FolderFileLoader(gameRoot);
        RawReader rawReader = compiledPath => fileLoader.TryReadRawCompiled(compiledPath);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var includeQueue = new Queue<string>();
        int ok = 0, fail = 0;
        var failures = new List<(string, string)>();

        for (int i = 0; i < targets.Count; i++)
        {
            var fullPath = targets[i];
            try
            {
                var rel = Path.GetRelativePath(gameRoot, fullPath).Replace('\\', '/');
                var visitKey = rel.EndsWith("_c", StringComparison.OrdinalIgnoreCase) ? rel[..^2] : rel;
                if (!visited.Add(visitKey)) continue;

                using var resource = new Resource { FileName = rel };
                resource.Read(fullPath);

                var modelFolder = ComputeModelFolder(outputRoot, rel, noModelFolder);
                Directory.CreateDirectory(modelFolder);

                if (verbose) Console.WriteLine($"[{i + 1}/{targets.Count}] {rel}");
                var includes = ProcessOneModel(
                    resource, modelFolder, fileLoader, rawReader,
                    noRawDeps, noSanitize, verbose);

                if (!noIncludeDeps)
                    foreach (var inc in includes)
                        if (visited.Add(inc)) includeQueue.Enqueue(inc);

                ok++;
                if (!verbose && i % 25 == 0) Console.WriteLine($"  ... {i + 1}/{targets.Count}");
            }
            catch (Exception ex)
            {
                fail++;
                failures.Add((fullPath, ex.Message));
                if (verbose) Console.Error.WriteLine($"  ERROR: {fullPath}: {ex.Message}");
            }
        }

        if (!noIncludeDeps && includeQueue.Count > 0)
        {
            Console.WriteLine($"[folder] Recursively decompiling {includeQueue.Count} include-dep model(s)...");
            int idx = 0;
            while (includeQueue.Count > 0)
            {
                idx++;
                var inc = includeQueue.Dequeue();
                var compiledInc = inc + "_c";
                var bytes = rawReader(compiledInc);
                if (bytes == null)
                {
                    if (verbose) Console.Error.WriteLine($"  include-dep miss on disk: {compiledInc}");
                    continue;
                }
                try
                {
                    using var ms = new MemoryStream(bytes);
                    using var resource = new Resource { FileName = compiledInc };
                    resource.Read(ms);

                    var modelFolder = ComputeModelFolder(outputRoot, compiledInc, noModelFolder);
                    Directory.CreateDirectory(modelFolder);

                    if (verbose) Console.WriteLine($"[inc {idx}] {compiledInc}");
                    var more = ProcessOneModel(
                        resource, modelFolder, fileLoader, rawReader,
                        noRawDeps, noSanitize, verbose);

                    foreach (var nxt in more)
                        if (visited.Add(nxt)) includeQueue.Enqueue(nxt);

                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    failures.Add((compiledInc, ex.Message));
                    if (verbose) Console.Error.WriteLine($"  ERROR: {compiledInc}: {ex.Message}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"[folder] Extracted: {ok}, failed: {fail}");
        Console.WriteLine($"[folder] Output: {Path.GetFullPath(outputRoot)}");
        ReportFailures(failures);
        return fail == 0 ? 0 : 1;
    }

    // ───────────────────────────── Per-model pipeline ─────────────────────────────

    private sealed class MergeSummary
    {
        public int SanitizedEmptyLines;
        public int VmeshLoaded;
        public int VmorfLoaded;
        public int HitboxesMerged;
        public int AttachmentsMerged;
        public int FlexControllersMerged;
        public int SyntheticSkeletonBones;
        public int AdditionalPhysShapes;
        public int AdditionalPhysFiles;
        public int SanitizedInvalidNodes;
        public int SanitizedBrokenAutoLayers;
        public int SanitizedBogusResourceTags;
        public int FixedBodyGroupChoiceNames;
        public int StrippedUnknownBoneRefs;
        public int StubMaterialsCreated;
        public int PatchedDmxFiles;
        public int PatchedDmxWeights;
        public int PatchedDmxSkeletons;
        public int PatchedDmxJoints;
        public int ActivityModifiersInjected;
        public int FrameRatesInjected;
        public int IncludeDepsQueued;
        public int DisabledNonWhitelistModSequences;
        public int MaterialGroupListInjected;
    }

    internal static List<string> ProcessOneModel(
        Resource resource, string modelFolder,
        IFileLoader fileLoader, RawReader rawReader,
        bool noRawDeps, bool noSanitize, bool verbose,
        string? outputBasename = null)
    {
        // outputBasename lets a caller rename just the top-level .vmdl (e.g. to drop an arcana's
        // content in as a hero's base model). It only affects this one file: mesh/anim/physics
        // dependency filenames are derived from the resource's own embedded data, not from this,
        // so they keep resolving correctly next to it either way.
        var modelBasename = outputBasename ?? SanitizeBasename(resource.FileName);
        var summary = new MergeSummary();
        var writtenDmx = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (resource.DataBlock is Model model)
            MergeExternalMeshDataIntoModel(model, resource, fileLoader, summary, verbose);

        if (verbose && resource.DataBlock is Model dbgModel)
        {
            int embedded = 0, refsCount = 0;
            foreach (var _ in dbgModel.GetEmbeddedMeshes()) embedded++;
            foreach (var _ in dbgModel.GetReferenceMeshNamesAndLoD()) refsCount++;
            var blockSummary = string.Join(", ", resource.Blocks.Select(b => b.Type.ToString()));
            Console.WriteLine($"    .vmdl_c blocks: [{blockSummary}]");
            Console.WriteLine($"    embedded meshes: {embedded}, reference meshes: {refsCount}");
            Console.WriteLine($"    model.HitboxSets: {dbgModel.HitboxSets.Count} sets, " +
                              $"model.Attachments: {dbgModel.Attachments.Count} entries, " +
                              $"model.Skeleton.Bones: {dbgModel.Skeleton.Bones.Length} bones");
        }

        using var contentFile = FileExtract.Extract(resource, fileLoader);

        var vmdlExt = FileExtract.GetExtension(resource) ?? "vmdl";
        var vmdlPath = Path.Combine(modelFolder, $"{modelBasename}.{vmdlExt}");

        var baseVmdlText = contentFile.Data != null
            ? Encoding.UTF8.GetString(contentFile.Data)
            : string.Empty;

        if (resource.DataBlock is Model modelForPhys && !string.IsNullOrEmpty(baseVmdlText))
        {
            baseVmdlText = MergeAdditionalPhysics(
                modelForPhys, modelFolder, fileLoader, baseVmdlText,
                writtenDmx, summary, verbose);
        }

        if (!noSanitize && !string.IsNullOrEmpty(baseVmdlText))
            baseVmdlText = SanitizeVmdlText(baseVmdlText, resource, summary, verbose);

        // VRF в `19.1.6199` читает только первый элемент `m_activityArray`
        // (только m_name + m_nWeight) и пишет AnimFile.activity_name/activity_weight.
        // Но в Source 2 каждая sequence может иметь МОДИФИКАТОР: второй элемент с
        // именем без префикса `ACT_` (e.g. "desolation", "injured"). Без него ModelDoc
        // не различает базовую анимацию и item/state-альтернативы — рандомит между
        // run_anim / run_desolation / run_injured при ACT_DOTA_RUN. → SF дёргается,
        // запускает анимации десолятор-бега и low-HP-idle без оснований.
        // (legacy) Child-узел inject — оставлен только для отладки (--activity-modifier-inject).
        if (!noSanitize && !SkipActivityModInject && !string.IsNullOrEmpty(baseVmdlText))
            baseVmdlText = EnrichVmdlActivityModifiers(baseVmdlText, resource, summary, verbose);
        else if (SkipActivityModInject && verbose)
            Console.WriteLine($"    ⊘ activity-modifier child-node inject SKIPPED (legacy, off by default)");

        // ✓ ROOT-FIX: Valve-canonical field-level inject модификаторов
        // (поле `activity_modifiers = [ ... ]` на уровне AnimFile).
        // Это то, что делает ModelDoc UI — расшифровано из строк modeldoc_editor.dll.
        // ModelDoc compile корректно записывает m_activityArray в ASEQ итогового
        // .vmdl_c. Параллельно: ставим delta=true для legacy-delta sequence.
        // ✓ ROOT-FIX: VRF doesn't reconstruct MaterialGroupList from compiled
        // m_materialGroups, causing skin/persona variants (e.g. axe Fall20,
        // sven_calavera, lina arcanas, pudge_cute calavera) to be lost on
        // re-compile. Inject a faithful MaterialGroupList node here using
        // the compiled .vmdl_c data as ground truth.
        if (!noSanitize && !string.IsNullOrEmpty(baseVmdlText))
            baseVmdlText = InjectMaterialGroupList(baseVmdlText, resource, summary, verbose);

        if (!noSanitize && !SkipFieldLevelModifierInject && !string.IsNullOrEmpty(baseVmdlText))
            baseVmdlText = InjectActivityModifierFields(baseVmdlText, resource, summary, verbose);
        else if (SkipFieldLevelModifierInject && verbose)
            Console.WriteLine($"    ⊘ field-level activity_modifiers inject SKIPPED (--no-field-mod-inject)");

        // (DEPRECATED-style костыль) Disable sequences с не-whitelisted модификаторами.
        // ВКЛЮЧАЕТСЯ ТОЛЬКО если field-level inject выключен — это резервная стратегия.
        // Когда field-level inject работает, движок сам корректно фильтрует кандидатов
        // через m_activityArray, и pruning не нужен.
        if (!noSanitize && SkipFieldLevelModifierInject && !SkipDisableNonWhitelistedMods && !string.IsNullOrEmpty(baseVmdlText))
        {
            var whitelist = LoadActivityModifierWhitelist(rawReader, verbose);
            baseVmdlText = DisableNonWhitelistedModifierSequences(
                baseVmdlText, resource, whitelist, summary, verbose);
        }
        else if (!SkipFieldLevelModifierInject && verbose)
            Console.WriteLine($"    ⊘ non-whitelisted modifier prune SKIPPED (field-level inject is active)");
        else if (SkipDisableNonWhitelistedMods && verbose)
            Console.WriteLine($"    ⊘ non-whitelisted modifier prune SKIPPED (--no-prune-modifiers)");

        // ✓ ROOT-FIX: VRF skips animation entries for "@@" autolayer-compressed and
        // "@*lookFrame*" pose anims when generating .vmdl source — even though it
        // does extract their .dmx files. ModelDoc compile then produces a .vmdl_c
        // with fewer embedded animations than Valve original (e.g. shadow_fiend_arcana:
        // missing 13 anims including all `@@run_*` and `@turns_arcana_lookFrame_*`).
        // Restore them by enumerating Valve's m_anims and appending AnimFile nodes
        // for any name not already present in the source.
        if (!noSanitize && !string.IsNullOrEmpty(baseVmdlText))
            baseVmdlText = InjectMissingAnimations(baseVmdlText, resource, summary, verbose);

        // ⚠ КРИТИЧНО: VRF не пишет `framerate`/`start_frame`/`end_frame` в AnimFile.
        // ModelDoc compile fallback'ится на default fps (~30) и считает frame count
        // по embedded .dmx duration. У многих анимаций fps нестандартный (33, 37,
        // даже 0.2 для versus_attack). Без явного framerate анимация ускоряется/
        // замедляется в игре — типичный кейс: SF run_alt_desolation_anim, run_haste_*,
        // run_fast_*. Извлекаем реальные fps/frameCount из VRF-Animation API и
        // явно прописываем в .vmdl.
        if (!noSanitize && !SkipFramerateInject && !string.IsNullOrEmpty(baseVmdlText) && resource.DataBlock is Model fpsModel)
            baseVmdlText = InjectAnimFrameRates(baseVmdlText, fpsModel, fileLoader, summary, verbose);
        else if (SkipFramerateInject && verbose)
            Console.WriteLine($"    ⊘ framerate inject SKIPPED (--no-framerate-inject)");

        // ✓ ROOT-FIX: ExtractMotion sanitize. VRF при decompile добавляет
        //   ExtractMotion { extract_tx=true, motion_type="uniform", ... }
        //   на КАЖДЫЙ AnimFile, что заставляет ModelDoc compile извлекать per-frame
        //   X-translation из root joint и писать m_movementArray=[33 entries]
        //   с реальными значениями. Engine видит motion → масштабирует cycle_rate
        //   относительно скорости героя → run играется в ~10x скорости.
        // Valve в оригинале НЕ имеет этих ExtractMotion на in-place locomotion,
        // поэтому m_movementArray=[{zeros}] и анимация играет в native fps.
        // Решение: занулить extract_t* флаги во всех ExtractMotion (compile тогда
        // не извлекает translation, m_movementArray остаётся пустым/zero).
        if (!noSanitize && !SkipExtractMotionSanitize && !string.IsNullOrEmpty(baseVmdlText))
            // Pass `resource` so we can derive per-anim motion profiles from
            // the source ANIM block — this preserves locomotion motion on
            // run/sprint/manta anims while still neutering it on in-place
            // anims (the original 10× speed bug fix).
            baseVmdlText = SanitizeExtractMotion(baseVmdlText, summary, verbose, resource);
        else if (SkipExtractMotionSanitize && verbose)
            Console.WriteLine($"    ⊘ ExtractMotion sanitize SKIPPED (--no-motion-sanitize)");

        // Bump KV3 schema header to modeldoc41 + inject ModelDoc default
        // fields that VRF doesn't emit but ModelDoc always writes back on
        // Save. Done unconditionally — no functional effect on compile, just
        // closes a cosmetic diff round-trip with ModelDoc UI.
        if (!string.IsNullOrEmpty(baseVmdlText))
        {
            baseVmdlText = UpgradeSchemaHeader(baseVmdlText);
            baseVmdlText = InjectModelDocDefaults(baseVmdlText);
        }

        // Defer the final vmdl write until DMX subfiles are extracted, so we can
        // detect "marker-only" DMX (no normals/texcoords) that segfault Workshop
        // Tools' resourcecompiler.exe and strip their RenderMeshFile refs.
        var markerDmxBasenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int subWritten = 0, subEmpty = 0;
        foreach (var sub in contentFile.SubFiles)
        {
            byte[]? subData = null;
            try { subData = sub.Extract?.Invoke(); }
            catch (Exception ex)
            {
                if (verbose) Console.Error.WriteLine($"    subfile '{sub.FileName}' threw: {ex.Message}");
            }
            if (subData == null || subData.Length == 0) { subEmpty++; continue; }

            var subPath = Path.Combine(modelFolder, Path.GetFileName(sub.FileName));
            File.WriteAllBytes(subPath, subData);
            writtenDmx.Add(Path.GetFileName(sub.FileName));
            subWritten++;
            if (verbose) Console.WriteLine($"    ↳ DMX  {Path.GetFileName(subPath)} ({subData.Length / 1024} KB)");

            if (!noSanitize && subPath.EndsWith(".dmx", StringComparison.OrdinalIgnoreCase))
            {
                // VRF 19.1.6199 не вызывает BuildDmeDagSkeleton в ConvertMeshToDatamodelMesh,
                // и сама BuildDmeDagSkeleton там тоже сломана (добавляет DmeModel в JointList).
                // Из-за этого DmeModel.JointList выходит = [DmeDag(mesh)] (1 элемент),
                // и blendindices$0 со значениями 0..N-1 (где N = bone count) ссылаются мимо
                // палитры → ModelDoc warning'ит "Invalid skinning bone index :: -1, valid range [0, 0]"
                // и клампит → стретчинг.
                //
                // Чиним: внедряем правильный DmeJoint per bone в DmeModel.JointList,
                // строим иерархию parent→children, оставляем mesh DmeDag в конце JointList.
                // Логика 1:1 повторяет master VRF BuildDmeDagSkeleton + ConvertMeshToDatamodelMesh.
                Skeleton? skeletonForPatch = (resource.DataBlock as Model)?.Skeleton;
                PatchDmxMeshBoneIndices(subPath, skeletonForPatch, summary, verbose);

                // Marker detection: VRF emits stripped-down DMX for "marker"
                // models (camera anchors, locator props) that lack normal/texcoord
                // streams. resourcecompiler.exe segfaults on such DMX. Drop both
                // the file and the corresponding RenderMeshFile from .vmdl.
                if (IsMarkerOnlyDmx(subPath))
                {
                    var basename = Path.GetFileNameWithoutExtension(subPath);
                    markerDmxBasenames.Add(basename);
                    try { File.Delete(subPath); } catch { }
                    if (verbose) Console.WriteLine($"    ⊘ DMX  {Path.GetFileName(subPath)} dropped (marker-only, no normals/texcoords)");
                }
            }
        }

        // Strip RenderMeshFile nodes referencing marker-only DMX files.
        if (markerDmxBasenames.Count > 0 && !string.IsNullOrEmpty(baseVmdlText))
        {
            baseVmdlText = StripMarkerRenderMeshFiles(baseVmdlText, markerDmxBasenames, out int strippedRmf);
            if (verbose && strippedRmf > 0)
                Console.WriteLine($"    ✓ stripped {strippedRmf} marker-only RenderMeshFile node(s) from .vmdl");
        }

        // Final write of .vmdl after marker-stripping.
        File.WriteAllBytes(vmdlPath, Encoding.UTF8.GetBytes(baseVmdlText));

        // Create stub .vmat files for material refs in DMX/vmdl that don't
        // exist in the source VPK. ModelDoc compile prints "Mesh referencing
        // missing material" warnings for these (often `heroes_staging/...`
        // dev/temp paths shipped accidentally). Stubs make compile log clean
        // without altering DMX content; users editing in ModelDoc UI can
        // remap them via MaterialGroupList.
        if (!noSanitize)
        {
            int stubCount = CreateStubMaterialsForMissingRefs(modelFolder, fileLoader, verbose);
            summary.StubMaterialsCreated = stubCount;
        }

        foreach (var add in contentFile.AdditionalFiles)
        {
            if (add.Data == null) continue;
            var addPath = Path.Combine(modelFolder, Path.GetFileName(add.FileName));
            File.WriteAllBytes(addPath, add.Data);
            if (verbose) Console.WriteLine($"    ↳ ADD  {Path.GetFileName(addPath)}");
        }

        if (verbose) Console.WriteLine($"    VRF pipeline subfiles: written={subWritten}, empty={subEmpty}");

        // ✓ ROOT-FIX: VRF's SubFile pipeline announces all m_anims but its Extract
        // callback returns null/empty for "@@" (autolayer-compressed) and "@*lookFrame*"
        // (multipose pose-frame) animations. Bypass the broken filter and call
        // ModelExtract.ToDmxAnim directly to recover their .dmx files. Without this,
        // ModelDoc compile cannot find source for the AnimFile nodes we inject below
        // and skips them — producing a .vmdl_c missing 13+ embedded animations.
        int recoveredAnims = ExtractMissingAnimDmx(resource, modelFolder, writtenDmx, verbose);
        if (verbose && recoveredAnims > 0)
            Console.WriteLine($"    ✓ recovered {recoveredAnims} animation .dmx file(s) skipped by VRF");

        if (!noRawDeps)
            ExtractDepsRecursive(resource, modelFolder, fileLoader, rawReader, writtenDmx, verbose);

        var includeRefs = new List<string>();
        if (resource.DataBlock is Model includeSrcModel)
        {
            foreach (var inc in GetAnimIncludeModelRefs(includeSrcModel))
                if (!string.IsNullOrEmpty(inc)) includeRefs.Add(inc);
            summary.IncludeDepsQueued = includeRefs.Count;
        }

        if (verbose) PrintMergeSummary(summary);
        return includeRefs;
    }

    // ───────────────────────────── External-mesh merge ─────────────────────────────

    private static void MergeExternalMeshDataIntoModel(
        Model model, Resource modelResource, IFileLoader fileLoader,
        MergeSummary summary, bool verbose)
    {
        bool needsHitboxes = model.HitboxSets == null || model.HitboxSets.Count == 0;
        bool needsAttachments = model.Attachments == null || model.Attachments.Count == 0;
        bool needsSkeleton = !HasNonEmptyModelSkeleton(model.Data);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        try
        {
            foreach (var r in model.GetReferenceMeshNamesAndLoD())
                if (!string.IsNullOrEmpty(r.MeshName) && seen.Add(r.MeshName))
                    candidates.Add(r.MeshName);
        }
        catch { }

        var rerl = modelResource.ExternalReferences;
        if (rerl != null)
        {
            foreach (var info in rerl.ResourceRefInfoList)
            {
                if (string.IsNullOrEmpty(info.Name)) continue;
                if (!info.Name.EndsWith(".vmesh", StringComparison.OrdinalIgnoreCase)) continue;
                if (seen.Add(info.Name)) candidates.Add(info.Name);
            }
        }

        if (candidates.Count == 0)
        {
            if (verbose) Console.WriteLine("    no external .vmesh refs to merge from");
            return;
        }

        Dictionary<string, Hitbox[]>? mergedHitboxes = null;
        Dictionary<string, Attachment>? mergedAttachments = null;
        KVObject? meshSkeletonForFallback = null;

        foreach (var name in candidates)
        {
            Resource? meshResource = null;
            try { meshResource = fileLoader.LoadFileCompiled(name); }
            catch (Exception ex)
            {
                if (verbose) Console.Error.WriteLine($"    LoadFileCompiled('{name}') threw: {ex.Message}");
                continue;
            }
            if (meshResource == null)
            {
                if (verbose) Console.Error.WriteLine($"    LoadFileCompiled('{name}') returned null");
                continue;
            }

            using (meshResource)
            {
                if (meshResource.DataBlock is not Mesh mesh)
                {
                    if (verbose) Console.Error.WriteLine(
                        $"    {name}: DataBlock is {meshResource.DataBlock?.GetType().Name ?? "null"}, not Mesh");
                    continue;
                }

                summary.VmeshLoaded++;

                bool morphLoadedHere = false;
                try
                {
                    var hadMorph = mesh.MorphData != null;
                    mesh.LoadExternalMorphData(fileLoader);
                    if (mesh.MorphData != null && !hadMorph)
                    {
                        morphLoadedHere = true;
                        summary.VmorfLoaded++;
                    }
                }
                catch (Exception ex)
                {
                    if (verbose) Console.Error.WriteLine($"    LoadExternalMorphData('{name}') threw: {ex.Message}");
                }

                try { model.SetExternalMeshData(mesh); } catch { }

                if (morphLoadedHere && summary.FlexControllersMerged == 0)
                {
                    try { summary.FlexControllersMerged = model.FlexControllers?.Length ?? 0; }
                    catch { }
                }

                if (needsHitboxes && mesh.HitboxSets != null && mesh.HitboxSets.Count > 0)
                {
                    mergedHitboxes ??= new Dictionary<string, Hitbox[]>();
                    foreach (var kv in mesh.HitboxSets) mergedHitboxes[kv.Key] = kv.Value;
                }
                if (needsAttachments && mesh.Attachments != null && mesh.Attachments.Count > 0)
                {
                    mergedAttachments ??= new Dictionary<string, Attachment>();
                    foreach (var kv in mesh.Attachments) mergedAttachments[kv.Key] = kv.Value;
                }

                if (needsSkeleton && meshSkeletonForFallback == null)
                {
                    var meshSkel = TryGetSubCollection(mesh.Data, "m_skeleton");
                    if (meshSkel != null)
                    {
                        var bones = TryGetArray(meshSkel, "m_bones");
                        if (bones != null && bones.Count > 0)
                            meshSkeletonForFallback = meshSkel;
                    }
                }
            }
        }

        if (mergedHitboxes != null)
        {
            SetPrivateProperty(model, nameof(Model.HitboxSets), mergedHitboxes);
            summary.HitboxesMerged = mergedHitboxes.Values.Sum(a => a.Length);
            if (verbose)
                Console.WriteLine(
                    $"    ✓ merged HitboxSets: {mergedHitboxes.Count} set(s), " +
                    $"{summary.HitboxesMerged} hitbox(es) total");
        }
        if (mergedAttachments != null)
        {
            SetPrivateProperty(model, nameof(Model.Attachments), mergedAttachments);
            summary.AttachmentsMerged = mergedAttachments.Count;
            if (verbose)
                Console.WriteLine($"    ✓ merged Attachments: {mergedAttachments.Count} entry/entries");
        }

        if (needsSkeleton && meshSkeletonForFallback != null)
        {
            try
            {
                var synth = BuildSyntheticModelSkeleton(meshSkeletonForFallback, verbose);
                if (synth != null)
                {
                    InjectModelSkeleton(model, synth);
                    summary.SyntheticSkeletonBones = synth.GetArray("m_boneName")?.Count ?? 0;
                    if (verbose)
                        Console.WriteLine(
                            $"    ✓ synthesized m_modelSkeleton from .vmesh_c: " +
                            $"{summary.SyntheticSkeletonBones} bone(s)");
                }
            }
            catch (Exception ex)
            {
                if (verbose) Console.Error.WriteLine($"    skeleton synthesis failed: {ex.Message}");
            }
        }
    }

    // ───────────────────────────── Skeleton synthesis ─────────────────────────────

    private static bool HasNonEmptyModelSkeleton(KVObject? modelData)
    {
        if (modelData == null) return false;
        if (!modelData.ContainsKey("m_modelSkeleton")) return false;
        var ms = TryGetSubCollection(modelData, "m_modelSkeleton");
        if (ms == null) return false;
        var names = TryGetArray<string>(ms, "m_boneName");
        return names != null && names.Length > 0;
    }

    private static KVObject? BuildSyntheticModelSkeleton(KVObject meshSkeletonKv, bool verbose)
    {
        var bonesArr = TryGetArray(meshSkeletonKv, "m_bones");
        if (bonesArr == null || bonesArr.Count == 0) return null;

        int n = bonesArr.Count;
        var names = new string[n];
        var parentNames = new string[n];
        var invBind = new Matrix4x4[n];

        for (int i = 0; i < n; i++)
        {
            var b = bonesArr[i];
            names[i] = SafeGetString(b, "m_boneName");
            parentNames[i] = SafeGetString(b, "m_parentName");

            float[]? pose = null;
            try { pose = b.GetFloatArray("m_invBindPose"); } catch { }
            if (pose == null || pose.Length < 12)
            {
                invBind[i] = Matrix4x4.Identity;
                continue;
            }
            invBind[i] = new Matrix4x4(
                pose[0], pose[4], pose[8], 0,
                pose[1], pose[5], pose[9], 0,
                pose[2], pose[6], pose[10], 0,
                pose[3], pose[7], pose[11], 1);
        }

        var nameToIdx = new Dictionary<string, int>(n, StringComparer.Ordinal);
        for (int i = 0; i < n; i++) nameToIdx.TryAdd(names[i], i);

        var parents = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (string.IsNullOrEmpty(parentNames[i])) { parents[i] = -1; continue; }
            parents[i] = nameToIdx.TryGetValue(parentNames[i], out var pi) ? pi : -1;
        }

        var bindPose = new Matrix4x4[n];
        for (int i = 0; i < n; i++)
            if (!Matrix4x4.Invert(invBind[i], out bindPose[i]))
                bindPose[i] = Matrix4x4.Identity;

        var localPos = new Vector3[n];
        var localRot = new Quaternion[n];
        for (int i = 0; i < n; i++)
        {
            var lx = parents[i] < 0 ? bindPose[i] : bindPose[i] * invBind[parents[i]];
            localPos[i] = lx.Translation;
            var q = Quaternion.CreateFromRotationMatrix(lx);
            localRot[i] = Quaternion.Normalize(q);
        }

        var skel = KVObject.Collection();

        var nameArr = KVObject.Array();
        for (int i = 0; i < n; i++) nameArr.Add(new KVObject(names[i] ?? string.Empty));
        skel["m_boneName"] = nameArr;

        var parentArr = KVObject.Array();
        for (int i = 0; i < n; i++) parentArr.Add(new KVObject(parents[i]));
        skel["m_nParent"] = parentArr;

        var flagArr = KVObject.Array();
        for (int i = 0; i < n; i++) flagArr.Add(new KVObject(0));
        skel["m_nFlag"] = flagArr;

        var posArr = KVObject.Array();
        for (int i = 0; i < n; i++)
        {
            var v = KVObject.Array();
            v.Add(new KVObject(localPos[i].X));
            v.Add(new KVObject(localPos[i].Y));
            v.Add(new KVObject(localPos[i].Z));
            posArr.Add(v);
        }
        skel["m_bonePosParent"] = posArr;

        var rotArr = KVObject.Array();
        for (int i = 0; i < n; i++)
        {
            var q = KVObject.Array();
            q.Add(new KVObject(localRot[i].X));
            q.Add(new KVObject(localRot[i].Y));
            q.Add(new KVObject(localRot[i].Z));
            q.Add(new KVObject(localRot[i].W));
            rotArr.Add(q);
        }
        skel["m_boneRotParent"] = rotArr;

        var lodCounts = KVObject.Array();
        lodCounts.Add(new KVObject(n));
        skel["m_nLODBoneCounts"] = lodCounts;

        return skel;
    }

    private static void InjectModelSkeleton(Model model, KVObject syntheticSkeleton)
    {
        model.Data["m_modelSkeleton"] = syntheticSkeleton;
        SetPrivateField(model, "cachedSkeleton", null);
    }

    // ───────────────────────────── Multi-phys merge ─────────────────────────────

    private static string MergeAdditionalPhysics(
        Model model, string modelFolder, IFileLoader fileLoader,
        string baseVmdlText, HashSet<string> writtenDmx,
        MergeSummary summary, bool verbose)
    {
        List<string> physRefs;
        try { physRefs = model.GetReferencedPhysNames()?.ToList() ?? []; }
        catch { return baseVmdlText; }
        if (physRefs.Count <= 1) return baseVmdlText;

        var auxChildrenChunks = new List<string>();

        for (int i = 1; i < physRefs.Count; i++)
        {
            var auxRef = physRefs[i];
            if (string.IsNullOrEmpty(auxRef)) continue;

            Resource? physResource = null;
            try { physResource = fileLoader.LoadFileCompiled(auxRef); }
            catch (Exception ex)
            {
                if (verbose) Console.Error.WriteLine($"    aux phys '{auxRef}': load threw: {ex.Message}");
                continue;
            }
            if (physResource == null)
            {
                if (verbose) Console.Error.WriteLine($"    aux phys '{auxRef}': load returned null");
                continue;
            }

            using (physResource)
            {
                if (physResource.DataBlock is not PhysAggregateData phys)
                {
                    if (verbose) Console.Error.WriteLine(
                        $"    aux phys '{auxRef}': DataBlock is {physResource.DataBlock?.GetType().Name ?? "null"}");
                    continue;
                }

                var auxName = Path.GetFileName(auxRef);
                ContentFile? auxContent = null;
                try
                {
                    var auxExtract = new ModelExtract(phys, auxName);
                    auxContent = auxExtract.ToContentFile();
                }
                catch (Exception ex)
                {
                    if (verbose) Console.Error.WriteLine($"    aux phys '{auxRef}': ModelExtract failed: {ex.Message}");
                    continue;
                }

                using (auxContent)
                {
                    summary.AdditionalPhysFiles++;

                    if (auxContent.Data != null)
                    {
                        var auxText = Encoding.UTF8.GetString(auxContent.Data);
                        var children = ExtractClassChildrenContent(auxText, "PhysicsShapeList");
                        if (!string.IsNullOrEmpty(children))
                        {
                            auxChildrenChunks.Add(children);
                            summary.AdditionalPhysShapes += CountTopLevelObjects(children);
                            if (verbose) Console.WriteLine($"    ✓ aux phys '{auxRef}': merged shapes");
                        }
                        else if (verbose)
                        {
                            Console.WriteLine($"    aux phys '{auxRef}': no PhysicsShapeList in aux .vmdl");
                        }
                    }

                    foreach (var sf in auxContent.SubFiles)
                    {
                        try
                        {
                            var data = sf.Extract?.Invoke();
                            if (data == null || data.Length == 0) continue;
                            var name = Path.GetFileName(sf.FileName);
                            if (writtenDmx.Contains(name)) continue;
                            File.WriteAllBytes(Path.Combine(modelFolder, name), data);
                            writtenDmx.Add(name);
                            if (verbose) Console.WriteLine($"    ↳ DMX  {name} ({data.Length / 1024} KB) (aux phys)");
                        }
                        catch { }
                    }
                }
            }
        }

        if (auxChildrenChunks.Count == 0) return baseVmdlText;

        var combined = string.Concat(auxChildrenChunks);
        return InjectIntoClassChildren(baseVmdlText, "PhysicsShapeList", combined, verbose);
    }

    // ───────────────────────────── KV3 text splicing ─────────────────────────────

    private static string ExtractClassChildrenContent(string text, string className)
    {
        var span = FindClassChildrenSpan(text, className);
        if (span is not (int openIdx, int closeIdx)) return string.Empty;
        return text.Substring(openIdx + 1, closeIdx - openIdx - 1);
    }

    private static string InjectIntoClassChildren(string text, string className, string contentToInject, bool verbose)
    {
        var span = FindClassChildrenSpan(text, className);
        if (span is null)
        {
            if (verbose) Console.Error.WriteLine($"    inject: '{className}' not found in base .vmdl, skipping splice");
            return text;
        }
        var (_, closeIdx) = span.Value;
        return text.Substring(0, closeIdx) + contentToInject + text.Substring(closeIdx);
    }

    private static (int OpenBracketIdx, int CloseBracketIdx)? FindClassChildrenSpan(string text, string className)
    {
        var classMarker = $"_class = \"{className}\"";
        var classIdx = text.IndexOf(classMarker, StringComparison.Ordinal);
        if (classIdx < 0) return null;

        var childrenIdx = text.IndexOf("children", classIdx, StringComparison.Ordinal);
        if (childrenIdx < 0) return null;

        var openBracket = text.IndexOf('[', childrenIdx);
        if (openBracket < 0) return null;

        int depth = 1, i = openBracket + 1;
        bool inString = false;
        while (i < text.Length)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '"' && text[i - 1] != '\\') inString = false;
            }
            else if (c == '"') inString = true;
            else if (c == '[' || c == '{') depth++;
            else if (c == ']' || c == '}')
            {
                depth--;
                if (depth == 0) return (openBracket, i);
            }
            i++;
        }
        return null;
    }

    private static int CountTopLevelObjects(string childrenContent)
    {
        int depth = 0, count = 0;
        bool inString = false;
        for (int i = 0; i < childrenContent.Length; i++)
        {
            char c = childrenContent[i];
            if (inString)
            {
                if (c == '"' && (i == 0 || childrenContent[i - 1] != '\\')) inString = false;
            }
            else if (c == '"') inString = true;
            else if (c == '{')
            {
                if (depth == 0) count++;
                depth++;
            }
            else if (c == '[') depth++;
            else if (c == '}' || c == ']') depth--;
        }
        return count;
    }

    // ───────────────────────────── Sanitizer (v5.3) ─────────────────────────────

    /// <summary>
    /// Нейтрализует ExtractMotion-узлы в .vmdl: ставит все extract_t*=false и
    /// motion_type="none". Это лечит баг decompile→compile, при котором
    /// VRF добавляет ExtractMotion с extract_tx=true на каждый AnimFile,
    /// и ModelDoc compile извлекает per-frame X-translation root joint'а в
    /// m_movementArray. Engine тогда масштабирует cycle_rate относительно
    /// скорости персонажа → run-анимация играет в ~10x.
    /// 
    /// После sanitize: m_movementArray=[{zeros}] или [] — engine не масштабирует
    /// cycle_rate, character перемещается через locomotion (внешний motion).
    /// </summary>
    /// <summary>
    /// Per-animation motion-extraction profile derived from the source
    /// .vmdl_c's m_movementArray. True components mean "ModelDoc compile
    /// SHOULD extract this axis from root-joint translation/rotation when
    /// rebuilding". Default (all false) = in-place animation, no extraction.
    /// </summary>
    private record struct MotionProfile(bool Tx, bool Ty, bool Tz, bool Rz)
    {
        public bool HasAnyMotion => Tx || Ty || Tz || Rz;
    }

    /// <summary>
    /// Smart ExtractMotion sanitizer. When <paramref name="sourceResource"/>
    /// is provided we read m_movementArray from the original .vmdl_c we're
    /// decompiling and set per-anim extract_tx/ty/tz/rz flags so:
    ///   * In-place anims (idle, channel)            → all flags = false
    ///   * Locomotion anims (run, sprint, manta_*)   → flags reflect Valve's motion
    /// This is the "right" fix for the 10× speed bug AND the locomotion-detach
    /// bug at once: ModelDoc compile sees the correct per-anim flags and
    /// produces a m_movementArray that matches Valve's binary output, so
    /// no post-compile patching is needed for the standard ModelDoc
    /// "Save & Build" workflow.
    ///
    /// When no source resource is available (e.g. extracting a stand-alone
    /// .vmdl_c file with no donor context) we fall back to the legacy
    /// blanket-zero behaviour, which still fixes the original 10× bug at the
    /// cost of zeroing legitimate locomotion motion. In that mode users must
    /// run --fix-motion (or --build) afterwards to restore real values.
    /// </summary>
    private static string SanitizeExtractMotion(
        string vmdlText, MergeSummary summary, bool verbose,
        ValveResourceFormat.Resource? sourceResource = null)
    {
        var motionMap = sourceResource != null
            ? BuildMotionProfileFromResource(sourceResource, verbose)
            : new Dictionary<string, MotionProfile>();

        if (motionMap.Count == 0)
            return LegacySanitizeExtractMotion(vmdlText, summary, verbose);

        return SmartSanitizeExtractMotion(vmdlText, motionMap, summary, verbose);
    }

    /// <summary>
    /// Blanket-zero sanitizer (legacy): forces every extract_t*=false and
    /// PascalCases motion_type. Used as fallback when we have no source
    /// resource to derive per-anim profiles from.
    /// </summary>
    private static string LegacySanitizeExtractMotion(string vmdlText, MergeSummary summary, bool verbose)
    {
        var rx = new System.Text.RegularExpressions.Regex(
            @"\{\s*_class\s*=\s*""ExtractMotion""[^}]*\}",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        int patched = 0;
        var result = rx.Replace(vmdlText, m =>
        {
            var node = m.Value;
            var orig = node;
            node = System.Text.RegularExpressions.Regex.Replace(node, @"extract_tx\s*=\s*true", "extract_tx = false");
            node = System.Text.RegularExpressions.Regex.Replace(node, @"extract_ty\s*=\s*true", "extract_ty = false");
            node = System.Text.RegularExpressions.Regex.Replace(node, @"extract_tz\s*=\s*true", "extract_tz = false");
            node = System.Text.RegularExpressions.Regex.Replace(node, @"extract_rz\s*=\s*true", "extract_rz = false");
            node = SetMotionTypePascalCase(node);
            if (node != orig) patched++;
            return node;
        });

        if (verbose && patched > 0)
            Console.WriteLine($"    ✓ ExtractMotion sanitized (legacy blanket-zero): {patched} node(s)");

        return result;
    }

    /// <summary>
    /// Smart sanitizer: walks every AnimFile block, finds its enclosing
    /// ExtractMotion node, and sets extract_t*/_rz per the donor map. Anims
    /// not present in the map default to all-false (safe for custom user
    /// sequences that wouldn't have engine-driven locomotion anyway).
    /// </summary>
    private static string SmartSanitizeExtractMotion(
        string vmdlText, Dictionary<string, MotionProfile> motionMap,
        MergeSummary summary, bool verbose)
    {
        var animFileRx = new System.Text.RegularExpressions.Regex(
            @"\A\s*\{\s*_class\s*=\s*""AnimFile""");
        var extractMotionRx = new System.Text.RegularExpressions.Regex(
            @"\A\s*\{\s*_class\s*=\s*""ExtractMotion""");
        var nameRx = new System.Text.RegularExpressions.Regex(
            @"^[ \t]*name\s*=\s*""([^""]+)""", System.Text.RegularExpressions.RegexOptions.Multiline);

        // Pass 1: classify every balanced brace pair as AnimFile / ExtractMotion / other.
        var animFiles = new List<(int Start, int End, string Name)>();
        var ems = new List<(int Start, int Len, string Body)>();
        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            int len = bEnd - bStart + 1;
            // Cheap header probe — only inspect the first 80 chars to decide _class.
            var headLen = Math.Min(80, len);
            var head = vmdlText.Substring(bStart, headLen);

            if (animFileRx.IsMatch(head))
            {
                var block = vmdlText.Substring(bStart, len);
                var nameM = nameRx.Match(block);
                if (nameM.Success)
                    animFiles.Add((bStart, bEnd, nameM.Groups[1].Value));
            }
            else if (extractMotionRx.IsMatch(head))
            {
                var body = vmdlText.Substring(bStart, len);
                ems.Add((bStart, len, body));
            }
        }

        // Pass 2: bind each ExtractMotion to its smallest enclosing AnimFile,
        // collect a list of edits indexed by absolute offset.
        var edits = new List<(int Start, int Len, string OldBody, string NewBody, MotionProfile P, bool ProfileFound)>();
        int patchedTrue = 0, patchedFalse = 0, missingProfile = 0, unchanged = 0;
        foreach (var em in ems)
        {
            string? animName = null;
            int bestSpan = int.MaxValue;
            foreach (var af in animFiles)
            {
                if (af.Start <= em.Start && em.Start + em.Len - 1 <= af.End)
                {
                    int span = af.End - af.Start;
                    if (span < bestSpan) { bestSpan = span; animName = af.Name; }
                }
            }
            if (animName == null) continue;

            // Lookup priority:
            //   1. Exact name (e.g. ".vmdl AnimFile.name = '@aw_run'" → "@aw_run").
            //   2. Stripped of leading '@' / '@@' prefix in case the source
            //      name carries the autolayer marker but the AnimFile in
            //      .vmdl uses the bare form.
            //   3. With added '@' / '@@' prefix in case the AnimFile uses
            //      the bare user-facing sequence name but Valve's ANIM block
            //      stores the autolayer-compressed variant. This is the
            //      common case for hero locomotion: .vmdl has both
            //      `@aw_run` (raw anim) and `aw_run` (sequence wrapper);
            //      the donor map only contains `@aw_run` because the
            //      m_movementArray sits there. We propagate the profile
            //      to the sequence wrapper so ModelDoc compile sets the
            //      same extract_t* on both.
            bool found = motionMap.TryGetValue(animName, out var profile);
            if (!found)
            {
                string[] candidates =
                {
                    "@" + animName,
                    "@@" + animName,
                    animName.TrimStart('@'),
                };
                foreach (var c in candidates)
                {
                    if (motionMap.TryGetValue(c, out var p2) && p2.HasAnyMotion)
                    {
                        profile = p2;
                        found = true;
                        break;
                    }
                }
            }
            if (!found) missingProfile++;

            var newBody = ApplyExtractFlags(em.Body, profile);
            if (newBody == em.Body) { unchanged++; continue; }

            edits.Add((em.Start, em.Len, em.Body, newBody, profile, found));
        }

        // Pass 3: apply in REVERSE position order so earlier edits don't
        // invalidate offsets of later ones.
        edits.Sort((a, b) => b.Start.CompareTo(a.Start));
        var sb = new System.Text.StringBuilder(vmdlText);
        foreach (var e in edits)
        {
            sb.Remove(e.Start, e.Len);
            sb.Insert(e.Start, e.NewBody);
            if (e.P.HasAnyMotion) patchedTrue++; else patchedFalse++;
        }

        if (verbose)
            Console.WriteLine($"    ✓ ExtractMotion smart-sanitized: {patchedTrue} with motion, " +
                $"{patchedFalse} in-place, {unchanged} already-correct, {missingProfile} unknown anims (defaulted to in-place)");

        return sb.ToString();
    }

    /// <summary>
    /// Apply <paramref name="p"/>'s flags onto an ExtractMotion node body
    /// while preserving everything else (motion_type capitalisation, custom
    /// fields, formatting). If a flag line is missing entirely it's left
    /// alone — ModelDoc will fall back to its own default on Save.
    /// </summary>
    private static string ApplyExtractFlags(string emBody, MotionProfile p)
    {
        string Lit(bool b) => b ? "true" : "false";
        emBody = System.Text.RegularExpressions.Regex.Replace(emBody, @"extract_tx\s*=\s*(?:true|false)", $"extract_tx = {Lit(p.Tx)}");
        emBody = System.Text.RegularExpressions.Regex.Replace(emBody, @"extract_ty\s*=\s*(?:true|false)", $"extract_ty = {Lit(p.Ty)}");
        emBody = System.Text.RegularExpressions.Regex.Replace(emBody, @"extract_tz\s*=\s*(?:true|false)", $"extract_tz = {Lit(p.Tz)}");
        emBody = System.Text.RegularExpressions.Regex.Replace(emBody, @"extract_rz\s*=\s*(?:true|false)", $"extract_rz = {Lit(p.Rz)}");
        emBody = SetMotionTypePascalCase(emBody);
        return emBody;
    }

    private static string SetMotionTypePascalCase(string node)
    {
        return System.Text.RegularExpressions.Regex.Replace(node,
            @"motion_type\s*=\s*""(uniform|linear|quadratic|none)""",
            m =>
            {
                var v = m.Groups[1].Value;
                // modeldoc41 doesn't ship a "None" variant; remap to "Uniform"
                // (no-op semantically when extract_t*=false anyway).
                var pascal = v.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? "Uniform"
                    : char.ToUpperInvariant(v[0]) + v.Substring(1);
                return $"motion_type = \"{pascal}\"";
            });
    }

    /// <summary>
    /// Read <c>m_movementArray</c> from the resource's ANIM block and
    /// summarise per-anim which motion components carry data. Threshold
    /// 1e-3 is conservative (Valve stores e.g. v0=452.55 for run, 0.00 for
    /// idle — never anywhere near 0.001).
    /// </summary>
    private static Dictionary<string, MotionProfile> BuildMotionProfileFromResource(
        ValveResourceFormat.Resource resource, bool verbose)
    {
        var map = new Dictionary<string, MotionProfile>(StringComparer.OrdinalIgnoreCase);
        var animBlock = resource.GetBlockByType(ValveResourceFormat.BlockType.ANIM);
        if (animBlock == null) return map;

        object? animDataObj = null;
        if (animBlock is ValveResourceFormat.ResourceTypes.BinaryKV3 bkv3) animDataObj = bkv3.Data;
        else if (animBlock is ValveResourceFormat.ResourceTypes.KeyValuesOrNTRO kvOrNtro) animDataObj = kvOrNtro.Data;
        if (animDataObj == null) return map;

        ValveKeyValue.KVObject? animData = animDataObj switch
        {
            ValveKeyValue.KVDocument doc => doc.Root,
            ValveKeyValue.KVObject kvo => kvo,
            _ => null,
        };
        if (animData == null || !animData.ContainsKey("m_animArray")) return map;

        const float kEps = 0.001f;
        foreach (var kvp in animData["m_animArray"])
        {
            var an = kvp.Value;
            var name = an["m_name"]?.ToString();
            if (string.IsNullOrEmpty(name)) continue;
            if (!an.ContainsKey("m_movementArray")) continue;
            var movArr = an["m_movementArray"];
            if (movArr == null || movArr.Count == 0) { map[name] = default; continue; }

            bool tx = false, ty = false, tz = false, rz = false;
            foreach (var mvKvp in movArr)
            {
                var mv = mvKvp.Value;
                float v0 = SafeFloat(mv, "v0");
                float v1 = SafeFloat(mv, "v1");
                float ang = SafeFloat(mv, "angle");
                if (Math.Abs(v0) > kEps) tx = true;
                if (Math.Abs(v1) > kEps) ty = true;
                if (Math.Abs(ang) > kEps) rz = true;
                // tz: pos[2] non-zero → vertical translation extracted.
                try
                {
                    if (mv.ContainsKey("position"))
                    {
                        var pos = mv["position"];
                        if (pos != null && pos.Count >= 3)
                        {
                            int i = 0;
                            foreach (var p in pos)
                            {
                                if (i == 2)
                                {
                                    try
                                    {
                                        var f = Convert.ToSingle(p.Value, System.Globalization.CultureInfo.InvariantCulture);
                                        if (Math.Abs(f) > kEps) tz = true;
                                    }
                                    catch { }
                                    break;
                                }
                                i++;
                            }
                        }
                    }
                }
                catch { /* missing fields tolerated */ }
            }
            map[name] = new MotionProfile(tx, ty, tz, rz);
        }

        if (verbose)
        {
            int withMotion = 0;
            foreach (var kv in map) if (kv.Value.HasAnyMotion) withMotion++;
            Console.WriteLine($"    [motion-profile] {map.Count} anim(s) read from source ANIM block, {withMotion} carry motion");
        }
        return map;
    }

    private static float SafeFloat(ValveKeyValue.KVObject obj, string key)
    {
        if (!obj.ContainsKey(key)) return 0f;
        try
        {
            var v = obj[key];
            if (v == null) return 0f;
            return Convert.ToSingle(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { return 0f; }
    }

    /// <summary>
    /// Inject the modeldoc41 default fields that ModelDoc itself writes back
    /// on Save & Compile but VRF omits during decompile. These are pure
    /// metadata — no semantic effect on the compiled .vmdl_c — but writing
    /// them up-front means our extracted source matches ModelDoc's canonical
    /// form, so opening the file in ModelDoc and saving produces a near-0
    /// line diff. Keeps round-trips reviewable.
    ///
    /// Coverage:
    ///   * `RootNode` trailing fields (model_archetype, primary_associated_entity,
    ///     anim_graph_name, document_sub_type).
    ///   * `BoneMarkupList.primary_root_bone`.
    ///   * `AnimFile` 9 schema defaults (is_default_idle_anim, weight_list_name,
    ///     anim_markup_ordered, disable_compression, animgraph_additive,
    ///     delete_from_compiled_model, import_bone_scales, reverse,
    ///     additional_anim_files) — closes ~870 of the 1100-line round-trip diff.
    ///   * `AnimEvent.event_end_frame` — MD writes -1 default per event.
    /// </summary>
    private static string InjectModelDocDefaults(string vmdlText)
    {
        vmdlText = InjectAnimFileSchemaDefaults(vmdlText);
        vmdlText = InjectAnimEventEndFrame(vmdlText);
        vmdlText = InjectParticleEventKeysDefaults(vmdlText);
        vmdlText = InjectExtractMotionDefaults(vmdlText);
        vmdlText = InjectRenderMeshFileImportFilter(vmdlText);
        vmdlText = InjectWeightListDefaults(vmdlText);
        vmdlText = InjectAnimationListDefaults(vmdlText);
        vmdlText = InjectHitboxDefaults(vmdlText);
        vmdlText = NormalizeFloatPrecision(vmdlText);
        vmdlText = StripEmptyBoneMarkupChildren(vmdlText);
        vmdlText = DisambiguateDuplicateHitboxNames(vmdlText);

        // 1. RootNode trailing defaults. The RootNode block ends with
        //    `]\r\n\t}\r\n}` (the inner `]` closes its `children` array,
        //    `\t}` closes RootNode, `}` closes the document). We insert the
        //    four metadata fields between the `]` and `\t}`.
        vmdlText = System.Text.RegularExpressions.Regex.Replace(
            vmdlText,
            @"(\r?\n\t\t\])(\r?\n\t\}\r?\n\}\s*)$",
            "$1\r\n\t\tmodel_archetype = \"\"\r\n\t\tprimary_associated_entity = \"\"\r\n\t\tanim_graph_name = \"\"\r\n\t\tdocument_sub_type = \"ModelDocSubType_None\"$2");

        // 2. BoneMarkupList primary_root_bone. Find any
        //    `_class = "BoneMarkupList"` block and add `primary_root_bone = ""`
        //    after `bone_cull_type` if not already present.
        vmdlText = System.Text.RegularExpressions.Regex.Replace(
            vmdlText,
            @"(\{\s*_class\s*=\s*""BoneMarkupList""[^}]*?bone_cull_type\s*=\s*""[^""]*"")(\s*\})",
            m =>
            {
                var head = m.Groups[1].Value;
                var tail = m.Groups[2].Value;
                if (head.Contains("primary_root_bone")) return m.Value;
                // Reuse the indentation style we see right before bone_cull_type.
                var indentMatch = System.Text.RegularExpressions.Regex.Match(
                    head, @"\r?\n([ \t]+)bone_cull_type");
                var indent = indentMatch.Success ? indentMatch.Groups[1].Value : "\t\t\t\t";
                return head + "\r\n" + indent + "primary_root_bone = \"\"" + tail;
            },
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return vmdlText;
    }

    /// <summary>
    /// Inject the 9 AnimFile schema-default fields that modeldoc41 emits but
    /// VRF omits. Each AnimFile node gets the missing fields appended right
    /// before its closing brace. Idempotent — skips fields already present.
    /// Closes ~870 lines of the round-trip diff with ModelDoc on a typical
    /// hero (e.g. SF arcana: 96 AnimFile nodes × up to 9 missing each).
    /// </summary>
    private static string InjectAnimFileSchemaDefaults(string vmdlText)
    {
        // Default values, ordered to match ModelDoc's canonical field order
        // when it Save-and-Compiles (see SF arcana sample at lines 217-233).
        // We append at end-of-node — order doesn't affect compile, just diff
        // legibility.
        var defaults = new (string Key, string Value)[]
        {
            ("is_default_idle_anim",        "false"),
            // ModelDoc writes activity_name/activity_weight on EVERY AnimFile,
            // even those without an explicit activity (default: empty + 1).
            // Already-set values are preserved by the per-key existence check.
            ("activity_name",               "\"\""),
            ("activity_weight",             "1"),
            ("weight_list_name",            "\"\""),
            ("anim_markup_ordered",         "false"),
            ("disable_compression",         "false"),
            ("animgraph_additive",          "false"),
            ("delete_from_compiled_model",  "false"),
            ("import_bone_scales",          "false"),
            ("reverse",                     "false"),
            ("additional_anim_files",       "[  ]"),
        };

        // Block matcher: AnimFile classes can have nested children (ActivityModifier),
        // so balanced-brace matching is required.
        var animFileClassRx = new System.Text.RegularExpressions.Regex(
            @"\A\{\s*_class\s*=\s*""AnimFile""", System.Text.RegularExpressions.RegexOptions.Compiled);

        var insertions = new List<(int Pos, string Text)>();
        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!animFileClassRx.IsMatch(block)) continue;

            // Determine the indent level used inside this AnimFile node by
            // grabbing whitespace before any existing top-level field (`name = `).
            var nameMatch = System.Text.RegularExpressions.Regex.Match(
                block, @"\r?\n([ \t]+)name\s*=\s*""");
            if (!nameMatch.Success) continue;
            var indent = nameMatch.Groups[1].Value;

            // Build only the missing fields.
            var sb = new StringBuilder();
            foreach (var (key, value) in defaults)
            {
                // Match key at top level of this block (not inside nested children).
                // We approximate: the indent on its own line. Children would have
                // deeper indentation — so a regex anchored to this exact indent is
                // sufficient.
                var keyRx = new System.Text.RegularExpressions.Regex(
                    @"^" + System.Text.RegularExpressions.Regex.Escape(indent) + System.Text.RegularExpressions.Regex.Escape(key) + @"\s*=",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                if (keyRx.IsMatch(block)) continue;
                sb.Append(indent).Append(key).Append(" = ").Append(value).Append("\r\n");
            }
            if (sb.Length == 0) continue;

            // Insert position: just before the closing `}` of the AnimFile block.
            // The closing `}` has indent = indent.Substring(0, indent.Length - 1)
            // (one tab less than children). Find the last `\n` then the `}` line.
            int relCloseBrace = block.LastIndexOf('}');
            if (relCloseBrace < 0) continue;
            // Walk back from `}` to start-of-line so we insert above it.
            int insertOffsetInBlock = relCloseBrace;
            while (insertOffsetInBlock > 0 && block[insertOffsetInBlock - 1] != '\n')
                insertOffsetInBlock--;

            insertions.Add((bStart + insertOffsetInBlock, sb.ToString()));
        }

        if (insertions.Count == 0) return vmdlText;

        // Apply insertions back-to-front so earlier offsets stay valid.
        insertions.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var output = new StringBuilder(vmdlText);
        foreach (var (pos, txt) in insertions) output.Insert(pos, txt);
        return output.ToString();
    }

    /// <summary>
    /// Inject `event_end_frame = -1` default into every AnimEvent node that
    /// doesn't already carry it. ModelDoc adds this field on Save (modeldoc41
    /// schema). -1 means "use start frame" — single-frame event.
    /// </summary>
    private static string InjectAnimEventEndFrame(string vmdlText)
    {
        var animEventRx = new System.Text.RegularExpressions.Regex(
            @"\A\{\s*_class\s*=\s*""AnimEvent""", System.Text.RegularExpressions.RegexOptions.Compiled);

        var insertions = new List<(int Pos, string Text)>();
        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!animEventRx.IsMatch(block)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(block, @"^[ \t]+event_end_frame\s*=", System.Text.RegularExpressions.RegexOptions.Multiline)) continue;

            // Match the indent of any existing top-level field (e.g. event_class).
            var anchorMatch = System.Text.RegularExpressions.Regex.Match(
                block, @"\r?\n([ \t]+)event_class\s*=");
            if (!anchorMatch.Success) continue;
            var indent = anchorMatch.Groups[1].Value;

            int relCloseBrace = block.LastIndexOf('}');
            if (relCloseBrace < 0) continue;
            int insertOffsetInBlock = relCloseBrace;
            while (insertOffsetInBlock > 0 && block[insertOffsetInBlock - 1] != '\n')
                insertOffsetInBlock--;

            insertions.Add((bStart + insertOffsetInBlock, indent + "event_end_frame = -1\r\n"));
        }

        if (insertions.Count == 0) return vmdlText;
        insertions.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var output = new StringBuilder(vmdlText);
        foreach (var (pos, txt) in insertions) output.Insert(pos, txt);
        return output.ToString();
    }

    /// <summary>
    /// Reconstruct the MaterialGroupList node inside the .vmdl source
    /// from the compiled .vmdl_c DATA block's <c>m_materialGroups</c>.
    ///
    /// Why VRF drops it: VRF's ModelExtract pipeline does not regenerate
    /// MaterialGroupList nodes from compiled material-group data. As a
    /// result, hero .vmdl_c files re-compiled from VRF-extracted .vmdl
    /// sources lose all skin/persona variant groups (e.g. axe Fall20 skin,
    /// pudge_cute calavera variant) and the default group entry.
    ///
    /// Format (verified via modeldoc_utils.dll string table — see
    /// classes <c>MaterialGroupList</c>, <c>DefaultMaterialGroup</c>,
    /// <c>MaterialGroup</c>, <c>BaseMaterialRemap</c>):
    ///
    /// <code>
    /// {
    ///     _class = "MaterialGroupList"
    ///     children = [
    ///         { _class = "DefaultMaterialGroup" name = "default" remaps = [ ] },
    ///         {
    ///             _class = "MaterialGroup"
    ///             name = "1"
    ///             remaps = [
    ///                 { _class = "BaseMaterialRemap"
    ///                   from = "materials/.../base.vmat"
    ///                   to   = "materials/.../skin.vmat" },
    ///                 ...
    ///             ]
    ///         },
    ///     ]
    /// }
    /// </code>
    ///
    /// Each variant-group remap pairs <c>m_materialGroups[0].m_materials[i]</c>
    /// (the default material) with <c>m_materialGroups[N].m_materials[i]</c>
    /// (the variant material), preserving array ordering.
    /// </summary>
    private static string InjectMaterialGroupList(string vmdlText, Resource resource, MergeSummary summary, bool verbose)
    {
        if (resource.GetBlockByType(BlockType.DATA) is not KeyValuesOrNTRO dataBlock) return vmdlText;
        if (dataBlock.Data is not KVObject data) return vmdlText;

        IReadOnlyList<KVObject>? groups;
        try { groups = data.GetArray("m_materialGroups"); }
        catch { return vmdlText; }
        if (groups == null || groups.Count == 0) return vmdlText;

        // Idempotency: skip if MaterialGroupList already present.
        if (vmdlText.Contains("\"MaterialGroupList\"", StringComparison.Ordinal))
            return vmdlText;

        // Read group [0] as the default reference list of materials.
        // Each variant group [N] is a parallel array of overrides at the
        // same indices.
        string defaultGroupName;
        string[]? defaultMaterials;
        try
        {
            defaultGroupName = groups[0].GetStringProperty("m_name") ?? "default";
            defaultMaterials = groups[0].GetArray<string>("m_materials");
        }
        catch { return vmdlText; }
        if (defaultMaterials == null) return vmdlText;

        var sb = new StringBuilder();
        // Tab-based indentation matches the rest of VRF-emitted .vmdl text.
        sb.Append("\t\t\t{\r\n");
        sb.Append("\t\t\t\t_class = \"MaterialGroupList\"\r\n");
        sb.Append("\t\t\t\tchildren = \r\n");
        sb.Append("\t\t\t\t[\r\n");

        // Default group (no remaps — engine pulls materials from the .vmesh).
        sb.Append("\t\t\t\t\t{\r\n");
        sb.Append("\t\t\t\t\t\t_class = \"DefaultMaterialGroup\"\r\n");
        sb.Append("\t\t\t\t\t\tname = \"").Append(EscapeKv3(defaultGroupName)).Append("\"\r\n");
        sb.Append("\t\t\t\t\t\tremaps = [  ]\r\n");
        sb.Append("\t\t\t\t\t},\r\n");

        // Variant groups: index 1..N — remap each default→variant material.
        for (int gi = 1; gi < groups.Count; gi++)
        {
            string vname;
            string[]? vmats;
            try
            {
                vname = groups[gi].GetStringProperty("m_name") ?? gi.ToString();
                vmats = groups[gi].GetArray<string>("m_materials");
            }
            catch { continue; }
            if (vmats == null || vmats.Length == 0) continue;

            sb.Append("\t\t\t\t\t{\r\n");
            sb.Append("\t\t\t\t\t\t_class = \"MaterialGroup\"\r\n");
            sb.Append("\t\t\t\t\t\tname = \"").Append(EscapeKv3(vname)).Append("\"\r\n");
            sb.Append("\t\t\t\t\t\tremaps = \r\n");
            sb.Append("\t\t\t\t\t\t[\r\n");

            int pairCount = Math.Min(defaultMaterials.Length, vmats.Length);
            for (int i = 0; i < pairCount; i++)
            {
                var fromMat = defaultMaterials[i] ?? "";
                var toMat = vmats[i] ?? "";
                // Skip identity remaps (some persona groups reuse some default
                // materials and only swap one — those don't generate a remap).
                if (string.Equals(fromMat, toMat, StringComparison.OrdinalIgnoreCase)) continue;

                sb.Append("\t\t\t\t\t\t\t{\r\n");
                sb.Append("\t\t\t\t\t\t\t\t_class = \"BaseMaterialRemap\"\r\n");
                sb.Append("\t\t\t\t\t\t\t\tfrom = \"").Append(EscapeKv3(fromMat)).Append("\"\r\n");
                sb.Append("\t\t\t\t\t\t\t\tto = \"").Append(EscapeKv3(toMat)).Append("\"\r\n");
                sb.Append("\t\t\t\t\t\t\t},\r\n");
            }
            sb.Append("\t\t\t\t\t\t]\r\n");
            sb.Append("\t\t\t\t\t},\r\n");
        }

        sb.Append("\t\t\t\t]\r\n");
        sb.Append("\t\t\t},\r\n");

        var injected = InjectIntoClassChildren(vmdlText, "RootNode", sb.ToString(), verbose);
        if (!ReferenceEquals(injected, vmdlText))
        {
            summary.MaterialGroupListInjected++;
            if (verbose)
                Console.WriteLine($"    ✓ injected MaterialGroupList ({groups.Count} group(s))");
        }
        return injected;
    }

    private static string EscapeKv3(string s)
    {
        // KV3 string-literal escaping: backslash and double-quote.
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Inject ExtractMotion modeldoc41 defaults: `extract_initial_offset = false`
    /// and `root_bone_name = ""`. Both are written by ModelDoc on every Save
    /// but VRF doesn't emit them. Adding here closes 40 diff lines on a typical
    /// hero (20 ExtractMotion nodes × 2 fields).
    /// </summary>
    private static string InjectExtractMotionDefaults(string vmdlText)
    {
        var defaults = new (string Key, string Value)[]
        {
            ("extract_initial_offset", "false"),
            ("root_bone_name",         "\"\""),
        };
        return InjectFieldsIntoNodes(vmdlText, "ExtractMotion", defaults, anchorField: "motion_type");
    }

    /// <summary>
    /// Inject WeightList modeldoc41 morph-related defaults: `master_morph_weight`
    /// and `morph_weights`. ModelDoc adds these on Save when the model has
    /// any morph data — VRF doesn't emit them. Also ensures `default_weight`
    /// is present (MD writes 0.0 when not specified).
    /// </summary>
    private static string InjectWeightListDefaults(string vmdlText)
    {
        var defaults = new (string Key, string Value)[]
        {
            ("default_weight",       "0.0"),
            ("master_morph_weight",  "0.0"),
            ("morph_weights",        "[  ]"),
        };
        return InjectFieldsIntoNodes(vmdlText, "WeightList", defaults, anchorField: "name");
    }

    /// <summary>
    /// Inject AnimationList default field: `default_root_bone_name = ""`.
    /// ModelDoc writes this top-level field on every AnimationList container
    /// in modeldoc41 schema. Single occurrence per file.
    /// </summary>
    private static string InjectAnimationListDefaults(string vmdlText)
    {
        var defaults = new (string Key, string Value)[]
        {
            ("default_root_bone_name", "\"\""),
        };
        // `children` is the only inner field aside from _class on AnimationList,
        // so it makes a reliable indent anchor.
        return InjectFieldsIntoNodes(vmdlText, "AnimationList", defaults, anchorField: "children");
    }

    /// <summary>
    /// Inject `surface_property = ""` default on Hitbox nodes. Most Hitboxes
    /// in our extraction already carry a non-empty surface_property from VRF;
    /// MD adds empty defaults to the few that lack it.
    /// </summary>
    private static string InjectHitboxDefaults(string vmdlText)
    {
        var defaults = new (string Key, string Value)[]
        {
            ("surface_property", "\"\""),
        };
        // Anchor on parent_bone since every Hitbox carries it; surface_property
        // appears between parent_bone and translation_only in MD canonical form.
        return InjectFieldsIntoNodes(vmdlText, "Hitbox", defaults, anchorField: "parent_bone");
    }

    /// <summary>
    /// Reformat float values inside Vector3-style arrays (`angles`, `origin`,
    /// `hitbox_maxs`, `hitbox_mins`) using .NET's default round-trip float
    /// formatting (G9-equivalent). VRF emits 6-decimal padded floats while
    /// ModelDoc uses shortest-roundtrip representation, producing benign but
    /// noisy diffs (e.g. `77.108414` vs `77.10841`). Reformatting through a
    /// float round-trip closes ~60 lines per typical hero with zero risk —
    /// the underlying float value stays bit-identical.
    /// </summary>
    private static string NormalizeFloatPrecision(string vmdlText)
    {
        // Match fields whose values are float Vector3 arrays. The negative
        // lookbehind on `_class` would be redundant — these field names never
        // collide with class names. Capture the leading key+`= [` and the
        // trailing `]` so we can reformat just the inner numbers.
        // Word boundary on the LEFT prevents matching e.g. `relative_angles`
        // or `relative_origin`, which use a different MD precision rule we
        // don't reformat (their values were already matching pre-extraction).
        var rx = new System.Text.RegularExpressions.Regex(
            @"\b(?<!relative_)(angles|origin|hitbox_maxs|hitbox_mins)(\s*=\s*\[\s*)([^\]]+?)(\s*\])",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        return rx.Replace(vmdlText, m =>
        {
            // Group 1 = key name; Group 2 = "= [ "; Group 3 = body; Group 4 = " ]".
            var key = m.Groups[1].Value;
            var sep = m.Groups[2].Value;
            var body = m.Groups[3].Value;
            var tail = m.Groups[4].Value;

            // Body is comma-separated float literals. Round-trip each via
            // float.Parse + ModelDoc-compatible formatting so the underlying
            // float value stays bit-identical but the textual representation
            // matches MD's canonical form. Anything that fails to parse is
            // passed through unchanged.
            var parts = body.Split(',');
            var newParts = new List<string>(parts.Length);
            foreach (var raw in parts)
            {
                var trimmed = raw.Trim();
                if (float.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var f))
                {
                    newParts.Add(FormatFloatLikeModelDoc(f));
                }
                else
                {
                    newParts.Add(trimmed);
                }
            }

            return key + sep + string.Join(", ", newParts) + tail;
        });
    }

    /// <summary>
    /// Disambiguate duplicate Hitbox `name` fields the way ModelDoc does on
    /// Save: when two or more `Hitbox` nodes inside the document share the
    /// same `name`, the first keeps its original name and each subsequent
    /// occurrence gets a numeric suffix (1, 2, ...). Bone names and other
    /// classes are untouched. ModelDoc-saved files always carry these unique
    /// hitbox names; without this pass our extracted file has bare duplicates
    /// and produces a 5-10 line cosmetic diff after Save.
    /// </summary>
    private static string DisambiguateDuplicateHitboxNames(string vmdlText)
    {
        // Find every line that's the `name` field of a Hitbox node and bucket
        // them by current name. We rely on the parent-class lookup pattern:
        // walk back at most ~6 lines from the `name = "..."` line to find
        // `_class = "Hitbox"`. Only those names are eligible for renaming.
        var hitboxNameRx = new System.Text.RegularExpressions.Regex(
            @"^([ \t]+)name\s*=\s*""([^""]+)""\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);
        var classRx = new System.Text.RegularExpressions.Regex(
            @"^[ \t]+_class\s*=\s*""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);

        var matches = hitboxNameRx.Matches(vmdlText);
        var hitboxOccurrences = new List<(int Pos, int Length, string OldName, string Indent)>();

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            // Look back up to 6 lines for the parent _class. 400 chars is
            // ample for any pretty-printed Hitbox or Bone preamble.
            int searchStart = Math.Max(0, m.Index - 400);
            var prefix = vmdlText.Substring(searchStart, m.Index - searchStart);
            var classMatches = classRx.Matches(prefix);
            if (classMatches.Count == 0) continue;
            var lastClass = classMatches[classMatches.Count - 1].Groups[1].Value;
            if (lastClass != "Hitbox") continue;

            hitboxOccurrences.Add((
                Pos: m.Index,
                Length: m.Length,
                OldName: m.Groups[2].Value,
                Indent: m.Groups[1].Value));
        }

        if (hitboxOccurrences.Count == 0) return vmdlText;

        // Bucket by name; the 1st occurrence of each name keeps its original
        // value, subsequent occurrences get 1, 2, ... suffixes.
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var renames = new List<(int Pos, int Length, string NewLine)>();

        foreach (var occ in hitboxOccurrences)
        {
            int seen = nameCounts.TryGetValue(occ.OldName, out var c) ? c : 0;
            nameCounts[occ.OldName] = seen + 1;
            if (seen == 0) continue; // first occurrence: untouched
            string newName = occ.OldName + seen.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string newLine = occ.Indent + "name = \"" + newName + "\"";
            renames.Add((occ.Pos, occ.Length, newLine));
        }

        if (renames.Count == 0) return vmdlText;

        renames.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var output = new StringBuilder(vmdlText);
        foreach (var (pos, len, line) in renames)
        {
            output.Remove(pos, len);
            output.Insert(pos, line);
        }
        return output.ToString();
    }

    /// <summary>
    /// Strip empty `children = [  ]` from BoneMarkupList nodes. VRF emits the
    /// empty array but ModelDoc removes it on Save (since BoneMarkupList holds
    /// markup metadata, not child nodes). Single-line regex; safe because it
    /// only targets the literal empty form — populated children arrays are
    /// untouched.
    /// </summary>
    private static string StripEmptyBoneMarkupChildren(string vmdlText)
    {
        // Match the BoneMarkupList class line followed (within the same node)
        // by `children = [  ]` and remove just the children line + its EOL.
        return System.Text.RegularExpressions.Regex.Replace(
            vmdlText,
            @"(_class\s*=\s*""BoneMarkupList""[\r\n]+)([ \t]*children\s*=\s*\[\s*\][\r\n]+)",
            "$1");
    }

    /// <summary>
    /// Format a float the way ModelDoc writes it on Save: always decimal
    /// notation (no scientific), at least one digit before and after the dot,
    /// and shortest representation that round-trips losslessly to the same
    /// 32-bit float. Examples:
    ///   * `0f`           → "0.0"
    ///   * `77.108414f`   → "77.10841" (.NET shortest-roundtrip)
    ///   * `2e-5f`        → "0.00002"  (force decimal even for tiny values)
    ///   * `30f`          → "30.0"     (always trailing ".0")
    /// </summary>
    private static string FormatFloatLikeModelDoc(float f)
    {
        // Collapse ±0 to canonical "0.0".
        if (f == 0f) return "0.0";

        // .NET 5+ default float ToString produces the shortest representation
        // that round-trips. For most values that's exactly what MD uses.
        string s = f.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Forbid scientific notation (.NET emits it for very small or very
        // large magnitudes). Fall back to a fixed-point format wide enough to
        // preserve full single-precision range (~7 sig figs).
        if (s.IndexOfAny(new[] { 'E', 'e' }) >= 0)
        {
            s = f.ToString("0.0##########", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Force at least one decimal digit (e.g. "30" → "30.0").
        if (s.IndexOf('.') < 0) s += ".0";

        return s;
    }

    /// <summary>
    /// Inject `aggregate = false` and `tags = null` defaults inside
    /// `event_keys = { ... }` structs of `AE_CL_CREATE_PARTICLE_EFFECT_CFG`
    /// AnimEvents. ModelDoc writes both on Save (modeldoc41 schema). Closes
    /// ~47 diff lines on a typical hero (29 aggregate + 18 tags).
    /// </summary>
    private static string InjectParticleEventKeysDefaults(string vmdlText)
    {
        // ModelDoc writes `aggregate = false` + `tags = null` on particle-effect
        // events, and `tags = null` (no aggregate) on PLAYSOUND/SUPPRESS-style
        // events. AnimEvent nodes carry `_class = "AnimEvent"` and a separate
        // `event_class` field for the actual type. The regex below matches any
        // AnimEvent block; per-block we look up `event_class` to decide which
        // defaults to add.
        var particleEventRx = new System.Text.RegularExpressions.Regex(
            @"\A\{\s*_class\s*=\s*""AnimEvent""",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);
        var eventClassRx = new System.Text.RegularExpressions.Regex(
            @"event_class\s*=\s*""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        // Inside a particle-event block, locate `event_keys = \r\n {...}` and
        // patch its body. Single-line + lazy match so we stop at the first
        // closing brace at the same indent level.
        var eventKeysRx = new System.Text.RegularExpressions.Regex(
            @"(event_keys\s*=\s*\r?\n)([ \t]+)\{(.*?)\r?\n\2\}",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        // Event types that also receive `tags = null` (but not `aggregate`).
        // SUPPRESS_EVENTS_WITH_TAG uses singular `tag` for its filter and
        // does NOT carry the modeldoc41 `tags` schema field, so it stays out.
        var tagsOnlyEvents = new HashSet<string>(StringComparer.Ordinal)
        {
            "AE_CL_PLAYSOUND",
        };

        var insertions = new List<(int Pos, int OldLen, string NewText)>();
        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!particleEventRx.IsMatch(block)) continue;

            var ecMatch = eventClassRx.Match(block);
            if (!ecMatch.Success) continue;
            var eventType = ecMatch.Groups[1].Value;

            bool isParticle = eventType == "AE_CL_CREATE_PARTICLE_EFFECT_CFG";
            bool isTagsOnly = tagsOnlyEvents.Contains(eventType);
            if (!isParticle && !isTagsOnly) continue;

            var ekMatch = eventKeysRx.Match(block);
            if (!ekMatch.Success) continue;

            var prefix = ekMatch.Groups[1].Value;
            var braceIndent = ekMatch.Groups[2].Value;
            var body = ekMatch.Groups[3].Value;
            var fieldIndent = braceIndent + "\t";

            var sb = new StringBuilder(body);
            if (!body.EndsWith("\n")) sb.Append("\r\n");

            bool added = false;
            // Particle events get both aggregate + tags. Tags-only events get
            // just tags.
            if (isParticle && !System.Text.RegularExpressions.Regex.IsMatch(
                    body, @"^[ \t]+aggregate\s*=", System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                sb.Append(fieldIndent).Append("aggregate = false\r\n");
                added = true;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    body, @"^[ \t]+tags\s*=", System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                sb.Append(fieldIndent).Append("tags = null\r\n");
                added = true;
            }
            if (!added) continue;

            var newBody = sb.ToString().TrimEnd('\r', '\n');
            var newEventKeys = prefix + braceIndent + "{" + newBody + "\r\n" + braceIndent + "}";
            insertions.Add((bStart + ekMatch.Index, ekMatch.Length, newEventKeys));
        }

        if (insertions.Count == 0) return vmdlText;
        insertions.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var output = new StringBuilder(vmdlText);
        foreach (var (pos, oldLen, newText) in insertions)
        {
            output.Remove(pos, oldLen);
            output.Insert(pos, newText);
        }
        return output.ToString();
    }

    /// <summary>
    /// Inject RenderMeshFile modeldoc41 defaults: `import_scale = 1.0` and a
    /// nested `import_filter` struct with the canonical empty filter. ModelDoc
    /// writes both on Save — adds 8 diff lines per RenderMeshFile (typically
    /// 2 nodes per model: main + LOD1).
    /// </summary>
    private static string InjectRenderMeshFileImportFilter(string vmdlText)
    {
        var rmfClassRx = new System.Text.RegularExpressions.Regex(
            @"\A\{\s*_class\s*=\s*""RenderMeshFile""", System.Text.RegularExpressions.RegexOptions.Compiled);

        var insertions = new List<(int Pos, string Text)>();
        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!rmfClassRx.IsMatch(block)) continue;

            // Use any existing top-level field's indentation as our anchor.
            var nameMatch = System.Text.RegularExpressions.Regex.Match(
                block, @"\r?\n([ \t]+)filename\s*=");
            if (!nameMatch.Success) continue;
            var indent = nameMatch.Groups[1].Value;

            var sb = new StringBuilder();
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    block,
                    @"^" + System.Text.RegularExpressions.Regex.Escape(indent) + @"import_scale\s*=",
                    System.Text.RegularExpressions.RegexOptions.Multiline))
                sb.Append(indent).Append("import_scale = 1.0\r\n");

            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    block,
                    @"^" + System.Text.RegularExpressions.Regex.Escape(indent) + @"import_filter\s*=",
                    System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                sb.Append(indent).Append("import_filter = \r\n")
                  .Append(indent).Append("{\r\n")
                  .Append(indent).Append("\texclude_by_default = false\r\n")
                  .Append(indent).Append("\texception_list = [  ]\r\n")
                  .Append(indent).Append("}\r\n");
            }
            if (sb.Length == 0) continue;

            int relCloseBrace = block.LastIndexOf('}');
            if (relCloseBrace < 0) continue;
            int insertOffsetInBlock = relCloseBrace;
            while (insertOffsetInBlock > 0 && block[insertOffsetInBlock - 1] != '\n')
                insertOffsetInBlock--;

            insertions.Add((bStart + insertOffsetInBlock, sb.ToString()));
        }

        if (insertions.Count == 0) return vmdlText;
        insertions.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var output = new StringBuilder(vmdlText);
        foreach (var (pos, txt) in insertions) output.Insert(pos, txt);
        return output.ToString();
    }

    /// <summary>
    /// Generic helper: walk every node of the given <paramref name="className"/>
    /// (matched at the start of a balanced-brace block) and append the missing
    /// fields right before the closing brace, using the indentation taken from
    /// the existing <paramref name="anchorField"/>. Used by ExtractMotion +
    /// Sequence default injectors to avoid duplicating the boilerplate.
    /// </summary>
    private static string InjectFieldsIntoNodes(
        string vmdlText, string className,
        (string Key, string Value)[] fields, string anchorField)
    {
        var classRx = new System.Text.RegularExpressions.Regex(
            @"\A\{\s*_class\s*=\s*""" + System.Text.RegularExpressions.Regex.Escape(className) + @"""",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        var anchorRx = new System.Text.RegularExpressions.Regex(
            @"\r?\n([ \t]+)" + System.Text.RegularExpressions.Regex.Escape(anchorField) + @"\s*=",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var insertions = new List<(int Pos, string Text)>();
        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!classRx.IsMatch(block)) continue;

            var anchorMatch = anchorRx.Match(block);
            if (!anchorMatch.Success) continue;
            var indent = anchorMatch.Groups[1].Value;

            var sb = new StringBuilder();
            foreach (var (key, value) in fields)
            {
                var keyRx = new System.Text.RegularExpressions.Regex(
                    @"^" + System.Text.RegularExpressions.Regex.Escape(indent) + System.Text.RegularExpressions.Regex.Escape(key) + @"\s*=",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                if (keyRx.IsMatch(block)) continue;
                sb.Append(indent).Append(key).Append(" = ").Append(value).Append("\r\n");
            }
            if (sb.Length == 0) continue;

            int relCloseBrace = block.LastIndexOf('}');
            if (relCloseBrace < 0) continue;
            int insertOffsetInBlock = relCloseBrace;
            while (insertOffsetInBlock > 0 && block[insertOffsetInBlock - 1] != '\n')
                insertOffsetInBlock--;

            insertions.Add((bStart + insertOffsetInBlock, sb.ToString()));
        }

        if (insertions.Count == 0) return vmdlText;
        insertions.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var output = new StringBuilder(vmdlText);
        foreach (var (pos, txt) in insertions) output.Insert(pos, txt);
        return output.ToString();
    }

    /// <summary>
    /// Bump the KV3 header from `format:modeldoc28` to `format:modeldoc41`
    /// (ModelDoc's current schema as of 2024+). Without this, ModelDoc opens
    /// our file, silently upgrades on Save, and produces a 1100+ line diff
    /// that's pure schema-version churn. Writing modeldoc41 up-front means
    /// Save & Compile becomes a no-op for the schema and the only diff after
    /// editing is the user's actual changes.
    /// </summary>
    private static string UpgradeSchemaHeader(string vmdlText)
    {
        // Header line is always the first <!-- ... --> comment on row 0.
        // We touch only the format:modelocXX token so we don't disturb the
        // encoding GUID or anything else custom in the header.
        return System.Text.RegularExpressions.Regex.Replace(
            vmdlText,
            @"format:modeldoc\d+:version\{[0-9a-fA-F-]+\}",
            "format:modeldoc41:version{12fc9d44-453a-4ae4-b4d9-7e2ac0bbd4e0}",
            System.Text.RegularExpressions.RegexOptions.None);
    }

    private static string SanitizeVmdlText(string text, Resource resource, MergeSummary summary, bool verbose)
    {
        var validAnims = CollectAnimNames(text);
        // Note: deliberately NOT seeding validAnims with ASEQ sequence names. VRF
        // does not emit `Sequence` nodes for multipose AnimSequences (turns_arcana
        // etc.), so references to them are inherently orphan in the .vmdl source.
        // ModelDoc compile drops them and warns. Stripping here gives a clean
        // compile log; functional behaviour is unchanged because compile would
        // drop them anyway. To preserve refs (e.g. for manual Sequence-node
        // editing in ModelDoc UI), enable CollectSequenceNamesFromAseq below.
        // CollectSequenceNamesFromAseq(resource, validAnims);
        int invalid = 0, broken = 0;

        var result = RemoveMatchingNodes(text, (cls, nodeText) =>
        {
            if (IsInvalidResourceNode(cls, nodeText)) { invalid++; return true; }
            if (IsBrokenAutoLayer(cls, nodeText, validAnims)) { broken++; return true; }
            return false;
        });

        result = StripEmptyResourceLines(result, out int emptyLines);
        result = FixBogusResourceTags(result, out int bogusTags);
        result = FixMissingBodyGroupChoiceNames(result, out int bgcFixed);
        result = StripHitboxAttachmentUnknownBones(result, out int strippedBoneRefs);

        summary.SanitizedInvalidNodes = invalid;
        summary.SanitizedBrokenAutoLayers = broken;
        summary.SanitizedEmptyLines = emptyLines;
        summary.SanitizedBogusResourceTags = bogusTags;
        summary.FixedBodyGroupChoiceNames = bgcFixed;
        summary.StrippedUnknownBoneRefs = strippedBoneRefs;

        if (verbose && (invalid > 0 || broken > 0 || emptyLines > 0 || bogusTags > 0 || bgcFixed > 0 || strippedBoneRefs > 0))
            Console.WriteLine(
                $"    ✓ sanitized: {invalid} invalid node(s), {broken} broken AutoLayer(s), " +
                $"{emptyLines} empty resource line(s), {bogusTags} bogus resource tag(s), " +
                $"{bgcFixed} BodyGroupChoice name(s) injected, " +
                $"{strippedBoneRefs} hitbox/attachment node(s) with unknown bones stripped");

        return result;
    }

    private static string StripEmptyResourceLines(string text, out int count)
    {
        var pattern = @"^[ \t]*(?:tags|surface_property|weight_list|graph_filename|anim_graph_name)[ \t]*=[ \t]*""[ \t]*""[ \t]*\r?\n";
        int c = 0;
        var newText = Regex.Replace(text, pattern, _ => { c++; return string.Empty; }, RegexOptions.Multiline);
        count = c;
        return newText;
    }

    // VRF в `19.1.6199` (см. AnimationActivity.cs) читает только m_name + m_nWeight
    // у первого элемента m_activityArray. У Dota-моделей (SF, и т.п.) каждая sequence
    // часто имеет вторую запись — модификатор без префикса `ACT_`:
    //
    //   m_activityArray = [
    //     { m_name = "ACT_DOTA_RUN", m_nWeight = 3 },
    //     { m_name = "desolation",   m_nWeight = 1 }   ← VRF теряет
    //   ]
    //
    // ModelDoc (см. строки в `modeldoc_editor.dll`) использует поле `activity_modifiers`
    // (МАССИВ строк, не singular!) и опционально `activity_modifier_weights`. Это фильтр:
    // run_desolation активируется только когда у юнита есть модификатор "desolation"
    // (от Desolator-айтема). Без него ModelDoc считает все варианты ACT_DOTA_RUN
    // равноправными и рандомит по весам → SF играет run_desolation/run_injured без
    // оснований, дёргается.
    //
    // ALSO: VRF теряет `m_bLegacyDelta` — пишет всегда `delta = false`. У Dota-моделей
    // sequence `turns` обычно имеет m_bLegacyDelta=true (additive overlay для поворотов).
    // Когда delta-флаг отсутствует, ModelDoc проигрывает дельту как обычную анимацию,
    // что даёт неправильные углы поверх run/idle → дёргание модели.
    //
    // Чиним:
    //   1) Парсим ASEQ.m_localS1SeqDescArray и собираем модификаторы каждой sequence
    //      и флаг m_bLegacyDelta.
    //   2) Регексом инжектим строки `activity_modifiers = [ "..." ]` в AnimFile.
    //   3) Меняем `delta = false` → `delta = true` для дельт-сиквенсов.
    private static string EnrichVmdlActivityModifiers(string vmdlText, Resource resource, MergeSummary summary, bool verbose)
    {
        if (resource.GetBlockByType(BlockType.ASEQ) is not KeyValuesOrNTRO aseq) return vmdlText;
        if (aseq.Data is not KVObject seqData) return vmdlText;

        IReadOnlyList<KVObject>? sequences;
        try { sequences = seqData.GetArray("m_localS1SeqDescArray"); }
        catch { return vmdlText; }
        if (sequences == null || sequences.Count == 0) return vmdlText;

        // Map: anim name → модификаторы (название + weight из оригинального ASEQ).
        // Weight принципиален — влияет на выбор sequence движком, хардкодили 1
        // — это ломало распределение.
        var modsByName = new Dictionary<string, List<(string Name, int Weight)>>(StringComparer.OrdinalIgnoreCase);
        // Set: anim name → нужно `delta = true`.
        var deltaByName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // v5.3.1: Set of multipose sequences (m_bMulti=1). VRF cannot
        // reconstruct multipose blend graphs from compiled .vmdl_c, so it
        // emits these as plain AnimFile + a duplicate of one lookFrame DMX.
        // When run-style sequences reference such a multipose via
        // AnimAddLayer { anim_name = "<multi>" }, ModelDoc adds the
        // single-frame "first lookFrame pose" additively to run - the
        // overlay is non-zero (a real turn pose), producing systematic
        // body/limb flipping in match (where AnimGraph2 is active) while
        // staying invisible in the loadout/profile preview. We strip those
        // AnimAddLayer references so run plays cleanly without the broken
        // overlay.
        var multiposeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seq in sequences)
        {
            string? animName = null;
            try { animName = seq.GetStringProperty("m_sName"); } catch { continue; }
            if (string.IsNullOrEmpty(animName)) continue;

            // m_bLegacyDelta и m_bMulti лежат во вложенном `m_flags` блоке.
            try
            {
                var flags = seq.GetSubCollection("m_flags");
                if (flags != null && flags.GetBooleanProperty("m_bLegacyDelta"))
                    deltaByName.Add(animName);
                if (flags != null && flags.GetBooleanProperty("m_bMulti"))
                    multiposeNames.Add(animName);
            }
            catch { }

            IReadOnlyList<KVObject>? activities;
            try { activities = seq.GetArray("m_activityArray"); }
            catch { continue; }
            if (activities == null || activities.Count < 2) continue;

            var mods = new List<(string Name, int Weight)>();
            for (int i = 1; i < activities.Count; i++)
            {
                string actName;
                int actWeight = 1;
                try { actName = activities[i].GetStringProperty("m_name"); }
                catch { continue; }
                // Skip ACT_* entries (those come from the primary activity_name
                // field on the AnimFile node, not from modifiers). DO NOT skip
                // empty names — Valve writes literal `name = ""` slots in
                // m_activityArray for some sequences (e.g. CM 'ward_stun'),
                // and ModelDoc compile reproduces them only if we inject the
                // matching ActivityModifier node with activity_name = "".
                if (actName == null) continue;
                if (actName.StartsWith("ACT_", StringComparison.Ordinal)) continue;
                try { actWeight = activities[i].GetInt32Property("m_nWeight"); } catch { /* default 1 */ }
                // No de-duplication: Valve's m_activityArray legitimately contains
                // repeated entries (e.g. CM 'dplus_loadout_spawn' has 'loadout'
                // listed twice). Each duplicate becomes a separate ActivityModifier
                // child node so ModelDoc compile reproduces the exact entry count.
                mods.Add((actName, actWeight));
            }
            if (mods.Count > 0) modsByName[animName] = mods;
        }

        if (modsByName.Count == 0 && deltaByName.Count == 0) return vmdlText;

        // Инжектим ПО-БЛОЧНО через FindBalancedBracePairs (см. ниже почему).
        // КРИТИЧНО: ModelDoc хранит модификаторы НЕ как поле AnimFile, а как ОТДЕЛЬНЫЙ
        // child-узел `_class = "ActivityModifier"` внутри `children = [...]`. Образец
        // взят из реальных Dota .vmdl (overthrow/midas_throne/kobold_*.vmdl):
        //
        //   {
        //       _class = "AnimFile"
        //       name = "..."
        //       children = [
        //           {
        //               _class = "ActivityModifier"
        //               activity_name = "<modifier>"
        //               activity_weight = 1
        //           },
        //           { _class = "AnimEvent", ... },
        //       ]
        //       activity_name = "ACT_DOTA_RUN"
        //       activity_weight = 3
        //       ...
        //   }
        //
        // Если AnimFile уже имеет `children = [...]` — добавляем ActivityModifier-узел
        // в ВЕРХ массива (перед существующими событиями). Если нет — создаём новый
        // блок `children = [ ... ]` сразу после строки `name = "..."`.
        var injections = new List<(int InsertAt, string TextToInsert)>();
        var replacements = new List<(int Start, int Length, string Replacement)>();
        var ownClassRx = new Regex(@"\A\{\s*_class\s*=\s*""AnimFile""", RegexOptions.Compiled);
        var nameRx = new Regex(@"^([ \t]*)name\s*=\s*""([^""]+)""\s*$", RegexOptions.Multiline);
        // children = [   (на отдельной строке, может быть один пробел/таб + перевод строки)
        var childrenOpenRx = new Regex(@"^([ \t]*)children\s*=\s*\r?\n[ \t]*\[\s*\r?\n", RegexOptions.Multiline);
        var deltaFalseRx = new Regex(@"^([ \t]*)delta\s*=\s*false([ \t]*\r?\n)", RegexOptions.Multiline);

        int modInjected = 0, deltaPatched = 0;

        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!ownClassRx.IsMatch(block)) continue;

            var nameMatch = nameRx.Match(block);
            if (!nameMatch.Success) continue;
            var animName = nameMatch.Groups[2].Value;
            var nameIndent = nameMatch.Groups[1].Value;

            // 1) ActivityModifier child-узлы. ModelDoc-схема (см. примеры в Dota SDK).
            if (modsByName.TryGetValue(animName, out var mods))
            {
                // Идемпотентность: если уже есть ActivityModifier — пропускаем.
                if (block.Contains("\"ActivityModifier\"", StringComparison.Ordinal))
                {
                    // ничего не делаем
                }
                else
                {
                    // Поиск существующего блока children = [ ... ].
                    var chMatch = childrenOpenRx.Match(block);
                    if (chMatch.Success)
                    {
                        // Вставка СРАЗУ после `[\n` — добавляем узлы в начало массива.
                        // Отступ child-узла = отступ `children` + 1 уровень (\t).
                        var baseIndent = chMatch.Groups[1].Value;
                        var childIndent = baseIndent + "\t";
                        var sb2 = new StringBuilder();
                        foreach (var (modName, modWeight) in mods)
                        {
                            sb2.Append(childIndent).Append("{\r\n");
                            sb2.Append(childIndent).Append("\t_class = \"ActivityModifier\"\r\n");
                            sb2.Append(childIndent).Append("\tactivity_name = \"").Append(modName).Append("\"\r\n");
                            sb2.Append(childIndent).Append("\tactivity_weight = ").Append(modWeight).Append("\r\n");
                            sb2.Append(childIndent).Append("},\r\n");
                        }
                        int insertAt = bStart + chMatch.Index + chMatch.Length;
                        injections.Add((insertAt, sb2.ToString()));
                        modInjected++;
                    }
                    else
                    {
                        // Нет children — создаём новый блок сразу после строки `name = "..."`.
                        var nameLineEnd = bStart + nameMatch.Index + nameMatch.Length;
                        // Найдём конец строки (включая \r\n).
                        while (nameLineEnd < vmdlText.Length && vmdlText[nameLineEnd] != '\n') nameLineEnd++;
                        if (nameLineEnd < vmdlText.Length) nameLineEnd++; // заглатываем \n
                        var childIndent = nameIndent + "\t";
                        var sb2 = new StringBuilder();
                        sb2.Append(nameIndent).Append("children = \r\n");
                        sb2.Append(nameIndent).Append("[\r\n");
                        foreach (var (modName, modWeight) in mods)
                        {
                            sb2.Append(childIndent).Append("{\r\n");
                            sb2.Append(childIndent).Append("\t_class = \"ActivityModifier\"\r\n");
                            sb2.Append(childIndent).Append("\tactivity_name = \"").Append(modName).Append("\"\r\n");
                            sb2.Append(childIndent).Append("\tactivity_weight = ").Append(modWeight).Append("\r\n");
                            sb2.Append(childIndent).Append("},\r\n");
                        }
                        sb2.Append(nameIndent).Append("]\r\n");
                        injections.Add((nameLineEnd, sb2.ToString()));
                        modInjected++;
                    }
                }
            }

            // 2) delta = true — для sequence c m_bLegacyDelta=true.
            // Note: m_bLegacyDelta=1 is set ONLY on the user-facing
            // multipose sequence (e.g. 'turns_arcana'); the @-prefixed
            // source AnimFiles (@turns_arcana, @turns_arcana_lookFrame_*)
            // have m_bLegacyDelta=0 in Valve ASEQ and must remain so.
            // We honour that here by patching only ASEQ-flagged names.
            if (deltaByName.Contains(animName))
            {
                var dfMatch = deltaFalseRx.Match(block);
                if (dfMatch.Success)
                {
                    var indent = dfMatch.Groups[1].Value;
                    var trailing = dfMatch.Groups[2].Value;
                    int absStart = bStart + dfMatch.Index;
                    replacements.Add((absStart, dfMatch.Length, $"{indent}delta = true{trailing}"));
                    deltaPatched++;
                }
            }
        }

        if (injections.Count == 0 && replacements.Count == 0) return vmdlText;

        // Применяем все правки с конца к началу, чтобы не сдвинуть индексы.
        var ops = new List<(int Pos, int Len, string Text)>();
        foreach (var (at, txt) in injections) ops.Add((at, 0, txt));
        foreach (var (start, len, repl) in replacements) ops.Add((start, len, repl));
        ops.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var sb = new StringBuilder(vmdlText);
        foreach (var (pos, len, txt) in ops)
        {
            if (len > 0) sb.Remove(pos, len);
            sb.Insert(pos, txt);
        }

        summary.ActivityModifiersInjected += modInjected;
        if (verbose)
        {
            if (modInjected > 0)
                Console.WriteLine($"    ✓ injected ActivityModifier child node(s) into {modInjected} AnimFile(s)");
            if (deltaPatched > 0)
                Console.WriteLine($"    ✓ patched delta=true on {deltaPatched} legacy-delta AnimFile node(s)");
        }

        // v5.3.1: strip AnimAddLayer/AnimAdd{Pose,World}Layer references to
        // multipose (m_bMulti=1) sequences. See comment above multiposeNames
        // for rationale. We do this as a post-pass on the already-mutated
        // text so indices for prior replacements are not affected.
        var afterInject = sb.ToString();
        if (multiposeNames.Count > 0 && !SkipMultiposeLayerStrip)
        {
            afterInject = StripMultiposeAddLayers(afterInject, multiposeNames, verbose, summary);
        }
        else if (SkipMultiposeLayerStrip && multiposeNames.Count > 0 && verbose)
        {
            Console.WriteLine($"    ⊘ multipose AnimAddLayer strip SKIPPED (--keep-multipose-layers)");
        }
        sb = new StringBuilder(afterInject);

        // Второй проход: дедупликация активити-кандидатов.
        // Когда несколько AnimFile в .vmdl имеют одинаковый ключ
        // (activity_name + отсортированный набор модификаторов), движок Source 2
        // в реалтайме переключается между ними по weighted-random — у не-AnimGraph
        // моделей это даёт «чечётку» (особенно заметна у SF+arcana с десолятором).
        // Оригинальный Dota AnimGraph (не извлекается VRF) делает детерминистический
        // выбор. Эмулируем: в каждой группе оставляем кандидата с MAX activity_weight
        // (это Valve-intended вариант). У остальных — очищаем activity_name и
        // удаляем ActivityModifier-детей, чтобы они выпали из активити-выбора.
        if (SkipDedup)
        {
            if (verbose) Console.WriteLine($"    ⊘ activity-modifier dedup SKIPPED (--no-dedup)");
            return sb.ToString();
        }
        return DedupActivityCandidates(sb.ToString(), verbose, summary);
    }

    // Грузим whitelist разрешённых activity-модификаторов из
    // `scripts/activity_modifier_weights.txt` (Dota 2). Это маленький KeyValues-файл:
    //   "weights" {
    //       "aggressive"          "1"
    //       "injured"             "2"
    //       "injured_aggressive"  "4"
    //       "haste"               "5"
    //   }
    // Если файл недоступен (extract из folder без VPK) — fallback на BuiltinModifierWhitelist.
    private static HashSet<string> LoadActivityModifierWhitelist(RawReader rawReader, bool verbose)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var bytes = rawReader("scripts/activity_modifier_weights.txt");
            if (bytes != null && bytes.Length > 0)
            {
                var text = Encoding.UTF8.GetString(bytes);
                // Простой парсер: "<key>"  "<value>" pairs внутри { ... }
                foreach (Match m in Regex.Matches(text, @"""([a-zA-Z_][a-zA-Z0-9_]*)""\s*""\d+"""))
                    set.Add(m.Groups[1].Value);
                if (verbose && set.Count > 0)
                    Console.WriteLine($"    ✓ loaded {set.Count} modifier(s) from scripts/activity_modifier_weights.txt: [{string.Join(", ", set)}]");
            }
        }
        catch { /* fall through */ }

        if (set.Count == 0)
        {
            foreach (var m in BuiltinModifierWhitelist) set.Add(m);
            if (verbose) Console.WriteLine($"    ⓘ using built-in modifier whitelist: [{string.Join(", ", set)}]");
        }
        return set;
    }

    // ROOT CAUSE «дёргания» SF arcana в custom-аддонах:
    // VRF извлекает все sequences из ASEQ как самостоятельные AnimFile-узлы. Часть
    // из них имеет `m_activityArray = [{ACT_DOTA_RUN, w}, {<modifier>, w}]`, где
    // <modifier> — ОДИН ИЗ:
    //   • `aggressive` / `injured` / `injured_aggressive` / `haste` — глобальные
    //     модификаторы из `scripts/activity_modifier_weights.txt`. Активируются
    //     обычной игровой логикой (low HP → injured, BattleFury speed → haste).
    //   • `desolation` / `fast_run` / `spawn_arcana` / `swag_gesture` — модификаторы,
    //     активируемые ТОЛЬКО через cosmetic-items в `scripts/items/items_game.txt`
    //     (например, item 8259 "Arms of Desolation" → "type=activity", "modifier=desolation").
    //
    // В custom-аддоне (witchblades и т.п.) cosmetic-инфраструктура НЕ участвует.
    // Модификатор `desolation` НИКОГДА не активен → но 8 sequences с этим модификатором
    // остаются кандидатами для ACT_DOTA_RUN. Движок Source 2 рандомит между ними,
    // включая run_alt_desolation_anim/run_haste_desolation_anim — отсюда «20× ускорение
    // и пересборка» в running.
    //
    // Фикс: для всех sequences с modifier'ом НЕ из whitelist — выставляем
    //   activity_weight = 0
    //   hidden = true
    // Это убирает их из weighted-random pool. Sequence сам остаётся в .vmdl
    // (на случай ручного вызова по имени из game logic), но не выбирается движком.
    private static string DisableNonWhitelistedModifierSequences(
        string vmdlText, Resource resource, HashSet<string> whitelist,
        MergeSummary summary, bool verbose)
    {
        if (resource.GetBlockByType(BlockType.ASEQ) is not KeyValuesOrNTRO aseq) return vmdlText;
        if (aseq.Data is not KVObject seqData) return vmdlText;

        IReadOnlyList<KVObject>? sequences;
        try { sequences = seqData.GetArray("m_localS1SeqDescArray"); }
        catch { return vmdlText; }
        if (sequences == null || sequences.Count == 0) return vmdlText;

        // Имена sequence'ов с не-whitelisted модификаторами.
        var disableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstFewExamples = new List<string>();
        foreach (var seq in sequences)
        {
            string? animName = null;
            try { animName = seq.GetStringProperty("m_sName"); } catch { continue; }
            if (string.IsNullOrEmpty(animName)) continue;

            IReadOnlyList<KVObject>? activities;
            try { activities = seq.GetArray("m_activityArray"); }
            catch { continue; }
            if (activities == null || activities.Count < 2) continue;

            // Проверяем все модификаторы. Если ХОТЯ БЫ ОДИН — не whitelisted, отключаем sequence.
            bool hasNonWhitelisted = false;
            string? offendingMod = null;
            for (int i = 1; i < activities.Count; i++)
            {
                string actName;
                try { actName = activities[i].GetStringProperty("m_name"); }
                catch { continue; }
                if (string.IsNullOrEmpty(actName) || actName.StartsWith("ACT_", StringComparison.Ordinal)) continue;
                if (!whitelist.Contains(actName))
                {
                    hasNonWhitelisted = true;
                    offendingMod = actName;
                    break;
                }
            }
            if (hasNonWhitelisted)
            {
                disableNames.Add(animName);
                if (firstFewExamples.Count < 5) firstFewExamples.Add($"{animName}({offendingMod})");
            }
        }

        if (disableNames.Count == 0) return vmdlText;

        // Идём по AnimFile блокам и для disableNames меняем поля.
        var ownClassRx = new Regex(@"\A\{\s*_class\s*=\s*""AnimFile""", RegexOptions.Compiled);
        var nameRx = new Regex(@"^([ \t]*)name\s*=\s*""([^""]+)""\s*$", RegexOptions.Multiline);
        var weightRx = new Regex(@"^([ \t]*)activity_weight\s*=\s*\d+([ \t]*\r?\n)", RegexOptions.Multiline);
        var hiddenFalseRx = new Regex(@"^([ \t]*)hidden\s*=\s*false([ \t]*\r?\n)", RegexOptions.Multiline);

        var replacements = new List<(int Start, int Length, string Replacement)>();
        int affected = 0;

        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!ownClassRx.IsMatch(block)) continue;
            var nm = nameRx.Match(block);
            if (!nm.Success) continue;
            var animName = nm.Groups[2].Value;
            if (!disableNames.Contains(animName)) continue;

            // 1) activity_weight = N → activity_weight = 0
            var wMatch = weightRx.Match(block);
            if (wMatch.Success)
            {
                var ind = wMatch.Groups[1].Value;
                var trail = wMatch.Groups[2].Value;
                int absStart = bStart + wMatch.Index;
                replacements.Add((absStart, wMatch.Length, $"{ind}activity_weight = 0{trail}"));
            }

            // 2) hidden = false → hidden = true
            var hMatch = hiddenFalseRx.Match(block);
            if (hMatch.Success)
            {
                var ind = hMatch.Groups[1].Value;
                var trail = hMatch.Groups[2].Value;
                int absStart = bStart + hMatch.Index;
                replacements.Add((absStart, hMatch.Length, $"{ind}hidden = true{trail}"));
            }

            affected++;
        }

        if (replacements.Count == 0) return vmdlText;

        replacements.Sort((a, b) => b.Start.CompareTo(a.Start));
        var sb = new StringBuilder(vmdlText);
        foreach (var (start, len, repl) in replacements)
        {
            sb.Remove(start, len);
            sb.Insert(start, repl);
        }

        summary.DisabledNonWhitelistModSequences = affected;
        if (verbose)
        {
            var sample = string.Join(", ", firstFewExamples);
            if (disableNames.Count > firstFewExamples.Count) sample += ", ...";
            Console.WriteLine($"    ✓ disabled {affected} non-whitelisted-modifier sequence(s): [{sample}]");
        }
        return sb.ToString();
    }

    // Правильный (Valve-canonical) inject модификаторов — поле `activity_modifiers`
    // на уровне AnimFile-узла. Не путать с `_class = "ActivityModifier"` child-узлом
    // (который служит для другого — UI tree артефакт).
    //
    // ИСХОДНИК ИНФЫ: строки в `tools/modeldoc_editor.dll`:
    //   "Activity (Primary)"   ← поле `activity_name`
    //   "Activity Modifiers"   ← поле `activity_modifiers` (массив, множ.число)
    //   "Frame Count"          ← поле `frame_count`
    //   "Frames Per Second"    ← поле `framerate`
    //
    // Что именно извлекаем из ASEQ:
    //   m_activityArray = [
    //     { m_name = "ACT_DOTA_RUN", m_nWeight = 4 },     ← основная активити  (UI: "Activity Primary")
    //     { m_name = "desolation",   m_nWeight = 1 },     ← модификатор #1     (UI: "Activity Modifiers")
    //     { m_name = "haste",        m_nWeight = 1 }      ← модификатор #2
    //   ]
    //
    // Что инжектим в .vmdl (после строки `activity_weight = N`):
    //   activity_modifiers = [ "desolation", "haste" ]
    //
    // Также ставим `delta = true` для sequences с `m_bLegacyDelta=true`
    // (turns/look-around — VRF теряет этот флаг → они проигрываются как обычные
    // анимации поверх движений и накладывают неверный поворот корпуса).
    //
    // Когда этот inject включён, ModelDoc compile корректно записывает m_activityArray
    // в ASEQ итогового .vmdl_c — структура совпадает с оригинальной Valve. Это
    // root-fix, без костылей вроде DisableNonWhitelistedModifierSequences.
    private static string InjectActivityModifierFields(
        string vmdlText, Resource resource, MergeSummary summary, bool verbose)
    {
        if (resource.GetBlockByType(BlockType.ASEQ) is not KeyValuesOrNTRO aseq) return vmdlText;
        if (aseq.Data is not KVObject seqData) return vmdlText;

        IReadOnlyList<KVObject>? sequences;
        try { sequences = seqData.GetArray("m_localS1SeqDescArray"); }
        catch { return vmdlText; }
        if (sequences == null || sequences.Count == 0) return vmdlText;

        // animName → list of modifier names (порядок важен — Valve пишет в том же).
        var modsByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        // animName → нужно ли delta=true.
        var deltaByName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seq in sequences)
        {
            string? animName = null;
            try { animName = seq.GetStringProperty("m_sName"); } catch { continue; }
            if (string.IsNullOrEmpty(animName)) continue;

            try
            {
                var flags = seq.GetSubCollection("m_flags");
                if (flags != null && flags.GetBooleanProperty("m_bLegacyDelta"))
                    deltaByName.Add(animName);
            }
            catch { }

            IReadOnlyList<KVObject>? activities;
            try { activities = seq.GetArray("m_activityArray"); }
            catch { continue; }
            if (activities == null || activities.Count < 2) continue;

            var mods = new List<string>();
            for (int i = 1; i < activities.Count; i++)
            {
                string actName;
                try { actName = activities[i].GetStringProperty("m_name"); }
                catch { continue; }
                if (string.IsNullOrEmpty(actName) || actName.StartsWith("ACT_", StringComparison.Ordinal)) continue;
                // No dedup: see InjectActivityModifierFields above for rationale.
                mods.Add(actName);
            }
            if (mods.Count > 0) modsByName[animName] = mods;
        }

        if (modsByName.Count == 0 && deltaByName.Count == 0) return vmdlText;

        var ownClassRx = new Regex(@"\A\{\s*_class\s*=\s*""AnimFile""", RegexOptions.Compiled);
        var nameRx = new Regex(@"^([ \t]*)name\s*=\s*""([^""]+)""\s*$", RegexOptions.Multiline);
        // Точка вставки: после строки `activity_weight = N` (если есть) или после `activity_name`.
        var actWeightRx = new Regex(@"^([ \t]*)activity_weight\s*=\s*\d+([ \t]*\r?\n)", RegexOptions.Multiline);
        var actNameRx = new Regex(@"^([ \t]*)activity_name\s*=\s*""[^""]*""([ \t]*\r?\n)", RegexOptions.Multiline);
        var deltaFalseRx = new Regex(@"^([ \t]*)delta\s*=\s*false([ \t]*\r?\n)", RegexOptions.Multiline);

        var ops = new List<(int Pos, int Len, string Text)>();
        int modInjected = 0, deltaPatched = 0;

        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!ownClassRx.IsMatch(block)) continue;
            var nm = nameRx.Match(block);
            if (!nm.Success) continue;
            var animName = nm.Groups[2].Value;

            // Идемпотентность: пропускаем если уже есть activity_modifiers.
            bool alreadyHas = block.Contains("activity_modifiers", StringComparison.Ordinal);

            // 1) Field-level inject модификаторов.
            if (!alreadyHas && modsByName.TryGetValue(animName, out var mods))
            {
                // Точка вставки — сразу после `activity_weight = N` строки (если есть),
                // иначе после `activity_name = "..."` строки.
                Match anchor = actWeightRx.Match(block);
                if (!anchor.Success) anchor = actNameRx.Match(block);
                if (anchor.Success)
                {
                    var indent = anchor.Groups[1].Value;
                    int insertAt = bStart + anchor.Index + anchor.Length;
                    var quoted = string.Join(", ", mods.Select(m => $"\"{m}\""));
                    var line = $"{indent}activity_modifiers = [ {quoted} ]\r\n";
                    ops.Add((insertAt, 0, line));
                    modInjected++;
                }
            }

            // 2) delta = false → delta = true для дельт-sequence.
            // Note: only patch ASEQ-flagged names (e.g. 'turns_arcana');
            // do NOT propagate to @-prefixed helpers - they are NOT delta
            // in Valve ASEQ (verified via m_bLegacyDelta=0).
            if (deltaByName.Contains(animName))
            {
                var df = deltaFalseRx.Match(block);
                if (df.Success)
                {
                    int absStart = bStart + df.Index;
                    var indent = df.Groups[1].Value;
                    var trail = df.Groups[2].Value;
                    ops.Add((absStart, df.Length, $"{indent}delta = true{trail}"));
                    deltaPatched++;
                }
            }
        }

        if (ops.Count == 0) return vmdlText;
        ops.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var sb = new StringBuilder(vmdlText);
        foreach (var (pos, len, txt) in ops)
        {
            if (len > 0) sb.Remove(pos, len);
            sb.Insert(pos, txt);
        }

        summary.ActivityModifiersInjected += modInjected;
        if (verbose)
        {
            if (modInjected > 0)
                Console.WriteLine($"    ✓ injected activity_modifiers field into {modInjected} AnimFile node(s)");
            if (deltaPatched > 0)
                Console.WriteLine($"    ✓ flipped delta=false→true on {deltaPatched} legacy-delta sequence(s)");
        }
        return sb.ToString();
    }

    // VRF при decompile теряет frame-rate metadata (framerate, frame count, range)
    // в AnimFile-узлах. Без этих полей ModelDoc compile использует defaults
    // (вероятно 30 fps), что приводит к ускоренным/замедленным анимациям в игре,
    // если оригинал был с нестандартным fps (Dota героев нередко имеет 33/37 fps).
    // Берём реальные fps/frame count из VRF Animation API (сам считает из anim
    // group/embedded data) и явно инжектим в AnimFile.
    private static string InjectAnimFrameRates(
        string vmdlText, Model model, IFileLoader fileLoader,
        MergeSummary summary, bool verbose)
    {
        // Собираем словарь name → (fps, frameCount).
        // Используем GetAllAnimations: в т.ч. embedded и referenced (anim groups).
        Dictionary<string, (float Fps, int FrameCount)> animMeta = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var anim in model.GetAllAnimations(fileLoader))
            {
                if (string.IsNullOrEmpty(anim.Name)) continue;
                if (animMeta.ContainsKey(anim.Name)) continue;
                animMeta[anim.Name] = (anim.Fps, anim.FrameCount);
            }
        }
        catch (Exception ex)
        {
            if (verbose) Console.WriteLine($"    [warn] could not enumerate animations for fps inject: {ex.Message}");
            return vmdlText;
        }
        if (animMeta.Count == 0) return vmdlText;

        var animFileClassRx = new Regex(@"\A\{\s*_class\s*=\s*""AnimFile""", RegexOptions.Compiled);
        var nameRx = new Regex(@"^([ \t]*)name\s*=\s*""([^""]+)""\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
        // Какое-нибудь поле `hidden = ...` на верхнем уровне — после него удобно
        // вставлять framerate/start_frame/end_frame. Если нет — после `name = ...`.
        var hiddenRx = new Regex(@"^([ \t]*)hidden\s*=\s*(?:true|false)([ \t]*\r?\n)", RegexOptions.Multiline | RegexOptions.Compiled);

        var injections = new List<(int InsertAt, string Text)>();
        int injected = 0;

        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!animFileClassRx.IsMatch(block)) continue;

            var nameM = nameRx.Match(block);
            if (!nameM.Success) continue;
            var animName = nameM.Groups[2].Value;
            var indent = nameM.Groups[1].Value;

            if (!animMeta.TryGetValue(animName, out var meta)) continue;

            // Идемпотентность: пропускаем, если поля уже есть.
            if (Regex.IsMatch(block, @"^[ \t]*framerate\s*=", RegexOptions.Multiline)) continue;

            // end_frame — индекс ПОСЛЕДНЕГО кадра (frames 0..N-1 → end_frame = N-1).
            // Если FrameCount == 0 (статичная поза или ошибка) — пропускаем.
            if (meta.FrameCount <= 0) continue;
            int endFrame = meta.FrameCount - 1;

            // Sub-1 FPS values (e.g. SF "versus_attack02" has m_flFps = 0.2
            // in the original Valve compile) are kept verbatim — ModelDoc
            // round-trips them unchanged, so emitting the same value keeps
            // diffs closed. We only refuse FrameCount <= 0 (handled above).
            // Format: integer-style if close to whole, otherwise 2 decimals.
            string fpsStr = (Math.Abs(meta.Fps - Math.Round(meta.Fps)) < 0.05f)
                ? ((int)Math.Round(meta.Fps)).ToString() + ".0"
                : meta.Fps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            // Найдём позицию для вставки — после строки `hidden = ...` если есть,
            // иначе сразу после `name = "..."`.
            var hM = hiddenRx.Match(block);
            int insertOffsetInBlock;
            string trailing;
            if (hM.Success)
            {
                insertOffsetInBlock = hM.Index + hM.Length;
                trailing = "";
            }
            else
            {
                // Найти конец строки name = "..."
                insertOffsetInBlock = nameM.Index + nameM.Length;
                while (insertOffsetInBlock < block.Length && block[insertOffsetInBlock] != '\n') insertOffsetInBlock++;
                if (insertOffsetInBlock < block.Length) insertOffsetInBlock++; // step past \n
                trailing = "";
            }

            var sbField = new StringBuilder();
            sbField.Append(indent).Append("framerate = ").Append(fpsStr).Append("\r\n");
            sbField.Append(indent).Append("start_frame = 0\r\n");
            sbField.Append(indent).Append("end_frame = ").Append(endFrame).Append("\r\n");

            injections.Add((bStart + insertOffsetInBlock, sbField.ToString() + trailing));
            injected++;
        }

        if (injections.Count == 0) return vmdlText;

        injections.Sort((a, b) => b.InsertAt.CompareTo(a.InsertAt));
        var sb = new StringBuilder(vmdlText);
        foreach (var (pos, txt) in injections) sb.Insert(pos, txt);

        summary.FrameRatesInjected += injected;
        if (verbose)
            Console.WriteLine($"    ✓ injected framerate/start_frame/end_frame into {injected} AnimFile node(s)");
        return sb.ToString();
    }

    /// <summary>
    /// Extract DMX bytes for animations whose .dmx file VRF's pipeline didn't write.
    /// VRF announces all m_anims as SubFiles but its <c>Extract</c> callback returns
    /// null/empty for autolayer-compressed (<c>@@</c>) and multipose pose-frame
    /// (<c>@*lookFrame*</c>) anims. We bypass the broken extractor by calling
    /// <c>ModelExtract.ToDmxAnim(model, anim)</c> directly — this gives us a valid
    /// DMX byte stream for ANY animation in the model, including the ones VRF skips.
    ///
    /// Files are written into <paramref name="modelFolder"/> using the animation's
    /// own name (e.g. "@@run_anim.dmx"). Returns the count of newly-written .dmx files.
    /// </summary>
    private static int ExtractMissingAnimDmx(
        Resource resource, string modelFolder, HashSet<string> writtenDmx,
        bool verbose)
    {
        if (resource.DataBlock is not Model model) return 0;

        int written = 0;
        foreach (var anim in model.GetEmbeddedAnimations())
        {
            if (string.IsNullOrEmpty(anim.Name)) continue;

            // VRF's SubFile filename uses the bare anim name + ".dmx".
            var fileName = anim.Name + ".dmx";

            // Skip if VRF already wrote it (avoid double-write).
            if (writtenDmx.Contains(fileName)) continue;

            var subPath = Path.Combine(modelFolder, fileName);
            if (File.Exists(subPath)) { writtenDmx.Add(fileName); continue; }

            try
            {
                var bytes = ValveResourceFormat.IO.ModelExtract.ToDmxAnim(model, anim);
                if (bytes == null || bytes.Length == 0)
                {
                    if (verbose) Console.WriteLine($"    [warn] ToDmxAnim returned empty for '{anim.Name}'");
                    continue;
                }
                File.WriteAllBytes(subPath, bytes);
                writtenDmx.Add(fileName);
                written++;
                if (verbose) Console.WriteLine($"    ✓ DMX (recovered) {fileName} ({bytes.Length / 1024} KB)");
            }
            catch (Exception ex)
            {
                if (verbose) Console.Error.WriteLine($"    [warn] ToDmxAnim failed for '{anim.Name}': {ex.Message}");
            }
        }
        return written;
    }

    /// <summary>
    /// Inject AnimFile nodes for animations that exist in Valve's m_anims (embedded
    /// animations of DATA block) but are not referenced in the .vmdl source.
    ///
    /// VRF's decompiler is known to skip certain animation names — typically those
    /// with "@@" prefix (compressed motion-only variants used by autolayer references)
    /// and "@*lookFrame*" pose anims used by BlendList multipose sequences. Without
    /// these AnimFile nodes ModelDoc compile produces a .vmdl_c with fewer embedded
    /// animations than Valve original, breaking sequence references.
    ///
    /// This injector lists Valve's animations via <c>Model.GetEmbeddedAnimations()</c>,
    /// finds which names are missing from the .vmdl text, and appends new AnimFile
    /// blocks at the end of the existing AnimFile list (using the last one as a
    /// formatting/path template).
    /// </summary>
    private static string InjectMissingAnimations(
        string vmdlText, Resource resource, MergeSummary summary, bool verbose)
    {
        if (string.IsNullOrEmpty(vmdlText) || resource.DataBlock is not Model model) return vmdlText;

        // Collect Valve animation names.
        var valveAnims = new List<string>();
        try
        {
            foreach (var a in model.GetEmbeddedAnimations())
                if (!string.IsNullOrEmpty(a.Name)) valveAnims.Add(a.Name);
        }
        catch (Exception ex)
        {
            if (verbose) Console.WriteLine($"    [warn] InjectMissingAnimations: enumerate failed: {ex.Message}");
            return vmdlText;
        }
        if (valveAnims.Count == 0) return vmdlText;

        // Parse existing AnimFile nodes' names + capture them as template candidates.
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var animFileBlocks = new List<(int Start, int End, string FieldIndent, string Name, string SourceFilename)>();
        var animFileClassRx = new Regex(@"\A\{\s*_class\s*=\s*""AnimFile""", RegexOptions.Compiled);
        var nameInBlockRx = new Regex(@"^([ \t]*)name\s*=\s*""([^""]+)""\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
        var srcFileRx = new Regex(@"^[ \t]*source_filename\s*=\s*""([^""]+)""\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!animFileClassRx.IsMatch(block)) continue;
            var nm = nameInBlockRx.Match(block);
            if (!nm.Success) continue;
            var sm = srcFileRx.Match(block);
            string srcPath = sm.Success ? sm.Groups[1].Value : "";
            present.Add(nm.Groups[2].Value);
            animFileBlocks.Add((bStart, bEnd, nm.Groups[1].Value, nm.Groups[2].Value, srcPath));
        }

        if (animFileBlocks.Count == 0) return vmdlText; // nothing to use as template

        var missing = valveAnims.Where(n => !present.Contains(n)).ToList();
        if (missing.Count == 0) return vmdlText;

        // Build template params from last existing AnimFile block.
        var tpl = animFileBlocks[^1];
        string fieldIndent = tpl.FieldIndent;                             // e.g. "\t\t\t\t"
        // Container indent: one tab less than fieldIndent (the `{`/`}` line indent).
        string containerIndent = fieldIndent.Length > 0 ? fieldIndent.Substring(0, fieldIndent.Length - 1) : "";

        // Derive directory prefix from template's source_filename:
        // e.g. "models/heroes/shadow_fiend/foo.dmx" → "models/heroes/shadow_fiend/"
        string srcDir = "";
        if (!string.IsNullOrEmpty(tpl.SourceFilename))
        {
            int slash = tpl.SourceFilename.LastIndexOf('/');
            if (slash >= 0) srcDir = tpl.SourceFilename.Substring(0, slash + 1);
        }

        // Insert new entries after the last AnimFile's closing `}`.
        int insertAt = tpl.End + 1;

        var sb = new StringBuilder();
        foreach (var name in missing)
        {
            string srcFn = srcDir + name + ".dmx";
            sb.Append(",\r\n");
            sb.Append(containerIndent).Append("{\r\n");
            sb.Append(fieldIndent).Append("_class = \"AnimFile\"\r\n");
            sb.Append(fieldIndent).Append("name = \"").Append(name).Append("\"\r\n");
            sb.Append(fieldIndent).Append("source_filename = \"").Append(srcFn).Append("\"\r\n");
            sb.Append(fieldIndent).Append("fade_in_time = 0.2\r\n");
            sb.Append(fieldIndent).Append("fade_out_time = 0.2\r\n");
            // Default looping: true for run/idle/loop-like names, false for pose/lookFrame.
            bool looping = !name.Contains("lookFrame", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("pose", StringComparison.OrdinalIgnoreCase);
            sb.Append(fieldIndent).Append("looping = ").Append(looping ? "true" : "false").Append("\r\n");
            sb.Append(fieldIndent).Append("delta = false\r\n");
            sb.Append(fieldIndent).Append("worldSpace = false\r\n");
            sb.Append(fieldIndent).Append("hidden = true\r\n");
            sb.Append(containerIndent).Append("}");
        }

        if (verbose)
            Console.WriteLine($"    ✓ injected {missing.Count} missing AnimFile node(s): {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? $" +{missing.Count - 5} more" : "")}");

        return vmdlText.Substring(0, insertAt) + sb.ToString() + vmdlText.Substring(insertAt);
    }

    private static string DedupActivityCandidates(string vmdlText, bool verbose, MergeSummary summary)
    {
        var animFileClassRx = new Regex(@"\A\{\s*_class\s*=\s*""AnimFile""", RegexOptions.Compiled);
        var nameRx = new Regex(@"^[ \t]*name\s*=\s*""([^""]+)""\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
        var actNameRx = new Regex(@"^[ \t]*activity_name\s*=\s*""([^""]*)""\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
        var actWeightRx = new Regex(@"^[ \t]*activity_weight\s*=\s*(\d+)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
        var modInChildRx = new Regex(@"_class\s*=\s*""ActivityModifier""[^}]*?activity_name\s*=\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.Singleline);

        var metas = new List<(int Start, int End, string Name, string Activity, int Weight, List<string> Modifiers)>();

        foreach (var (bStart, bEnd, _) in FindBalancedBracePairs(vmdlText))
        {
            var block = vmdlText.Substring(bStart, bEnd - bStart + 1);
            if (!animFileClassRx.IsMatch(block)) continue;

            var nameM = nameRx.Match(block);
            if (!nameM.Success) continue;
            var animName = nameM.Groups[1].Value;

            // activity_name на верхнем уровне AnimFile (не у дочернего ActivityModifier).
            // Берём ВСЕ activity_name матчи и фильтруем по indent < indent ActivityModifier'ов.
            // Проще: ищем тот activity_name, который не входит ни в один child-блок.
            // Поскольку child-блоки имеют больший indent, top-level activity_name стоит
            // на том же уровне как `name = "..."`. Сравним по indent с nameM.
            var topIndent = Regex.Match(nameM.Value, @"^([ \t]*)").Groups[1].Value;
            string? topActName = null;
            int topActWeight = 0;
            foreach (Match m in actNameRx.Matches(block))
            {
                var lineIndent = Regex.Match(m.Value, @"^([ \t]*)").Groups[1].Value;
                if (lineIndent.Length == topIndent.Length)
                {
                    topActName = m.Groups[1].Value;
                    break;
                }
            }
            foreach (Match m in actWeightRx.Matches(block))
            {
                var lineIndent = Regex.Match(m.Value, @"^([ \t]*)").Groups[1].Value;
                if (lineIndent.Length == topIndent.Length)
                {
                    topActWeight = int.Parse(m.Groups[1].Value);
                    break;
                }
            }

            // Если activity_name пуст или это не ACT_-активити (e.g. "" уже отключён) —
            // эта sequence не участвует в активити-выборе, не дедуплицируем.
            if (string.IsNullOrEmpty(topActName) || !topActName.StartsWith("ACT_", StringComparison.Ordinal))
                continue;

            // Собираем модификаторы из ActivityModifier-детей.
            var modifiers = new List<string>();
            foreach (Match m in modInChildRx.Matches(block))
            {
                var modName = m.Groups[1].Value;
                if (!string.IsNullOrEmpty(modName)) modifiers.Add(modName);
            }
            modifiers.Sort(StringComparer.Ordinal);

            metas.Add((bStart, bEnd, animName, topActName, topActWeight, modifiers));
        }

        // Группировка по (activity, modifiers).
        var groups = metas
            .GroupBy(a => $"{a.Activity}|{string.Join(",", a.Modifiers)}")
            .Where(g => g.Count() > 1)
            .ToList();

        if (groups.Count == 0) return vmdlText;

        // Список изменений для лузеров (с конца к началу).
        var edits = new List<(int Pos, int Len, string Text)>();
        int losersCount = 0;
        var losersInfo = new List<(string Group, string Keeper, List<string> Losers)>();

        foreach (var group in groups)
        {
            var sorted = group.OrderByDescending(a => a.Weight).ThenBy(a => a.Start).ToList();
            var keeper = sorted[0];
            var losers = sorted.Skip(1).ToList();
            losersInfo.Add((group.Key, keeper.Name, losers.Select(l => l.Name).ToList()));

            foreach (var loser in losers)
            {
                losersCount++;
                var block = vmdlText.Substring(loser.Start, loser.End - loser.Start + 1);

                // 1) Заменить activity_name = "ACT_..." → activity_name = "" (только top-level).
                var actNameMatchInBlock = actNameRx.Matches(block);
                Match? topActNameM = null;
                var topIndent = Regex.Match(nameRx.Match(block).Value, @"^([ \t]*)").Groups[1].Value;
                foreach (Match m in actNameMatchInBlock)
                {
                    var lineIndent = Regex.Match(m.Value, @"^([ \t]*)").Groups[1].Value;
                    if (lineIndent.Length == topIndent.Length) { topActNameM = m; break; }
                }
                if (topActNameM != null)
                {
                    int absStart = loser.Start + topActNameM.Index;
                    var replacement = $"{topIndent}activity_name = \"\"";
                    edits.Add((absStart, topActNameM.Length, replacement));
                }

                // 1b) Top-level activity_weight = N → activity_weight = 0 (HARD-сигнал
                //     движку: «не выбирать»). Без этого Source 2 random-selector может
                //     всё равно подхватить кандидата, если weight > 0 и есть имя
                //     activity-модификатора в child-узле, который мы уже удалили.
                //     Делаем weight = 0 для надёжности.
                Match? topActWeightM = null;
                foreach (Match m in actWeightRx.Matches(block))
                {
                    var lineIndent = Regex.Match(m.Value, @"^([ \t]*)").Groups[1].Value;
                    if (lineIndent.Length == topIndent.Length) { topActWeightM = m; break; }
                }
                if (topActWeightM != null)
                {
                    int absStart = loser.Start + topActWeightM.Index;
                    var replacement = $"{topIndent}activity_weight = 0";
                    edits.Add((absStart, topActWeightM.Length, replacement));
                }

                // 1c) hidden = false → hidden = true (убираем из списка обычных
                //     sequences для случаев, когда движок ходит по всем
                //     non-hidden anim'ам и пытается «угадать» ACT_DOTA_RUN).
                var hiddenRx = new Regex(@"^[ \t]*hidden\s*=\s*false\s*$", RegexOptions.Multiline);
                Match? topHiddenM = null;
                foreach (Match m in hiddenRx.Matches(block))
                {
                    var lineIndent = Regex.Match(m.Value, @"^([ \t]*)").Groups[1].Value;
                    if (lineIndent.Length == topIndent.Length) { topHiddenM = m; break; }
                }
                if (topHiddenM != null)
                {
                    int absStart = loser.Start + topHiddenM.Index;
                    var replacement = $"{topIndent}hidden = true";
                    edits.Add((absStart, topHiddenM.Length, replacement));
                }

                // 2) Удалить все child-узлы _class = "ActivityModifier" внутри блока.
                // Каждый ActivityModifier — это `{ ... },` блок (с возможной запятой и переводом строки после).
                // Используем FindBalancedBracePairs внутри loser-блока.
                foreach (var (cStart, cEnd, _) in FindBalancedBracePairs(block))
                {
                    var childBlock = block.Substring(cStart, cEnd - cStart + 1);
                    if (!Regex.IsMatch(childBlock, @"\A\{\s*_class\s*=\s*""ActivityModifier""")) continue;

                    // Поглощаем trailing `,` + horizontal whitespace + один \r\n (или \n).
                    int blockEndIdx = cEnd + 1;
                    if (blockEndIdx < block.Length && block[blockEndIdx] == ',') blockEndIdx++;
                    while (blockEndIdx < block.Length && (block[blockEndIdx] == ' ' || block[blockEndIdx] == '\t'))
                        blockEndIdx++;
                    if (blockEndIdx < block.Length && block[blockEndIdx] == '\r') blockEndIdx++;
                    if (blockEndIdx < block.Length && block[blockEndIdx] == '\n') blockEndIdx++;

                    // Также захватываем leading whitespace (отступ строки этого блока).
                    int blockStartIdx = cStart;
                    while (blockStartIdx > 0 && (block[blockStartIdx - 1] == ' ' || block[blockStartIdx - 1] == '\t'))
                        blockStartIdx--;

                    int absChildStart = loser.Start + blockStartIdx;
                    int absChildLen = blockEndIdx - blockStartIdx;
                    edits.Add((absChildStart, absChildLen, ""));
                }
            }
        }

        // Применяем правки с конца к началу.
        edits.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var sb = new StringBuilder(vmdlText);
        foreach (var (pos, len, txt) in edits)
        {
            sb.Remove(pos, len);
            if (txt.Length > 0) sb.Insert(pos, txt);
        }

        if (verbose)
        {
            Console.WriteLine($"    ✓ deduped {losersCount} duplicate activity-modifier candidate(s) across {groups.Count} group(s)");
            foreach (var (key, keeper, losers) in losersInfo)
            {
                Console.WriteLine($"        [{key}] keep: {keeper}; disable: {string.Join(", ", losers)}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// VRF emits BodyGroupChoice nodes WITHOUT the required `name` field, but
    /// ModelDoc compile rejects them with "Invalid empty body group choice name in
    /// bodygroup '...'. Non-empty choice names are required." This pass derives a
    /// reasonable name from the first mesh in the choice's `meshes = [ "..." ]`
    /// array and injects it right after the `_class` line.
    /// </summary>
    private static string FixMissingBodyGroupChoiceNames(string text, out int count)
    {
        int c = 0;
        // Match: { _class = "BodyGroupChoice"  ... meshes = [ "first", ... ] ... }
        // Iterate balanced braces and only fix top-level BGC nodes lacking `name`.
        var pairs = FindBalancedBracePairs(text);
        // Apply edits from end to start so absolute offsets remain valid.
        var edits = new List<(int Pos, string InsertText)>();
        foreach (var (start, end, _) in pairs)
        {
            var nodeText = text.Substring(start, end - start + 1);
            if (!Regex.IsMatch(nodeText, @"\A\{\s*_class\s*=\s*""BodyGroupChoice""")) continue;
            // Skip if already has a top-level `name = "..."` (any non-empty literal).
            // We match indentation-aware: the `name` line must be at the same depth as `_class`.
            if (Regex.IsMatch(nodeText, @"^[ \t]+name\s*=\s*""[^""]+""\s*$", RegexOptions.Multiline))
                continue;
            // Extract first mesh name.
            var meshMatch = Regex.Match(nodeText, @"meshes\s*=\s*\[\s*""([^""]+)""", RegexOptions.Singleline);
            if (!meshMatch.Success) continue;
            var meshName = meshMatch.Groups[1].Value;
            // Find indentation of `_class` line (first line after `{`) to match.
            var classLine = Regex.Match(nodeText, @"\n([ \t]+)_class\s*=\s*""BodyGroupChoice""");
            var indent = classLine.Success ? classLine.Groups[1].Value : "\t\t\t\t\t\t";
            // Find absolute offset of end-of-line after `_class = "BodyGroupChoice"`.
            var classLineMatch = Regex.Match(nodeText, @"_class\s*=\s*""BodyGroupChoice""[ \t]*\r?\n");
            if (!classLineMatch.Success) continue;
            int insertOffsetInNode = classLineMatch.Index + classLineMatch.Length;
            int absInsert = start + insertOffsetInNode;
            var insertion = $"{indent}name = \"{meshName}\"\r\n";
            edits.Add((absInsert, insertion));
            c++;
        }
        if (edits.Count == 0) { count = 0; return text; }
        edits.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        var sb = new StringBuilder(text);
        foreach (var (pos, ins) in edits) sb.Insert(pos, ins);
        count = c;
        return sb.ToString();
    }

    /// <summary>
    /// Strip Hitbox/HitboxCapsule/HitboxSphere/Attachment nodes that reference
    /// `parent_bone` names not present in the skeleton. ModelDoc compile prints
    /// "Unknown bone 'X'" warnings and drops these nodes anyway — doing it
    /// upstream gives a cleaner build log and prevents downstream tooling
    /// confusion. Common case: `cp_*` collision-proxy bones referenced by
    /// hitboxes but missing from the source skeleton (they live in animgraph
    /// constraint output, not the source rig).
    /// </summary>
    private static string StripHitboxAttachmentUnknownBones(string text, out int count)
    {
        // Collect all skeleton bone names by pairing `_class = "Bone"` with the
        // adjacent `name = "..."` field at any indentation level.
        var bones = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, @"_class\s*=\s*""Bone""\s*\r?\n[ \t]*name\s*=\s*""([^""]+)""", RegexOptions.Singleline))
            bones.Add(m.Groups[1].Value);
        // Also accept order: `name = "X"` then `_class = "Bone"` (rare, but VRF
        // sometimes emits fields in either order depending on KV3 codec).
        foreach (Match m in Regex.Matches(text, @"name\s*=\s*""([^""]+)""\s*\r?\n[ \t]*_class\s*=\s*""Bone""", RegexOptions.Singleline))
            bones.Add(m.Groups[1].Value);

        // Note: even with 0 bones we still strip — props/static models lack a
        // skeleton, and any hitbox/attachment they carry references nothing
        // valid (compile would warn and drop them). Exception: ModelDoc treats
        // empty string `""` as "no parent" — attachments mounted to model
        // origin — which is always valid.

        int c = 0;
        var result = RemoveMatchingNodes(text, (cls, nodeText) =>
        {
            // Only act on hitbox/attachment node families.
            if (cls != "Hitbox" && cls != "HitboxCapsule" && cls != "HitboxSphere" && cls != "Attachment")
                return false;
            var pbm = Regex.Match(nodeText, @"parent_bone\s*=\s*""([^""]*)""");
            if (!pbm.Success) return false;
            var bn = pbm.Groups[1].Value;
            // Empty parent_bone is special-cased by ModelDoc as "no parent".
            if (string.IsNullOrEmpty(bn)) return false;
            if (bones.Contains(bn)) return false;
            c++;
            return true;
        });
        count = c;
        return result;
    }

    private static string FixBogusResourceTags(string text, out int count)
    {
        // VRF мисс-типизирует поле `tag` события AE_CL_SUPPRESS_EVENTS_WITH_TAG как
        // resource:"name", но это просто строковый идентификатор, не путь к ассету.
        // ModelDoc валится с "Bad resource reference" → "Tried to register an empty
        // resource reference" → Compile Failed. Переписываем как plain string.
        var pattern = @"^([ \t]*tag[ \t]*=[ \t]*)resource:""([^""]*)""([ \t]*\r?\n)";
        int c = 0;
        var newText = Regex.Replace(text, pattern, m =>
        {
            c++;
            return $"{m.Groups[1].Value}\"{m.Groups[2].Value}\"{m.Groups[3].Value}";
        }, RegexOptions.Multiline);
        count = c;
        return newText;
    }

    private static bool IsInvalidResourceNode(string cls, string nodeText)
    {
        // VRF выводит resource-ссылочные поля даже если они пустые — ModelDoc на это
        // ругается "Tried to register an empty resource reference" и валит компиляцию.
        // Разные классы используют разные имена поля.
        var field = cls switch
        {
            "AnimGraph2" or "DefaultAnimGraph2" or "NmSkeletonReference" or "MorphFile" => "filename",
            "AnimIncludeModel" => "model",
            "AnimFile" => "source_filename",
            _ => null,
        };
        if (field == null) return false;
        return Regex.IsMatch(nodeText, $@"\b{field}\s*=\s*""\s*""");
    }

    private static bool IsBrokenAutoLayer(string cls, string nodeText, HashSet<string> validAnims)
    {
        // AnimAddLayer / AnimSubtractLayer ссылаются на анимацию по имени.
        //
        // ИСТОРИЯ ВОПРОСА:
        // VRF (19.1.6199) НЕ извлекает multi-pose AnimSequence (m_b1D/m_b2D/m_bMulti)
        // как .vmdl-узлы — например `turns_arcana` у SF arcana — это AnimSequence с
        // 1D-pose-param `turn` и тремя sub-references. В .vmdl source такой sequence
        // отсутствует, поэтому AnimAddLayer-ссылки на него становятся "orphan" и
        // ModelDoc UI показывает "Invalid animation reference" warning'и.
        //
        // ПРОВЕРЕНО (08.05.2026):
        // ModelDoc compile DROP'ает orphan AnimAddLayer-references — в нашем compiled
        // .vmdl_c НЕТ SeqDesc для turns_arcana (0 mentions vs 10 у Valve original).
        // Это значит layer'ы уже фактически НЕ работают в игре — просто warning'и в
        // UI остаются от unresolved string references.
        //
        // РЕШЕНИЕ:
        // Удаляем AnimAddLayer/AnimSubtractLayer узлы со ссылкой на animation,
        // которой нет в .vmdl source. Это идентично текущему поведению (compile
        // и так их выкидывает), но убирает warning'и в ModelDoc UI.
        if (cls != "AnimAddLayer" && cls != "AnimSubtractLayer") return false;
        var match = Regex.Match(nodeText, @"\b(?:anim_name|anim)\s*=\s*""([^""]*)""");
        if (!match.Success) return true; // вообще нет поля anim_name — ломанный
        var animName = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(animName)) return true; // пустое имя
        // Orphan reference: anim_name указан, но такой AnimFile/AnimAlias нет в .vmdl.
        return !validAnims.Contains(animName);
    }

    private static HashSet<string> CollectAnimNames(string text)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (start, end, _) in FindBalancedBracePairs(text))
        {
            var nodeText = text.Substring(start, end - start + 1);
            var classMatch = Regex.Match(nodeText, @"\A\{\s*_class\s*=\s*""([^""]+)""");
            if (!classMatch.Success) continue;
            var cls = classMatch.Groups[1].Value;
            if (cls != "AnimFile" && cls != "AnimAlias") continue;
            var nameMatch = Regex.Match(nodeText, @"\bname\s*=\s*""([^""]+)""");
            if (nameMatch.Success) names.Add(nameMatch.Groups[1].Value);
        }
        return names;
    }

    /// <summary>
    /// Add to <paramref name="names"/> all sequence names from the ASEQ block
    /// (m_localS1SeqDescArray). Used to extend alidAnims so that AutoLayer
    /// references to multi-pose Sequence names (e.g. turns_arcana, which VRF
    /// does NOT emit as a .vmdl Sequence node) are not classified as orphan
    /// references and removed by the sanitizer.
    /// </summary>
    private static void CollectSequenceNamesFromAseq(Resource resource, HashSet<string> names)
    {
        try
        {
            if (resource.GetBlockByType(BlockType.ASEQ) is not KeyValuesOrNTRO aseq) return;
            if (aseq.Data is not KVObject seqData) return;
            IReadOnlyList<KVObject>? sequences = null;
            try { sequences = seqData.GetArray("m_localS1SeqDescArray"); } catch { }
            if (sequences == null) return;
            foreach (var seq in sequences)
            {
                string? seqName = null;
                try { seqName = seq.GetStringProperty("m_sName"); } catch { }
                if (!string.IsNullOrEmpty(seqName)) names.Add(seqName);
            }
        }
        catch { /* best-effort: missing/unparseable ASEQ - sanitizer falls back */ }
    }
    private static List<(int Start, int End, int Depth)> FindBalancedBracePairs(string text)
    {
        var result = new List<(int, int, int)>();
        var stack = new Stack<int>();
        int depth = 0;
        bool inString = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '"' && text[i - 1] != '\\') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': stack.Push(i); depth++; break;
                case '}':
                    if (stack.Count > 0)
                    {
                        var openIdx = stack.Pop();
                        depth--;
                        result.Add((openIdx, i, depth + 1));
                    }
                    break;
                case '[': depth++; break;
                case ']': depth--; break;
            }
        }
        return result;
    }

    // v5.3.1: remove every `{ _class = "AnimAddLayer" anim_name = "<name>" }`
    // (or AnimAddPoseLayer / AnimAddWorldSpaceLayer) whose anim_name targets
    // a multipose (m_bMulti=1) sequence. VRF cannot reconstruct multipose
    // blend graphs - it emits the multipose as a plain AnimFile pointing at
    // a duplicate of the first lookFrame DMX. When ModelDoc compiles such
    // an AnimAddLayer, the engine adds that single non-zero "look" pose
    // additively onto the host run animation, producing systematic body
    // flipping in match (where AnimGraph2 runs) but invisible in profile
    // preview (which plays activities raw without overlays).
    private static string StripMultiposeAddLayers(
        string text, HashSet<string> multiposeNames, bool verbose, MergeSummary summary)
    {
        var animNameRx = new Regex(@"anim_name\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        int stripped = 0;
        var hits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = RemoveMatchingNodes(text, (cls, nodeText) =>
        {
            if (cls != "AnimAddLayer" && cls != "AnimAddPoseLayer" && cls != "AnimAddWorldSpaceLayer")
                return false;
            var m = animNameRx.Match(nodeText);
            if (!m.Success) return false;
            var an = m.Groups[1].Value;
            if (!multiposeNames.Contains(an)) return false;
            stripped++;
            hits.Add(an);
            return true;
        });
        if (verbose && stripped > 0)
            Console.WriteLine(
                $"    [v5.3.1] stripped {stripped} AnimAddLayer ref(s) to multipose sequence(s) ({string.Join(", ", hits)})");
        return result;
    }

    private static string RemoveMatchingNodes(string text, Func<string, string, bool> shouldRemove)
    {
        var pairs = FindBalancedBracePairs(text);
        var ranges = new List<(int Start, int End)>();

        foreach (var (start, end, _) in pairs)
        {
            var nodeText = text.Substring(start, end - start + 1);
            var match = Regex.Match(nodeText, @"\A\{\s*_class\s*=\s*""([^""]+)""");
            if (!match.Success) continue;
            if (!shouldRemove(match.Groups[1].Value, nodeText)) continue;

            // Захватываем ведущие пробелы/табы и хвостовую запятую+перевод строки,
            // чтобы не оставить за собой висячие "," и пустые строки.
            int s = start;
            while (s > 0 && (text[s - 1] == ' ' || text[s - 1] == '\t')) s--;
            int e = end + 1;
            while (e < text.Length && (text[e] == ' ' || text[e] == '\t')) e++;
            if (e < text.Length && text[e] == ',') e++;
            while (e < text.Length && (text[e] == ' ' || text[e] == '\t')) e++;
            if (e < text.Length - 1 && text[e] == '\r' && text[e + 1] == '\n') e += 2;
            else if (e < text.Length && text[e] == '\n') e++;
            ranges.Add((s, e));
        }

        if (ranges.Count == 0) return text;

        // Сортируем и схлопываем вложенные диапазоны (внешний поглощает внутренние),
        // чтобы StringBuilder.Remove не работал по уже удалённой области.
        ranges.Sort((a, b) => a.Start - b.Start);
        var merged = new List<(int Start, int End)>();
        int lastEnd = -1;
        foreach (var r in ranges)
        {
            if (r.Start >= lastEnd)
            {
                merged.Add(r);
                lastEnd = r.End;
            }
        }

        var sb = new StringBuilder(text);
        for (int i = merged.Count - 1; i >= 0; i--)
            sb.Remove(merged[i].Start, merged[i].End - merged[i].Start);
        return sb.ToString();
    }

    /// <summary>
    /// Walks every .dmx in <paramref name="modelFolder"/>, harvests material
    /// references (DmeMaterial.mtlName), and creates a tiny stub .vmat for any
    /// path that does not resolve in <paramref name="loader"/> (i.e. missing
    /// from the source VPK + addon content). Returns the number of stubs
    /// created. Stubs are placed at the addon-relative material path so that
    /// the user's resourcecompiler resolves them locally.
    ///
    /// Why: VRF emits DMX with the material name baked in by Valve at original
    /// compile time. For Dota some heroes ship with `heroes_staging/...`
    /// dev-only paths whose .vmat was never published (e.g. `pudge_cute`).
    /// ModelDoc warns "Mesh 'X' referencing missing material 'Y'". Stubs
    /// silence the warning while remaining inert at runtime (the model uses
    /// the MaterialGroupList remap target, not the staging path).
    /// </summary>
    private static int CreateStubMaterialsForMissingRefs(string modelFolder, IFileLoader loader, bool verbose)
    {
        int created = 0;
        // Determine output content root by walking up from modelFolder until
        // we leave a `models/` subtree. Stubs go under <root>/<vmatRelPath>.
        var contentRoot = modelFolder;
        while (true)
        {
            var parent = Path.GetDirectoryName(contentRoot);
            if (string.IsNullOrEmpty(parent)) break;
            var name = Path.GetFileName(contentRoot);
            if (string.Equals(name, "models", StringComparison.OrdinalIgnoreCase))
            {
                contentRoot = parent;
                break;
            }
            contentRoot = parent;
        }

        // Collect material refs from every .dmx under modelFolder.
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dmxPath in Directory.EnumerateFiles(modelFolder, "*.dmx", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var fs = File.OpenRead(dmxPath);
                using var dm = Datamodel.Datamodel.Load(fs, Datamodel.Codecs.DeferredMode.Disabled);
                foreach (var elem in dm.AllElements)
                {
                    if (elem == null || elem.ClassName != "DmeMaterial") continue;
                    if (elem["mtlName"] is string s && !string.IsNullOrEmpty(s)) refs.Add(s);
                }
            }
            catch { /* skip unreadable DMX */ }
        }

        foreach (var matRef in refs)
        {
            // Normalize: ensure ends with `.vmat`, leading slashes stripped.
            var rel = matRef.TrimStart('/').Replace('\\', '/');
            if (!rel.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase)) rel += ".vmat";
            // Already exists upstream (in VPK or addon)? Skip.
            try
            {
                using var existing = loader.LoadFile(rel + "_c");
                if (existing != null) continue;
            }
            catch { }
            // Compute target path under contentRoot (cross-platform separators).
            var target = Path.Combine(contentRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target)) continue;
            // Minimal vmat: shader=error, no params. Compile gives 1 vmat_c stub.
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target,
                    "// stub vmat generated by VmdlExtractor — missing-material warning suppression\n" +
                    "Layer0\n{\n\tshader \"error.vfx\"\n}\n");
                created++;
                if (verbose) Console.WriteLine($"    ↳ STUB {rel}");
            }
            catch { }
        }
        return created;
    }

    /// <summary>
    /// Detect a marker-only DMX (no per-vertex normal/texcoord streams) that
    /// segfaults Workshop Tools' resourcecompiler.exe. Used to drop those
    /// files + their RenderMeshFile refs from .vmdl.
    /// </summary>
    private static bool IsMarkerOnlyDmx(string dmxPath)
    {
        Datamodel.Datamodel? dm = null;
        try
        {
            using var fs = File.OpenRead(dmxPath);
            dm = Datamodel.Datamodel.Load(fs, Datamodel.Codecs.DeferredMode.Disabled);
            foreach (var elem in dm.AllElements)
            {
                if (elem == null || elem.ClassName != "DmeVertexData") continue;
                bool hasNormal = false, hasTexcoord = false;
                foreach (var key in elem.Keys)
                {
                    if (key.StartsWith("normal", StringComparison.OrdinalIgnoreCase) && !key.Contains("Indices")) hasNormal = true;
                    if (key.StartsWith("texcoord", StringComparison.OrdinalIgnoreCase) && !key.Contains("Indices")) hasTexcoord = true;
                }
                // Marker DMX: emits only position+blend* streams, no normals/UVs.
                if (!hasNormal && !hasTexcoord) return true;
            }
            return false;
        }
        catch { return false; }
        finally { dm?.Dispose(); }
    }

    /// <summary>
    /// Remove every RenderMeshFile node whose `filename` (or `name`) basename
    /// matches one of <paramref name="markerBasenames"/>. Used after marker DMX
    /// detection — the corresponding mesh file no longer exists on disk so the
    /// reference must be pruned to keep ModelDoc compile happy.
    /// </summary>
    private static string StripMarkerRenderMeshFiles(string text, HashSet<string> markerBasenames, out int count)
    {
        int c = 0;
        var result = RemoveMatchingNodes(text, (cls, nodeText) =>
        {
            if (cls != "RenderMeshFile") return false;
            // Try `filename = ".../X.dmx"` first.
            var fnm = Regex.Match(nodeText, @"filename\s*=\s*""([^""]+)""");
            if (fnm.Success)
            {
                var bn = Path.GetFileNameWithoutExtension(fnm.Groups[1].Value);
                if (markerBasenames.Contains(bn)) { c++; return true; }
            }
            // Fallback: match by `name = "X"` if filename missing/unparseable.
            var nm = Regex.Match(nodeText, @"\bname\s*=\s*""([^""]+)""");
            if (nm.Success && markerBasenames.Contains(nm.Groups[1].Value)) { c++; return true; }
            return false;
        });
        count = c;
        return result;
    }

    private static void PatchDmxMeshBoneIndices(string dmxPath, Skeleton? skeleton, MergeSummary summary, bool verbose)
    {
        // === Что чиним ===
        // VRF 19.1.6199 в ConvertMeshToDatamodelMesh НЕ вызывает BuildDmeDagSkeleton
        // (а сама BuildDmeDagSkeleton там сломана: добавляет DmeModel в JointList).
        // Из-за этого DmeModel.JointList в выходных DMX = [DmeDag(mesh)] (1 элемент),
        // а blendindices$0 содержит глобальные индексы костей 0..N-1.
        // ModelDoc валидирует blendindices ∈ [0, JointList.Count-1] = [0, 0],
        // клампит всё, что вне диапазона, в -1 и пишет warning
        //   "Invalid skinning bone index :: -1, valid range [0, 0]".
        // В рантайме клампинг идёт в bone 0 (root) → вершины паразитно тянутся
        // за root_motion в turns_anim / idle → визуальный стретчинг.
        //
        // === Как чиним ===
        // 1) Внедряем правильный скелет в DmeModel: DmeJoint per bone в bone.Index
        //    порядке (1:1 повторяет master VRF BuildDmeDagSkeleton), строим
        //    parent→Children иерархию, mesh DmeDag оставляем в конце JointList и
        //    в DmeModel.Children. После этого blendindices$0[i] напрямую индексирует
        //    JointList[i] = bone i. ModelDoc больше не клампит, стретчинг исчезает.
        // 2) Дефенсивно: если в blendindices$0 всё же остались -1 (некоторые
        //    устаревшие стримы у физ.прокси), зануляем index=0, weight=0.
        Datamodel.Datamodel? dm = null;
        try
        {
            using var fs = File.OpenRead(dmxPath);
            dm = Datamodel.Datamodel.Load(fs, Datamodel.Codecs.DeferredMode.Disabled);
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"    dmx patch: load failed '{Path.GetFileName(dmxPath)}': {ex.Message}");
            return;
        }

        int weightsPatched = 0;
        int streamsTouched = 0;
        int jointsInjected = 0;
        bool modified = false;
        try
        {
            // 1) Skeleton injection.
            if (skeleton is { Bones.Length: > 0 })
            {
                jointsInjected = InjectSkeletonIntoDmeModel(dm, skeleton);
                if (jointsInjected > 0) modified = true;
            }

            // 2) Defensive blendindices clamp (works on the same in-memory dm).
            foreach (var elem in dm.AllElements)
            {
                if (elem == null) continue;
                foreach (var key in elem.Keys.ToList())
                {
                    if (!key.StartsWith("blendindices$", StringComparison.Ordinal)) continue;
                    var suffix = key.Substring("blendindices$".Length);
                    var weightsKey = "blendweights$" + suffix;
                    if (!elem.ContainsKey(weightsKey)) continue;

                    int streamPatched = TryPatchBlendStream(elem, key, weightsKey);
                    if (streamPatched > 0)
                    {
                        weightsPatched += streamPatched;
                        streamsTouched++;
                        modified = true;
                    }
                }
            }

            if (modified)
            {
                using var ofs = File.Create(dmxPath);
                dm.Save(ofs, dm.Encoding, dm.EncodingVersion);
                summary.PatchedDmxFiles++;
                if (jointsInjected > 0)
                {
                    summary.PatchedDmxSkeletons++;
                    summary.PatchedDmxJoints += jointsInjected;
                }
                if (weightsPatched > 0)
                {
                    summary.PatchedDmxWeights += weightsPatched;
                }
                if (verbose)
                {
                    var parts = new List<string>(2);
                    if (jointsInjected > 0) parts.Add($"+skeleton({jointsInjected} joints)");
                    if (weightsPatched > 0) parts.Add($"-1 weights={weightsPatched} in {streamsTouched} stream(s)");
                    Console.WriteLine($"    ✓ dmx fix {Path.GetFileName(dmxPath)}: {string.Join(", ", parts)}");
                }
            }
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"    dmx patch: process failed '{Path.GetFileName(dmxPath)}': {ex.Message}");
        }
        finally
        {
            dm?.Dispose();
        }
    }

    // Внедряет в DmeModel внутри уже загруженного `dm` правильный скелет:
    //   JointList = [DmeJoint(bone0), ..., DmeJoint(boneN-1), <existing mesh DmeDag(s)>]
    //   Children  = [<root DmeJoint>..., <existing mesh DmeDag(s)>]
    // Мутирует существующий DmeModel.JointList / Children. Возвращает кол-во
    // добавленных DmeJoint, или 0 если DMX уже пропатчен / DmeModel не найден.
    private static int InjectSkeletonIntoDmeModel(Datamodel.Datamodel dm, Skeleton skeleton)
    {
        // Находим DmeModel в дереве. У DMX-mesh он один и единственный.
        Element? dmeModel = null;
        foreach (var elem in dm.AllElements)
        {
            if (elem != null && elem.ClassName == "DmeModel") { dmeModel = elem; break; }
        }
        if (dmeModel == null) return 0;
        if (dmeModel["jointList"] is not ElementArray jointList) return 0;
        if (dmeModel["children"] is not ElementArray children) return 0;

        // Идемпотентность: если хоть один DmeJoint уже в JointList — DMX уже патчен.
        for (int i = 0; i < jointList.Count; i++)
        {
            if (jointList[i] != null && jointList[i].ClassName == "DmeJoint") return 0;
        }

        // Сохраняем "не-joint" entries (mesh DmeDag-и) — их вернём после костей.
        var preservedJoints = new List<Element>(jointList.Count);
        for (int i = 0; i < jointList.Count; i++)
        {
            if (jointList[i] != null) preservedJoints.Add(jointList[i]);
        }
        var preservedChildren = new List<Element>(children.Count);
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] != null) preservedChildren.Add(children[i]);
        }

        // Строим DmeJoint per bone — типы public, .NET-ная фабрика сама
        // зарегистрирует элементы в `dm` при добавлении в ElementArray.
        var bones = skeleton.Bones;
        var jointByIndex = new DmeJoint[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            var joint = new DmeJoint { Name = bone.Name };
            joint.Transform.Name = bone.Name;
            joint.Transform.Position = bone.Position;
            joint.Transform.Orientation = bone.Angle;
            // Shape — оставляем default (DmeShape), он не мешает скиннингу.
            jointByIndex[bone.Index] = joint;
        }

        // Иерархия: child bone → parent.Children, root bone → dmeModel.Children.
        // Сначала собираем root bones для children, parented bones — в parent.Children.
        var rootJoints = new List<DmeJoint>();
        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            var joint = jointByIndex[bone.Index];
            if (bone.Parent != null)
            {
                jointByIndex[bone.Parent.Index].Children.Add(joint);
            }
            else
            {
                rootJoints.Add(joint);
            }
        }

        // Перезаписываем JointList: bones first (in bone.Index order), потом mesh dags.
        jointList.Clear();
        for (int i = 0; i < bones.Length; i++) jointList.Add(jointByIndex[i]);
        foreach (var meshDag in preservedJoints) jointList.Add(meshDag);

        // Перезаписываем Children: roots first, потом существующие mesh dags.
        children.Clear();
        foreach (var root in rootJoints) children.Add(root);
        foreach (var existing in preservedChildren) children.Add(existing);

        return bones.Length;
    }

    // Заменяет в паре blendindices$N / blendweights$N все отрицательные индексы
    // на 0 с обнулением соответствующего веса. Возвращает кол-во изменённых слотов.
    // Datamodel.NET может хранить массивы как IntArray/FloatArray (CodecBinary 9)
    // ИЛИ как голые int[]/float[] (KV2/KV3 текст), поэтому ловим оба случая.
    private static int TryPatchBlendStream(Datamodel.Element elem, string indicesKey, string weightsKey)
    {
        var indicesObj = elem[indicesKey];
        var weightsObj = elem[weightsKey];

        // Случай 1: типизированные обёртки Datamodel.NET (binary 9 / model 22).
        if (indicesObj is IntArray ia && weightsObj is FloatArray fa)
        {
            if (ia.Count != fa.Count) return 0;
            int n = 0;
            for (int i = 0; i < ia.Count; i++)
            {
                if (ia[i] < 0)
                {
                    ia[i] = 0;
                    fa[i] = 0f;
                    n++;
                }
            }
            return n;
        }

        // Случай 2: голые массивы (некоторые кодеки используют int[]/float[]).
        if (indicesObj is int[] iarr && weightsObj is float[] farr)
        {
            if (iarr.Length != farr.Length) return 0;
            int n = 0;
            for (int i = 0; i < iarr.Length; i++)
            {
                if (iarr[i] < 0)
                {
                    iarr[i] = 0;
                    farr[i] = 0f;
                    n++;
                }
            }
            // Голые массивы — value type на уровне Element.SetValue должен сохраниться,
            // т.к. мы мутировали элементы массива по индексу in-place.
            return n;
        }

        return 0;
    }

    // ───────────────────────────── Include-deps (v5.1) ─────────────────────────────

    private static List<string> GetAnimIncludeModelRefs(Model m)
    {
        var result = new List<string>();

        var method = m.GetType().GetMethod(
            "GetReferencedAnimationIncludeModelNames",
            BindingFlags.Public | BindingFlags.Instance);
        if (method != null)
        {
            try
            {
                if (method.Invoke(m, null) is System.Collections.IEnumerable enumerable)
                    foreach (var o in enumerable)
                        if (o is string s && !string.IsNullOrEmpty(s)) result.Add(s);
            }
            catch { }
        }
        if (result.Count > 0) return result;

        try
        {
            if (m.Data == null || !m.Data.ContainsKey("m_refAnimIncludeModels")) return result;

            try
            {
                var arr = m.Data.GetArray<string>("m_refAnimIncludeModels");
                if (arr != null)
                    foreach (var s in arr) if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
            catch { }

            if (result.Count == 0)
            {
                var arr = m.Data.GetArray("m_refAnimIncludeModels");
                if (arr != null)
                    foreach (var item in arr)
                    {
                        try
                        {
                            var s = item.GetStringProperty("m_includeModel");
                            if (!string.IsNullOrEmpty(s)) result.Add(s);
                        }
                        catch { }
                    }
            }
        }
        catch { }

        return result;
    }

    // ───────────────────────────── Reflection / KV helpers ─────────────────────────────

    private static void SetPrivateProperty<T>(object target, string propertyName, T value)
    {
        var prop = target.GetType().GetProperty(
            propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return;
        var setter = prop.GetSetMethod(nonPublic: true);
        setter?.Invoke(target, [value]);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var f = target.GetType().GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        f?.SetValue(target, value);
    }

    private static KVObject? TryGetSubCollection(KVObject? kv, string key)
    {
        if (kv == null || !kv.ContainsKey(key)) return null;
        try { return kv.GetSubCollection(key); }
        catch { return null; }
    }

    private static IReadOnlyList<KVObject>? TryGetArray(KVObject kv, string key)
    {
        try { return kv.GetArray(key); }
        catch { return null; }
    }

    private static T[]? TryGetArray<T>(KVObject kv, string key)
    {
        try { return kv.GetArray<T>(key); }
        catch { return null; }
    }

    private static string SafeGetString(KVObject kv, string key)
    {
        try { return kv.GetStringProperty(key) ?? string.Empty; }
        catch { return string.Empty; }
    }

    // ───────────────────────────── Verbose summary ─────────────────────────────

    private static void PrintMergeSummary(MergeSummary s)
    {
        Console.WriteLine("    ── merge summary ──");
        Console.WriteLine($"      .vmesh_c loaded         : {s.VmeshLoaded}");
        Console.WriteLine($"      .vmorf_c loaded         : {s.VmorfLoaded}");
        Console.WriteLine($"      hitboxes merged         : {s.HitboxesMerged}");
        Console.WriteLine($"      attachments merged      : {s.AttachmentsMerged}");
        Console.WriteLine($"      flex controllers        : {s.FlexControllersMerged}");
        Console.WriteLine($"      synth skeleton bones    : {s.SyntheticSkeletonBones}");
        Console.WriteLine($"      additional phys files   : {s.AdditionalPhysFiles}");
        Console.WriteLine($"      additional phys shapes  : {s.AdditionalPhysShapes}");
        Console.WriteLine($"      sanitized invalid nodes : {s.SanitizedInvalidNodes}");
        Console.WriteLine($"      sanitized auto layers   : {s.SanitizedBrokenAutoLayers}");
        Console.WriteLine($"      include-deps queued     : {s.IncludeDepsQueued}");
        Console.WriteLine($"      sanitized empty lines   : {s.SanitizedEmptyLines}");
        Console.WriteLine($"      bogus resource tags     : {s.SanitizedBogusResourceTags}");
        Console.WriteLine($"      patched dmx files       : {s.PatchedDmxFiles}");
        Console.WriteLine($"      patched dmx weights     : {s.PatchedDmxWeights}");
        Console.WriteLine($"      injected dmx skeletons  : {s.PatchedDmxSkeletons}");
        Console.WriteLine($"      injected dmx joints     : {s.PatchedDmxJoints}");
        Console.WriteLine($"      activity_modifiers      : {s.ActivityModifiersInjected}");
        Console.WriteLine($"      framerate injects       : {s.FrameRatesInjected}");
    }

    // ───────────────────────────── Deep dep extraction ─────────────────────────────

    private static void ExtractDepsRecursive(
        Resource topResource, string modelFolder,
        IFileLoader fileLoader, RawReader rawReader,
        HashSet<string> writtenDmx, bool verbose)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        EnqueueRefs(topResource, queue, visited);

        int rawCopied = 0, rawMissing = 0, deepDmx = 0, sourceTexts = 0;

        while (queue.Count > 0)
        {
            var refPath = queue.Dequeue();
            var compiledPath = refPath + "_c";

            var bytes = rawReader(compiledPath);
            if (bytes == null)
            {
                rawMissing++;
                if (verbose) Console.Error.WriteLine($"    ↳ RAW  miss {compiledPath}");
                continue;
            }

            File.WriteAllBytes(Path.Combine(modelFolder, Path.GetFileName(compiledPath)), bytes);
            rawCopied++;
            if (verbose) Console.WriteLine($"    ↳ RAW  {Path.GetFileName(compiledPath)} ({bytes.Length / 1024} KB)");

            Resource? depResource = null;
            try
            {
                using var ms = new MemoryStream(bytes);
                depResource = new Resource { FileName = compiledPath };
                depResource.Read(ms);
            }
            catch (Exception ex)
            {
                if (verbose) Console.Error.WriteLine($"          parse failed: {ex.Message}");
                continue;
            }

            if (verbose)
            {
                var blockList = string.Join(", ", depResource.Blocks.Select(b => b.Type.ToString()));
                Console.WriteLine($"          blocks: [{blockList}]");
            }

            try
            {
                using var depContent = FileExtract.Extract(depResource, fileLoader);
                if (depContent.Data != null && depContent.Data.Length > 0)
                {
                    var srcExt = FileExtract.GetExtension(depResource)
                                 ?? Path.GetExtension(refPath).TrimStart('.');
                    var srcName = Path.GetFileNameWithoutExtension(compiledPath) + "." + srcExt;
                    File.WriteAllBytes(Path.Combine(modelFolder, srcName), depContent.Data);
                    sourceTexts++;
                    if (verbose) Console.WriteLine($"          ↳ SRC  {srcName} ({depContent.Data.Length / 1024} KB)");
                }
                foreach (var dsub in depContent.SubFiles)
                {
                    try
                    {
                        var dsubData = dsub.Extract?.Invoke();
                        if (dsubData == null || dsubData.Length == 0) continue;
                        var dsubName = Path.GetFileName(dsub.FileName);
                        if (writtenDmx.Contains(dsubName)) continue;
                        File.WriteAllBytes(Path.Combine(modelFolder, dsubName), dsubData);
                        writtenDmx.Add(dsubName);
                        deepDmx++;
                        if (verbose) Console.WriteLine($"          ↳ DMX  {dsubName} ({dsubData.Length / 1024} KB) (deep)");
                    }
                    catch (Exception ex)
                    {
                        if (verbose) Console.Error.WriteLine($"          deep subfile threw: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (verbose) Console.Error.WriteLine($"          source-extract failed: {ex.Message}");
            }

            if (compiledPath.EndsWith(".vmesh_c", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (depResource.DataBlock is Mesh mesh)
                    {
                        var basename = Path.GetFileNameWithoutExtension(compiledPath);
                        var dmxName = basename + ".dmx";
                        if (!writtenDmx.Contains(dmxName))
                        {
                            var dmxBytes = ModelExtract.ToDmxMesh(mesh, basename);
                            if (dmxBytes != null && dmxBytes.Length > 0)
                            {
                                File.WriteAllBytes(Path.Combine(modelFolder, dmxName), dmxBytes);
                                writtenDmx.Add(dmxName);
                                deepDmx++;
                                if (verbose) Console.WriteLine($"          ↳ DMX* {dmxName} ({dmxBytes.Length / 1024} KB) (direct ToDmxMesh)");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (verbose) Console.Error.WriteLine($"          direct ToDmxMesh failed: {ex.Message}");
                }
            }

            if (compiledPath.EndsWith(".vphys_c", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (depResource.DataBlock is PhysAggregateData phys)
                    {
                        var physExtract = new ModelExtract(phys, Path.GetFileName(compiledPath));
                        using var physContent = physExtract.ToContentFile();
                        foreach (var psub in physContent.SubFiles)
                        {
                            try
                            {
                                var psubData = psub.Extract?.Invoke();
                                if (psubData == null || psubData.Length == 0) continue;
                                var psubName = Path.GetFileName(psub.FileName);
                                if (writtenDmx.Contains(psubName)) continue;
                                File.WriteAllBytes(Path.Combine(modelFolder, psubName), psubData);
                                writtenDmx.Add(psubName);
                                deepDmx++;
                                if (verbose) Console.WriteLine($"          ↳ DMX* {psubName} ({psubData.Length / 1024} KB) (direct phys)");
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (verbose) Console.Error.WriteLine($"          direct phys-extract failed: {ex.Message}");
                }
            }

            try { EnqueueRefs(depResource, queue, visited); } catch { }
            depResource.Dispose();
        }

        if (verbose)
            Console.WriteLine(
                $"    deep deps: raw_copied={rawCopied}, raw_missing={rawMissing}, " +
                $"source_texts={sourceTexts}, deep_dmx={deepDmx}");
    }

    private static void EnqueueRefs(Resource resource, Queue<string> queue, HashSet<string> visited)
    {
        var refs = resource.ExternalReferences;
        if (refs == null) return;

        foreach (var info in refs.ResourceRefInfoList)
        {
            var name = info.Name;
            if (string.IsNullOrEmpty(name)) continue;

            var dotIdx = name.LastIndexOf('.');
            if (dotIdx < 0) continue;
            var ext = name[dotIdx..];

            bool match = false;
            foreach (var allowed in ModelDepExts)
                if (ext.Equals(allowed, StringComparison.OrdinalIgnoreCase)) { match = true; break; }
            if (!match) continue;

            if (visited.Add(name)) queue.Enqueue(name);
        }
    }

    internal static string ComputeModelFolder(string outputRoot, string relativeOrInVpkPath, bool noModelFolder)
    {
        var rel = relativeOrInVpkPath.Replace('/', Path.DirectorySeparatorChar);
        var dir = Path.GetDirectoryName(rel) ?? string.Empty;
        var basename = Path.GetFileNameWithoutExtension(rel);
        return noModelFolder
            ? Path.Combine(outputRoot, dir)
            : Path.Combine(outputRoot, dir, basename);
    }

    private static string SanitizeBasename(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "model";
        var baseName = Path.GetFileNameWithoutExtension(fileName.Replace('/', Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(baseName) ? "model" : baseName;
    }

    private static void ReportFailures(List<(string, string)> failures)
    {
        if (failures.Count == 0) return;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Failures ({failures.Count}):");
        foreach (var (f, m) in failures.Take(20)) Console.Error.WriteLine($"  {f}: {m}");
        if (failures.Count > 20) Console.Error.WriteLine($"  ... and {failures.Count - 20} more");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            "VmdlExtractor v5.3 — full ModelDoc-compatible decompilation of Source 2 .vmdl_c models.\n" +
            "Merges Skeleton (with .vmesh_c m_skeleton fallback), HitboxSetList, AttachmentList,\n" +
            "FlexControllers (incl. external .vmorf_c) and PhysicsShapeList (incl. additional\n" +
            ".vphys_c). Sanitizes empty resource refs and broken AutoLayer references, and\n" +
            "recursively decompiles m_refAnimIncludeModels — so ModelDoc compile passes cleanly.\n\n" +
            "Modes (auto-detected by --input):\n" +
            "  VPK mode:    --input ends with .vpk\n" +
            "  Folder mode: --input is a directory (game root with extracted compiled assets)\n" +
            "  File mode:   --input is a single .vmdl_c\n\n" +
            "Usage:\n" +
            "  VmdlExtractor -i <vpk|folder|file.vmdl_c> -o <output_root> [options]\n\n" +
            "Options:\n" +
            "  -i, --input <path>           .vpk / folder / single .vmdl_c.\n" +
            "  -o, --output <folder>        Output root.\n" +
            "  -g, --game-root <folder>     (folder/file mode) base for resolving externals.\n" +
            "  -p, --vpk-prefix <prefix>    (vpk mode) limit to in-VPK directory prefix.\n" +
            "  -f, --file <path>            (vpk mode) extract exactly ONE file by its in-VPK\n" +
            "                               path (e.g. models/heroes/lina/lina.vmdl_c).\n" +
            "                               Wins over -p. Dependencies are still pulled in.\n" +
            "  -e, --extensions <list>      Source extensions to scan (default: .vmdl_c).\n" +
            "      --no-model-folder        Use <out>/<inVpkPath>/<file> layout.\n" +
            "      --no-raw-deps            Skip raw safety-net + source-text dumps.\n" +
            "      --no-sanitize            Skip post-process .vmdl sanitizer.\n" +
            "      --no-include-deps        Skip recursive m_refAnimIncludeModels decompile.\n" +
            "      --copy-to <folder>       Copy extracted .vmdl+deps to content/<addon>/models.\n" +
            "      --copy-from <vpk-path>   Limit --copy-to to this in-VPK sub-prefix.\n" +
            "      --vmdlc-target <folder>  Copy original .vmdl_c bytes verbatim (bypass compile).\n" +
            "      --build                  After --copy-to: auto-run resourcecompiler.exe on .vmdl,\n" +
            "                               then apply (a) motion patch (fix 10x speed run) and\n" +
            "                               (b) fidelity transplant when input is a VPK -- splices\n" +
            "                               MRPH and full PHYS (ragdoll/cloth) blocks from the\n" +
            "                               original .vmdl_c into our recompile so block-set in the\n" +
            "                               output matches Valve's at the semantic level.\n" +
            "                               Replaces manual ModelDoc Save & Build workflow.\n" +
            "      --resourcecompiler <p>   Path to resourcecompiler.exe (auto-detected from --copy-to).\n" +
            "      --fix-motion <vmdl_c>    STANDALONE: patch m_movementArray in an already-compiled\n" +
            "                               .vmdl_c by copying values from --donor-vpk. Use when you\n" +
            "                               compiled in ModelDoc and the model now \"detaches\" from\n" +
            "                               its hero on movespeed items (locomotion treadmill bug).\n" +
            "                               Skips full extract pipeline. Requires --donor-vpk.\n" +
            "      --donor-vpk <pak.vpk>    Original Valve VPK to copy m_movementArray from\n" +
            "                               (used by --fix-motion and --build).\n" +
            "      --donor-inner <path>     Override auto-detected inner VPK path for --fix-motion\n" +
            "                               (e.g. \"models/heroes/arc_warden/arc_warden.vmdl_c\").\n" +
            "      --no-motion-sanitize     Skip ExtractMotion neutralization in .vmdl source.\n" +
            "      --keep-multipose-layers  Skip stripping AnimAddLayer refs to m_bMulti=1 sequences\n" +
            "                               (the v5.3.1 in-match flipped-body fix - opt out only if a\n" +
            "                               specific model relies on the broken overlay).\n" +
            "  -v, --verbose                Per-file diagnostics + merge summary.\n" +
            "      --version                Print version banner.\n" +
            "  -h, --help                   Show this help.\n\n" +
            "Injection toggles (ModelDoc-compatible defaults):\n" +
            "      --activity-modifier-inject     [default ON]  Inject ActivityModifier child nodes\n" +
            "                                                   (required: compile preserves modifiers).\n" +
            "      --no-activity-modifier-inject  Disable (legacy, for debugging).\n" +
            "      --no-framerate-inject          Skip framerate/start/end_frame inject into AnimFile.\n" +
            "      --prune-modifiers              [default OFF] Hard-disable sequences whose modifiers\n" +
            "                                                   are NOT in scripts/activity_modifier_weights.txt\n" +
            "                                                   (desolation/fast_run/spawn_arcana/...).\n" +
            "                                                   Use for custom addons without items_game.txt\n" +
            "                                                   bindings (e.g. witchblades) where engine\n" +
            "                                                   would otherwise weighted-random into\n" +
            "                                                   desolator-run animations ~66% of the time.\n" +
            "      --no-prune-modifiers           Keep all sequences (default).\n" +
            "      --no-dedup                     Skip dedup of activity candidates.\n" +
            "      --no-field-mod-inject          Skip field-level activity_modifiers inject (ignored by compile).\n\n" +
            "Example (Shadow Fiend Arcana from Dota 2 7.28c, full auto-build):\n" +
            "  VmdlExtractor ^\n" +
            "    -i \"D:\\Games\\Dota 2 7.28c\\game\\dota\\pak01_dir.vpk\" ^\n" +
            "    -p \"models/heroes/shadow_fiend\" ^\n" +
            "    -o \".\\out\" ^\n" +
            "    --copy-to \"E:\\...\\content\\dota_addons\\mymod\\models\\heroes\\shadow_fiend\" ^\n" +
            "    --copy-from \"models/heroes/shadow_fiend/shadow_fiend_arcana\" ^\n" +
            "    --build -v\n" +
            "\n" +
            "  This single command: extracts → copies → compiles → motion-patches → ready in game.\n" +
            "  Do NOT open ModelDoc afterwards — its Save & Build will overwrite the motion patch.");
    }
}
