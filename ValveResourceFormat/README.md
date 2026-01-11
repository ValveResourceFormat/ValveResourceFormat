# VRF / Valve Resource Format
## [🔗 View VRF website](https://valveresourceformat.github.io)

Valve's Source 2 resource file format parser, decompiler, and exporter.
Source 2 files usually files end with `_c`, for example `.vmdl_c`.

## ⚠️ Breaking Changes Notice

**The primary user of this library is the [Source 2 Viewer](https://valveresourceformat.github.io).** As such, updates may contain breaking changes and backwards incompatible API changes, as the viewer does not require backwards compatibility with older library versions. Additionally, Source 2 games themselves may update and change file formats at any time, which may necessitate breaking changes in this library. **If you need to support newer file formats, you will need to update the library.** That said, we do aim to support older file formats going back to the very first Source 2 project.

## Basic usage

```csharp
var file = "textures/debug.vtex_c";

using var resource = new Resource();
resource.Read(file);

// You can access blocks and data on `resource` object
```

Extract a texture as png bytes:

```csharp
using var bitmap = ((Texture)resource.DataBlock).GenerateBitmap();
var bytes = TextureExtract.ToPngImage(bitmap);
```


Or use file extract helper which works for various resource types:

```csharp
using var contentFile = FileExtract.Extract(resource, null);
var outFilePath = "dump";

DumpContentFile(outFilePath, contentFile);

void DumpContentFile(string path, ContentFile contentFile)
{
    DumpFile(path, contentFile.Data);

    foreach (var contentSubFile in contentFile.SubFiles)
    {
        DumpFile(Path.Combine(Path.GetDirectoryName(path), contentSubFile.FileName), contentSubFile.Extract.Invoke());
    }
}

void DumpFile(string path, ReadOnlySpan<byte> data)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path));

    File.WriteAllBytes(path, data.ToArray());
}
```
