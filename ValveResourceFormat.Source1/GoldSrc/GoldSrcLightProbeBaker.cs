using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// Lights the models of a GoldSrc map the way the engine does, from the lightmap under each point, and stores the result as
/// <c>env_light_probe_volume</c> entities with a probe grid texture each, which is what Source 2 maps light models with.
/// </summary>
internal static class GoldSrcLightProbeBaker
{
    // The probe grid in a volume has this many cells at most along each axis
    private const int TileCells = 32;

    // The scene lights at most this many volumes
    private const int MaxVolumes = 256;

    private const long MaxTextureBytes = 32L * 1024 * 1024;

    private const int FaceCount = 6;
    private const int BytesPerCell = FaceCount * 4 * 2;

    // How far the light wraps around a sphere, where 1 is a plain dot product
    private const float Lambert = 1.4953241f;

    private static readonly float[] CellSizes = [32f, 64f, 128f, 256f, 512f];

    // The faces of the probe texture are the slices +X, +Y, +Z, -X, -Y, -Z
    private static readonly Vector3[] FaceNormals =
    [
        Vector3.UnitX,
        Vector3.UnitY,
        Vector3.UnitZ,
        -Vector3.UnitX,
        -Vector3.UnitY,
        -Vector3.UnitZ,
    ];

    private readonly record struct Grid(Vector3 Origin, float CellSize, int CountX, int CountY, int CountZ);

    private readonly record struct Tile(int X, int Y, int Z, int SizeX, int SizeY, int SizeZ);

    /// <summary>Adds the probe volumes of a map and their textures to a converted map.</summary>
    /// <param name="map">The converted map, which receives the textures.</param>
    /// <param name="bsp">The map.</param>
    /// <param name="lump">The entity lump the entities belong to.</param>
    /// <param name="styleScales">The sample factor of each light style.</param>
    /// <param name="volumes">Receives the entities of the volumes.</param>
    /// <param name="logger">Where a map that gets no volumes is reported.</param>
    public static void Add(GoldSrcConvertedMap map, GoldSrcBspFile bsp, EntityLump lump, int[] styleScales, List<EntityLump.Entity> volumes, ILogger logger)
    {
        if (bsp.Models.Length == 0 || bsp.Lighting.Length == 0)
        {
            return;
        }

        var world = bsp.Models[0];
        var sampler = new GoldSrcLightSampler(bsp, styleScales);

        foreach (var cellSize in CellSizes)
        {
            var grid = CreateGrid(world.Min, world.Max, cellSize);
            var tiles = FindTiles(sampler, grid);

            if (tiles == null)
            {
                continue;
            }

            for (var i = 0; i < tiles.Count; i++)
            {
                volumes.Add(AddVolume(map, sampler, lump, grid, tiles[i], i));
            }

            return;
        }

        logger.LogWarning("No probe grid of the map fits the volume and texture limits, so models are lit like a model on its own");
    }

    private static Grid CreateGrid(Vector3 min, Vector3 max, float cellSize)
    {
        var size = max - min;

        return new Grid(
            min,
            cellSize,
            (int)MathF.Ceiling(size.X / cellSize),
            (int)MathF.Ceiling(size.Y / cellSize),
            (int)MathF.Ceiling(size.Z / cellSize));
    }

    private static Vector3 GetCellCenter(Grid grid, int x, int y, int z)
        => grid.Origin + (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * grid.CellSize);

    // The tiles of the grid that have a cell outside the solid, or null when they exceed the volume or texture limits
    private static List<Tile>? FindTiles(GoldSrcLightSampler sampler, Grid grid)
    {
        var tiles = new List<Tile>();
        var bytes = 0L;

        for (var z = 0; z < grid.CountZ; z += TileCells)
        {
            for (var y = 0; y < grid.CountY; y += TileCells)
            {
                for (var x = 0; x < grid.CountX; x += TileCells)
                {
                    var tile = new Tile(
                        x,
                        y,
                        z,
                        Math.Min(TileCells, grid.CountX - x),
                        Math.Min(TileCells, grid.CountY - y),
                        Math.Min(TileCells, grid.CountZ - z));

                    if (!HasOpenCell(sampler, grid, tile))
                    {
                        continue;
                    }

                    tiles.Add(tile);
                    bytes += (long)tile.SizeX * tile.SizeY * tile.SizeZ * BytesPerCell;

                    if (tiles.Count > MaxVolumes || bytes > MaxTextureBytes)
                    {
                        return null;
                    }
                }
            }
        }

        return tiles;
    }

    private static bool HasOpenCell(GoldSrcLightSampler sampler, Grid grid, Tile tile)
    {
        for (var z = 0; z < tile.SizeZ; z++)
        {
            for (var y = 0; y < tile.SizeY; y++)
            {
                for (var x = 0; x < tile.SizeX; x++)
                {
                    var center = GetCellCenter(grid, tile.X + x, tile.Y + y, tile.Z + z);

                    if (sampler.GetContents(center) != GoldSrcContents.Solid)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static EntityLump.Entity AddVolume(GoldSrcConvertedMap map, GoldSrcLightSampler sampler, EntityLump lump, Grid grid, Tile tile, int index)
    {
        var pixels = BakeTile(sampler, grid, tile);
        var path = string.Create(CultureInfo.InvariantCulture, $"{map.MapPath}/lightprobes/probe{index}.vtex");

        var texture = Texture.Create(
            (ushort)tile.SizeX,
            (ushort)tile.SizeY,
            VTexFormat.RGBA16161616F,
            VTexFlags.VOLUME_TEXTURE,
            Vector4.One,
            [pixels],
            depth: (ushort)(tile.SizeZ * FaceCount));

        map.AddFile(path, texture);

        var mins = grid.Origin + (new Vector3(tile.X, tile.Y, tile.Z) * grid.CellSize);
        var size = new Vector3(tile.SizeX, tile.SizeY, tile.SizeZ) * grid.CellSize;
        var center = mins + (size * 0.5f);

        var entity = new EntityLump.Entity { ParentLump = lump };
        entity["classname"] = "env_light_probe_volume";
        entity["origin"] = Source1EntityConverter.FormatVector(center);
        entity["box_mins"] = Source1EntityConverter.FormatVector(-size * 0.5f);
        entity["box_maxs"] = Source1EntityConverter.FormatVector(size * 0.5f);
        entity["edge_fade_dists"] = Source1EntityConverter.FormatVector(Vector3.Zero);
        entity["voxel_size"] = grid.CellSize;
        entity["lightprobetexture"] = Source1ResourceReferences.Reference(path);

        return entity;
    }

    // The faces are one slice stack each, one after another, which is the layout the shaders read
    private static byte[] BakeTile(GoldSrcLightSampler sampler, Grid grid, Tile tile)
    {
        var cellCount = tile.SizeX * tile.SizeY * tile.SizeZ;
        var lights = new GoldSrcLightSampler.StudioLight[cellCount];
        var cell = 0;

        for (var z = 0; z < tile.SizeZ; z++)
        {
            for (var y = 0; y < tile.SizeY; y++)
            {
                for (var x = 0; x < tile.SizeX; x++)
                {
                    lights[cell++] = sampler.Sample(GetCellCenter(grid, tile.X + x, tile.Y + y, tile.Z + z));
                }
            }
        }

        var texels = new Half[cellCount * FaceCount * 4];
        var texel = 0;

        for (var face = 0; face < FaceCount; face++)
        {
            foreach (var light in lights)
            {
                var color = GetFaceColor(light, FaceNormals[face]);

                texels[texel++] = (Half)color.X;
                texels[texel++] = (Half)color.Y;
                texels[texel++] = (Half)color.Z;
                texels[texel++] = Half.One;
            }
        }

        return MemoryMarshal.AsBytes(texels.AsSpan()).ToArray();
    }

    // The ambient light plus the shade of the surface facing away from the light direction, drawn through the light curve
    private static Vector3 GetFaceColor(GoldSrcLightSampler.StudioLight light, Vector3 normal)
    {
        var cosine = MathF.Min(Vector3.Dot(normal, light.Direction), 1f);
        var wrapped = (Lambert - 1f + cosine) / Lambert;
        var shade = light.Shade * (1f - MathF.Max(wrapped, 0f));
        var brightness = Math.Clamp(light.Ambient + shade, 0f, 1f);
        var curved = MathF.Pow(brightness, GoldSrcGamma.LightExponent);

        return ColorSpace.SrgbGammaToLinear(light.Color * curved);
    }
}
