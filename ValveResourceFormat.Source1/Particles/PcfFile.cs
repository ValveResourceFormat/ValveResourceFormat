using System.IO;
using Datamodel;
using Datamodel.Codecs;

namespace ValveResourceFormat.Source1.Particles;

/// <summary>
/// One function of a particle system, such as an operator or an initializer. Its settings are the
/// attributes of <see cref="Element"/>, named as the function declares them.
/// </summary>
/// <param name="FunctionName">The display name of the function, such as <c>Movement Basic</c>.</param>
/// <param name="Element">The element that holds the settings of the function.</param>
internal sealed record PcfFunction(string FunctionName, Element Element);

/// <summary>A child system a particle system starts together with itself, or when it ends.</summary>
/// <param name="Name">The name of the child system.</param>
/// <param name="Delay">Seconds the child waits before it starts.</param>
/// <param name="EndCap">Whether the child plays when the parent stops, instead of with it.</param>
internal sealed record PcfChild(string Name, float Delay, bool EndCap);

/// <summary>A <c>DmeParticleSystemDefinition</c>: the settings of a system and the lists of functions that run it.</summary>
internal sealed class PcfParticleSystem
{
    public required string Name { get; init; }

    /// <summary>The element with the system-wide settings, such as <c>max_particles</c> and <c>material</c>.</summary>
    public required Element Element { get; init; }

    public List<PcfFunction> Renderers { get; } = [];

    public List<PcfFunction> Operators { get; } = [];

    public List<PcfFunction> Initializers { get; } = [];

    public List<PcfFunction> Emitters { get; } = [];

    public List<PcfFunction> Forces { get; } = [];

    public List<PcfChild> Children { get; } = [];

    public List<PcfFunction> Constraints { get; } = [];

    public static PcfParticleSystem FromElement(Element element)
    {
        var system = new PcfParticleSystem { Name = element.Name, Element = element };

        ReadFunctions(element, "renderers", system.Renderers);
        ReadFunctions(element, "operators", system.Operators);
        ReadFunctions(element, "initializers", system.Initializers);
        ReadFunctions(element, "emitters", system.Emitters);
        ReadFunctions(element, "forces", system.Forces);
        ReadFunctions(element, "constraints", system.Constraints);

        foreach (var childElement in element.GetElements("children"))
        {
            if (!childElement.TryGetAttribute("child", out var value) || value is not Element { Stub: false } child)
            {
                continue;
            }

            system.Children.Add(new PcfChild(child.Name, childElement.GetFloat("delay"), childElement.GetBool("end cap effect")));
        }

        return system;
    }

    private static void ReadFunctions(Element element, string key, List<PcfFunction> functions)
    {
        foreach (var functionElement in element.GetElements(key))
        {
            var functionName = functionElement.GetString("functionName", functionElement.Name);
            functions.Add(new PcfFunction(functionName, functionElement));
        }
    }
}

/// <summary>The particle systems of a <c>.pcf</c> file.</summary>
internal sealed class PcfFile
{
    private const string SystemType = "DmeParticleSystemDefinition";

    /// <summary>The elements of the systems of the file by name, for <see cref="PcfParticleSystem.FromElement"/>. A name used twice keeps its first system.</summary>
    public Dictionary<string, Element> Systems { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a <c>.pcf</c> file.</summary>
    /// <exception cref="InvalidDataException">The stream is not a DMX file.</exception>
    /// <exception cref="UnexpectedMagicException">The stream is not a DMX particle file or has an unsupported version.</exception>
    public static PcfFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var datamodel = Load(stream);

        if (!datamodel.Format.Equals("pcf", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnexpectedMagicException("DMX file is not a particle file", datamodel.Format, nameof(datamodel.Format));
        }

        var root = datamodel.Root ?? throw new InvalidDataException("DMX file has no elements.");
        var file = new PcfFile();

        foreach (var element in root.GetElements("particleSystemDefinitions"))
        {
            if (element.ClassName != SystemType)
            {
                continue;
            }

            file.Systems.TryAdd(element.Name, element);
        }

        return file;
    }

    private static Datamodel.Datamodel Load(Stream stream)
    {
        var data = Source1Streams.ReadAll(stream);

        try
        {
            return Datamodel.Datamodel.Load(data, DeferredMode.Disabled);
        }
        catch (Exception e) when (e is CodecException or InvalidOperationException)
        {
            throw new InvalidDataException(e.Message, e);
        }
    }
}
