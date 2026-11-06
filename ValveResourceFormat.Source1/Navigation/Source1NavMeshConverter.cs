using System.IO;
using System.Text;
using ValveResourceFormat.NavMesh;

namespace ValveResourceFormat.Source1.Navigation;

/// <summary>
/// Converts the navigation mesh of a Source 1 game to the file of a Source 2 game, with a single hull. The areas are
/// stored as rectangles with a height at each corner, and become polygons of four corners.
/// </summary>
/// <remarks>
/// The place names, area attributes and everything the bots of the game use, such as encounter paths and visibility,
/// are not converted.
/// </remarks>
internal static class Source1NavMeshConverter
{
    private const uint Version15 = 15;
    private const uint Version16 = 16;

    // The latest version that stores no key values blocks
    private const uint OutputVersion = 35;
    private const int OutputGenerationVersion = 12;

    // The sub-version is owned by the game, and decides the data stored after the common part of each area
    private const uint ApproachAreasSubVersion = 1;
    private const uint GameAttributesSubVersion = 2;
    private const uint SurvivalSubVersion = 13;

    /// <summary>Converts a Source 1 navigation mesh file to a Source 2 one.</summary>
    public static MemoryStream Convert(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var reader = new BinaryReader(new MemoryStream(Source1Streams.ReadAll(source)), Encoding.UTF8);
        var mesh = Read(reader);

        var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            Write(writer, mesh);
        }

        output.Position = 0;
        return output;
    }

    private sealed class Mesh
    {
        public bool IsAnalyzed { get; init; }
        public List<NavMeshArea> Areas { get; } = [];
        public List<NavMeshLadder> Ladders { get; } = [];
    }

    private static Mesh Read(BinaryReader reader)
    {
        var magic = reader.ReadUInt32();
        if (magic != NavMeshFile.MAGIC)
        {
            throw new UnexpectedMagicException("Unexpected magic", magic, nameof(magic));
        }

        var version = reader.ReadUInt32();
        if (version is not (Version15 or Version16))
        {
            throw new UnexpectedMagicException("Unsupported nav version", version, nameof(version));
        }

        var subVersion = reader.ReadUInt32();
        var isKnownSubVersion = subVersion <= GameAttributesSubVersion || subVersion >= SurvivalSubVersion;
        if (!isKnownSubVersion)
        {
            throw new UnexpectedMagicException("Unsupported nav sub version", subVersion, nameof(subVersion));
        }

        // Size of the map the mesh was built for
        reader.ReadUInt32();

        var mesh = new Mesh { IsAnalyzed = reader.ReadByte() != 0 };

        var placeCount = reader.ReadUInt16();
        for (var i = 0; i < placeCount; i++)
        {
            var length = reader.ReadUInt16();
            reader.BaseStream.Seek(length, SeekOrigin.Current);
        }

        // Whether any area has no place name
        reader.ReadByte();

        if (subVersion >= SurvivalSubVersion)
        {
            // Name and flags stored ahead of the areas, not used
            reader.ReadNullTermString(Encoding.ASCII);
            reader.ReadUInt16();
        }

        var areasPosition = reader.BaseStream.Position;

        if (subVersion == ApproachAreasSubVersion)
        {
            // Sub-version 1 is not unique to one game, the other layout stores a 32-bit value instead of the approach areas
            if (TryReadAreasAndLadders(reader, mesh, version, subVersion, hasApproachAreas: true))
            {
                return mesh;
            }

            reader.BaseStream.Position = areasPosition;
            mesh.Areas.Clear();
            mesh.Ladders.Clear();
        }

        ReadAreasAndLadders(reader, mesh, version, subVersion, hasApproachAreas: false);
        return mesh;
    }

    private static bool TryReadAreasAndLadders(BinaryReader reader, Mesh mesh, uint version, uint subVersion, bool hasApproachAreas)
    {
        try
        {
            ReadAreasAndLadders(reader, mesh, version, subVersion, hasApproachAreas);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
        {
            return false;
        }

        return reader.BaseStream.Position == reader.BaseStream.Length;
    }

    private static void ReadAreasAndLadders(BinaryReader reader, Mesh mesh, uint version, uint subVersion, bool hasApproachAreas)
    {
        var areaCount = reader.ReadUInt32();
        for (var i = 0; i < areaCount; i++)
        {
            mesh.Areas.Add(ReadArea(reader, version, subVersion, hasApproachAreas));
        }

        var areas = new Dictionary<uint, NavMeshArea>();
        foreach (var area in mesh.Areas)
        {
            areas[area.AreaId] = area;
        }

        var ladderCount = ReadCount(reader, sizeof(uint));
        for (var i = 0; i < ladderCount; i++)
        {
            mesh.Ladders.Add(ReadLadder(reader, areas));
        }

        if (subVersion >= SurvivalSubVersion)
        {
            // Game specific value stored after the ladders, not used
            reader.ReadUInt32();
        }
    }

    // The corners are stored northwest, northeast, southeast, southwest, so the connections of each edge are the
    // ones of the north, east, south and west side.
    private static NavMeshArea ReadArea(BinaryReader reader, uint version, uint subVersion, bool hasApproachAreas)
    {
        var area = new NavMeshArea { AreaId = reader.ReadUInt32() };

        // Attributes of the game
        reader.ReadInt32();

        var northwest = ReadVector3(reader);
        var southeast = ReadVector3(reader);
        var northeastHeight = reader.ReadSingle();
        var southwestHeight = reader.ReadSingle();

        area.Corners =
        [
            northwest,
            new Vector3(southeast.X, northwest.Y, northeastHeight),
            southeast,
            new Vector3(northwest.X, southeast.Y, southwestHeight),
        ];

        area.Connections = new NavMeshConnection[area.Corners.Length][];
        for (var i = 0; i < area.Corners.Length; i++)
        {
            area.Connections[i] = ReadConnections(reader);
        }

        var hidingSpotCount = reader.ReadByte();
        area.HidingSpots = new NavMeshHidingSpot[hidingSpotCount];
        for (var i = 0; i < hidingSpotCount; i++)
        {
            area.HidingSpots[i] = NavMeshHidingSpot.Read(reader);
        }

        SkipEncounterPaths(reader);

        // Place name index
        reader.ReadUInt16();

        area.LaddersAbove = ReadIds(reader);
        area.LaddersBelow = ReadIds(reader);

        // Earliest occupy time of each team, and the light intensity at each corner
        reader.BaseStream.Seek(2 * sizeof(float) + 4 * sizeof(float), SeekOrigin.Current);

        SkipGameData(reader, version, subVersion, hasApproachAreas);

        return area;
    }

    private static NavMeshLadder ReadLadder(BinaryReader reader, Dictionary<uint, NavMeshArea> areas)
    {
        var ladder = new NavMeshLadder
        {
            Id = reader.ReadUInt32(),
            Width = reader.ReadSingle(),
            Top = ReadVector3(reader),
            Bottom = ReadVector3(reader),
            Length = reader.ReadSingle(),
            Direction = (NavDirectionType)reader.ReadUInt32(),
        };

        ladder.TopForwardArea = areas.GetValueOrDefault(reader.ReadUInt32());
        ladder.TopLeftArea = areas.GetValueOrDefault(reader.ReadUInt32());
        ladder.TopRightArea = areas.GetValueOrDefault(reader.ReadUInt32());
        ladder.TopBehindArea = areas.GetValueOrDefault(reader.ReadUInt32());
        ladder.BottomArea = areas.GetValueOrDefault(reader.ReadUInt32());

        return ladder;
    }

    private static Vector3 ReadVector3(BinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static NavMeshConnection[] ReadConnections(BinaryReader reader)
    {
        var connectionCount = ReadCount(reader, sizeof(uint));
        var connections = new NavMeshConnection[connectionCount];

        for (var i = 0; i < connectionCount; i++)
        {
            connections[i] = new NavMeshConnection { AreaId = reader.ReadUInt32() };
        }

        return connections;
    }

    private static uint[] ReadIds(BinaryReader reader)
    {
        var idCount = ReadCount(reader, sizeof(uint));
        var ids = new uint[idCount];
        for (var i = 0; i < idCount; i++)
        {
            ids[i] = reader.ReadUInt32();
        }

        return ids;
    }

    // Reads an element count and rejects one that cannot fit in the rest of the stream
    private static uint ReadCount(BinaryReader reader, int elementSize)
    {
        var count = reader.ReadUInt32();
        var remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (count > remaining / elementSize)
        {
            throw new InvalidDataException($"Element count {count} does not fit in the file");
        }

        return count;
    }

    private static void SkipEncounterPaths(BinaryReader reader)
    {
        var pathCount = ReadCount(reader, 2 * (sizeof(uint) + sizeof(byte)) + sizeof(byte));
        for (var i = 0; i < pathCount; i++)
        {
            // Area and direction the path comes from and goes to
            reader.BaseStream.Seek(2 * (sizeof(uint) + sizeof(byte)), SeekOrigin.Current);

            var spotCount = reader.ReadByte();
            reader.BaseStream.Seek(spotCount * (sizeof(uint) + sizeof(byte)), SeekOrigin.Current);
        }
    }

    private static void SkipVisibleAreas(BinaryReader reader)
    {
        // Id and attributes of each visible area, then the area visibility is inherited from
        var visibleAreaCount = reader.ReadUInt32();
        reader.BaseStream.Seek(visibleAreaCount * (sizeof(uint) + sizeof(byte)) + sizeof(uint), SeekOrigin.Current);
    }

    private static void SkipGameData(BinaryReader reader, uint version, uint subVersion, bool hasApproachAreas)
    {
        if (subVersion >= SurvivalSubVersion)
        {
            reader.BaseStream.Seek(sizeof(uint) + sizeof(ushort), SeekOrigin.Current);
            SkipVisibleAreas(reader);
            return;
        }

        if (version >= Version16)
        {
            SkipVisibleAreas(reader);
        }

        if (hasApproachAreas)
        {
            var approachAreaCount = reader.ReadByte();
            reader.BaseStream.Seek(approachAreaCount * (3 * sizeof(uint) + 2 * sizeof(byte)), SeekOrigin.Current);
            return;
        }

        if (subVersion is ApproachAreasSubVersion or GameAttributesSubVersion)
        {
            reader.ReadUInt32();
        }
    }

    private static void Write(BinaryWriter writer, Mesh mesh)
    {
        writer.Write(NavMeshFile.MAGIC);
        writer.Write(OutputVersion);

        // Sub-version, which stores custom data when set
        writer.Write(0u);
        writer.Write(mesh.IsAnalyzed);

        // No places, and no area without a place
        writer.Write((ushort)0);
        writer.Write(false);

        WritePolygons(writer, mesh.Areas);

        // No movable meshes
        writer.Write(0u);

        writer.Write(mesh.Areas.Count);
        var polygonIndex = 0u;
        foreach (var area in mesh.Areas)
        {
            WriteArea(writer, area, polygonIndex++);
        }

        writer.Write(mesh.Ladders.Count);
        foreach (var ladder in mesh.Ladders)
        {
            WriteLadder(writer, ladder);
        }

        // No transformed bounds
        writer.Write(0);

        WriteGenerationParams(writer);
    }

    // Every polygon owns its corners, so the corner indices of a polygon follow on from the previous one
    private static void WritePolygons(BinaryWriter writer, List<NavMeshArea> areas)
    {
        var cornerCount = 0;
        foreach (var area in areas)
        {
            cornerCount += area.Corners.Length;
        }

        writer.Write(cornerCount);
        foreach (var area in areas)
        {
            foreach (var corner in area.Corners)
            {
                WriteVector3(writer, corner);
            }
        }

        writer.Write(areas.Count);
        var cornerIndex = 0u;
        foreach (var area in areas)
        {
            writer.Write((byte)area.Corners.Length);
            foreach (var corner in area.Corners)
            {
                writer.Write(cornerIndex++);
            }

            writer.Write(NavMeshFile.NoMovableMesh);
        }

        // No corner gravity directions
        writer.Write(0u);
    }

    private static void WriteArea(BinaryWriter writer, NavMeshArea area, uint polygonIndex)
    {
        writer.Write(area.AreaId);

        // Attributes
        writer.Write(0L);

        // Hull
        writer.Write((byte)0);

        writer.Write(polygonIndex);

        // Not used
        writer.Write(0f);

        foreach (var edgeConnections in area.Connections)
        {
            writer.Write(edgeConnections.Length);
            foreach (var connection in edgeConnections)
            {
                writer.Write(connection.AreaId);
                writer.Write(connection.EdgeId);
            }
        }

        writer.Write((byte)area.HidingSpots.Length);
        foreach (var spot in area.HidingSpots)
        {
            writer.Write(spot.Id);
            WriteVector3(writer, spot.Position);
            writer.Write((byte)spot.Flags);
        }

        // No encounter paths
        writer.Write(0u);

        WriteIds(writer, area.LaddersAbove);
        WriteIds(writer, area.LaddersBelow);
    }

    private static void WriteLadder(BinaryWriter writer, NavMeshLadder ladder)
    {
        writer.Write(ladder.Id);
        writer.Write(ladder.Width);
        WriteVector3(writer, ladder.Top);
        WriteVector3(writer, ladder.Bottom);
        writer.Write(ladder.Length);
        writer.Write((uint)ladder.Direction);

        writer.Write(ladder.TopForwardArea?.AreaId ?? 0);
        writer.Write(ladder.TopLeftArea?.AreaId ?? 0);
        writer.Write(ladder.TopRightArea?.AreaId ?? 0);
        writer.Write(ladder.TopBehindArea?.AreaId ?? 0);
        writer.Write(ladder.BottomArea?.AreaId ?? 0);

        // Source 1 ladders have no areas on the sides of the bottom
        writer.Write(0u);
        writer.Write(0u);
    }

    // The values of the generation are not known, only the hull count matters to readers
    private static void WriteGenerationParams(BinaryWriter writer)
    {
        writer.Write(OutputGenerationVersion);

        // Use project defaults
        writer.Write(0u);

        // Tile size, cell size and cell height
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);

        // Minimum and merged region size
        writer.Write(0);
        writer.Write(0);

        // Mesh sample distance and maximum sample error
        writer.Write(0f);
        writer.Write(0f);

        // Maximum edge length and error, then the vertices per polygon
        writer.Write(0);
        writer.Write(0f);
        writer.Write(0);

        // Small area on edge removal
        writer.Write(0f);

        // Hull preset name and hull definitions file
        writer.Write((byte)0);
        writer.Write((byte)0);

        const int hullCount = 1;
        writer.Write(hullCount);

        // Enabled, radius and height
        writer.Write(true);
        writer.Write(0f);
        writer.Write(0f);

        // Short height enabled and short height
        writer.Write(false);
        writer.Write(0f);

        // Maximum climb, maximum slope and the jump distances down, horizontally and up
        writer.Write(0f);
        writer.Write(0);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);

        // Border erosion
        writer.Write(0);

        // Gravity follows rotation
        writer.Write(false);
    }

    private static void WriteIds(BinaryWriter writer, uint[] ids)
    {
        writer.Write(ids.Length);
        foreach (var id in ids)
        {
            writer.Write(id);
        }
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }
}
