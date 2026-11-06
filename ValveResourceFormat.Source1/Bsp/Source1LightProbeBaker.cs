using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1.Entities;
using VrfTexture = ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Turns the ambient cubes the map compiler stored in the leaves of a Source 1 map into <c>env_light_probe_volume</c>
/// entities with a probe grid texture each, which is what Source 2 maps light models with. Every probe also
/// holds the direct lights a model at it takes, as light indices and strengths.
/// </summary>
internal static class Source1LightProbeBaker
{
    // The probe grid in a volume has this many cells at most along each axis
    private const int TileCells = 32;

    // Cells around a tile that are filled in the same way as the tile, so the filling is the same on both sides of a tile edge
    private const int Margin = 4;

    // A block of cells this small is checked cell by cell
    private const int MaxScannedCells = 8;

    // The scene lights at most this many volumes
    private const int MaxVolumes = 256;

    private const long MaxTextureBytes = 32L * 1024 * 1024;

    private const int BytesPerCell = (BspLeafAmbient.FaceCount * 4 * 2) + (2 * 4);

    private const int DirectLights = Source1ModelLightSelector.MaxDirectLights;

    private static readonly float[] CellSizes = [16f, 32f, 64f, 128f, 256f, 512f];

    // The texture holds the faces in the order +X, +Y, +Z, -X, -Y, -Z, the leaf samples hold +X, -X, +Y, -Y, +Z, -Z
    private static readonly int[] LeafFaceOfSlice = [0, 2, 4, 1, 3, 5];

    private readonly record struct Grid(Vector3 Origin, float CellSize, int CountX, int CountY, int CountZ);

    private readonly record struct LayerCell(int Cell, int X, int Y, int Z, int Leaf, Vector3 Center);

    private readonly record struct Tile(int X, int Y, int Z, int SizeX, int SizeY, int SizeZ);

    // The cells inside or outside the area of the 3D sky box
    private readonly record struct Region(IBrushLightingLocator Locator, bool InSky)
    {
        public bool Contains(Vector3 point) => Locator.IsInSky(point) == InSky;

        public bool ExcludesBox(Vector3 min, Vector3 max)
            => InSky ? !Locator.OverlapsSky(min, max) : Locator.IsEntirelyInSky(min, max);
    }

    /// <summary>Adds the probe volumes of a map and their textures to a converted map.</summary>
    /// <param name="map">The converted map, which receives the textures and the entities of the volumes outside the 3D sky area.</param>
    /// <param name="bsp">The map.</param>
    /// <param name="lump">The entity lump the entities belong to.</param>
    /// <param name="locator">Finds the 3D sky area.</param>
    /// <param name="skyVolumes">Receives the entities of the volumes inside the 3D sky area, which light only what is drawn there.</param>
    public static void Add(Source1ConvertedMap map, BspFile bsp, EntityLump lump, IBrushLightingLocator locator, List<EntityLump.Entity> skyVolumes)
    {
        if (bsp.Models.Length == 0)
        {
            return;
        }

        var leaves = new BspLeafAmbient(bsp);
        var lights = new Source1ModelLightSelector(bsp, map.Entities);

        AddRegion(map, leaves, lights, lump, bsp.Models[0], new Region(locator, InSky: false), "probe", map.Entities);
        AddRegion(map, leaves, lights, lump, bsp.Models[0], new Region(locator, InSky: true), "skyprobe", skyVolumes);

        lights.ApplyToEntities();
    }

    // Bakes the cells of the region, so that the volumes of one area never carry the light of another
    private static void AddRegion(
        Source1ConvertedMap map,
        BspLeafAmbient leaves,
        Source1ModelLightSelector lights,
        EntityLump lump,
        BspModel world,
        Region region,
        string name,
        List<EntityLump.Entity> volumes)
    {
        foreach (var cellSize in CellSizes)
        {
            var grid = CreateGrid(world.Min, world.Max, cellSize);
            var tiles = FindTiles(leaves, grid, region);

            if (tiles == null)
            {
                continue;
            }

            var baked = new BakedCells();

            for (var i = 0; i < tiles.Count; i++)
            {
                volumes.Add(AddVolume(map, leaves, lights, lump, grid, tiles[i], region, baked, name, i));
            }

            return;
        }
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

    // The tiles of the grid that have a cell in a leaf with ambient samples, or null when they exceed the volume or texture limits
    private static List<Tile>? FindTiles(BspLeafAmbient leaves, Grid grid, Region region)
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

                    if (!HasAmbient(leaves, grid, tile, region))
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

    // Halves the block of cells until the nodes of the world rule out the half, or it is small enough to look at each cell
    private static bool HasAmbient(BspLeafAmbient leaves, Grid grid, Tile block, Region region)
    {
        var firstCenter = GetCellCenter(grid, block.X, block.Y, block.Z);
        var lastCenter = GetCellCenter(grid, block.X + block.SizeX - 1, block.Y + block.SizeY - 1, block.Z + block.SizeZ - 1);

        if (region.ExcludesBox(firstCenter, lastCenter) || !leaves.MayHaveAmbient(firstCenter, lastCenter))
        {
            return false;
        }

        if (block.SizeX * block.SizeY * block.SizeZ > MaxScannedCells)
        {
            var (first, second) = Split(block);

            return HasAmbient(leaves, grid, first, region) || HasAmbient(leaves, grid, second, region);
        }

        for (var z = 0; z < block.SizeZ; z++)
        {
            for (var y = 0; y < block.SizeY; y++)
            {
                for (var x = 0; x < block.SizeX; x++)
                {
                    var center = GetCellCenter(grid, block.X + x, block.Y + y, block.Z + z);

                    if (region.Contains(center) && leaves.HasAmbient(leaves.FindLeaf(center)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // Splits a block of cells in two along its longest side
    private static (Tile First, Tile Second) Split(Tile block)
    {
        if (block.SizeX >= block.SizeY && block.SizeX >= block.SizeZ)
        {
            var half = block.SizeX / 2;

            return (block with { SizeX = half }, block with { X = block.X + half, SizeX = block.SizeX - half });
        }

        if (block.SizeY >= block.SizeZ)
        {
            var half = block.SizeY / 2;

            return (block with { SizeY = half }, block with { Y = block.Y + half, SizeY = block.SizeY - half });
        }

        var halfZ = block.SizeZ / 2;

        return (block with { SizeZ = halfZ }, block with { Z = block.Z + halfZ, SizeZ = block.SizeZ - halfZ });
    }

    private static EntityLump.Entity AddVolume(
        Source1ConvertedMap map,
        BspLeafAmbient leaves,
        Source1ModelLightSelector lights,
        EntityLump lump,
        Grid grid,
        Tile tile,
        Region region,
        BakedCells baked,
        string name,
        int index)
    {
        var block = BakeTile(leaves, lights, grid, tile, region, baked);
        var path = string.Create(CultureInfo.InvariantCulture, $"{map.MapPath}/lightprobes/{name}{index}.vtex");
        var indicesPath = string.Create(CultureInfo.InvariantCulture, $"{map.MapPath}/lightprobes/{name}{index}_dli.vtex");
        var strengthsPath = string.Create(CultureInfo.InvariantCulture, $"{map.MapPath}/lightprobes/{name}{index}_dls.vtex");

        var texture = VrfTexture.Create(
            (ushort)tile.SizeX,
            (ushort)tile.SizeY,
            VTexFormat.RGBA16161616F,
            VTexFlags.VOLUME_TEXTURE,
            Vector4.One,
            [WriteTexels(block, tile)],
            depth: (ushort)(tile.SizeZ * BspLeafAmbient.FaceCount));

        var (indices, strengths) = WriteLightTexels(block, tile);

        Source1WorldConverter.AddFile(map, path, texture);
        Source1WorldConverter.AddFile(map, indicesPath, CreateLightTexture(tile, indices));
        Source1WorldConverter.AddFile(map, strengthsPath, CreateLightTexture(tile, strengths));

        var mins = grid.Origin + (new Vector3(tile.X, tile.Y, tile.Z) * grid.CellSize);
        var size = new Vector3(tile.SizeX, tile.SizeY, tile.SizeZ) * grid.CellSize;
        var center = mins + (size * 0.5f);

        var entity = new EntityLump.Entity { ParentLump = lump };
        entity["classname"] = "env_light_probe_volume";
        entity["origin"] = Source1EntityConverter.FormatVector(center);
        entity["box_mins"] = Source1EntityConverter.FormatVector(-size * 0.5f);
        entity["box_maxs"] = Source1EntityConverter.FormatVector(size * 0.5f);
        entity["voxel_size"] = Source1EntityConverter.GetProbeVoxelSize(grid.CellSize);
        entity[Source1MapExport.RegroupKey] = 1L;
        entity["lightprobetexture"] = Source1ResourceReferences.Reference(path);
        entity["lightprobetexture_dli"] = Source1ResourceReferences.Reference(indicesPath);
        entity["lightprobetexture_dls"] = Source1ResourceReferences.Reference(strengthsPath);

        return entity;
    }

    private static Resource CreateLightTexture(Tile tile, byte[] texels)
        => VrfTexture.Create(
            (ushort)tile.SizeX,
            (ushort)tile.SizeY,
            VTexFormat.RGBA8888,
            VTexFlags.VOLUME_TEXTURE,
            Vector4.One,
            [texels],
            depth: (ushort)tile.SizeZ);

    // The cells of the tile and the margin around it, each with the six faces of its ambient cube and its direct lights
    private sealed class CellBlock
    {
        public CellBlock(int sizeX, int sizeY, int sizeZ)
        {
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            Cubes = new Vector3[sizeX * sizeY * sizeZ * BspLeafAmbient.FaceCount];
            Lights = new int[sizeX * sizeY * sizeZ * DirectLights];
            Valid = new bool[sizeX * sizeY * sizeZ];
        }

        public int SizeX { get; }

        public int SizeY { get; }

        public int SizeZ { get; }

        public Vector3[] Cubes { get; }

        public int[] Lights { get; }

        public bool[] Valid { get; }

        public int GetIndex(int x, int y, int z) => (((z * SizeY) + y) * SizeX) + x;
    }

    // The ambient cubes and direct lights of the cells baked so far by their position in the grid. The margin of a tile
    // holds cells of its neighbors, and a cell gets the same cube and lights however many tiles reach it.
    private sealed class BakedCells
    {
        private readonly Dictionary<(int X, int Y, int Z), int> indices = [];
        private readonly List<Vector3> cubes = [];
        private readonly List<int> lights = [];

        public bool TryCopy(int x, int y, int z, Span<Vector3> cube, Span<int> cellLights)
        {
            if (!indices.TryGetValue((x, y, z), out var index))
            {
                return false;
            }

            CollectionsMarshal.AsSpan(cubes).Slice(index * BspLeafAmbient.FaceCount, BspLeafAmbient.FaceCount).CopyTo(cube);
            CollectionsMarshal.AsSpan(lights).Slice(index * DirectLights, DirectLights).CopyTo(cellLights);

            return true;
        }

        public void Add(int x, int y, int z, ReadOnlySpan<Vector3> cube, ReadOnlySpan<int> cellLights)
        {
            indices.Add((x, y, z), indices.Count);
            cubes.AddRange(cube);
            lights.AddRange(cellLights);
        }
    }

    private static CellBlock BakeTile(BspLeafAmbient leaves, Source1ModelLightSelector lights, Grid grid, Tile tile, Region region, BakedCells baked)
    {
        var block = new CellBlock(tile.SizeX + (2 * Margin), tile.SizeY + (2 * Margin), tile.SizeZ + (2 * Margin));
        var layerCells = block.SizeX * block.SizeY;
        var layer = new List<LayerCell>(layerCells);
        var selections = new Source1ModelLightSelector.Selection[layerCells];

        for (var i = 0; i < selections.Length; i++)
        {
            selections[i] = new Source1ModelLightSelector.Selection();
        }

        for (var z = 0; z < block.SizeZ; z++)
        {
            layer.Clear();

            for (var y = 0; y < block.SizeY; y++)
            {
                for (var x = 0; x < block.SizeX; x++)
                {
                    var gridX = tile.X + x - Margin;
                    var gridY = tile.Y + y - Margin;
                    var gridZ = tile.Z + z - Margin;
                    var center = GetCellCenter(grid, gridX, gridY, gridZ);

                    if (!region.Contains(center))
                    {
                        continue;
                    }

                    var leaf = leaves.FindLeaf(center);

                    if (!leaves.HasAmbient(leaf))
                    {
                        continue;
                    }

                    var cell = block.GetIndex(x, y, z);
                    var cube = block.Cubes.AsSpan(cell * BspLeafAmbient.FaceCount, BspLeafAmbient.FaceCount);
                    var cellLights = block.Lights.AsSpan(cell * DirectLights, DirectLights);

                    if (baked.TryCopy(gridX, gridY, gridZ, cube, cellLights))
                    {
                        block.Valid[cell] = true;
                        continue;
                    }

                    if (!leaves.TryBlendAmbientCube(leaf, center, cube))
                    {
                        continue;
                    }

                    layer.Add(new LayerCell(cell, gridX, gridY, gridZ, leaf, center));
                }
            }

            // Finding the lights of a cell is the slow part and independent of the other cells. Light indices are
            // given out in cell order, so the cells are applied one after the other.
            Parallel.For(0, layer.Count, i => lights.Gather(layer[i].Leaf, layer[i].Center, selections[i]));

            for (var i = 0; i < layer.Count; i++)
            {
                var layerCell = layer[i];
                var cube = block.Cubes.AsSpan(layerCell.Cell * BspLeafAmbient.FaceCount, BspLeafAmbient.FaceCount);
                var cellLights = block.Lights.AsSpan(layerCell.Cell * DirectLights, DirectLights);

                lights.Apply(selections[i], cube, cellLights);
                baked.Add(layerCell.X, layerCell.Y, layerCell.Z, cube, cellLights);

                block.Valid[layerCell.Cell] = true;
            }
        }

        for (var pass = 0; pass < Margin; pass++)
        {
            FillFromNeighbors(block);
        }

        return block;
    }

    // Gives every empty cell next to filled cells the average of them. Cells filled by this pass are only used by the next one.
    private static void FillFromNeighbors(CellBlock block)
    {
        var filled = new List<int>();

        for (var z = 0; z < block.SizeZ; z++)
        {
            for (var y = 0; y < block.SizeY; y++)
            {
                for (var x = 0; x < block.SizeX; x++)
                {
                    var cell = block.GetIndex(x, y, z);

                    if (block.Valid[cell])
                    {
                        continue;
                    }

                    if (AverageNeighbors(block, x, y, z, cell))
                    {
                        filled.Add(cell);
                    }
                }
            }
        }

        foreach (var cell in filled)
        {
            block.Valid[cell] = true;
        }
    }

    private static bool AverageNeighbors(CellBlock block, int x, int y, int z, int cell)
    {
        var count = 0;
        Span<Vector3> sum = stackalloc Vector3[BspLeafAmbient.FaceCount];

        var lightsFrom = -1;

        if (AddNeighbor(block, x + 1, y, z, sum))
        {
            count++;
            lightsFrom = block.GetIndex(x + 1, y, z);
        }

        if (AddNeighbor(block, x - 1, y, z, sum))
        {
            count++;
            lightsFrom = block.GetIndex(x - 1, y, z);
        }

        if (AddNeighbor(block, x, y + 1, z, sum))
        {
            count++;
            lightsFrom = block.GetIndex(x, y + 1, z);
        }

        if (AddNeighbor(block, x, y - 1, z, sum))
        {
            count++;
            lightsFrom = block.GetIndex(x, y - 1, z);
        }

        if (AddNeighbor(block, x, y, z + 1, sum))
        {
            count++;
            lightsFrom = block.GetIndex(x, y, z + 1);
        }

        if (AddNeighbor(block, x, y, z - 1, sum))
        {
            count++;
            lightsFrom = block.GetIndex(x, y, z - 1);
        }

        if (count == 0)
        {
            return false;
        }

        for (var face = 0; face < BspLeafAmbient.FaceCount; face++)
        {
            block.Cubes[(cell * BspLeafAmbient.FaceCount) + face] = sum[face] / count;
        }

        // Light indices cannot be averaged, so the cell takes the lights of one of its neighbors
        Array.Copy(block.Lights, lightsFrom * DirectLights, block.Lights, cell * DirectLights, DirectLights);

        return true;
    }

    // Adds the faces of a filled cell to the sums, and returns false for a cell that is outside the block or empty
    private static bool AddNeighbor(CellBlock block, int x, int y, int z, Span<Vector3> sum)
    {
        if ((uint)x >= (uint)block.SizeX || (uint)y >= (uint)block.SizeY || (uint)z >= (uint)block.SizeZ)
        {
            return false;
        }

        var neighbor = block.GetIndex(x, y, z);

        if (!block.Valid[neighbor])
        {
            return false;
        }

        for (var face = 0; face < BspLeafAmbient.FaceCount; face++)
        {
            sum[face] += block.Cubes[(neighbor * BspLeafAmbient.FaceCount) + face];
        }

        return true;
    }

    // The faces are one slice stack each, one after another, which is the layout the shaders read
    private static byte[] WriteTexels(CellBlock block, Tile tile)
    {
        var texels = new Half[tile.SizeX * tile.SizeY * tile.SizeZ * BspLeafAmbient.FaceCount * 4];
        var texel = 0;

        for (var slice = 0; slice < BspLeafAmbient.FaceCount; slice++)
        {
            var face = LeafFaceOfSlice[slice];

            for (var z = 0; z < tile.SizeZ; z++)
            {
                for (var y = 0; y < tile.SizeY; y++)
                {
                    for (var x = 0; x < tile.SizeX; x++)
                    {
                        var cell = block.GetIndex(x + Margin, y + Margin, z + Margin);
                        var color = block.Cubes[(cell * BspLeafAmbient.FaceCount) + face];

                        texels[texel++] = (Half)color.X;
                        texels[texel++] = (Half)color.Y;
                        texels[texel++] = (Half)color.Z;
                        texels[texel++] = Half.One;
                    }
                }
            }
        }

        return MemoryMarshal.AsBytes(texels.AsSpan()).ToArray();
    }

    // One texel per cell with the index of a direct light in each channel, and one with its strength, which is
    // zero for an unused slot
    private static (byte[] Indices, byte[] Strengths) WriteLightTexels(CellBlock block, Tile tile)
    {
        var cellCount = tile.SizeX * tile.SizeY * tile.SizeZ;
        var indices = new byte[cellCount * DirectLights];
        var strengths = new byte[cellCount * DirectLights];
        var texel = 0;

        for (var z = 0; z < tile.SizeZ; z++)
        {
            for (var y = 0; y < tile.SizeY; y++)
            {
                for (var x = 0; x < tile.SizeX; x++)
                {
                    var cell = block.GetIndex(x + Margin, y + Margin, z + Margin);

                    for (var slot = 0; slot < DirectLights; slot++)
                    {
                        var light = block.Valid[cell] ? block.Lights[(cell * DirectLights) + slot] : -1;

                        if (light >= 0)
                        {
                            indices[texel] = (byte)light;
                            strengths[texel] = byte.MaxValue;
                        }

                        texel++;
                    }
                }
            }
        }

        return (indices, strengths);
    }
}
