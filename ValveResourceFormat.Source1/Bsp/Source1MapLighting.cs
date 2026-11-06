namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Finds whether a point of a Source 1 map is in the area of its 3D sky box.
/// </summary>
internal sealed class Source1MapLighting : IBrushLightingLocator
{
    private readonly BspFile bsp;
    private readonly BspLeafAmbient leaves;
    private readonly AABB? skyBounds;

    /// <summary>Creates the lookups of a map.</summary>
    public Source1MapLighting(BspFile bsp)
    {
        ArgumentNullException.ThrowIfNull(bsp);

        this.bsp = bsp;

        leaves = new BspLeafAmbient(bsp);
        skyBounds = FindSkyBounds();
    }

    /// <inheritdoc/>
    public bool IsInSky(Vector3 position) => skyBounds is { } sky && sky.Contains(position);

    /// <inheritdoc/>
    public bool OverlapsSky(Vector3 min, Vector3 max) => skyBounds is { } sky && sky.Intersects(new AABB(min, max));

    /// <inheritdoc/>
    public bool IsEntirelyInSky(Vector3 min, Vector3 max) => skyBounds is { } sky && sky.Contains(new AABB(min, max));

    // The area of the sky_camera is the 3D sky box, which sits inside the map away from the playable space. A map whose
    // playable space is in the same area has none.
    private AABB? FindSkyBounds()
    {
        BspEntity? skyCamera = null;
        BspEntity? spawn = null;

        foreach (var entity in bsp.Entities)
        {
            if (entity.ClassName == "sky_camera")
            {
                skyCamera ??= entity;
            }
            else if (entity.ClassName.StartsWith("info_player", StringComparison.Ordinal))
            {
                spawn ??= entity;
            }
        }

        if (skyCamera == null)
        {
            return null;
        }

        var area = FindArea(skyCamera.Origin);

        if (area <= 0 || (spawn != null && FindArea(spawn.Origin) == area))
        {
            return null;
        }

        return GetAreaBounds(area);
    }

    private int FindArea(Vector3 point)
    {
        var leaf = leaves.FindLeaf(point);

        return leaf >= 0 && leaf < bsp.Leafs.Length ? bsp.Leafs[leaf].Area : -1;
    }

    private AABB? GetAreaBounds(int area)
    {
        var found = false;
        var bounds = new AABB();

        foreach (var leaf in bsp.Leafs)
        {
            if (leaf.Area != area || leaf.Contents == 1)
            {
                continue;
            }

            var leafBounds = new AABB(leaf.Mins, leaf.Maxs);
            bounds = found ? bounds.Union(leafBounds) : leafBounds;
            found = true;
        }

        return found ? bounds : null;
    }
}
