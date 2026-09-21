using System;
using System.IO;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

if (args.Length < 1)
{
    Console.WriteLine("Usage: AseqCompare <vmdl_c | vpk-path!inner-path> [seqNameFilter]");
    return 1;
}
string seqFilter = args.Length > 1 ? args[1].ToLowerInvariant() : "";

using var res = new Resource();
int bang = args[0].IndexOf('!');
if (bang > 0 && args[0][..bang].EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
{
    var pkg = new Package();
    pkg.Read(args[0][..bang]);
    var entry = pkg.FindEntry(args[0][(bang + 1)..])
        ?? throw new FileNotFoundException("vpk inner: " + args[0][(bang + 1)..]);
    pkg.ReadEntry(entry, out var bytes);
    res.Read(new MemoryStream(bytes, writable: false));
}
else
{
    res.Read(args[0]);
}

// Special filter "@DATA" → dump DATA block (materialGroups etc.) instead of ASEQ.
if (seqFilter == "@data")
{
    if (res.GetBlockByType(BlockType.DATA) is not KeyValuesOrNTRO data)
    {
        Console.WriteLine("No DATA");
        return 1;
    }
    var dd = data.Data;
    if (dd == null) { Console.WriteLine("No KV data"); return 1; }
    var mg = dd.GetArray("m_materialGroups");
    Console.WriteLine($"m_materialGroups: {(mg?.Count ?? 0)} group(s)");
    if (mg != null)
    {
        for (int gi = 0; gi < mg.Count; gi++)
        {
            string gname = "";
            try { gname = mg[gi].GetStringProperty("m_name"); } catch { }
            var mats = mg[gi].GetArray<string>("m_materials");
            Console.WriteLine($"  [{gi}] name='{gname}' m_materials({mats?.Length ?? 0}):");
            if (mats != null) foreach (var m in mats) Console.WriteLine($"      \"{m}\"");
        }
    }
    return 0;
}

if (res.GetBlockByType(BlockType.ASEQ) is not KeyValuesOrNTRO aseq) { Console.WriteLine("No ASEQ"); return 1; }
var seqData = aseq.Data;
if (seqData == null) { Console.WriteLine("No KV data"); return 1; }

var sequences = seqData.GetArray("m_localS1SeqDescArray");
if (sequences == null) { Console.WriteLine("No m_localS1SeqDescArray"); return 1; }

Console.WriteLine($"Total sequences: {sequences.Count}");
foreach (var seq in sequences)
{
    string name;
    try { name = seq.GetStringProperty("m_sName"); } catch { continue; }
    if (!string.IsNullOrEmpty(seqFilter) && !name.ToLowerInvariant().Contains(seqFilter)) continue;
    Console.WriteLine($"\n--- Sequence: '{name}' ---");
    var flags = seq.GetSubCollection("m_flags");
    if (flags != null)
    {
        Console.WriteLine("  m_flags:");
        foreach (var k in flags)
            Console.WriteLine($"    {k.Key} = {k.Value}");
    }
    var acts = seq.GetArray("m_activityArray");
    if (acts != null)
    {
        Console.WriteLine($"  m_activityArray ({acts.Count} entries):");
        foreach (var a in acts)
        {
            string aname = ""; int w = 0;
            try { aname = a.GetStringProperty("m_name"); } catch { }
            try { w = a.GetInt32Property("m_nWeight"); } catch { }
            Console.WriteLine($"    name='{aname}' weight={w}");
        }
    }
}
return 0;
