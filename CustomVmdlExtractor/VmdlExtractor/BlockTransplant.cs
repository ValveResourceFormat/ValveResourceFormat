// SPDX-License-Identifier: MIT
//
// BlockTransplant — surgical, byte-level injection of resource blocks from a
// donor compiled `.vmdl_c` into a freshly-recompiled one. Used to restore
// Source-2 fidelity blocks (MRPH, full PHYS with cloth, etc.) that VRF +
// ModelDoc cannot round-trip through the .vmdl source pipeline.
//
// Why byte-level (and not VRF's `Resource.Serialize`)?
//   `Resource.Serialize` is documented "NOT PRODUCTION READY" — when called on
//   a Resource that already has Mesh-family blocks (MIDX/MVTX/MDAT) it emits
//   each linked buffer twice (we observed Lina balloon 850 KB → 2.16 MB with
//   four duplicate blocks after one transplant attempt). To stay deterministic
//   we treat the file as raw bytes, parse the table, splice in the donor's
//   block payload, and rewrite offsets ourselves.
//
// File format (Source 2 resource container, verified against
// `ValveResourceFormat/Resource/Resource.cs` Read/Serialize methods):
//   0..3    uint32  file_size
//   4..5    uint16  header_version  (KnownHeaderVersion = 12)
//   6..7    uint16  version
//   8..11   uint32  blocks_offset   (always 8 — table begins at offset 16)
//   12..15  uint32  blocks_count
//   16..    blocks table — N × 12-byte entries:
//             uint32 type        (FourCC, e.g. 'MRPH')
//             uint32 data_offset (relative to *this entry's* offset field, i.e.
//                                 to position `entry_start + 4`)
//             uint32 data_size
//   then    block payload, 16-byte aligned, in arbitrary order
//
// Public API:
//   <see cref="TransplantBlocks(string, byte[], BlockType[], bool)"/>
//   <see cref="TransplantBlocksFromVpk(string, Package, string, BlockType[], bool)"/>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ValvePak;
using ValveResourceFormat;

internal static class BlockTransplant
{
    private const int HeaderSize = 16;
    private const int EntrySize = 12;
    private const int BlocksTableStart = HeaderSize; // 16
    // Each entry's `data_offset` is encoded relative to its own offset field
    // (entry_pos + 4). To reach the data: data_pos = entry_pos + 4 + offset.
    private const int OffsetFieldDelta = 4;
    // Field offsets within the 16-byte resource header.
    private const int FileSizeOffset = 0;
    private const int BlocksOffsetField = 8;   // value must be 8 (data follows immediately)
    private const int BlocksCountField = 12;

    /// <summary>
    /// Result of a transplant call — diagnostics, NOT failure-bearing. Caller
    /// inspects to see what was added (or skipped because already present /
    /// missing in donor).
    /// </summary>
    public sealed class TransplantResult
    {
        public List<BlockType> Added { get; } = new();
        public List<BlockType> SkippedAlreadyPresent { get; } = new();
        public List<BlockType> SkippedMissingInDonor { get; } = new();
        public long OriginalSize;
        public long NewSize;
    }

    private sealed record BlockEntry(uint Type, long EntryStart, long DataOffsetField, long DataPos, uint DataSize);

    /// <summary>
    /// Transplant <paramref name="typesToAdd"/> from <paramref name="donorBytes"/>
    /// into the resource at <paramref name="targetPath"/>. Existing blocks are
    /// kept verbatim; donor blocks of types already present in the target are
    /// skipped (we never duplicate). Returns diagnostics.
    /// </summary>
    public static TransplantResult TransplantBlocks(
        string targetPath, byte[] donorBytes, BlockType[] typesToAdd, bool verbose = false)
    {
        var result = new TransplantResult();
        var targetBytes = File.ReadAllBytes(targetPath);
        result.OriginalSize = targetBytes.Length;

        var targetEntries = ParseHeader(targetBytes);
        var donorEntries = ParseHeader(donorBytes);

        var existingTypes = targetEntries.Select(e => e.Type).ToHashSet();

        var toCopy = new List<(uint Type, byte[] Data)>();
        foreach (var bt in typesToAdd)
        {
            var typeFourCC = (uint)bt;
            if (existingTypes.Contains(typeFourCC))
            {
                result.SkippedAlreadyPresent.Add(bt);
                continue;
            }
            var donor = donorEntries.FirstOrDefault(e => e.Type == typeFourCC);
            if (donor == null)
            {
                result.SkippedMissingInDonor.Add(bt);
                continue;
            }
            var data = new byte[donor.DataSize];
            Array.Copy(donorBytes, donor.DataPos, data, 0, donor.DataSize);
            toCopy.Add((typeFourCC, data));
            result.Added.Add(bt);
        }

        if (toCopy.Count == 0)
        {
            result.NewSize = targetBytes.Length;
            return result;
        }

        var rebuilt = Rebuild(targetBytes, targetEntries, toCopy);
        File.WriteAllBytes(targetPath, rebuilt);
        result.NewSize = rebuilt.Length;
        if (verbose)
        {
            Console.WriteLine($"  [transplant] {Path.GetFileName(targetPath)}: " +
                $"+{string.Join(",", result.Added)} | size {result.OriginalSize:N0} → {result.NewSize:N0}");
        }
        return result;
    }

    /// <summary>
    /// Convenience wrapper: pull donor bytes out of <paramref name="vpk"/> at
    /// <paramref name="vpkInternalPath"/>.
    /// </summary>
    public static TransplantResult? TransplantBlocksFromVpk(
        string targetPath, Package vpk, string vpkInternalPath, BlockType[] typesToAdd, bool verbose = false)
    {
        var entry = vpk.FindEntry(vpkInternalPath);
        if (entry == null) return null;
        vpk.ReadEntry(entry, out var donor);
        return TransplantBlocks(targetPath, donor, typesToAdd, verbose);
    }

    /// <summary>
    /// Parse the resource header + block table into entries. Each entry stores
    /// the absolute byte position of its `data_offset` field so we can rewrite
    /// offsets when shifting data later.
    /// </summary>
    private static List<BlockEntry> ParseHeader(byte[] bytes)
    {
        var entries = new List<BlockEntry>();
        if (bytes.Length < HeaderSize) throw new InvalidDataException("file too small for resource header");
        var blocksOffset = BitConverter.ToUInt32(bytes, BlocksOffsetField);
        var blocksCount = BitConverter.ToUInt32(bytes, BlocksCountField);
        if (blocksOffset != 8) throw new InvalidDataException($"unexpected blocks_offset {blocksOffset}, want 8");

        for (int i = 0; i < blocksCount; i++)
        {
            long entryStart = BlocksTableStart + i * EntrySize;
            uint type = BitConverter.ToUInt32(bytes, (int)entryStart);
            long offsetField = entryStart + OffsetFieldDelta;
            uint relOffset = BitConverter.ToUInt32(bytes, (int)offsetField);
            uint size = BitConverter.ToUInt32(bytes, (int)(offsetField + 4));
            long dataPos = offsetField + relOffset;
            entries.Add(new BlockEntry(type, entryStart, offsetField, dataPos, size));
        }
        return entries;
    }

    /// <summary>
    /// Build a new resource file: keep existing payload as-is, append donor
    /// block data 16-byte-aligned at the end, rewrite the block table to
    /// account for both the table growth and the new entries.
    /// </summary>
    private static byte[] Rebuild(byte[] targetBytes, List<BlockEntry> existing, List<(uint Type, byte[] Data)> toAdd)
    {
        int oldCount = existing.Count;
        int newCount = oldCount + toAdd.Count;
        int tableShift = toAdd.Count * EntrySize; // bytes added to table

        long oldTableEnd = BlocksTableStart + oldCount * EntrySize;
        long oldDataLength = targetBytes.Length - oldTableEnd;

        using var ms = new MemoryStream();
        // Header (18 bytes) — copy verbatim, we'll patch file_size & blocks_count below.
        ms.Write(targetBytes, 0, HeaderSize);

        // Block table — existing entries with shifted offsets.
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            foreach (var e in existing)
            {
                bw.Write(e.Type);
                // The data position is the same byte sequence in the new file
                // shifted by `tableShift`; the offset field is at the same
                // relative location within the table, so we add `tableShift`.
                long oldOffsetVal = e.DataPos - e.DataOffsetField;
                bw.Write((uint)(oldOffsetVal + tableShift));
                bw.Write(e.DataSize);
            }
            // New entries — placeholders for now.
            long newEntriesStart = ms.Position;
            foreach (var (type, _) in toAdd)
            {
                bw.Write(type);
                bw.Write(0u);
                bw.Write(0u);
            }

            // Existing block payload — verbatim, position-shifted by tableShift bytes.
            ms.Write(targetBytes, (int)oldTableEnd, (int)oldDataLength);

            // Append donor data, each 16-byte aligned to match Source-2 layout.
            var donorPositions = new List<long>(toAdd.Count);
            foreach (var (_, data) in toAdd)
            {
                while (ms.Position % 16 != 0) bw.Write((byte)0);
                donorPositions.Add(ms.Position);
                bw.Write(data);
            }

            // Patch new-entry placeholders.
            for (int i = 0; i < toAdd.Count; i++)
            {
                long entryStart = newEntriesStart + i * EntrySize;
                long offsetField = entryStart + OffsetFieldDelta;
                long offsetVal = donorPositions[i] - offsetField;
                if (offsetVal < 0 || offsetVal > uint.MaxValue)
                    throw new InvalidDataException("donor offset out of uint32 range");
                ms.Position = offsetField;
                bw.Write((uint)offsetVal);
                bw.Write((uint)toAdd[i].Data.Length);
            }

            // Patch header.
            ms.Position = FileSizeOffset;
            bw.Write((uint)ms.Length);
            ms.Position = BlocksCountField;
            bw.Write((uint)newCount);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Inspect the block list of a compiled .vmdl_c without parsing payload.
    /// Used by diagnostics & tests.
    /// </summary>
    public static IReadOnlyList<BlockType> ReadBlockList(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return ParseHeader(bytes).Select(e => (BlockType)e.Type).ToList();
    }
}
