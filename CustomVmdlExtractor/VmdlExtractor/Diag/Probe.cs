// Dump full ASEQ block of a vmdl as KV3 text, OR dump DMX animation metadata,
// OR dump native animation group fps/frameCount via VRF.
// Usage:
//   Probe.exe aseq <vpk> <vmdl_inner_path> <out_path>
//   Probe.exe dmx <dmx_file_or_dir>
//   Probe.exe anims <vpk> <vmdl_inner_path>
//   Probe.exe diag <vpk> <vmdl_inner_path>   — full diagnostics (AnimGraph refs,
//                                              skeleton, sequences, animation list)
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;
using Datamodel;

class P
{
    static int Main(string[] a)
    {
        if (a.Length == 0) { Console.WriteLine("usage: Probe.exe aseq <vpk> <vmdl> <out>  |  Probe.exe dmx <path>  |  Probe.exe anims <vpk> <vmdl>"); return 1; }
        if (a[0] == "dmx") return DumpDmx(a);
        if (a[0] == "aseq") return DumpAseq(a.Skip(1).ToArray());
        if (a[0] == "anims") return DumpAnims(a.Skip(1).ToArray());
        if (a[0] == "aseqfile")
        {
            // aseqfile <path-to-.vmdl_c> <out-text>
            var p = a[1]; var outPath = a.Length > 2 ? a[2] : "aseq.txt";
            using var fs = File.OpenRead(p);
            var rr = new Resource { FileName = Path.GetFileName(p) };
            rr.Read(fs);
            var b = rr.GetBlockByType(BlockType.ASEQ);
            if (b == null) { Console.WriteLine("no ASEQ"); return 2; }
            File.WriteAllText(outPath, b.ToString());
            Console.WriteLine($"wrote -> {outPath}");
            return 0;
        }
        if (a[0] == "blocks")
        {
            // blocks <path-to-.vmdl_c> — list block types & sizes
            var p = a[1];
            using var fs = File.OpenRead(p);
            var rr = new Resource { FileName = Path.GetFileName(p) };
            rr.Read(fs);
            Console.WriteLine($"File: {p}  ({new FileInfo(p).Length} bytes)");
            Console.WriteLine($"Type: {rr.ResourceType}");
            foreach (var b in rr.Blocks)
                Console.WriteLine($"  {b.Type,-6} : {b.Size,10} bytes");
            return 0;
        }
        if (a[0] == "dmxdump")
        {
            // dmxdump <path-to.dmx> <toName> <toAttribute> — print first 5 values of matched channel
            var p = a[1]; var targetName = a[2]; var targetAttr = a[3];
            using var fs = File.OpenRead(p);
            var dm = Datamodel.Datamodel.Load(fs, Datamodel.Codecs.DeferredMode.Disabled);
            foreach (var elem in dm.AllElements)
            {
                if (elem == null || elem.ClassName != "DmeChannel") continue;
                string toAttr = elem.ContainsKey("toAttribute") ? elem.Get<string>("toAttribute") ?? "" : "";
                string toName = "";
                try
                {
                    if (elem.ContainsKey("toElement"))
                    {
                        var to = elem.Get<Datamodel.Element>("toElement");
                        if (to != null && to.ContainsKey("name")) toName = to.Get<string>("name") ?? "";
                        if (string.IsNullOrEmpty(toName) && to != null) toName = to.Name ?? "";
                    }
                }
                catch { }
                if (toName != targetName || toAttr != targetAttr) continue;
                Console.WriteLine($"Found channel: toName='{toName}' toAttribute='{toAttr}'");
                Console.WriteLine($"  Channel keys: {string.Join(",", elem.Keys)}");
                if (!elem.ContainsKey("log")) continue;
                var log = elem.Get<Datamodel.Element>("log");
                if (log == null) { Console.WriteLine("  log == null"); continue; }
                Console.WriteLine($"  Log className={log.ClassName}, keys={string.Join(",", log.Keys)}");
                if (log.ContainsKey("layers"))
                {
                    var layers = log.Get<Datamodel.ElementArray>("layers");
                    Console.WriteLine($"  Layer count: {layers.Count}");
                    int li = 0;
                    foreach (var layer in layers)
                    {
                        if (layer == null) { li++; continue; }
                        Console.WriteLine($"  Layer[{li}] className={layer.ClassName}, keys={string.Join(",", layer.Keys)}");
                        if (layer.ContainsKey("values"))
                        {
                            object vals = layer["values"];
                            Console.WriteLine($"    values type: {vals?.GetType().FullName}");
                            if (vals is System.Collections.IEnumerable enu)
                            {
                                int idx = 0;
                                foreach (var v in enu)
                                {
                                    if (idx < 5) Console.WriteLine($"    [{idx}] {v}");
                                    idx++;
                                }
                                Console.WriteLine($"    total values: {idx}");
                            }
                        }
                        li++;
                    }
                }
                return 0;
            }
            Console.WriteLine($"channel '{targetName}'/'{targetAttr}' NOT FOUND");
            return 1;
        }
        if (a[0] == "vmdlc-patchmotion")
        {
            // vmdlc-patchmotion <path-to.vmdl_c> [animname-substring]
            // Replaces per-frame m_movementArray entries in ANIM block with single zero entry,
            // matching Valve's original compile behaviour. Optional substring filter for animation name.
            var p = a[1];
            string? filter = a.Length > 2 ? a[2] : null;

            var rr = new ValveResourceFormat.Resource { FileName = Path.GetFileName(p) };
            // VRF keeps stream reference for lazy data access — must keep stream alive
            // for entire lifetime including Serialize. Use a MemoryStream copy.
            var srcBytes = File.ReadAllBytes(p);
            var memStream = new MemoryStream(srcBytes, writable: false);
            rr.Read(memStream);

            var animBlock = rr.GetBlockByType(ValveResourceFormat.BlockType.ANIM);
            if (animBlock == null) { Console.WriteLine("no ANIM block"); return 2; }
            Console.WriteLine($"ANIM block type: {animBlock.GetType().FullName}");
            // ANIM may be BinaryKV3 OR KeyValuesOrNTRO
            object? animDataObj = null;
            if (animBlock is ValveResourceFormat.ResourceTypes.BinaryKV3 bkv3) animDataObj = bkv3.Data;
            else if (animBlock is ValveResourceFormat.ResourceTypes.KeyValuesOrNTRO kvOrNtro) animDataObj = kvOrNtro.Data;
            else { Console.WriteLine($"unsupported ANIM block class"); return 2; }
            ValveKeyValue.KVObject animData;
            if (animDataObj is ValveKeyValue.KVDocument doc) animData = doc.Root;
            else if (animDataObj is ValveKeyValue.KVObject kvo) animData = kvo;
            else { Console.WriteLine($"anim.Data type: {animDataObj?.GetType().FullName}"); return 2; }

            // Walk m_animArray. We use the underlying KVObject (not extension method)
            // to get a *mutable* reference to the array element.
            var animArrKv = animData.ContainsKey("m_animArray") ? animData["m_animArray"] : null;
            if (animArrKv == null) { Console.WriteLine("no m_animArray"); return 2; }

            int patched = 0;
            int totalAnims = 0;
            int idx = -1;
            foreach (var kvp in animArrKv)
            {
                idx++;
                totalAnims++;
                ValveKeyValue.KVObject an = kvp.Value;
                string nm = "";
                try { nm = an.GetStringProperty("m_name") ?? ""; } catch { }
                if (filter != null && !nm.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

                if (!an.ContainsKey("m_movementArray")) continue;
                var movArrKv = an["m_movementArray"];
                if (movArrKv == null) continue;
                int currentCount = movArrKv.Count;
                if (currentCount == 0) continue;

                // Determine endframe: max of existing entries, or nFrames-1 fallback
                int lastEnd = 0;
                foreach (var mvp in movArrKv)
                {
                    var mv = mvp.Value;
                    int ef = 0;
                    try { ef = mv.GetInt32Property("endframe"); } catch { }
                    if (ef > lastEnd) lastEnd = ef;
                }
                if (lastEnd == 0)
                {
                    try
                    {
                        var pData = an.GetSubCollection("m_pData");
                        int nf = pData?.GetInt32Property("m_nFrames") ?? 0;
                        if (nf > 1) lastEnd = nf - 1;
                    }
                    catch { }
                }

                // Build zero entry: { endframe, motionflags=64, v0=0, v1=0, angle=0, vector=[0,0,0], position=[0,0,0] }
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

                // Replace the array contents: clear + add zeroEntry
                movArrKv.Clear();
                movArrKv.Add(zeroEntry);

                if (patched < 5) Console.WriteLine($"  [{idx}] '{nm}': {currentCount} entries → 1 (endframe={lastEnd})");
                patched++;
            }

            Console.WriteLine($"Patched m_movementArray on {patched} of {totalAnims} animation(s).");
            if (patched == 0) return 1;

            // Write modified resource back
            using (var ofs = File.Create(p))
                rr.Serialize(ofs);
            Console.WriteLine($"Saved -> {p}");
            return 0;
        }
        if (a[0] == "listapi")
        {
            // Enumerate VRF API types matching pattern
            var pat = a.Length > 1 ? a[1] : "KV3";
            var asmName = a.Length > 2 ? a[2] : "ValveResourceFormat";
            var asm = asmName == "ValveKeyValue"
                ? typeof(ValveKeyValue.KVObject).Assembly
                : typeof(ValveResourceFormat.Resource).Assembly;
            var types = asm.GetTypes().Where(t => t.Name.Contains(pat, StringComparison.OrdinalIgnoreCase) && t.IsPublic).ToList();
            foreach (var t in types)
            {
                Console.WriteLine($"== {t.FullName} ==");
                foreach (var ctor in t.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    Console.WriteLine($"   ctor {ctor}");
                foreach (var pr in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    Console.WriteLine($"   prop {pr.PropertyType.Name} {pr.Name} (get={pr.CanRead}, set={pr.CanWrite})");
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
                    Console.WriteLine($"   meth {m}");
            }
            return 0;
        }
        if (a[0] == "dmxinspectclass")
        {
            // dmxinspectclass <path> <className> — dump first element of given class
            var p = a[1]; var target = a[2];
            using var fs = File.OpenRead(p);
            var dm = Datamodel.Datamodel.Load(fs, Datamodel.Codecs.DeferredMode.Disabled);
            foreach (var elem in dm.AllElements)
            {
                if (elem == null || elem.ClassName != target) continue;
                Console.WriteLine($"Element: name='{elem.Name}' className='{elem.ClassName}'");
                Console.WriteLine($"  Keys: {string.Join(",", elem.Keys)}");
                foreach (var k in elem.Keys)
                {
                    object v = elem[k];
                    string vstr;
                    if (v is Datamodel.Element vEl)
                        vstr = $"<Element class='{vEl.ClassName}' name='{vEl.Name}'>";
                    else if (v is Datamodel.ElementArray earr)
                        vstr = $"<ElementArray count={earr.Count}>";
                    else if (v is System.Collections.IEnumerable enu2 && !(v is string))
                    {
                        int cnt = 0; foreach (var __ in enu2) cnt++;
                        vstr = $"<{v.GetType().Name} count={cnt}>";
                    }
                    else vstr = v?.ToString() ?? "null";
                    Console.WriteLine($"    {k} = {vstr}");
                }
                return 0;
            }
            Console.WriteLine($"No element with className='{target}' found");
            return 1;
        }
        if (a[0] == "dmxinspectjoint")
        {
            // dmxinspectjoint <path-to.dmx> <jointName> — dump joint element + transform attributes
            var p = a[1]; var target = a[2];
            using var fs = File.OpenRead(p);
            var dm = Datamodel.Datamodel.Load(fs, Datamodel.Codecs.DeferredMode.Disabled);
            foreach (var elem in dm.AllElements)
            {
                if (elem == null) continue;
                if (elem.Name != target) continue;
                Console.WriteLine($"Element: name='{elem.Name}' className='{elem.ClassName}'");
                Console.WriteLine($"  Keys: {string.Join(",", elem.Keys)}");
                foreach (var k in elem.Keys)
                {
                    object v = elem[k];
                    string vstr;
                    if (v is Datamodel.Element vEl)
                    {
                        vstr = $"<Element name='{vEl.Name}' class='{vEl.ClassName}' keys=[{string.Join(",", vEl.Keys)}]>";
                    }
                    else if (v is System.Collections.IEnumerable && !(v is string))
                        vstr = $"<{v.GetType().Name} count=?>";
                    else vstr = v?.ToString() ?? "null";
                    Console.WriteLine($"    {k} = {vstr}");
                }
                if (elem.ContainsKey("transform"))
                {
                    var tr = elem.Get<Datamodel.Element>("transform");
                    if (tr != null)
                    {
                        Console.WriteLine($"  TRANSFORM: name='{tr.Name}' class='{tr.ClassName}'");
                        foreach (var k in tr.Keys)
                            Console.WriteLine($"    {k} = {tr[k]}");
                    }
                }
                return 0;
            }
            Console.WriteLine($"Joint '{target}' not found");
            return 1;
        }
        if (a[0] == "dmxzeroroot")
        {
            // dmxzeroroot <path-to.dmx> — zero out Root0_JNT position values
            var p = a[1];
            Datamodel.Datamodel dm;
            using (var fs = File.OpenRead(p))
                dm = Datamodel.Datamodel.Load(fs, Datamodel.Codecs.DeferredMode.Disabled);
            int patched = 0, layersZeroed = 0;
            foreach (var elem in dm.AllElements)
            {
                if (elem == null || elem.ClassName != "DmeChannel") continue;
                string toAttr = elem.ContainsKey("toAttribute") ? elem.Get<string>("toAttribute") ?? "" : "";
                string toName = "";
                try
                {
                    if (elem.ContainsKey("toElement"))
                    {
                        var to = elem.Get<Datamodel.Element>("toElement");
                        if (to != null && to.ContainsKey("name")) toName = to.Get<string>("name") ?? "";
                    }
                }
                catch { }
                // Fallback: also check Element.Name
                if (string.IsNullOrEmpty(toName))
                {
                    try
                    {
                        if (elem.ContainsKey("toElement"))
                        {
                            var to2 = elem.Get<Datamodel.Element>("toElement");
                            if (to2 != null) toName = to2.Name ?? "";
                        }
                    }
                    catch { }
                }
                // Patch root joint position AND orientation channels — both must be static
                // for compile to bake single zero m_movementArray entry instead of per-frame.
                if (!(toName == "Root0_JNT" || string.IsNullOrEmpty(toName))) continue;
                if (toAttr != "position" && toAttr != "orientation") continue;
                Console.WriteLine($"  Patching: toElement='{toName}' toAttribute='{toAttr}'");

                if (!elem.ContainsKey("log")) continue;
                var log = elem.Get<Datamodel.Element>("log");
                if (log == null) continue;

                // Set usedefaultvalue=true and zero defaultvalue
                if (log.ContainsKey("usedefaultvalue")) log["usedefaultvalue"] = true;
                if (toAttr == "position")
                {
                    if (log.ContainsKey("defaultvalue")) log["defaultvalue"] = System.Numerics.Vector3.Zero;
                }
                else // orientation
                {
                    if (log.ContainsKey("defaultvalue")) log["defaultvalue"] = System.Numerics.Quaternion.Identity;
                }

                // Zero/identity all layer values (defensive — uses log values regardless of usedefaultvalue)
                if (log.ContainsKey("layers"))
                {
                    var layers = log.Get<Datamodel.ElementArray>("layers");
                    if (layers != null)
                    {
                        foreach (var layer in layers)
                        {
                            if (layer == null) continue;
                            if (!layer.ContainsKey("values")) continue;
                            if (toAttr == "position")
                            {
                                var vals = layer.Get<System.Collections.Generic.IList<System.Numerics.Vector3>>("values");
                                if (vals == null) continue;
                                for (int i = 0; i < vals.Count; i++) vals[i] = System.Numerics.Vector3.Zero;
                                layersZeroed++;
                            }
                            else // orientation
                            {
                                var vals = layer.Get<System.Collections.Generic.IList<System.Numerics.Quaternion>>("values");
                                if (vals == null) continue;
                                for (int i = 0; i < vals.Count; i++) vals[i] = System.Numerics.Quaternion.Identity;
                                layersZeroed++;
                            }
                        }
                    }
                }
                patched++;
            }
            using (var ofs = File.Create(p))
                dm.Save(ofs, dm.Encoding, dm.EncodingVersion);
            Console.WriteLine($"Patched {patched} root position channel(s), {layersZeroed} layer(s) zeroed.");
            return 0;
        }
        if (a[0] == "dmxstruct")
        {
            // dmxstruct <path-to.dmx> [maxDepth=3] — dump element structure
            var p = a[1]; int maxDepth = a.Length > 2 ? int.Parse(a[2]) : 3;
            using var fs = File.OpenRead(p);
            var dm = Datamodel.Datamodel.Load(fs, Datamodel.Codecs.DeferredMode.Disabled);
            Console.WriteLine($"DMX: {p}");
            Console.WriteLine($"Format: {dm.Format} v{dm.FormatVersion}, Encoding: {dm.Encoding} v{dm.EncodingVersion}");
            Console.WriteLine($"Total elements: {dm.AllElements.Count()}");
            Console.WriteLine();
            Console.WriteLine("Element types breakdown:");
            foreach (var grp in dm.AllElements.GroupBy(e => e.ClassName).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {grp.Key,-30} {grp.Count(),5}");
            Console.WriteLine();
            // Print DmeChannel elements with their target attributes
            Console.WriteLine("DmeChannel attributes (toAttribute / toElement.name):");
            int chCount = 0;
            foreach (var elem in dm.AllElements)
            {
                if (elem == null || elem.ClassName != "DmeChannel") continue;
                string toAttr = elem.ContainsKey("toAttribute") ? elem.Get<string>("toAttribute") ?? "?" : "?";
                string toName = "?";
                try
                {
                    if (elem.ContainsKey("toElement"))
                    {
                        var to = elem.Get<Datamodel.Element>("toElement");
                        if (to != null && to.ContainsKey("name")) toName = to.Get<string>("name") ?? "?";
                        else if (to != null) toName = to.Name ?? "?";
                    }
                }
                catch { }
                if (chCount < 20)
                {
                    Console.WriteLine($"  [{chCount}] toElement='{toName}' toAttribute='{toAttr}'");
                }
                chCount++;
            }
            Console.WriteLine($"  ... ({chCount} DmeChannel total)");
            return 0;
        }
        if (a[0] == "blockfile")
        {
            // blockfile <path-to-.vmdl_c> <block-name> <out-text>
            var p = a[1]; var blockName = a[2]; var outPath = a.Length > 3 ? a[3] : $"{blockName}.txt";
            using var fs = File.OpenRead(p);
            var rr = new Resource { FileName = Path.GetFileName(p) };
            rr.Read(fs);
            foreach (var b in rr.Blocks)
            {
                if (b.Type.ToString() == blockName)
                {
                    File.WriteAllText(outPath, b.ToString());
                    Console.WriteLine($"wrote {b.Type} -> {outPath} ({new FileInfo(outPath).Length} bytes)");
                    return 0;
                }
            }
            Console.WriteLine($"block {blockName} not found");
            return 2;
        }
        if (a[0] == "extract")
        {
            // extract <vpk> <inner_path> <out_file>
            var vpkPath = a[1]; var inner = a[2]; var outFile = a[3];
            using var pkg = new Package();
            pkg.Read(vpkPath);
            var entry = pkg.FindEntry(inner) ?? pkg.FindEntry(inner + "_c");
            if (entry == null) { Console.WriteLine($"not found: {inner}"); return 1; }
            pkg.ReadEntry(entry, out var bytes);
            File.WriteAllBytes(outFile, bytes);
            Console.WriteLine($"wrote {outFile} ({bytes.Length} bytes) from {entry.GetFullPath()}");
            return 0;
        }
        if (a[0] == "rawkv")
        {
            // rawkv <vpk> <inner_path> [out]  — dump first KV3-like block as text
            var vpkPath = a[1]; var inner = a[2]; var outPath = a.Length > 3 ? a[3] : "rawkv.txt";
            using var pkg = new Package();
            pkg.Read(vpkPath);
            var entry = pkg.FindEntry(inner + "_c") ?? pkg.FindEntry(inner);
            if (entry == null) { Console.WriteLine($"not found: {inner}"); return 1; }
            pkg.ReadEntry(entry, out var bytes);
            using var ms = new MemoryStream(bytes);
            var r = new Resource { FileName = inner };
            r.Read(ms);
            Console.WriteLine($"Resource type: {r.ResourceType}");
            Console.WriteLine($"Blocks: {string.Join(", ", r.Blocks.Select(b => b.Type.ToString()))}");
            foreach (var b in r.Blocks)
            {
                Console.WriteLine($"--- Block {b.Type} ---");
                File.WriteAllText($"{outPath}.{b.Type}.txt", b.ToString());
                Console.WriteLine($"  wrote -> {outPath}.{b.Type}.txt ({new FileInfo($"{outPath}.{b.Type}.txt").Length} bytes)");
            }
            return 0;
        }
        if (a[0] == "list")
        {
            // list <vpk> <ext> [filter]
            var vpkPath = a[1]; var ext = a[2]; var filter = a.Length > 3 ? a[3] : null;
            using var pkg = new Package();
            pkg.Read(vpkPath);
            foreach (var kvp in pkg.Entries)
            {
                if (!kvp.Key.Contains(ext, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var entry in kvp.Value)
                {
                    var path = entry.GetFullPath();
                    if (filter != null && !path.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                    Console.WriteLine($"  {kvp.Key,-12} {path}");
                }
            }
            return 0;
        }
        if (a[0] == "diag") return Diagnose(a.Skip(1).ToArray());
        return DumpAseq(a);
    }

    static int Diagnose(string[] a)
    {
        var vpkPath = a[0];
        using var pkg = new Package();
        pkg.Read(vpkPath);

        // Diagnose two models in parallel: base + arcana (or whatever was passed).
        var targets = a.Skip(1).ToArray();
        if (targets.Length == 0) targets = new[] { "models/heroes/shadow_fiend/shadow_fiend.vmdl", "models/heroes/shadow_fiend/shadow_fiend_arcana.vmdl" };

        foreach (var inner in targets)
        {
            DiagnoseModel(pkg, inner);
            Console.WriteLine();
            Console.WriteLine(new string('=', 80));
            Console.WriteLine();
        }
        return 0;
    }

    static void DiagnoseModel(Package pkg, string vmdlInner)
    {
        Console.WriteLine($"### Model: {vmdlInner}");
        Console.WriteLine();
        var entry = pkg.FindEntry(vmdlInner + "_c") ?? pkg.FindEntry(vmdlInner);
        if (entry == null) { Console.WriteLine("NOT FOUND"); return; }
        pkg.ReadEntry(entry, out var bytes);
        using var ms = new MemoryStream(bytes);
        var r = new Resource { FileName = vmdlInner };
        r.Read(ms);
        var model = (Model)r.DataBlock!;

        // [1] AnimGraph references
        Console.WriteLine("## [1] AnimGraph references");
        var modelData = model.Data;
        try
        {
            var refAnimGraph = SafeGetString(modelData, "m_refAnimGraph");
            Console.WriteLine($"  m_refAnimGraph = {refAnimGraph ?? "(null/missing)"}");
        }
        catch (Exception ex) { Console.WriteLine($"  err: {ex.Message}"); }

        // External references in the resource
        var extRefs = r.ExternalReferences;
        if (extRefs?.ResourceRefInfoList != null)
        {
            var animGraphRefs = extRefs.ResourceRefInfoList.Where(x => x.Name.EndsWith(".vagrp", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("animgraph")).ToList();
            Console.WriteLine($"  external animgraph refs ({animGraphRefs.Count}):");
            foreach (var er in animGraphRefs) Console.WriteLine($"    {er.Name}");
        }

        // [2] Skeleton bone count
        Console.WriteLine();
        Console.WriteLine("## [2] Skeleton");
        try
        {
            var skel = model.Skeleton;
            Console.WriteLine($"  bone count: {skel.Bones.Length}");
            // First 5 bones for sanity
            for (int i = 0; i < Math.Min(5, skel.Bones.Length); i++)
                Console.WriteLine($"    [{i}] {skel.Bones[i].Name}");
        }
        catch (Exception ex) { Console.WriteLine($"  err: {ex.Message}"); }

        // [3] Sequences with activities + modifiers + weights
        Console.WriteLine();
        Console.WriteLine("## [3] Sequences (activity + modifiers + weights)");
        var aseq = r.GetBlockByType(BlockType.ASEQ) as KeyValuesOrNTRO;
        if (aseq?.Data is ValveKeyValue.KVObject kvSeq)
        {
            var sequences = SafeGetArray(kvSeq, "m_localS1SeqDescArray");
            if (sequences != null)
            {
                Console.WriteLine($"  total sequences: {sequences.Count}");
                Console.WriteLine($"  {"name",-42} {"activity",-25} {"modifiers",-30} {"w",2} {"flags"}");
                foreach (var seq in sequences)
                {
                    string name = SafeGetString(seq, "m_sName") ?? "?";
                    var activities = SafeGetArray(seq, "m_activityArray");
                    string actName = "-";
                    int actWeight = 0;
                    var modifiers = new System.Collections.Generic.List<string>();
                    if (activities != null && activities.Count > 0)
                    {
                        actName = SafeGetString(activities[0], "m_name") ?? "-";
                        try { actWeight = (int)activities[0].GetIntegerProperty("m_nWeight"); } catch { }
                        for (int i = 1; i < activities.Count; i++)
                        {
                            var m = SafeGetString(activities[i], "m_name");
                            if (!string.IsNullOrEmpty(m)) modifiers.Add(m);
                        }
                    }
                    string flagsStr = "";
                    try
                    {
                        var flags = seq.GetSubCollection("m_flags");
                        if (flags != null)
                        {
                            if (flags.GetBooleanProperty("m_bLegacyDelta")) flagsStr += "delta ";
                            if (flags.GetBooleanProperty("m_bLooping")) flagsStr += "loop ";
                            if (flags.GetBooleanProperty("m_bWorldspaceBlend")) flagsStr += "worldsp ";
                            if (flags.GetBooleanProperty("m_bHidden")) flagsStr += "hidden ";
                        }
                    }
                    catch { }
                    Console.WriteLine($"  {name,-42} {actName,-25} {string.Join(",", modifiers),-30} {actWeight,2} {flagsStr.Trim()}");
                }
            }
        }

        // [4] Group sequences by activity+modifiers — find collisions.
        Console.WriteLine();
        Console.WriteLine("## [4] Activity-modifier groups with >1 candidate (CHECHETKA SOURCE)");
        var aseq2 = r.GetBlockByType(BlockType.ASEQ) as KeyValuesOrNTRO;
        if (aseq2?.Data is ValveKeyValue.KVObject kvSeq2)
        {
            var sequences = SafeGetArray(kvSeq2, "m_localS1SeqDescArray");
            if (sequences != null)
            {
                var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(string Name, int Weight)>>();
                foreach (var seq in sequences)
                {
                    string name = SafeGetString(seq, "m_sName") ?? "?";
                    var activities = SafeGetArray(seq, "m_activityArray");
                    if (activities == null || activities.Count == 0) continue;
                    string actName = SafeGetString(activities[0], "m_name") ?? "-";
                    if (!actName.StartsWith("ACT_")) continue;
                    int actWeight = 0;
                    try { actWeight = (int)activities[0].GetIntegerProperty("m_nWeight"); } catch { }
                    var modifiers = new System.Collections.Generic.List<string>();
                    for (int i = 1; i < activities.Count; i++)
                    {
                        var m = SafeGetString(activities[i], "m_name");
                        if (!string.IsNullOrEmpty(m)) modifiers.Add(m);
                    }
                    modifiers.Sort(StringComparer.Ordinal);
                    var key = $"{actName}|{string.Join(",", modifiers)}";
                    if (!groups.ContainsKey(key)) groups[key] = new();
                    groups[key].Add((name, actWeight));
                }
                int collisions = 0;
                foreach (var (key, list) in groups.Where(g => g.Value.Count > 1).OrderBy(g => g.Key))
                {
                    collisions++;
                    Console.WriteLine($"  {key,-50}  ({list.Count} candidates)");
                    foreach (var (n, w) in list.OrderByDescending(x => x.Weight))
                        Console.WriteLine($"    {n,-40}  weight={w}");
                }
                if (collisions == 0) Console.WriteLine("  none — all activity+modifier keys are unique.");
            }
        }

        // [5] Anim list with fps/frame counts
        Console.WriteLine();
        Console.WriteLine("## [5] All animations (fps + frame count)");
        try
        {
            var loader = new VpkLoader(pkg);
            int count = 0;
            Console.WriteLine($"  {"name",-42} {"fps",6} {"frames",7} {"duration_s",10}");
            foreach (var anim in model.GetAllAnimations(loader))
            {
                count++;
                double dur = anim.FrameCount / (double)Math.Max(0.001f, anim.Fps);
                Console.WriteLine($"  {anim.Name,-42} {anim.Fps,6:F1} {anim.FrameCount,7} {dur,10:F4}");
            }
            Console.WriteLine($"  total: {count}");
        }
        catch (Exception ex) { Console.WriteLine($"  err: {ex.Message}"); }
    }

    static string? SafeGetString(ValveKeyValue.KVObject kv, string field)
    {
        try { return kv.GetStringProperty(field); } catch { return null; }
    }
    static System.Collections.Generic.IReadOnlyList<ValveKeyValue.KVObject>? SafeGetArray(ValveKeyValue.KVObject kv, string field)
    {
        try { return kv.GetArray(field); } catch { return null; }
    }

    sealed class VpkLoader : ValveResourceFormat.IO.IFileLoader
    {
        readonly Package _pkg;
        public VpkLoader(Package pkg) { _pkg = pkg; }
        public Resource? LoadFile(string file)
        {
            var entry = _pkg.FindEntry(file) ?? _pkg.FindEntry(file + "_c");
            if (entry == null) return null;
            _pkg.ReadEntry(entry, out var bytes);
            var ms = new MemoryStream(bytes);
            var r = new Resource { FileName = file };
            r.Read(ms);
            return r;
        }
        public Resource? LoadFileCompiled(string file) => LoadFile(file + "_c");
        public ValveResourceFormat.CompiledShader.ShaderCollection LoadShader(string shaderName) => null!;
    }

    static int DumpAnims(string[] a)
    {
        var vpkPath = a[0];
        var vmdlInner = a[1];
        using var pkg = new Package();
        pkg.Read(vpkPath);
        var entry = pkg.FindEntry(vmdlInner + "_c") ?? pkg.FindEntry(vmdlInner);
        if (entry == null) { Console.WriteLine("vmdl not found"); return 1; }
        pkg.ReadEntry(entry, out var bytes);
        using var ms = new MemoryStream(bytes);
        var r = new Resource { FileName = vmdlInner };
        r.Read(ms);
        var model = (Model)r.DataBlock!;

        // List all available methods on Model.
        Console.Error.WriteLine("Model methods:");
        foreach (var m in typeof(Model).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name.Contains("Anim") || m.Name.Contains("Sequence")))
        {
            Console.Error.WriteLine($"  {m}");
        }

        // Try GetEmbeddedAnimations() (parameterless).
        var getEmbedded = typeof(Model).GetMethod("GetEmbeddedAnimations", Type.EmptyTypes);
        Console.Error.WriteLine($"GetEmbeddedAnimations: {getEmbedded}");

        if (getEmbedded != null)
        {
            var animsObj = getEmbedded.Invoke(model, null);
            Console.WriteLine($"{"animName",-40}  {"fps",6}  {"frames",7}  {"duration_s",10}");
            if (animsObj is System.Collections.IEnumerable en)
            {
                int count = 0;
                foreach (var anim in en)
                {
                    count++;
                    var t = anim.GetType();
                    var name = t.GetProperty("Name")?.GetValue(anim) ?? "?";
                    var fps = t.GetProperty("Fps")?.GetValue(anim) ?? "?";
                    var frames = t.GetProperty("FrameCount")?.GetValue(anim) ?? "?";
                    double dur = -1;
                    try { var fpsf = Convert.ToSingle(fps); var frC = Convert.ToInt32(frames); dur = frC / fpsf; } catch { }
                    Console.WriteLine($"{name,-40}  {fps,6}  {frames,7}  {dur,10:F4}");
                }
                Console.Error.WriteLine($"Total: {count} embedded animations");
            }
        }
        return 0;
    }

    static int DumpAseq(string[] a)
    {
        var vpkPath = a[0];
        var vmdlInner = a[1];
        var outPath = a.Length > 2 ? a[2] : "aseq.txt";
        using var pkg = new Package();
        pkg.Read(vpkPath);
        var entry = pkg.FindEntry(vmdlInner + "_c") ?? pkg.FindEntry(vmdlInner);
        if (entry == null) { Console.WriteLine("not found in vpk"); return 1; }
        pkg.ReadEntry(entry, out var bytes);
        using var ms = new MemoryStream(bytes);
        var r = new Resource { FileName = vmdlInner };
        r.Read(ms);
        var aseq = r.GetBlockByType(BlockType.ASEQ);
        if (aseq == null) { Console.WriteLine("no ASEQ"); return 2; }
        File.WriteAllText(outPath, aseq.ToString());
        Console.WriteLine($"wrote -> {outPath}");
        return 0;
    }

    static int DumpDmx(string[] a)
    {
        var pathArg = a[1];
        var files = Directory.Exists(pathArg)
            ? Directory.EnumerateFiles(pathArg, "*.dmx").ToArray()
            : new[] { pathArg };

        // Try register binary codec via VRF's Datamodel codec assembly.
        TryRegisterDmxCodecs();

        Console.WriteLine($"{"file",-50}  {"duration_s",10}  {"frameRate",9}  {"start_s",8}  {"channels",8}  {"timeKeysFirst",13}  {"timeKeysLast",12}");
        foreach (var f in files)
        {
            try
            {
                using var fs = File.OpenRead(f);
                var dm = Datamodel.Datamodel.Load(fs);
                var root = dm.Root;
                int? frameRate = null;
                try { frameRate = (int?)root.GetType().GetProperty("Item", new[] { typeof(string) })?.GetValue(root, new object[] { "frameRate" }); } catch { }

                // Walk to first DmeChannelsClip
                Element? animList = null;
                try { animList = root["animationList"] as Element; } catch { }
                Element? clip = null;
                if (animList != null)
                {
                    var anims = animList["animations"];
                    if (anims is System.Collections.IEnumerable en)
                    {
                        foreach (var x in en) { if (x is Element e) { clip = e; break; } }
                    }
                }

                double durSec = -1, startSec = -1;
                int channels = -1;
                double firstTime = -1, lastTime = -1;
                if (clip != null)
                {
                    var tf = clip["timeFrame"] as Element;
                    if (tf != null)
                    {
                        var d = tf["duration"];
                        if (d is TimeSpan ts) durSec = ts.TotalSeconds;
                        var s = tf["start"];
                        if (s is TimeSpan ts2) startSec = ts2.TotalSeconds;
                    }
                    var chArr = clip["channels"];
                    if (chArr is System.Collections.IList lst) channels = lst.Count;

                    // Get times of first channel's first vector log layer
                    if (chArr is System.Collections.IEnumerable en2)
                    {
                        foreach (var x in en2)
                        {
                            if (x is Element ch)
                            {
                                var log = ch["log"] as Element;
                                if (log != null)
                                {
                                    var layers = log["layers"];
                                    if (layers is System.Collections.IEnumerable layEn)
                                    {
                                        foreach (var l in layEn)
                                        {
                                            if (l is Element layer)
                                            {
                                                var times = layer["times"];
                                                if (times is System.Collections.IList timesList && timesList.Count > 0)
                                                {
                                                    if (timesList[0] is TimeSpan t1) firstTime = t1.TotalSeconds;
                                                    if (timesList[timesList.Count - 1] is TimeSpan t2) lastTime = t2.TotalSeconds;
                                                }
                                            }
                                            break;
                                        }
                                    }
                                }
                                break;
                            }
                        }
                    }
                }

                Console.WriteLine($"{Path.GetFileName(f),-50}  {durSec,10:F4}  {(frameRate.HasValue ? frameRate.Value.ToString() : "?"),9}  {startSec,8:F4}  {channels,8}  {firstTime,13:F4}  {lastTime,12:F4}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Path.GetFileName(f),-50}  ERROR: {ex.Message}");
            }
        }
        return 0;
    }

    static bool _codecRegistered = false;
    static void TryRegisterDmxCodecs()
    {
        if (_codecRegistered) return;
        var asm = typeof(Datamodel.Datamodel).Assembly;
        var registerCodec = typeof(Datamodel.Datamodel).GetMethod("RegisterCodec", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var codecs = new[] { "Datamodel.Codecs.Binary", "Datamodel.Codecs.KeyValues2" };
        foreach (var name in codecs)
        {
            var t = asm.GetType(name);
            if (t == null) continue;
            try { registerCodec!.Invoke(null, new object[] { t }); }
            catch (Exception ex) { Console.Error.WriteLine($"[probe] register {name} err: {ex.InnerException?.Message ?? ex.Message}"); }
        }
        _codecRegistered = true;
    }
}
