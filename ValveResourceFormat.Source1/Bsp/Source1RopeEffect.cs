using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles.Upgrade;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// The particle system that draws a rope as a cable through the nodes of a path. The path holds the settled rope, so the
/// particles take their place from it and nothing moves them.
/// </summary>
internal static class Source1RopeEffect
{
    private const string PlaceholderSnapshot = "particles/entity/path_particle_rope_placeholder.vsnap";

    // The particles are created in the first step, and the system holds where they are after that
    private const float StopSimulationTime = 0.1f;

    // The attributes of the snapshot that the particles take, in the order of the particle attributes
    private const int PositionAttribute = 0;
    private const int ColorAttribute = 6;
    private const int RadiusAttribute = 3;

    /// <summary>Creates the effect that draws a rope with a material.</summary>
    /// <param name="path">The path of the effect.</param>
    /// <param name="materialPath">The path of the material of the cable.</param>
    /// <param name="textureRepeats">How often the texture of the material repeats along the whole rope.</param>
    public static Resource Create(string path, string materialPath, float textureRepeats)
    {
        var data = KVObject.Collection();

        data.Add("_class", "CParticleSystemDefinition");
        data.Add("m_ConstantNormal", KVHelpers.ToKVArray(Vector3.One));
        data.Add("m_flConstantRadius", 1f);
        data.Add("m_nBehaviorVersion", 12);
        data.Add("m_flMaximumTimeStep", 0.01f);
        data.Add("m_hSnapshot", Source1ResourceReferences.Reference(PlaceholderSnapshot));
        data.Add("m_flStopSimulationAfterTime", StopSimulationTime);
        data.Add("m_flMaximumSimTime", 0.01f);
        data.Add("m_flMinimumSimTime", 0.01f);
        data.Add("m_flMinimumTimeStep", 0.01f);
        data.Add("m_nMinimumFrames", 10);

        var emitter = KVObject.Collection();
        emitter.Add("_class", "C_OP_InstantaneousEmitter");
        emitter.Add("m_nSnapshotControlPoint", 0);
        emitter.Add("m_nParticlesToEmit", Literal(1f));
        data.Add("m_Emitters", CreateArray(emitter));

        data.Add("m_Initializers", CreateArray(
            ReadSnapshot(PositionAttribute, PositionAttribute),
            ReadSnapshot(ColorAttribute, ColorAttribute),
            ReadSnapshot(RadiusAttribute, RadiusAttribute)));

        var lighting = KVObject.Collection();
        lighting.Add("m_nType", "PT_TYPE_INVALID");

        var cable = KVObject.Collection();
        cable.Add("_class", "C_OP_RenderCables");
        cable.Add("m_flTextureRepeatsPerSegment", Literal(textureRepeats));
        cable.Add("m_hMaterial", Source1ResourceReferences.Reference(materialPath));
        cable.Add("m_nMaxTesselation", 4);
        cable.Add("m_nRoundness", 1);
        cable.Add("m_nTextureRepetitionMode", "TEXTURE_REPETITION_PATH");
        cable.Add("m_flTessScale", 0f);
        cable.Add("m_nMinTesselation", 0);
        cable.Add("m_LightingTransform", lighting);
        data.Add("m_Renderers", CreateArray(cable));

        var resource = new Resource(ResourceType.Particle) { FileName = path };
        resource.AddBlock(ParticleSystem.Create(data, ParticleFormatUpgrader.LatestFormat));
        Source1ResourceReferences.Update(resource);

        return resource;
    }

    private static KVObject ReadSnapshot(int attributeToRead, int attributeToWrite)
    {
        var initializer = KVObject.Collection();
        initializer.Add("_class", "C_INIT_InitFromCPSnapshot");
        initializer.Add("m_nAttributeToRead", attributeToRead);
        initializer.Add("m_nAttributeToWrite", attributeToWrite);

        return initializer;
    }

    private static KVObject Literal(float value)
    {
        var input = KVObject.Collection();
        input.Add("m_nType", "PF_TYPE_LITERAL");
        input.Add("m_flLiteralValue", value);

        return input;
    }

    private static KVObject CreateArray(params KVObject[] items)
    {
        var array = KVObject.Array();

        foreach (var item in items)
        {
            array.Add(item);
        }

        return array;
    }
}
