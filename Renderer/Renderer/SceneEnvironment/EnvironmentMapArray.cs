using System.Linq;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Materials;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>Packs environment maps with separate cube textures into one cube map array.</summary>
public static class EnvironmentMapArray
{
    private const int CubeFaces = 6;

    /// <summary>
    /// Copies the cube textures into one array sized to the smallest cube, and points each environment map at it.
    /// Cubes in another format are skipped.
    /// </summary>
    /// <param name="envMaps">Environment maps with their own cube textures.</param>
    /// <param name="materialLoader">Material loader the cube textures are evicted from.</param>
    /// <param name="logger">Logger for skipped environment maps.</param>
    /// <returns>The array, or <see langword="null"/> if nothing was packed.</returns>
    public static RenderTexture? Pack(IReadOnlyList<SceneEnvMap> envMaps, MaterialLoader materialLoader, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(envMaps);
        ArgumentNullException.ThrowIfNull(materialLoader);
        ArgumentNullException.ThrowIfNull(logger);

        var cubes = envMaps.Where(static envMap => envMap.EnvMapTexture.Target == TextureTarget.TextureCubeMap).ToList();

        if (cubes.Count == 0)
        {
            return null;
        }

        var size = cubes.Min(static envMap => envMap.EnvMapTexture.Width);
        var format = GetInternalFormat(cubes[0].EnvMapTexture);
        var maxCubes = GL.GetInteger(GetPName.MaxArrayTextureLayers) / CubeFaces;

        var packed = new List<(SceneEnvMap EnvMap, int FirstLevel)>(cubes.Count);

        foreach (var envMap in cubes)
        {
            var texture = envMap.EnvMapTexture;
            var firstLevel = FindLevelOfSize(texture, size);

            if (firstLevel < 0 || GetInternalFormat(texture) != format)
            {
                logger.LogWarning("Skipping environment map {Width}x{Height} ({Format}), incompatible with the {Size}x{Size} ({ArrayFormat}) cube map array",
                    texture.Width, texture.Height, GetInternalFormat(texture), size, size, format);
                continue;
            }

            if (packed.Count == maxCubes)
            {
                logger.LogWarning("Only {Count} of {Total} environment maps fit in the cube map array, skipping the rest", maxCubes, cubes.Count);
                break;
            }

            packed.Add((envMap, firstLevel));
        }

        if (packed.Count == 0)
        {
            return null;
        }

        // Every cube needs each level of the array, which a cube with fewer mips below that size cannot fill
        var levels = packed.Min(static cube => cube.EnvMap.EnvMapTexture.NumMipLevels - cube.FirstLevel);

        var array = new RenderTexture(TextureTarget.TextureCubeMapArray, size, size, packed.Count, levels, "EnvironmentMapArray");
        GL.TextureStorage3D(array.Handle, levels, format, size, size, packed.Count * CubeFaces);
        array.SetFiltering(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
        array.SetWrapMode(RsTextureAddressMode.Clamp);

        var copied = new HashSet<RenderTexture>();

        for (var index = 0; index < packed.Count; index++)
        {
            var (envMap, firstLevel) = packed[index];
            var texture = envMap.EnvMapTexture;

            for (var level = 0; level < levels; level++)
            {
                var levelSize = Math.Max(size >> level, 1);

                // The faces of a cube are its layers
                GL.CopyImageSubData(
                    texture.Handle, ImageTarget.TextureCubeMap, firstLevel + level, 0, 0, 0,
                    array.Handle, ImageTarget.TextureCubeMapArray, level, 0, 0, index * CubeFaces,
                    levelSize, levelSize, CubeFaces);
            }

            copied.Add(texture);
            envMap.EnvMapTexture = array;
            envMap.ArrayIndex = index;
        }

        // Only textures cached by the material loader are deleted, others are owned elsewhere
        foreach (var texture in copied)
        {
            materialLoader.EvictTexture(texture);
        }

        logger.LogDebug("Packed {Count} environment maps into a {Size}x{Size} cube map array with {Levels} mips", packed.Count, size, size, levels);

        return array;
    }

    private static int FindLevelOfSize(RenderTexture texture, int size)
    {
        for (var level = 0; level < texture.NumMipLevels; level++)
        {
            if (Math.Max(texture.Width >> level, 1) == size)
            {
                return level;
            }
        }

        return -1;
    }

    private static SizedInternalFormat GetInternalFormat(RenderTexture texture)
    {
        GL.GetTextureLevelParameter(texture.Handle, 0, GetTextureParameter.TextureInternalFormat, out int format);
        return (SizedInternalFormat)format;
    }
}
