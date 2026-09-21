// RegressionSweep: orchestrates extract → compile → diff for all hero
// models in a Dota 2 VPK and aggregates discrepancies between the
// recompiled .vmdl_c and the Valve original. Used to find systematic
// extractor bugs (missing blocks, wrong counts, broken structures, etc).
//
// Pipeline per model:
//   1. Extract Valve .vmdl_c via VmdlExtractor --copy-to <content> --build
//      (this does extract → write .vmdl/dmx → resourcecompile → produce
//      compiled .vmdl_c in <game>/<addon>/...)
//   2. Locate compiled .vmdl_c in <game>/<addon>/...
//   3. Open both compiled .vmdl_c (orig from VPK, ours from disk).
//   4. Compare blocks, counts, and key structures.
//   5. Emit a per-model report; aggregate global stats.
//
// Usage:
//   RegressionSweep --vpk <pak01_dir> --content <content/<addon>>
//                   --extractor <VmdlExtractor.dll>
//                   [--out report.json] [--limit N]
//                   [--filter <hero1,hero2,...>] [--skip-existing]
//                   [--no-build]   # only diff already-compiled outputs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using KVObject = ValveKeyValue.KVObject;

namespace RegressionSweep;

public static class Program
{
    public static int Main(string[] args)
    {
        string? vpk = null, content = null, extractor = null, outFile = null;
        var heroFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int limit = int.MaxValue;
        bool skipExisting = false, noBuild = false, verbose = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vpk": vpk = args[++i]; break;
                case "--content": content = args[++i]; break;
                case "--extractor": extractor = args[++i]; break;
                case "--out": outFile = args[++i]; break;
                case "--limit": limit = int.Parse(args[++i]); break;
                case "--filter":
                    foreach (var h in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries))
                        heroFilter.Add(h.Trim());
                    break;
                case "--skip-existing": skipExisting = true; break;
                case "--no-build": noBuild = true; break;
                case "-v": case "--verbose": verbose = true; break;
                case "-h": case "--help": PrintHelp(); return 0;
            }
        }
        if (vpk == null || content == null || (!noBuild && extractor == null))
        {
            PrintHelp();
            return 1;
        }

        var dotaRoot = FindDotaRoot(vpk);
        if (dotaRoot == null)
        {
            Console.Error.WriteLine($"[err] cannot find dota2 root from vpk: {vpk}");
            return 2;
        }
        // game-dir = parallel to content-dir (content/dota_addons/X → game/dota_addons/X).
        var gameDir = ContentToGameDir(content);
        if (gameDir == null)
        {
            Console.Error.WriteLine($"[err] cannot derive game-dir from content: {content}");
            return 2;
        }
        Console.WriteLine($"[init] vpk:        {vpk}");
        Console.WriteLine($"[init] content:    {content}");
        Console.WriteLine($"[init] game (out): {gameDir}");
        Console.WriteLine($"[init] extractor:  {extractor ?? "(diff-only mode)"}");

        // ── Step 1: enumerate all models/heroes/<name>/<name>.vmdl_c ──
        var pkg = new Package();
        pkg.Read(vpk);
        var heroes = EnumerateHeroes(pkg, heroFilter);
        Console.WriteLine($"[init] found {heroes.Count} hero models");
        if (heroes.Count == 0) return 0;

        if (limit < heroes.Count) heroes = heroes.Take(limit).ToList();

        // ── Step 2: per-hero loop ──
        var reports = new List<HeroReport>();
        int idx = 0;
        var sw = Stopwatch.StartNew();
        foreach (var inner in heroes)
        {
            idx++;
            var heroName = ExtractHeroName(inner);
            Console.WriteLine($"\n[{idx}/{heroes.Count}] {heroName}  ({inner})");

            var report = new HeroReport { Name = heroName, InnerPath = inner };

            // 2a. Extract+compile via VmdlExtractor (skip if --no-build).
            var compiledOurs = Path.Combine(gameDir, inner.Replace('/', Path.DirectorySeparatorChar));
            if (!noBuild)
            {
                if (skipExisting && File.Exists(compiledOurs))
                {
                    if (verbose) Console.WriteLine($"  [skip] already compiled: {compiledOurs}");
                }
                else
                {
                    var rcOk = RunExtractor(extractor!, vpk, inner, content, verbose, out string buildLog);
                    report.BuildOk = rcOk;
                    report.BuildLog = TrimBuildLog(buildLog);
                    if (!rcOk)
                    {
                        Console.Error.WriteLine($"  [err] extract/build failed (exit code != 0)");
                        reports.Add(report);
                        continue;
                    }
                }
            }
            if (!File.Exists(compiledOurs))
            {
                Console.Error.WriteLine($"  [err] compiled output missing: {compiledOurs}");
                report.Error = $"compiled output missing: {compiledOurs}";
                reports.Add(report);
                continue;
            }

            // 2b. Diff orig vs ours.
            try
            {
                var origR = LoadResourceFromVpk(pkg, inner);
                var oursR = LoadResourceFromDisk(compiledOurs);
                ComputeDiff(origR, oursR, report);
                Console.WriteLine($"  diff: {SummarizeDiff(report)}");
            }
            catch (Exception ex)
            {
                report.Error = $"diff failed: {ex.Message}";
                Console.Error.WriteLine($"  [err] {ex.Message}");
            }

            reports.Add(report);
        }
        sw.Stop();
        Console.WriteLine($"\n[done] {reports.Count} models in {sw.Elapsed.TotalMinutes:F1} min");

        // ── Step 3: aggregate report ──
        AggregateReport(reports);

        if (outFile != null)
        {
            var json = JsonSerializer.Serialize(reports, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
            File.WriteAllText(outFile, json);
            Console.WriteLine($"[done] full report → {outFile}");
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("RegressionSweep — bulk extract+compile+diff for Dota 2 hero models.");
        Console.WriteLine("");
        Console.WriteLine("Usage:");
        Console.WriteLine("  RegressionSweep --vpk <pak01_dir> --content <content/addon>");
        Console.WriteLine("                  --extractor <VmdlExtractor.dll>");
        Console.WriteLine("                  [--out <report.json>] [--limit N]");
        Console.WriteLine("                  [--filter h1,h2,...] [--skip-existing] [--no-build] [-v]");
    }

    // ─────────────────────── enumeration ───────────────────────────

    private static List<string> EnumerateHeroes(Package pkg, HashSet<string> filter)
    {
        if (!pkg.Entries.TryGetValue("vmdl_c", out var vmdlcs)) return new();

        // Pattern: models/heroes/<name>/<name>.vmdl_c — the hero base model.
        var canonical = vmdlcs
            .Where(e => e.DirectoryName != null
                     && e.DirectoryName.StartsWith("models/heroes/", StringComparison.OrdinalIgnoreCase))
            .Where(e =>
            {
                // Must be at depth: models/heroes/<hero>/<file>.vmdl_c
                var parts = e.DirectoryName!.Split('/');
                if (parts.Length != 3) return false;            // models, heroes, <hero>
                return string.Equals(parts[2], e.FileName, StringComparison.OrdinalIgnoreCase);
            })
            .Select(e => e.DirectoryName + "/" + e.FileName + ".vmdl_c")
            .Where(p => filter.Count == 0 || filter.Any(f => p.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return canonical;
    }

    private static string ExtractHeroName(string innerPath)
    {
        // models/heroes/<name>/<file>.vmdl_c → <name>
        var parts = innerPath.Split('/');
        return parts.Length >= 3 ? parts[2] : innerPath;
    }

    // ─────────────────────── extractor invocation ─────────────────

    private static bool RunExtractor(string extractorDll, string vpk, string inner,
        string content, bool verbose, out string log)
    {
        // Each invocation:
        //   dotnet VmdlExtractor.dll --input <vpk> -f <inner> --output <tmp>
        //                            --copy-to <content> --build
        //                            (smart sanitize is on by default)
        var tmp = Path.Combine(Path.GetTempPath(), "vmdl_sweep_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList =
                {
                    extractorDll,
                    "--input", vpk,
                    "-f", inner,
                    "--output", tmp,
                    "--copy-to", content,
                    "--build",
                },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) { log = "[failed to start]"; return false; }
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            log = stdout + (stderr.Length > 0 ? "\n--STDERR--\n" + stderr : "");
            if (verbose) Console.WriteLine(log);
            return proc.ExitCode == 0;
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
    }

    private static string TrimBuildLog(string log)
    {
        // Keep only the most relevant lines (errors, warnings, key info).
        var keep = new List<string>();
        foreach (var line in log.Split('\n'))
        {
            var t = line.TrimEnd();
            if (t.Length == 0) continue;
            if (t.Contains("error", StringComparison.OrdinalIgnoreCase)
                || t.Contains("warning", StringComparison.OrdinalIgnoreCase)
                || t.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
                || t.Contains("[build]", StringComparison.OrdinalIgnoreCase))
                keep.Add(t);
        }
        return string.Join("\n", keep);
    }

    // ─────────────────────── resource loading ──────────────────────

    private static Resource LoadResourceFromVpk(Package pkg, string inner)
    {
        var entry = pkg.FindEntry(inner)
            ?? throw new FileNotFoundException($"vpk entry not found: {inner}");
        pkg.ReadEntry(entry, out var bytes);
        var ms = new MemoryStream(bytes, writable: false);
        var r = new Resource { FileName = Path.GetFileName(inner) };
        r.Read(ms);
        return r;
    }

    private static Resource LoadResourceFromDisk(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var ms = new MemoryStream(bytes, writable: false);
        var r = new Resource { FileName = Path.GetFileName(path) };
        r.Read(ms);
        return r;
    }

    // ─────────────────────── diff computation ─────────────────────

    private static void ComputeDiff(Resource orig, Resource ours, HeroReport rep)
    {
        // Block-level: count occurrences and total size per block type. The
        // Source-2 file format permits multiple blocks of the same type
        // (e.g. duplicate MDAT/MIDX/MVTX blocks emitted by VRF
        // Resource.Serialize roundtrip), so we cannot use a Dictionary
        // keyed on type — that throws on duplicate keys. Group instead.
        var origBlocks = orig.Blocks.GroupBy(b => b.Type.ToString())
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Size: g.Sum(b => (long)b.Size)));
        var oursBlocks = ours.Blocks.GroupBy(b => b.Type.ToString())
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Size: g.Sum(b => (long)b.Size)));

        foreach (var k in origBlocks.Keys.Union(oursBlocks.Keys).OrderBy(s => s))
        {
            origBlocks.TryGetValue(k, out var ob);
            oursBlocks.TryGetValue(k, out var nb);
            var entry = new BlockReport
            {
                Type = k,
                InOrig = ob.Count > 0,
                InOurs = nb.Count > 0,
                OrigCount = ob.Count,
                OursCount = nb.Count,
                OrigSize = (int?)ob.Size,
                OursSize = (int?)nb.Size,
            };
            rep.Blocks.Add(entry);
        }

        // ANIM block: per-anim comparison.
        DiffAnimBlock(orig, ours, rep);
        // ASEQ block: sequence comparison (counts, flags, modifiers).
        DiffAseqBlock(orig, ours, rep);
        // DATA block: bone count, materials, AnimGraph reference.
        DiffDataBlock(orig, ours, rep);
        // CTRL block: skeleton control-point counts.
        DiffCtrlBlock(orig, ours, rep);
        // PHYS block: presence + complexity (joints, bodies).
        DiffPhysBlock(orig, ours, rep);
    }

    private static void DiffAnimBlock(Resource orig, Resource ours, HeroReport rep)
    {
        var oA = ReadAnimMap(orig);
        var nA = ReadAnimMap(ours);
        rep.AnimsOrig = oA.Count;
        rep.AnimsOurs = nA.Count;

        var allNames = oA.Keys.Union(nA.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var n in allNames)
        {
            oA.TryGetValue(n, out var oo);
            nA.TryGetValue(n, out var nn);
            if (oo == null) { rep.AnimsOnlyInOurs.Add(n); continue; }
            if (nn == null) { rep.AnimsOnlyInOrig.Add(n); continue; }
            if (oo.MovCount != nn.MovCount || Math.Abs(oo.MaxV - nn.MaxV) > 0.5f)
                rep.AnimMotionMismatch.Add($"{n} (orig:{oo.MovCount}/{oo.MaxV:F1}, ours:{nn.MovCount}/{nn.MaxV:F1})");
        }
    }

    private static Dictionary<string, AnimEntry> ReadAnimMap(Resource r)
    {
        var result = new Dictionary<string, AnimEntry>(StringComparer.OrdinalIgnoreCase);
        var blk = r.GetBlockByType(BlockType.ANIM);
        if (blk == null) return result;
        var data = ExtractKv(blk);
        if (data == null || !data.ContainsKey("m_animArray")) return result;
        foreach (var kv in data["m_animArray"])
        {
            var an = kv.Value;
            var name = an["m_name"]?.ToString() ?? "<noname>";
            int movCount = 0; float maxV = 0;
            if (an.ContainsKey("m_movementArray"))
            {
                var mv = an["m_movementArray"];
                movCount = mv?.Count ?? 0;
                if (mv != null)
                {
                    foreach (var m in mv)
                    {
                        try
                        {
                            float v0 = ToF(m.Value["v0"]);
                            float v1 = ToF(m.Value["v1"]);
                            var mag = MathF.Sqrt(v0 * v0 + v1 * v1);
                            if (mag > maxV) maxV = mag;
                        }
                        catch { }
                    }
                }
            }
            result[name] = new AnimEntry(movCount, maxV);
        }
        return result;
    }

    private static void DiffAseqBlock(Resource orig, Resource ours, HeroReport rep)
    {
        var o = ReadSeqMap(orig);
        var n = ReadSeqMap(ours);
        rep.SeqsOrig = o.Count;
        rep.SeqsOurs = n.Count;
        var all = o.Keys.Union(n.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in all)
        {
            o.TryGetValue(name, out var oo);
            n.TryGetValue(name, out var nn);
            if (oo == null) { rep.SeqsOnlyInOurs.Add(name); continue; }
            if (nn == null) { rep.SeqsOnlyInOrig.Add(name); continue; }
            // Compare key flags
            var diffs = new List<string>();
            if (oo.IsDelta != nn.IsDelta) diffs.Add($"delta:{oo.IsDelta}->{nn.IsDelta}");
            if (oo.IsMulti != nn.IsMulti) diffs.Add($"multi:{oo.IsMulti}->{nn.IsMulti}");
            if (oo.ActivityCount != nn.ActivityCount) diffs.Add($"activities:{oo.ActivityCount}->{nn.ActivityCount}");
            if (diffs.Count > 0) rep.SeqMismatch.Add($"{name}: {string.Join(",", diffs)}");
        }
    }

    private static Dictionary<string, SeqEntry> ReadSeqMap(Resource r)
    {
        var result = new Dictionary<string, SeqEntry>(StringComparer.OrdinalIgnoreCase);
        var blk = r.GetBlockByType(BlockType.ASEQ);
        if (blk == null) return result;
        var data = ExtractKv(blk);
        if (data == null) return result;
        if (!data.ContainsKey("m_localS1SeqDescArray")) return result;
        foreach (var kv in data["m_localS1SeqDescArray"])
        {
            var s = kv.Value;
            var name = s["m_sName"]?.ToString() ?? "<noname>";
            bool isDelta = false, isMulti = false;
            try
            {
                var flags = s["m_flags"];
                if (flags != null)
                {
                    isDelta = ToBool(flags["m_bLegacyDelta"]);
                    isMulti = ToBool(flags["m_bMulti"]);
                }
            }
            catch { }
            int actCount = 0;
            try
            {
                var arr = s["m_activityArray"];
                if (arr != null) actCount = arr.Count;
            }
            catch { }
            result[name] = new SeqEntry(isDelta, isMulti, actCount);
        }
        return result;
    }

    private static void DiffDataBlock(Resource orig, Resource ours, HeroReport rep)
    {
        var oo = ReadDataInfo(orig);
        var nn = ReadDataInfo(ours);
        rep.BonesOrig = oo.BoneCount;
        rep.BonesOurs = nn.BoneCount;
        rep.MaterialsOrig = oo.MaterialCount;
        rep.MaterialsOurs = nn.MaterialCount;
        rep.AgrpOrig = oo.AnimGraphRef;
        rep.AgrpOurs = nn.AnimGraphRef;

        if (oo.BoneCount != nn.BoneCount)
            rep.DataMismatches.Add($"bone count: {oo.BoneCount} → {nn.BoneCount}");
        if (oo.MaterialCount != nn.MaterialCount)
            rep.DataMismatches.Add($"material count: {oo.MaterialCount} → {nn.MaterialCount}");
        if (oo.AnimGraphRef != nn.AnimGraphRef && (oo.AnimGraphRef != null || nn.AnimGraphRef != null))
            rep.DataMismatches.Add($"agrp ref: '{oo.AnimGraphRef}' → '{nn.AnimGraphRef}'");
        // Bone name set
        var origBones = new HashSet<string>(oo.BoneNames, StringComparer.OrdinalIgnoreCase);
        var oursBones = new HashSet<string>(nn.BoneNames, StringComparer.OrdinalIgnoreCase);
        var missing = origBones.Except(oursBones, StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        var extra   = oursBones.Except(origBones, StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        if (missing.Count > 0) rep.DataMismatches.Add($"missing bones (sample): {string.Join(",", missing)}");
        if (extra.Count > 0)   rep.DataMismatches.Add($"extra bones (sample): {string.Join(",", extra)}");
    }

    private static DataInfo ReadDataInfo(Resource r)
    {
        var info = new DataInfo();
        var blk = r.GetBlockByType(BlockType.DATA);
        if (blk == null) return info;
        var data = ExtractKv(blk);
        if (data == null) return info;
        // Bones
        try
        {
            if (data.ContainsKey("m_modelSkeleton"))
            {
                var skel = data["m_modelSkeleton"];
                if (skel != null && skel.ContainsKey("m_boneName"))
                {
                    var bn = skel["m_boneName"];
                    if (bn != null)
                    {
                        info.BoneCount = bn.Count;
                        info.BoneNames = bn.Select(x => x.Value?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                    }
                }
            }
        }
        catch { }
        // Materials (m_materialGroups[].m_materials[])
        try
        {
            if (data.ContainsKey("m_materialGroups"))
            {
                var mg = data["m_materialGroups"];
                if (mg != null && mg.Count > 0)
                {
                    var first = mg.First().Value;
                    if (first.ContainsKey("m_materials"))
                    {
                        var mats = first["m_materials"];
                        info.MaterialCount = mats?.Count ?? 0;
                    }
                }
            }
        }
        catch { }
        // AnimGraph reference (m_refAnimGroups or similar)
        foreach (var key in new[] { "m_refAnimGroups", "m_animGraphResource" })
        {
            try
            {
                if (data.ContainsKey(key))
                {
                    var v = data[key];
                    if (v != null)
                    {
                        if (v.Count == 0) info.AnimGraphRef = v.ToString();
                        else
                        {
                            var first = v.First().Value;
                            info.AnimGraphRef = first?.ToString();
                        }
                    }
                }
            }
            catch { }
        }
        return info;
    }

    private static void DiffCtrlBlock(Resource orig, Resource ours, HeroReport rep)
    {
        var blkO = orig.GetBlockByType(BlockType.CTRL);
        var blkN = ours.GetBlockByType(BlockType.CTRL);
        rep.CtrlInOrig = blkO != null;
        rep.CtrlInOurs = blkN != null;
        if (blkO != null) rep.CtrlSizeOrig = (int)blkO.Size;
        if (blkN != null) rep.CtrlSizeOurs = (int)blkN.Size;
    }

    private static void DiffPhysBlock(Resource orig, Resource ours, HeroReport rep)
    {
        var blkO = orig.GetBlockByType(BlockType.PHYS);
        var blkN = ours.GetBlockByType(BlockType.PHYS);
        rep.PhysInOrig = blkO != null;
        rep.PhysInOurs = blkN != null;
        if (blkO != null) rep.PhysSizeOrig = (int)blkO.Size;
        if (blkN != null) rep.PhysSizeOurs = (int)blkN.Size;
    }

    // ─────────────────────── helpers ──────────────────────────────

    private static KVObject? ExtractKv(object? b)
    {
        if (b == null) return null;
        object? data = b switch
        {
            BinaryKV3 bkv3 => bkv3.Data,
            KeyValuesOrNTRO kvOrNtro => kvOrNtro.Data,
            _ => null,
        };
        if (data == null) return null;
        return data switch
        {
            ValveKeyValue.KVDocument doc => doc.Root,
            KVObject kvo => kvo,
            _ => null,
        };
    }

    private static float ToF(object? v)
    {
        if (v == null) return 0f;
        try
        {
            var prop = v.GetType().GetProperty("Value");
            object? raw = prop != null ? prop.GetValue(v) : v;
            if (raw == null) return 0f;
            return Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { return 0f; }
    }

    private static bool ToBool(object? v)
    {
        if (v == null) return false;
        try
        {
            var prop = v.GetType().GetProperty("Value");
            object? raw = prop != null ? prop.GetValue(v) : v;
            if (raw == null) return false;
            return Convert.ToBoolean(raw);
        }
        catch { return false; }
    }

    private static string SummarizeDiff(HeroReport r)
    {
        var bits = new List<string>();
        var missing = r.Blocks.Where(b => b.InOrig && !b.InOurs).Select(b => b.Type).ToList();
        var extra   = r.Blocks.Where(b => !b.InOrig && b.InOurs).Select(b => b.Type).ToList();
        var dup     = r.Blocks.Where(b => b.OrigCount != b.OursCount && b.InOrig && b.InOurs)
                              .Select(b => $"{b.Type}:{b.OrigCount}/{b.OursCount}").ToList();
        if (missing.Count > 0) bits.Add($"miss:{string.Join("/", missing)}");
        if (extra.Count > 0)   bits.Add($"extra:{string.Join("/", extra)}");
        if (dup.Count > 0)     bits.Add($"countDiff:{string.Join("/", dup)}");
        if (r.AnimsOrig != r.AnimsOurs) bits.Add($"anim:{r.AnimsOrig}≠{r.AnimsOurs}");
        if (r.SeqsOrig != r.SeqsOurs) bits.Add($"seq:{r.SeqsOrig}≠{r.SeqsOurs}");
        if (r.BonesOrig != r.BonesOurs) bits.Add($"bone:{r.BonesOrig}≠{r.BonesOurs}");
        if (r.AnimMotionMismatch.Count > 0) bits.Add($"motion:{r.AnimMotionMismatch.Count}");
        if (r.SeqMismatch.Count > 0) bits.Add($"seqMM:{r.SeqMismatch.Count}");
        if (r.DataMismatches.Count > 0) bits.Add($"data:{r.DataMismatches.Count}");
        return bits.Count == 0 ? "✓ identical" : string.Join(" ", bits);
    }

    private static void AggregateReport(List<HeroReport> reports)
    {
        Console.WriteLine("\n══════════════════ AGGREGATE REPORT ══════════════════");
        var failed = reports.Where(r => r.Error != null).ToList();
        if (failed.Count > 0)
        {
            Console.WriteLine($"\nFailed: {failed.Count}");
            foreach (var f in failed.Take(10)) Console.WriteLine($"  {f.Name}: {f.Error}");
        }

        var ok = reports.Where(r => r.Error == null).ToList();

        // 1. Block presence patterns
        var missingBlocks = new Dictionary<string, int>();
        var extraBlocks   = new Dictionary<string, int>();
        foreach (var r in ok)
        {
            foreach (var b in r.Blocks)
            {
                if (b.InOrig && !b.InOurs) Incr(missingBlocks, b.Type);
                if (!b.InOrig && b.InOurs) Incr(extraBlocks, b.Type);
            }
        }
        if (missingBlocks.Count > 0)
        {
            Console.WriteLine("\nBlocks missing in OURS (should be present per Valve original):");
            foreach (var (t, n) in missingBlocks.OrderByDescending(p => p.Value))
                Console.WriteLine($"  {t,-8} : {n,4} models");
        }
        if (extraBlocks.Count > 0)
        {
            Console.WriteLine("\nBlocks present in OURS but absent in ORIG:");
            foreach (var (t, n) in extraBlocks.OrderByDescending(p => p.Value))
                Console.WriteLine($"  {t,-8} : {n,4} models");
        }

        // 1.5 Duplicate-block counts: blocks present in both, but count differs.
        //     E.g. orig has 1 MDAT, ours has 8 MDAT (VRF Resource.Serialize bug).
        var dupBlocks = new Dictionary<string, (int OrigSum, int OursSum, int Models)>();
        foreach (var r in ok)
        {
            foreach (var b in r.Blocks)
            {
                if (!b.InOrig || !b.InOurs) continue;
                if (b.OrigCount == b.OursCount) continue;
                dupBlocks.TryGetValue(b.Type, out var cur);
                dupBlocks[b.Type] = (cur.OrigSum + b.OrigCount, cur.OursSum + b.OursCount, cur.Models + 1);
            }
        }
        if (dupBlocks.Count > 0)
        {
            Console.WriteLine("\nBlocks where count differs between orig and ours:");
            Console.WriteLine($"  {"type",-8} {"models",-7} {"avg.orig",-9} {"avg.ours",-9}");
            foreach (var (t, v) in dupBlocks.OrderByDescending(p => p.Value.Models))
                Console.WriteLine($"  {t,-8} {v.Models,-7} {(double)v.OrigSum/v.Models,-9:F2} {(double)v.OursSum/v.Models,-9:F2}");
        }

        // 2. Anim count distribution
        int animDiff = ok.Count(r => r.AnimsOrig != r.AnimsOurs);
        int seqDiff  = ok.Count(r => r.SeqsOrig  != r.SeqsOurs);
        int boneDiff = ok.Count(r => r.BonesOrig != r.BonesOurs);
        int matDiff  = ok.Count(r => r.MaterialsOrig != r.MaterialsOurs);
        int physDiff = ok.Count(r => r.PhysInOrig && !r.PhysInOurs);
        int agrpDiff = ok.Count(r => r.AgrpOrig != r.AgrpOurs);

        Console.WriteLine("\nCount mismatches:");
        Console.WriteLine($"  anim count differs : {animDiff} models");
        Console.WriteLine($"  seq count differs  : {seqDiff} models");
        Console.WriteLine($"  bone count differs : {boneDiff} models");
        Console.WriteLine($"  material count differs : {matDiff} models");
        Console.WriteLine($"  PHYS missing in ours   : {physDiff} models");
        Console.WriteLine($"  agrp ref differs       : {agrpDiff} models");

        int motionTotal = ok.Sum(r => r.AnimMotionMismatch.Count);
        int seqTotal    = ok.Sum(r => r.SeqMismatch.Count);
        Console.WriteLine($"\nPer-anim motion mismatches: {motionTotal} total across {ok.Count(r => r.AnimMotionMismatch.Count > 0)} models");
        Console.WriteLine($"Per-seq flag mismatches: {seqTotal} total across {ok.Count(r => r.SeqMismatch.Count > 0)} models");

        // 3. Most affected models (by total issue count)
        var ranked = ok
            .Select(r => (r.Name, Total: CountIssues(r)))
            .Where(t => t.Total > 0)
            .OrderByDescending(t => t.Total)
            .Take(15)
            .ToList();
        if (ranked.Count > 0)
        {
            Console.WriteLine("\nMost-affected models (by total issue count):");
            foreach (var (n, t) in ranked) Console.WriteLine($"  {t,4}  {n}");
        }

        var clean = ok.Count(r => CountIssues(r) == 0);
        Console.WriteLine($"\nClean: {clean}/{ok.Count} models match Valve original");
    }

    private static int CountIssues(HeroReport r)
    {
        int n = 0;
        n += r.Blocks.Count(b => b.InOrig != b.InOurs);
        n += r.Blocks.Count(b => b.InOrig && b.InOurs && b.OrigCount != b.OursCount);
        if (r.AnimsOrig != r.AnimsOurs) n++;
        if (r.SeqsOrig != r.SeqsOurs) n++;
        if (r.BonesOrig != r.BonesOurs) n++;
        if (r.MaterialsOrig != r.MaterialsOurs) n++;
        if (r.AgrpOrig != r.AgrpOurs) n++;
        n += r.AnimMotionMismatch.Count;
        n += r.SeqMismatch.Count;
        n += r.DataMismatches.Count;
        return n;
    }

    private static void Incr(Dictionary<string, int> d, string k)
    {
        if (!d.ContainsKey(k)) d[k] = 0;
        d[k]++;
    }

    private static string? FindDotaRoot(string vpkOrContent)
    {
        var dir = Path.GetFullPath(vpkOrContent);
        for (int i = 0; i < 12; i++)
        {
            var candidate = Path.Combine(dir, "game", "bin", "win64", "resourcecompiler.exe");
            if (File.Exists(candidate)) return dir;
            var parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static string? ContentToGameDir(string content)
    {
        var norm = Path.GetFullPath(content).Replace('\\', '/');
        int idx = norm.LastIndexOf("/content/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return (norm[..idx] + "/game/" + norm[(idx + "/content/".Length)..]).Replace('/', Path.DirectorySeparatorChar);
    }
}

// ─────────────────────── data records ─────────────────────────

internal record AnimEntry(int MovCount, float MaxV);
internal record SeqEntry(bool IsDelta, bool IsMulti, int ActivityCount);
internal class DataInfo
{
    public int BoneCount;
    public List<string> BoneNames = new();
    public int MaterialCount;
    public string? AnimGraphRef;
}
internal class BlockReport
{
    public string Type { get; set; } = "";
    public bool InOrig { get; set; }
    public bool InOurs { get; set; }
    public int OrigCount { get; set; }
    public int OursCount { get; set; }
    public int? OrigSize { get; set; }
    public int? OursSize { get; set; }
}
internal class HeroReport
{
    public string Name { get; set; } = "";
    public string InnerPath { get; set; } = "";
    public bool BuildOk { get; set; }
    public string? BuildLog { get; set; }
    public string? Error { get; set; }
    public List<BlockReport> Blocks { get; set; } = new();
    public int AnimsOrig { get; set; }
    public int AnimsOurs { get; set; }
    public List<string> AnimsOnlyInOrig { get; set; } = new();
    public List<string> AnimsOnlyInOurs { get; set; } = new();
    public List<string> AnimMotionMismatch { get; set; } = new();
    public int SeqsOrig { get; set; }
    public int SeqsOurs { get; set; }
    public List<string> SeqsOnlyInOrig { get; set; } = new();
    public List<string> SeqsOnlyInOurs { get; set; } = new();
    public List<string> SeqMismatch { get; set; } = new();
    public int BonesOrig { get; set; }
    public int BonesOurs { get; set; }
    public int MaterialsOrig { get; set; }
    public int MaterialsOurs { get; set; }
    public string? AgrpOrig { get; set; }
    public string? AgrpOurs { get; set; }
    public List<string> DataMismatches { get; set; } = new();
    public bool CtrlInOrig { get; set; }
    public bool CtrlInOurs { get; set; }
    public int CtrlSizeOrig { get; set; }
    public int CtrlSizeOurs { get; set; }
    public bool PhysInOrig { get; set; }
    public bool PhysInOurs { get; set; }
    public int PhysSizeOrig { get; set; }
    public int PhysSizeOurs { get; set; }
}
