using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1.Model;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Adds the static props of a map to a world node as scene objects, with their baked per vertex lighting as extra vertex streams.
/// </summary>
internal sealed class Source1StaticPropConverter(
    BspFile bsp,
    Func<string, Source1Model?> loadModel,
    Func<string, Stream?> openFile,
    KVObject sceneObjects)
{
    private readonly record struct DrawCallStream(int MeshIndex, int DrawCallIndex, Vector4[] Colors);

    /// <summary>Gets the extra vertex streams, one for each draw call of a prop that has baked lighting.</summary>
    public KVObject ExtraVertexStreams { get; } = KVObject.Array();

    /// <summary>Gets what binds each stream to the draw calls of the scene object it belongs to.</summary>
    public KVObject ExtraVertexStreamOverrides { get; } = KVObject.Array();

    /// <summary>Adds a prop as the next scene object of the world node.</summary>
    /// <param name="propIndex">The index of the prop in the map, which names its lighting file.</param>
    public void Add(int propIndex)
    {
        var prop = bsp.StaticProps.Props[propIndex];
        var source = loadModel(prop.ModelName);
        var sceneObjectIndex = sceneObjects.Count;
        sceneObjects.Add(CreateSceneObject(prop, source));

        if (source == null || (prop.Flags & StaticPropFlags.NoPerVertexLighting) != 0)
        {
            return;
        }

        var streams = LoadDrawCallStreams(propIndex, source);

        foreach (var stream in streams)
        {
            AddStream(sceneObjectIndex, stream);
        }
    }

    /// <summary>Adds a detail model as the next scene object of the world node, without baked lighting.</summary>
    /// <param name="modelName">The path of the model.</param>
    /// <param name="transform">The transform of the model.</param>
    public void AddDetailModel(string modelName, Matrix4x4 transform)
    {
        if (modelName.Length == 0)
        {
            return;
        }

        var modelPath = loadModel(modelName) == null ? Source1Paths.ErrorModelPath : Source1Paths.ToModelPath(Source1Paths.Normalize(modelName));

        var sceneObject = CreateModelSceneObject(modelPath, Source1ModelConverter.GetSkinName(0), transform, 0L, Vector4.One);
        sceneObject.Add("m_vLightingOrigin", KVHelpers.ToKVArray(transform.Translation));
        sceneObjects.Add(sceneObject);
    }

    private static KVObject CreateModelSceneObject(string modelPath, string skin, Matrix4x4 transform, long flags, Vector4 tint)
    {
        var sceneObject = KVObject.Collection();
        sceneObject.Add("m_renderableModel", Source1ResourceReferences.Reference(modelPath));
        sceneObject.Add("m_skin", skin);
        sceneObject.Add("m_vTransform", Source1WorldConverter.ToColumns(transform));
        sceneObject.Add("m_nObjectTypeFlags", flags);
        sceneObject.Add("m_vTintColor", KVHelpers.ToKVArray(tint));

        return sceneObject;
    }

    private static KVObject CreateSceneObject(BspStaticProp prop, Source1Model? source)
    {
        var scale = prop.Scale;

        if (scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
        {
            scale = Vector3.One;
        }

        var transform = Matrix4x4.CreateScale(scale) * EntityTransformHelper.ToRigidTransformationMatrix(prop.Angles, prop.Origin);
        var color = new Color32(prop.ColorModulation);
        var modelPath = Source1Paths.ErrorModelPath;
        var skin = Source1ModelConverter.GetSkinName(0);

        if (source != null)
        {
            modelPath = Source1Paths.ToModelPath(Source1Paths.Normalize(prop.ModelName));
            skin = Source1ModelConverter.GetSkinName(source, prop.Skin);
        }

        var flags = 0L;

        // The no shadow flag only keeps the prop out of the lightmaps, the extended flag keeps it out of the shadow maps
        if ((prop.ExtendedFlags & StaticPropExtendedFlags.DisableShadowDepth) != 0)
        {
            flags |= (long)ObjectTypeFlags.NoShadows;
        }

        var tint = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1f);
        var sceneObject = CreateModelSceneObject(modelPath, skin, transform, flags, tint);

        if ((prop.Flags & StaticPropFlags.Fades) != 0)
        {
            var (fadeStart, fadeEnd) = GetFadeDistances(prop.FadeMinDist, prop.FadeMaxDist);

            if (fadeEnd > 0f)
            {
                sceneObject.Add("m_flFadeStartDistance", fadeStart);
                sceneObject.Add("m_flFadeEndDistance", fadeEnd);
            }
        }

        var useLightingOrigin = (prop.Flags & StaticPropFlags.UseLightingOrigin) != 0;
        sceneObject.Add("m_vLightingOrigin", KVHelpers.ToKVArray(useLightingOrigin ? prop.LightingOrigin : prop.Origin));

        return sceneObject;
    }

    // A negative start is an offset from the end
    private static (float Start, float End) GetFadeDistances(float start, float end)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (start < 0f)
        {
            start = Math.Max(end + start, 0f);
        }

        return (start, end);
    }

    private List<DrawCallStream> LoadDrawCallStreams(int propIndex, Source1Model source)
    {
        var lighting = LoadVertexLighting(propIndex);

        if (lighting == null || lighting.Checksum != source.Checksum)
        {
            return [];
        }

        // Where the baked light leaves out the sun, the sun amount is kept so the sun can be added with its shadows
        var withSunAmount = (bsp.MapFlags & BspMapFlags.StaticPropLightingWithoutSun) != 0;

        // The converted model has one mesh for each level of detail that has geometry of each sub-model, in this order,
        // with a draw call and a vertex buffer for each mesh of the level of detail
        var streams = new List<DrawCallStream>();
        var meshIndex = 0;

        foreach (var part in source.BodyParts)
        {
            foreach (var subModel in part.Models)
            {
                foreach (var lod in subModel.Lods)
                {
                    if (lod.Length == 0)
                    {
                        continue;
                    }

                    for (var drawCallIndex = 0; drawCallIndex < lod.Length; drawCallIndex++)
                    {
                        var colors = lighting.GetVertexLighting(lod[drawCallIndex], withSunAmount);

                        if (colors == null)
                        {
                            return [];
                        }

                        streams.Add(new DrawCallStream(meshIndex, drawCallIndex, colors));
                    }

                    meshIndex++;
                }
            }
        }

        return streams;
    }

    private VhvFile? LoadVertexLighting(int propIndex)
    {
        var hdrName = string.Create(CultureInfo.InvariantCulture, $"sp_hdr_{propIndex}.vhv");
        var ldrName = string.Create(CultureInfo.InvariantCulture, $"sp_{propIndex}.vhv");
        using var stream = (bsp.HasHdrLighting ? openFile(hdrName) : null) ?? openFile(ldrName);

        if (stream == null)
        {
            return null;
        }

        var colorsPerVertex = (bsp.MapFlags & BspMapFlags.StaticPropLightingThreeColors) != 0 ? 3 : 1;

        return VhvFile.Parse(stream, colorsPerVertex);
    }

    private void AddStream(int sceneObjectIndex, DrawCallStream stream)
    {
        var streamIndex = ExtraVertexStreams.Count;
        ExtraVertexStreams.Add(CreateColorStream(stream.Colors));

        var binding = KVObject.Collection();
        binding.Add("m_hBuffer", streamIndex);

        var streamOverride = KVObject.Collection();
        streamOverride.Add("m_nSceneObjectIndex", (long)sceneObjectIndex);
        streamOverride.Add("m_nSubSceneObject", (long)stream.MeshIndex);
        streamOverride.Add("m_nDrawCallIndex", (long)stream.DrawCallIndex);
        streamOverride.Add("m_extraBufferBinding", binding);
        streamOverride.Add("m_nAdditionalMeshDrawPrimitiveFlags", (long)RenderMeshDrawPrimitiveFlags.HasBakedLightingFromVertexStream);
        ExtraVertexStreamOverrides.Add(streamOverride);
    }

    // Read as COLOR1, which the Source 1 model shaders take the baked light from, and the sun amount in the alpha
    private static KVObject CreateColorStream(Vector4[] colors)
    {
        var field = KVObject.Collection();
        field.Add("m_pSemanticName", "COLOR");
        field.Add("m_nSemanticIndex", 1);
        field.Add("m_Format", (long)DXGI_FORMAT.R32G32B32A32_FLOAT);
        field.Add("m_nOffset", 0);
        field.Add("m_nSlot", 0);
        field.Add("m_nSlotType", 0L);
        field.Add("m_nInstanceStepRate", 0);
        field.Add("m_szShaderSemantic", string.Empty);

        var fields = KVObject.Array();
        fields.Add(field);

        var stream = KVObject.Collection();
        stream.Add("m_nElementCount", (long)colors.Length);
        stream.Add("m_nElementSizeInBytes", (long)Marshal.SizeOf<Vector4>());
        stream.Add("m_inputLayoutFields", fields);
        stream.Add("m_pData", KVObject.Blob(MemoryMarshal.AsBytes(colors.AsSpan()).ToArray()));

        return stream;
    }
}
