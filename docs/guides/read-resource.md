# Read resource

This is a basic example to open a VPK file, read a texture from it, and then export it as a PNG.

```cs
// Open package and read a file
using var package = new Package();
package.Read("pak01_dir.vpk");

var packageEntry = package.FindEntry("textures/debug.vtex_c");
package.ReadEntry(packageEntry, out var rawFile);

// Read file as a resource
using var ms = new MemoryStream(rawFile);
using var resource = new Resource();
resource.Read(ms);

Debug.Assert(resource.ResourceType == ResourceType.Texture);

// Get a png from the texture
var texture = (Texture)resource.DataBlock;
using var bitmap = texture.GenerateBitmap();
var png = TextureExtract.ToPngImage(bitmap);

File.WriteAllBytes("image.png", png);
```
