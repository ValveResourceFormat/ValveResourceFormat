using System.Globalization;
using System.IO;
using ValveResourceFormat.Source1.Material;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Resolves the materials the map compiler patches for each cube map sample its faces see. The patch only sets the cube map,
/// so a face draws the material the patch includes and names the sample through the handshake of its scene object.
/// </summary>
internal sealed class Source1CubemapPatches
{
    /// <summary>The material a texture name is drawn with.</summary>
    /// <param name="Material">The name of the material.</param>
    /// <param name="CubeMap">The handshake of the cube map sample, or 0 when the material is not a patch or names no sample.</param>
    /// <param name="IsPatch">Whether the texture name is a patch of another material.</param>
    public readonly record struct DrawnMaterial(string Material, int CubeMap, bool IsPatch);

    private readonly Func<string, Stream?> openFile;
    private readonly Dictionary<string, int> handshakes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DrawnMaterial> cache = new(StringComparer.OrdinalIgnoreCase);

    public Source1CubemapPatches(BspFile bsp, Func<string, Stream?> openFile)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        ArgumentNullException.ThrowIfNull(openFile);

        this.openFile = openFile;

        var samples = bsp.Cubemaps;

        for (var i = 0; i < samples.Length; i++)
        {
            handshakes.TryAdd(GetSampleName(samples[i]), GetHandshake(i));
        }
    }

    /// <summary>Gets the handshake that names the cube map sample at an index of the map.</summary>
    public static int GetHandshake(int sampleIndex) => sampleIndex + 1;

    /// <summary>Gets the file name of the cube map a sample was baked to, without the extension.</summary>
    public static string GetSampleName(CubemapSample sample)
        => string.Create(CultureInfo.InvariantCulture, $"c{sample.OriginX}_{sample.OriginY}_{sample.OriginZ}");

    /// <summary>Gets the material a texture name of the map is drawn with.</summary>
    public DrawnMaterial Resolve(string textureName)
    {
        if (cache.TryGetValue(textureName, out var drawn))
        {
            return drawn;
        }

        drawn = new DrawnMaterial(textureName, 0, false);

        if (VmtFile.GetEnvmapPatch(Source1Paths.ToGameMaterialPath(textureName), openFile) is { } patch)
        {
            var cubeMap = 0;

            if (patch.Envmap != null)
            {
                handshakes.TryGetValue(Path.GetFileNameWithoutExtension(patch.Envmap), out cubeMap);
            }

            drawn = new DrawnMaterial(patch.Target, cubeMap, true);
        }

        cache.Add(textureName, drawn);

        return drawn;
    }
}
