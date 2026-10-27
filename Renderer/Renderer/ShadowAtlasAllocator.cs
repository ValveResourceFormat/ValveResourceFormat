using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer;

/// <summary>Places the shadow faces of the lights a <see cref="ShadowMapper"/> binned in the atlas, anew every frame.</summary>
public class ShadowAtlasAllocator
{
    /// <summary>Gets the occupancy of the atlas.</summary>
    protected ShadowAtlasPacker Atlas { get; } = new();

    /// <summary>Starts placing the faces of a frame.</summary>
    /// <param name="atlasSize">Pixel size of the shadow atlas texture.</param>
    public virtual void BeginFrame(int atlasSize)
    {
        Atlas.Begin(atlasSize);
    }

    /// <summary>Finds a shadow face a region in the atlas, shrinking it until it fits.</summary>
    /// <param name="light">The light the face belongs to.</param>
    /// <param name="lightPlace">Place of the light among the shadowed lights in binned order, closest first.</param>
    /// <param name="face">Index of the face within the light's faces.</param>
    /// <param name="width">Face width in texels the light asks for.</param>
    /// <param name="height">Face height in texels the light asks for.</param>
    /// <param name="region">The region found.</param>
    /// <param name="isUpToDate">Whether the region already holds the depth wanted for the face this frame, so it is not rendered.</param>
    /// <returns>Whether a region was found.</returns>
    public virtual bool TryPlaceFace(SceneLight light, int lightPlace, int face, int width, int height,
        out ShadowAtlasRegion region, out bool isUpToDate)
    {
        isUpToDate = false;

        return TryAllocate(width, height, out region);
    }

    /// <summary>Called once the faces of a frame are placed.</summary>
    /// <param name="placedAll">Whether every face found a region.</param>
    /// <returns>Whether the atlas was emptied for the faces to be placed again.</returns>
    public virtual bool Repack(bool placedAll) => false;

    /// <summary>Frees a region of the atlas when a face does not fit.</summary>
    /// <returns>Whether a region was freed.</returns>
    protected virtual bool MakeRoom() => false;

    /// <summary>Allocates a region, making room before shrinking the face.</summary>
    /// <param name="width">Face width in texels.</param>
    /// <param name="height">Face height in texels.</param>
    /// <param name="region">The region allocated.</param>
    /// <returns>Whether a region was allocated.</returns>
    protected bool TryAllocate(int width, int height, out ShadowAtlasRegion region)
    {
        while (!Atlas.TryAllocate(width, height, out region))
        {
            if (MakeRoom())
            {
                continue;
            }

            if (Math.Max(width, height) <= ShadowMapper.MinShadowFaceSize)
            {
                return false;
            }

            ShadowMapper.ShrinkFace(ref width, ref height);
        }

        return true;
    }
}
