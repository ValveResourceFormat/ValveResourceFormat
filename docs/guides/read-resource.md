# Valve Resource File

This document describes the binary file format used by Valve's Source 2 engine for compiled resource files (files with `_c` suffix such as `.vmdl_c`, `.vtex_c`, `.vmat_c`, etc.).

## Overview

Valve resource files are binary container files that store compiled game assets. The format uses a block-based structure where different types of data are stored in separate blocks. This modular design allows the engine to load only the data it needs.

All multi-byte values are stored in **little-endian** byte order.

## File Header

| Offset | Size | Type   | Field Name    | Description                                           |
|--------|------|--------|---------------|-------------------------------------------------------|
| `0x00` | `4`  | uint32 | FileSize      | Total size of the resource data in bytes              |
| `0x04` | `2`  | uint16 | HeaderVersion | Header format version (currently always **12**)       |
| `0x06` | `2`  | uint16 | Version       | Resource type-specific version number                 |
| `0x08` | `4`  | uint32 | BlockOffset   | Offset to block index table (typically **8**)         |
| `0x0C` | `4`  | uint32 | BlockCount    | Number of blocks in this resource                     |

### FileSize
The total size of the structured resource file in bytes. However, this may not match the actual file size on disk for certain resource types that have streaming data like sounds and textures.

### HeaderVersion
The resource header format version. **Must be 12** for current Source 2 resources. If this value differs, the file uses an unsupported or legacy format.

### Version
A version number specific to the resource type. Different resource types maintain independent versioning.

### BlockOffset
The offset in bytes from the **current read position** (after reading `BlockCount`) to the start of the block index table.

### BlockCount
The total number of data blocks in this resource. The block index table will contain exactly this many entries.

## Block Index Table

The block index table begins at offset `0x10 + BlockOffset` (typically at offset 0x10, immediately after the header).

Each entry in the table is exactly **12 bytes** and describes one block:

| Offset | Size | Type   | Field Name | Description                                      |
|--------|------|--------|------------|--------------------------------------------------|
| `0x00` | `4`  | uint32 | BlockType  | FourCC code identifying the block type           |
| `0x04` | `4`  | uint32 | Offset     | Relative offset from this position to block data |
| `0x08` | `4`  | uint32 | Size       | Size of the block data in bytes                  |

### BlockType (FourCC)

Block types are identified using **FourCC** (Four Character Code) values. These are stored as little-endian uint32 values where each byte represents an ASCII character.

For example, the `DATA` block:
```csharp
Characters: 'D' 'A' 'T' 'A'
ASCII:      0x44 0x41 0x54 0x41
As uint32:  0x41544144
```

### Block Offset

**IMPORTANT**: The offset is **relative** to the current file position, not absolute.

The offset is calculated from the position **immediately after reading the BlockType field**. In other words, the offset value indicates how many bytes to skip forward from the position where the Offset field itself is located.

To calculate the absolute position of block data: `absolute_position = position_of_offset_field + offset_value`

Where `position_of_offset_field` is the file position immediately after reading the 4-byte BlockType value.

### Block Size

The size of the block data in bytes, may be zero.

### Block Alignment

Block data in the file is typically aligned to **16-byte boundaries**. Padding bytes are inserted between blocks to maintain this alignment.

## Resource Type Detection

The resource type can be determined through multiple methods:

1. **File extension** - The file extension indicates the resource type (e.g., `.vmdl_c` → [Model](../api/ValveResourceFormat.ResourceTypes.Model.html), `.vmat_c` → [Material](../api/ValveResourceFormat.ResourceTypes.Material.html), `.vtex_c` → [Texture](../api/ValveResourceFormat.ResourceTypes.Texture.html))
2. **Compiler identifier** - Special dependencies in REDI/RED2 blocks contain compiler identifier strings that indicate the resource type
3. **Input dependency** - If there's exactly one input dependency in the edit info, its extension can indicate the type

## Read a resource

This is a basic example to open a VPK file using [the ValvePak library](https://github.com/ValveResourceFormat/ValvePak), read a [Texture](../api/ValveResourceFormat.ResourceTypes.Texture.html) from it using [Resource](../api/ValveResourceFormat.Resource.html), and then export it as a PNG.

```cs
// Open package and read a file
using var package = new Package();
package.Read("pak01_dir.vpk");

var packageEntry = package.FindEntry("textures/debug.vtex_c");
package.ReadEntry(packageEntry, out var rawFile);

// Read file as a resource
using var ms = new MemoryStream(rawFile);
using var resource = new Resource()
{
    FileName = packageEntry.GetFullPath()
};
resource.Read(ms);

Debug.Assert(resource.ResourceType == ResourceType.Texture);

// Get a png from the texture
var texture = (Texture)resource.DataBlock;
using var bitmap = texture.GenerateBitmap();
var png = TextureExtract.ToPngImage(bitmap);

File.WriteAllBytes("image.png", png);
```
