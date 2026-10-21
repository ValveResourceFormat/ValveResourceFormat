using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// The cube array the editor copies the cubemaps it previews into, so they are sampled like the array a compiled map
/// bakes: every fragment blends the ones it is inside of. Each cubemap takes the next slot.
/// </summary>
public sealed class EnvironmentMapCache
{
    /// <summary>Gets the cube array.</summary>
    public RenderTexture Texture { get; }

    private EnvironmentMapCache(RenderTexture texture)
    {
        Texture = texture;
    }

    /// <summary>
    /// Makes the cube array, sized and formatted like the first cubemap, copies every cubemap into its slot, and points
    /// each environment map at it. A cubemap of another format is left out, and one of another size only fills the mips
    /// the two share.
    /// </summary>
    /// <param name="envMaps">The environment maps, each with its own cubemap, or a cubemap array and its index in it.</param>
    /// <param name="logger">Where cubemaps that do not fit the array are reported.</param>
    /// <returns>The cache, or <see langword="null"/> when there are no environment maps.</returns>
    public static EnvironmentMapCache? Create(IReadOnlyList<SceneEnvMap> envMaps, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(envMaps);
        ArgumentNullException.ThrowIfNull(logger);

        if (envMaps.Count == 0)
        {
            return null;
        }

        var first = envMaps[0].EnvMapTexture;
        var format = GetInternalFormat(first);

        var texture = new RenderTexture(TextureTarget.TextureCubeMapArray, first.Width, first.Height, envMaps.Count, first.NumMipLevels, nameof(EnvironmentMapCache));
        GL.TextureStorage3D(texture.Handle, first.NumMipLevels, format, first.Width, first.Height, envMaps.Count * 6);
        texture.SetFiltering(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
        texture.SetWrapMode(RsTextureAddressMode.Clamp);

        for (var slot = 0; slot < envMaps.Count; slot++)
        {
            var envMap = envMaps[slot];
            var source = envMap.EnvMapTexture;

            // A cubemap array source names the cube to take by its array index
            var sourceIndex = Math.Max(envMap.ArrayIndex, 0);

            envMap.EnvMapTexture = texture;
            envMap.ArrayIndex = slot;

            if (GetInternalFormat(source) != format)
            {
                logger.LogWarning("Cubemap {Slot} is not in the format of the others and is left out", slot);
                continue;
            }

            var sourceTarget = source.Target == TextureTarget.TextureCubeMapArray ? ImageTarget.TextureCubeMapArray : ImageTarget.TextureCubeMap;

            for (var level = 0; level < texture.NumMipLevels; level++)
            {
                var size = Math.Max(texture.Width >> level, 1);
                var sourceLevel = FindLevelOfSize(source, size);

                if (sourceLevel < 0)
                {
                    continue;
                }

                // A cube's faces are its layers
                GL.CopyImageSubData(
                    source.Handle, sourceTarget, sourceLevel, 0, 0, sourceIndex * 6,
                    texture.Handle, ImageTarget.TextureCubeMapArray, level, 0, 0, slot * 6,
                    size, size, 6);
            }
        }

        return new EnvironmentMapCache(texture);
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
