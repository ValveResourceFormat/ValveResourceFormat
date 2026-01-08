# VRF / Valve Resource Format
## [🔗 View VRF website](https://valveresourceformat.github.io)

Valve's Source 2 resource file format parser, decompiler, and exporter.
Source 2 files usually files end with `_c`, for example `.vmdl_c`.

Basic usage:

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
