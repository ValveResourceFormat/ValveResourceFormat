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

Without `Exporter`, the glTF export writes its files through an exporter of its own rooted at the folder of the `.gltf`.

To export many files at once, such as the files of a package, pass them as items. They are exported in parallel and in an order where a model claims its meshes before they come up on their own, and the files of a compiled shader are decompiled together into one `.vfx`:

```csharp
using var package = new Package();
package.Read("pak01_dir.vpk");

using var packageLoader = new GameFileLoader(package, package.FileName);
var exporter = new ContentExporter("dump");

exporter.Export(package.Entries!.Values.SelectMany(entries => entries).Select(entry => new ContentExportItem(package, entry)
{
    FileLoader = packageLoader,
    Output = ContentExportOutput.Decompile, // or Gltf, Glb, Raw
}));
```

`Settings` holds how an export is laid out, which dependencies it also exports, what happens to existing files and the options of each decompiler. Every setting is also a `ContentExportOption` that reads and writes it as text, and `ContentExportOption.GetRelevant` returns the ones that apply to the files of an export, so a user interface can list them and store their values:

```csharp
var settings = new ContentExportSettings { Dependencies = ContentExportDependencies.All };
ContentExportOption.Find("model_skip_animations")!.TrySetValue(settings, "true");

var exporter = new ContentExporter("dump") { Settings = settings };
```
