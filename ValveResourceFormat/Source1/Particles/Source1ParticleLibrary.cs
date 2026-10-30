using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Material;

namespace ValveResourceFormat.Source1.Particles;

/// <summary>
/// The particle systems of a Source 1 game: finds the <c>.pcf</c> file that defines a system by name,
/// through the manifests of the game and of the map, and converts the system to a Source 2 definition.
/// </summary>
internal sealed class Source1ParticleLibrary
{
    private readonly Source1FileSystem fileSystem;
    private readonly ILogger logger;
    private readonly Dictionary<string, DmxElement> systems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ParticleSystem?> converted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PcfMaterialInfo?> materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loadedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> reportedFunctions = [];
    private readonly Lock gate = new();
    private string? mapName;
    private bool loaded;

    public Source1ParticleLibrary(Source1FileSystem fileSystem, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        this.fileSystem = fileSystem;
        this.logger = logger;
    }

    /// <summary>Sets the map whose manifest adds systems. Must be called before the first lookup.</summary>
    public void SetMap(string name)
    {
        lock (gate)
        {
            mapName = name;
        }
    }

    /// <summary>
    /// Gets the Source 2 definition of a system.
    /// </summary>
    /// <returns>The definition, or <see langword="null"/> when no loaded file defines the system.</returns>
    public ParticleSystem? GetParticleSystem(string name)
    {
        lock (gate)
        {
            EnsureLoaded();

            if (converted.TryGetValue(name, out var existing))
            {
                return existing;
            }

            // Reserved first, so a system that lists itself as a child does not convert forever
            converted[name] = null;

            if (!systems.TryGetValue(name, out var element))
            {
                return null;
            }

            PcfParticleSystem system;

            try
            {
                system = PcfParticleSystem.FromElement(element);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or FormatException)
            {
                logger.LogWarning("Failed to read particle system {System}: {Message}", name, e.Message);
                return null;
            }

            var particleSystem = Source1ParticleConverter.Convert(system, ReadMaterial, logger, reportedFunctions);
            particleSystem.Name = system.Name;
            particleSystem.ChildResolver = GetParticleSystem;
            converted[name] = particleSystem;

            return particleSystem;
        }
    }

    private PcfMaterialInfo? ReadMaterial(string name)
    {
        if (materials.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var material = LoadMaterial(name);
        materials.Add(name, material);

        return material;
    }

    private PcfMaterialInfo? LoadMaterial(string name)
    {
        VmtFile vmt;

        try
        {
            vmt = VmtFile.Load(Source1Paths.ToMaterialPath(name), fileSystem.OpenFile);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            logger.LogWarning("Failed to read particle material {Material}: {Message}", name, e.Message);
            return null;
        }

        var values = vmt.Values;
        var baseTexture = values.String("$basetexture");
        var texture = string.IsNullOrWhiteSpace(baseTexture) ? null : VmtFile.NormalizePath(baseTexture, ".vtf");

        return new PcfMaterialInfo(
            texture,
            values.Boolean("$additive"),
            values.Boolean("$addoverblend"),
            values.Float("$overbrightfactor", 1f),
            values.Float("$addself"),
            values.Boolean("$blendframes", true));
    }

    private void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;

        var manifests = new List<string> { PcfManifest.GamePath, PcfManifest.PakPath };

        if (!string.IsNullOrEmpty(mapName))
        {
            manifests.Add(PcfManifest.GetMapPath(mapName));
        }

        foreach (var manifest in manifests)
        {
            using var stream = fileSystem.OpenFile(manifest);

            if (stream == null)
            {
                continue;
            }

            foreach (var path in PcfManifest.Read(stream))
            {
                LoadFile(path);
            }
        }
    }

    private void LoadFile(string path)
    {
        if (!loadedFiles.Add(path))
        {
            return;
        }

        using var stream = fileSystem.OpenFile(path);

        if (stream == null)
        {
            logger.LogDebug("Particle file {File} listed by a manifest was not found", path);
            return;
        }

        try
        {
            var file = PcfFile.Read(stream);

            foreach (var (name, element) in file.Systems)
            {
                // The engine reads the files in order and a later definition replaces an earlier one
                systems[name] = element;
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or FormatException)
        {
            logger.LogWarning("Failed to read particle file {File}: {Message}", path, e.Message);
        }
    }
}
