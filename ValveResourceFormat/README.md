# VRF / Valve Resource Format

Valve's Source 2 resource file format parser, decompiler, and exporter.
Source 2 files usually end with `_c`, for example `.vmdl_c`.

## ⚠️ Breaking Changes Notice

**The primary user of this library is the [Source 2 Viewer](https://s2v.app).** As such, updates may contain breaking changes and backwards incompatible API changes, as the viewer does not require backwards compatibility with older library versions. Additionally, Source 2 games themselves may update and change file formats at any time, which may necessitate breaking changes in this library. **If you need to support newer file formats, you will need to update the library.** That said, we do aim to support older file formats going back to the very first Source 2 project.

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


Or decompile it with `ContentExporter`, which writes the decompiled file and every file it is made of below a folder at their content paths. The resource's `FileName` is its content path, and the file loader finds the files it references:

```csharp
using var fileLoader = new GameFileLoader(null, file);
var exporter = new ContentExporter("dump");

exporter.Export(resource, fileLoader);
```

A model, mesh or map resource can also be exported to glTF through the same exporter, which writes the buffers and textures next to the `.gltf`:

```csharp
var gltfExporter = new GltfModelExporter(fileLoader)
{
    Exporter = exporter,
};

gltfExporter.Export(resource, exporter.GetOutputPath("models/props/crate.gltf"));
```
