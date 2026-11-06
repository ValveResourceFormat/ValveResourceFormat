using System.Buffers.Binary;
using System.IO;
using System.IO.Hashing;
using System.Text;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.Source1.Bsp;

namespace ValveResourceFormat.Source1.Choreo;

/// <summary>
/// A Source 1 <c>scenes.image</c>: the compiled scenes of a game in one file. Scenes are keyed by a checksum of their
/// name, so the image holds no names.
/// </summary>
public sealed class SceneImage
{
    private const int Magic = 0x46495356;
    private const int HeaderSize = 20;
    private const int EntrySize = 16;
    private const int MinVersion = 2;
    private const int MaxVersion = 3;

    /// <summary>The path of the image in a game.</summary>
    public const string Path = "scenes/scenes.image";

    /// <summary>A compiled scene of the image.</summary>
    /// <param name="Checksum">The checksum of the scene name, see <see cref="GetChecksum"/>.</param>
    /// <param name="Offset">The offset of the compiled scene in the image.</param>
    /// <param name="Length">The size of the compiled scene.</param>
    /// <param name="Duration">The length of the scene in seconds.</param>
    /// <param name="Sounds">The sounds the scene speaks.</param>
    public readonly record struct Entry(uint Checksum, int Offset, int Length, float Duration, string[] Sounds);

    private readonly byte[] data;
    private readonly int stringCount;

    /// <summary>Gets the scenes of the image, sorted by checksum.</summary>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>Reads an image.</summary>
    /// <param name="stream">The stream of the image, which is read to its end and left open.</param>
    public SceneImage(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        data = copy.ToArray();

        if (data.Length < HeaderSize || ReadInt32(0) != Magic)
        {
            throw new InvalidDataException("The file is not a scene image.");
        }

        var version = ReadInt32(4);
        if (version is < MinVersion or > MaxVersion)
        {
            throw new InvalidDataException($"Unsupported scene image version {version}.");
        }

        var sceneCount = ReadInt32(8);
        stringCount = ReadInt32(12);
        var entryOffset = ReadInt32(16);

        var entries = new Entry[sceneCount];
        for (var i = 0; i < sceneCount; i++)
        {
            var offset = entryOffset + (i * EntrySize);
            var checksum = (uint)ReadInt32(offset);
            var dataOffset = ReadInt32(offset + 4);
            var dataLength = ReadInt32(offset + 8);
            var summaryOffset = ReadInt32(offset + 12);

            entries[i] = ReadSummary(checksum, dataOffset, dataLength, summaryOffset, version);
        }

        Entries = entries;
    }

    /// <summary>Gets the checksum a scene is keyed by.</summary>
    /// <param name="name">The name of the scene, such as <c>scenes/npc/alyx/hi.vcd</c>.</param>
    public static uint GetChecksum(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var normalized = name.ToLowerInvariant().Replace('/', '\\');
        var sceneFolder = normalized.IndexOf("scenes\\", StringComparison.Ordinal);
        if (sceneFolder > 0)
        {
            normalized = normalized[sceneFolder..];
        }

        return Crc32.HashToUInt32(Encoding.UTF8.GetBytes(normalized));
    }

    /// <summary>Finds a scene by its name.</summary>
    /// <param name="name">The name of the scene, such as <c>scenes/npc/alyx/hi.vcd</c>.</param>
    /// <param name="entry">The scene.</param>
    /// <returns>Whether the image has the scene.</returns>
    public bool TryFind(string name, out Entry entry)
    {
        var checksum = GetChecksum(name);

        foreach (var candidate in Entries)
        {
            if (candidate.Checksum == checksum)
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    /// <summary>Reads a compiled scene.</summary>
    /// <param name="entry">The scene of <see cref="Entries"/>.</param>
    /// <param name="name">The name to give the scene.</param>
    public ChoreoScene ReadScene(Entry entry, string? name = null)
    {
        var compiled = data.AsSpan(entry.Offset, entry.Length);
        if (BspFile.IsLzmaCompressed(compiled))
        {
            var decompressedSize = BinaryPrimitives.ReadInt32LittleEndian(compiled[4..]);
            compiled = BspFile.DecompressLzma(compiled, decompressedSize);
        }

        var scene = Source1ChoreoReader.Read(compiled, GetString);
        scene.Name = name;
        scene.Duration = (int)MathF.Round(entry.Duration * 1000f);

        return scene;
    }

    private Entry ReadSummary(uint checksum, int offset, int length, int summaryOffset, int version)
    {
        var duration = ReadInt32(summaryOffset);

        // Version 3 adds the time of the last speak event
        var soundCountOffset = summaryOffset + (version >= 3 ? 8 : 4);
        var soundCount = ReadInt32(soundCountOffset);

        var sounds = new string[soundCount];
        for (var i = 0; i < soundCount; i++)
        {
            sounds[i] = GetString((short)ReadInt32(soundCountOffset + 4 + (i * 4)));
        }

        return new Entry(checksum, offset, length, duration / 1000f, sounds);
    }

    private string GetString(short index)
    {
        if (index < 0 || index >= stringCount)
        {
            throw new InvalidDataException($"The scene refers to string {index} of {stringCount}.");
        }

        var start = ReadInt32(HeaderSize + (index * 4));
        var end = Array.IndexOf(data, (byte)0, start);

        return Encoding.UTF8.GetString(data, start, end - start);
    }

    private int ReadInt32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
}
