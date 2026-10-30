using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

internal sealed partial class Source1Model
{
    private const int MaxIncludeDepth = 8;
    private const int MaxBlendCount = 1024;

    private Func<string, Stream?>? animationOpenFile;
    private MdlFile? baseMdl;
    private byte[]? baseMdlData;
    private string baseModelPath = string.Empty;
    private Source1Sequence[]? sequences;
    private Source1Animation[]? animations;

    /// <summary>
    /// Sequences of the model followed by the sequences of its included models.
    /// Loaded on first use with the file opener the model was loaded with.
    /// </summary>
    public Source1Sequence[] Sequences
    {
        get
        {
            EnsureAnimations();
            return sequences!;
        }
    }

    /// <summary>
    /// Animations of the model followed by the animations of its included models.
    /// Loaded on first use with the file opener the model was loaded with.
    /// </summary>
    public Source1Animation[] Animations
    {
        get
        {
            EnsureAnimations();
            return animations!;
        }
    }

    /// <summary>Pose parameters of the model, which drive the blend axes of sequences.</summary>
    public Source1PoseParameter[] PoseParameters
    {
        get
        {
            EnsureAnimations();
            return poseParameters;
        }
    }

    private Source1PoseParameter[] poseParameters = [];
    private List<Source1PoseParameter> mergedPoseParameters = [];

    /// <summary>
    /// Loads the sequences and animations of the model, its included models and its external .ani file.
    /// Bones of included models are remapped to the bones of this model by name.
    /// </summary>
    /// <param name="openFile">Opens a file by path, returning null when it does not exist.</param>
    public void LoadAnimations(Func<string, Stream?> openFile)
    {
        ArgumentNullException.ThrowIfNull(openFile);

        if (baseMdl == null || baseMdlData == null)
        {
            throw new InvalidOperationException("The model was not loaded from an MDL file.");
        }

        var loadedSequences = new List<Source1Sequence>();
        var loadedAnimations = new List<Source1Animation>();
        var sequenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizePath(baseModelPath) };

        mergedPoseParameters = [.. ReadPoseParameters(baseMdl, baseMdlData)];

        var boneMap = new int[baseMdl.Bones.Length];

        for (var i = 0; i < boneMap.Length; i++)
        {
            boneMap[i] = i;
        }

        AddGroup(baseMdl, baseMdlData, baseModelPath, boneMap, openFile, loadedSequences, loadedAnimations, sequenceNames, visited, 0);

        var byName = new Dictionary<string, Source1Sequence>(StringComparer.OrdinalIgnoreCase);

        foreach (var sequence in loadedSequences)
        {
            byName.TryAdd(sequence.Name, sequence);
        }

        foreach (var sequence in loadedSequences)
        {
            foreach (var layer in sequence.AutoLayers)
            {
                layer.Sequence = byName.GetValueOrDefault(layer.SequenceName);
            }
        }

        poseParameters = [.. mergedPoseParameters];
        sequences = [.. loadedSequences];
        animations = [.. loadedAnimations];
    }

    /// <summary>Finds a sequence by name, ignoring case.</summary>
    /// <returns>The sequence, or null when the model has none with that name.</returns>
    public Source1Sequence? FindSequence(string name)
    {
        foreach (var sequence in Sequences)
        {
            if (sequence.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return sequence;
            }
        }

        return null;
    }

    /// <summary>Finds the index of a pose parameter by name, ignoring case.</summary>
    /// <returns>Index into <see cref="PoseParameters"/>, or -1 when there is none with that name.</returns>
    public int FindPoseParameter(string name)
    {
        for (var i = 0; i < PoseParameters.Length; i++)
        {
            if (PoseParameters[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Creates pose parameter values for the animation evaluation functions, one per entry of <see cref="PoseParameters"/>.
    /// Every value is zero, moved into the range of its parameter.
    /// </summary>
    public float[] CreateDefaultPoseParameters()
    {
        _ = Sequences;

        var values = new float[PoseParameters.Length];

        for (var i = 0; i < values.Length; i++)
        {
            var parameter = PoseParameters[i];
            var low = MathF.Min(parameter.Start, parameter.End);
            var high = MathF.Max(parameter.Start, parameter.End);
            values[i] = Math.Clamp(0f, low, high);
        }

        return values;
    }

    /// <summary>Fills local transforms with the reference pose of the bones.</summary>
    /// <param name="positions">Receives local positions, at least <see cref="Bones"/> long.</param>
    /// <param name="rotations">Receives local rotations, at least <see cref="Bones"/> long.</param>
    public void GetReferencePose(Span<Vector3> positions, Span<Quaternion> rotations)
    {
        if (positions.Length < Bones.Length || rotations.Length < Bones.Length)
        {
            throw new ArgumentException("Output spans are shorter than the bone count.");
        }

        for (var i = 0; i < Bones.Length; i++)
        {
            positions[i] = Bones[i].Position;
            rotations[i] = Bones[i].Rotation;
        }
    }

    /// <summary>
    /// Sequences that play on every pose, in model order. The engine adds them under the active sequence,
    /// driven by real time.
    /// </summary>
    public IEnumerable<Source1Sequence> AutoplaySequences
    {
        get
        {
            foreach (var sequence in Sequences)
            {
                if ((sequence.Flags & Source1AnimationFlags.AutoPlay) != 0)
                {
                    yield return sequence;
                }
            }
        }
    }

    private void EnsureAnimations()
    {
        if (sequences != null)
        {
            return;
        }

        if (animationOpenFile == null)
        {
            throw new InvalidOperationException("The model was not loaded from an MDL file.");
        }

        LoadAnimations(animationOpenFile);
    }

    private void SetAnimationSource(MdlFile mdl, byte[] data, string mdlBasePath, Func<string, Stream?> openFile)
    {
        baseMdl = mdl;
        baseMdlData = data;
        baseModelPath = mdlBasePath;
        animationOpenFile = openFile;
    }

    private void AddGroup(
        MdlFile mdl,
        byte[] data,
        string basePath,
        int[] boneMap,
        Func<string, Stream?> openFile,
        List<Source1Sequence> loadedSequences,
        List<Source1Animation> loadedAnimations,
        HashSet<string> sequenceNames,
        HashSet<string> visited,
        int depth)
    {
        var source = CreateAnimationSource(mdl, data, basePath, boneMap, openFile);
        var groupAnimations = ReadAnimations(mdl, data, source);
        var poseParameters = ReadPoseParameters(mdl, data);

        foreach (var parameter in poseParameters)
        {
            if (FindMergedPoseParameter(parameter.Name) < 0)
            {
                mergedPoseParameters.Add(parameter);
            }
        }

        loadedAnimations.AddRange(groupAnimations);

        var sequenceSize = Marshal.SizeOf<MdlFile.SeqDesc>();
        var descs = LumpReader<MdlFile.SeqDesc>.ReadLumpAt(data, mdl.Hdr.LocalSeqIndex, mdl.Hdr.NumLocalSeq);
        var localNames = new string[descs.Length];

        for (var i = 0; i < descs.Length; i++)
        {
            localNames[i] = MdlFile.ReadCString(data, mdl.Hdr.LocalSeqIndex + (i * sequenceSize) + descs[i].LabelIndex);
        }

        for (var i = 0; i < descs.Length; i++)
        {
            var offset = mdl.Hdr.LocalSeqIndex + (i * sequenceSize);

            if ((descs[i].Flags & (int)Source1AnimationFlags.Override) != 0)
            {
                continue;
            }

            var sequence = ReadSequence(mdl, data, source, groupAnimations, poseParameters, localNames, descs[i], offset, i);

            if (sequence != null && sequenceNames.Add(sequence.Name))
            {
                loadedSequences.Add(sequence);
            }
        }

        if (depth >= MaxIncludeDepth)
        {
            return;
        }

        var groupSize = Marshal.SizeOf<MdlFile.ModelGroup>();
        var groups = LumpReader<MdlFile.ModelGroup>.ReadLumpAt(data, mdl.Hdr.IncludeModelOffset, mdl.Hdr.NumIncludeModels);

        for (var i = 0; i < groups.Length; i++)
        {
            var groupOffset = mdl.Hdr.IncludeModelOffset + (i * groupSize);
            var includePath = NormalizePath(MdlFile.ReadCString(data, groupOffset + groups[i].NameIndex));

            if (includePath.Length == 0 || !visited.Add(includePath))
            {
                continue;
            }

            try
            {
                using var stream = openFile(includePath);

                if (stream == null)
                {
                    Warnings.Add($"Included model '{includePath}' was not found.");
                    continue;
                }

                var includeData = ReadAll(stream);
                var includeMdl = new MdlFile(includeData);
                var includeMap = BuildBoneMap(includeMdl);
                var includeBase = includePath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ? includePath[..^4] : includePath;

                AddGroup(includeMdl, includeData, includeBase, includeMap, openFile, loadedSequences, loadedAnimations, sequenceNames, visited, depth + 1);
            }
            catch (InvalidDataException e)
            {
                Warnings.Add($"Included model '{includePath}' failed to load: {e.Message}");
            }
        }
    }

    private int[] BuildBoneMap(MdlFile mdl)
    {
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < Bones.Length; i++)
        {
            byName.TryAdd(Bones[i].Name, i);
        }

        var map = new int[mdl.Bones.Length];

        for (var i = 0; i < map.Length; i++)
        {
            map[i] = byName.TryGetValue(mdl.BoneNames[i], out var index) ? index : -1;
        }

        return map;
    }

    private Source1AnimationSource CreateAnimationSource(MdlFile mdl, byte[] data, string basePath, int[] boneMap, Func<string, Stream?> openFile)
    {
        byte[]? aniData = null;
        var blocks = LumpReader<MdlFile.AnimBlock>.ReadLumpAt(data, mdl.Hdr.AnimBlockOffset, mdl.Hdr.NumAnimBlocks);

        if (blocks.Length > 1)
        {
            var blockName = mdl.Hdr.AnimBlockNameOffset > 0 ? NormalizePath(MdlFile.ReadCString(data, mdl.Hdr.AnimBlockNameOffset)) : string.Empty;

            foreach (var candidate in new[] { blockName, basePath + ".ani" })
            {
                if (candidate.Length == 0)
                {
                    continue;
                }

                using var stream = openFile(candidate);

                if (stream != null)
                {
                    aniData = ReadAll(stream);
                    break;
                }
            }

            if (aniData == null)
            {
                Warnings.Add($"Animation block file '{blockName}' was not found.");
            }
        }

        var positions = new Vector3[Bones.Length];
        var rotations = new Quaternion[Bones.Length];

        for (var i = 0; i < Bones.Length; i++)
        {
            positions[i] = Bones[i].Position;
            rotations[i] = Bones[i].Rotation;
        }

        return new Source1AnimationSource
        {
            Mdl = mdl,
            MdlData = data,
            AniData = aniData,
            Blocks = blocks,
            BoneToBase = boneMap,
            BaseBoneCount = Bones.Length,
            BasePositions = positions,
            BaseRotations = rotations,
        };
    }

    private static Source1Animation[] ReadAnimations(MdlFile mdl, byte[] data, Source1AnimationSource source)
    {
        var size = Marshal.SizeOf<MdlFile.AnimDesc>();
        var descs = LumpReader<MdlFile.AnimDesc>.ReadLumpAt(data, mdl.Hdr.LocalAnimIndex, mdl.Hdr.NumLocalAnims);
        var result = new Source1Animation[descs.Length];

        for (var i = 0; i < descs.Length; i++)
        {
            var desc = descs[i];
            var offset = mdl.Hdr.LocalAnimIndex + (i * size);
            var name = MdlFile.ReadCString(data, offset + desc.NameIndex);

            Source1Movement[] movements = [];

            if (desc.NumMovements > 0)
            {
                var raw = LumpReader<MdlFile.AnimMovement>.ReadLumpAt(data, offset + desc.MovementIndex, desc.NumMovements);
                movements = new Source1Movement[raw.Length];

                for (var j = 0; j < raw.Length; j++)
                {
                    movements[j] = new Source1Movement(raw[j].EndFrame, raw[j].MotionFlags, raw[j].V0, raw[j].V1, raw[j].Angle, raw[j].Vector, raw[j].Position);
                }
            }

            result[i] = new Source1Animation(source, i, desc, offset, name, movements, IsAnimationDataAvailable(source, desc, offset));
        }

        return result;
    }

    private static bool IsAnimationDataAvailable(Source1AnimationSource source, MdlFile.AnimDesc desc, int descOffset)
    {
        if ((desc.Flags & (int)Source1AnimationFlags.AllZeros) != 0 || desc.NumFrames <= 0)
        {
            return true;
        }

        if (desc.SectionFrames == 0)
        {
            return IsBlockAvailable(source, desc.AnimBlock);
        }

        var sectionCount = (desc.NumFrames / desc.SectionFrames) + 2;

        for (var i = 0; i < sectionCount; i++)
        {
            var offset = (long)descOffset + desc.SectionIndex + (i * 8L);

            if (offset < 0 || offset + 8 > source.MdlData.Length)
            {
                return false;
            }

            var block = BinaryPrimitives.ReadInt32LittleEndian(source.MdlData.AsSpan((int)offset));

            if (!IsBlockAvailable(source, block))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBlockAvailable(Source1AnimationSource source, int block)
    {
        if (block == 0)
        {
            return true;
        }

        return block > 0 && source.AniData != null && block < source.Blocks.Length;
    }

    private Source1Sequence? ReadSequence(
        MdlFile mdl,
        byte[] data,
        Source1AnimationSource source,
        Source1Animation[] groupAnimations,
        Source1PoseParameter[] poseParameters,
        string[] localNames,
        MdlFile.SeqDesc desc,
        int offset,
        int index)
    {
        var countX = Math.Max(desc.GroupSize0, 1);
        var countY = Math.Max(desc.GroupSize1, 1);

        if (countX * countY > MaxBlendCount)
        {
            throw new InvalidDataException("Sequence blend grid is too large.");
        }

        var indices = LumpReader<short>.ReadLumpAt(data, offset + desc.AnimIndexIndex, countX * countY);
        var blends = new Source1Animation[indices.Length];

        for (var i = 0; i < blends.Length; i++)
        {
            if ((uint)indices[i] >= (uint)groupAnimations.Length)
            {
                Warnings.Add($"Sequence '{MdlFile.ReadCString(data, offset + desc.LabelIndex)}' references animation {indices[i]} of {groupAnimations.Length}.");
                return null;
            }

            blends[i] = groupAnimations[indices[i]];
        }

        var weights = new float[Bones.Length];

        if (desc.WeightListIndex > 0)
        {
            var raw = LumpReader<float>.ReadLumpAt(data, offset + desc.WeightListIndex, mdl.Bones.Length);

            for (var i = 0; i < raw.Length; i++)
            {
                var target = source.BoneToBase[i];

                if (target >= 0)
                {
                    weights[target] = raw[i];
                }
            }
        }
        else
        {
            Array.Fill(weights, 1f);
        }

        Source1AnimationEvent[] events = [];

        if (desc.NumEvents > 0)
        {
            var eventSize = Marshal.SizeOf<MdlFile.SeqEvent>();
            var raw = LumpReader<MdlFile.SeqEvent>.ReadLumpAt(data, offset + desc.EventIndex, desc.NumEvents);
            events = new Source1AnimationEvent[raw.Length];

            for (var i = 0; i < raw.Length; i++)
            {
                var eventOffset = offset + desc.EventIndex + (i * eventSize);
                var options = MdlFile.ReadCString(raw[i].Options, 0);
                var name = raw[i].NameIndex != 0 ? MdlFile.ReadCString(data, eventOffset + raw[i].NameIndex) : string.Empty;
                events[i] = new Source1AnimationEvent(raw[i].Cycle, raw[i].Event, raw[i].Type, options, name);
            }
        }

        var autoLayers = new Source1AutoLayer[desc.NumAutoLayers > 0 ? desc.NumAutoLayers : 0];

        if (autoLayers.Length > 0)
        {
            var raw = LumpReader<MdlFile.AutoLayer>.ReadLumpAt(data, offset + desc.AutoLayerIndex, autoLayers.Length);

            for (var i = 0; i < raw.Length; i++)
            {
                var targetName = (uint)raw[i].Sequence < (uint)localNames.Length ? localNames[raw[i].Sequence] : string.Empty;

                autoLayers[i] = new Source1AutoLayer
                {
                    SequenceName = targetName,
                    PoseParameterIndex = FindSharedPoseParameter(poseParameters, raw[i].Pose),
                    Flags = (Source1AutoLayerFlags)raw[i].Flags,
                    Start = raw[i].Start,
                    Peak = raw[i].Peak,
                    Tail = raw[i].Tail,
                    End = raw[i].End,
                };
            }
        }

        float[] poseKeys = [];

        if (desc.PoseKeyIndex != 0)
        {
            poseKeys = LumpReader<float>.ReadLumpAt(data, offset + desc.PoseKeyIndex, countX + countY);
        }

        return new Source1Sequence
        {
            Model = this,
            AutoLayers = autoLayers,
            PoseKeys = poseKeys,
            BlendIndexX = FindSharedPoseParameter(poseParameters, desc.ParamIndex0),
            BlendIndexY = FindSharedPoseParameter(poseParameters, desc.ParamIndex1),
            CyclePoseIndex = FindSharedPoseParameter(poseParameters, desc.CyclePoseIndex),
            Index = index,
            Name = MdlFile.ReadCString(data, offset + desc.LabelIndex),
            ActivityName = desc.ActivityNameIndex != 0 ? MdlFile.ReadCString(data, offset + desc.ActivityNameIndex) : string.Empty,
            Activity = desc.Activity,
            ActivityWeight = desc.ActivityWeight,
            Flags = (Source1AnimationFlags)desc.Flags,
            BlendCountX = countX,
            BlendCountY = countY,
            Blends = blends,
            BlendParameterX = GetPoseParameter(poseParameters, desc.ParamIndex0),
            BlendParameterY = GetPoseParameter(poseParameters, desc.ParamIndex1),
            BlendStartX = desc.ParamStart0,
            BlendEndX = desc.ParamEnd0,
            BlendStartY = desc.ParamStart1,
            BlendEndY = desc.ParamEnd1,
            BoneWeights = weights,
            Events = events,
            FadeInTime = desc.FadeInTime,
            FadeOutTime = desc.FadeOutTime,
            EntryPhase = desc.EntryPhase,
            ExitPhase = desc.ExitPhase,
            BoundingBoxMin = desc.BoundingBoxMin,
            BoundingBoxMax = desc.BoundingBoxMax,
        };
    }

    private int FindSharedPoseParameter(Source1PoseParameter[] groupParameters, int localIndex)
    {
        if ((uint)localIndex >= (uint)groupParameters.Length)
        {
            return -1;
        }

        return FindMergedPoseParameter(groupParameters[localIndex].Name);
    }

    private int FindMergedPoseParameter(string name)
    {
        for (var i = 0; i < mergedPoseParameters.Count; i++)
        {
            if (mergedPoseParameters[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static Source1PoseParameter? GetPoseParameter(Source1PoseParameter[] parameters, int index)
    {
        if ((uint)index >= (uint)parameters.Length)
        {
            return null;
        }

        return parameters[index];
    }

    private static Source1PoseParameter[] ReadPoseParameters(MdlFile mdl, byte[] data)
    {
        var size = Marshal.SizeOf<MdlFile.PoseParamDesc>();
        var descs = LumpReader<MdlFile.PoseParamDesc>.ReadLumpAt(data, mdl.Hdr.LocalPoseParamOffset, mdl.Hdr.NumLocalPoseParams);
        var result = new Source1PoseParameter[descs.Length];

        for (var i = 0; i < descs.Length; i++)
        {
            var offset = mdl.Hdr.LocalPoseParamOffset + (i * size);
            result[i] = new Source1PoseParameter(MdlFile.ReadCString(data, offset + descs[i].NameIndex), descs[i].Start, descs[i].End, descs[i].Loop);
        }

        return result;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }
}
