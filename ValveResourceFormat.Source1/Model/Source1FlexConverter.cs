using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using VrfTexture = ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Source1.Model;

/// <summary>Converts the flexes of a Source 1 model to a Source 2 morph set for each mesh they move.</summary>
/// <remarks>
/// The first morphs are the flex descriptors, in the order their rules run and then the ones without a rule. They hold
/// the weights the rules compute and move nothing. After them comes a morph for each descriptor and target ramp that a
/// flex uses, whose rule ramps the weight of its descriptor and which holds the deltas of those flexes. A stereo flex
/// adds its deltas to the morph of each side, scaled by how much each vertex belongs to that side. An eyelid descriptor
/// gets a rule that places the lid on the eyes, raised or lowered by the elevation of their view target.
/// </remarks>
internal sealed class Source1FlexConverter
{
    // Wide enough for most meshes to fit one row of vertices per flex, and no wider than a morph composite atlas starts
    private const int MaxGridWidth = 1024;

    private const int BytesPerTexel = 4;

    internal const string EyeLookUpController = "eye_look_up";
    internal const string EyeLookDownController = "eye_look_down";

    private readonly record struct RampMorph(int FlexDesc, Vector4 Targets);

    private sealed class MorphDeltas(int vertexCount)
    {
        public Vector4[] Positions { get; } = new Vector4[vertexCount];
        public Vector4[] NormalWrinkles { get; } = new Vector4[vertexCount];
    }

    private readonly record struct DeltaRect(int Morph, int FirstRow, int RowCount);

    private readonly record struct MorphVertexDelta(int Vertex, Vector3 Position, Vector3 Normal, float Wrinkle);

    private readonly Source1Model source;
    private readonly bool eyelidsFollowEyes;
    private readonly int[] morphByFlexDesc;
    private readonly List<RampMorph> rampMorphs = [];
    private readonly Dictionary<RampMorph, int> rampMorphIds = [];

    public Source1FlexConverter(Source1Model source, bool eyelidsFollowEyes)
    {
        this.source = source;
        this.eyelidsFollowEyes = eyelidsFollowEyes && source.Eyelids.Length > 0;
        morphByFlexDesc = CreateDescriptorMorphIds(source);

        foreach (var part in source.BodyParts)
        {
            foreach (var subModel in part.Models)
            {
                foreach (var lodMeshes in subModel.Lods)
                {
                    foreach (var mesh in lodMeshes)
                    {
                        foreach (var flex in mesh.Flexes)
                        {
                            AddRampMorph(flex.FlexDesc, flex.Targets);

                            if (flex.FlexPair >= 0)
                            {
                                AddRampMorph(flex.FlexPair, flex.Targets);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Creates the morph set of a mesh, or returns <see langword="null"/> when no flex moves its vertices.</summary>
    /// <param name="meshes">The meshes whose vertices follow one another in the vertex buffer.</param>
    /// <param name="baseVertices">For each mesh, the index of its first vertex in the vertex buffer.</param>
    /// <param name="vertexCount">The number of vertices in the vertex buffer.</param>
    /// <param name="bindToRest">The rotation the vertices were given, which the deltas are given too.</param>
    public Morph? CreateMorph(IReadOnlyList<Source1Mesh> meshes, IReadOnlyList<int> baseVertices, int vertexCount, Matrix4x4 bindToRest)
    {
        var deltasByMorph = new SortedDictionary<int, MorphDeltas>();

        for (var i = 0; i < meshes.Count; i++)
        {
            AddMeshDeltas(deltasByMorph, meshes[i], baseVertices[i], vertexCount, bindToRest);
        }

        if (deltasByMorph.Count == 0)
        {
            return null;
        }

        var gridWidth = Math.Min(vertexCount, MaxGridWidth);
        var gridHeight = (vertexCount + gridWidth - 1) / gridWidth;
        var rects = new List<DeltaRect>();

        foreach (var (morph, deltas) in deltasByMorph)
        {
            AddDeltaRects(rects, morph, deltas, gridWidth, gridHeight);
        }

        var atlasWidth = gridWidth * 2;
        var atlasHeight = 0;

        foreach (var rect in rects)
        {
            atlasHeight += rect.RowCount;
        }

        var pixels = new byte[atlasWidth * atlasHeight * BytesPerTexel];
        var rectDatas = new Dictionary<int, KVObject>();
        var atlasRow = 0;

        foreach (var rect in rects)
        {
            var deltas = deltasByMorph[rect.Morph];
            var positionBundle = EncodeBundle(pixels, deltas.Positions, rect, gridWidth, atlasWidth, atlasHeight, atlasRow, 0);
            var normalBundle = EncodeBundle(pixels, deltas.NormalWrinkles, rect, gridWidth, atlasWidth, atlasHeight, atlasRow, gridWidth);

            var bundleDatas = KVObject.Array();
            bundleDatas.Add(positionBundle);
            bundleDatas.Add(normalBundle);

            var rectData = KVObject.Collection();
            rectData.Add("m_nXLeftDst", 0);
            rectData.Add("m_nYTopDst", rect.FirstRow);
            rectData.Add("m_flUWidthSrc", gridWidth / (float)atlasWidth);
            rectData.Add("m_flVHeightSrc", rect.RowCount / (float)atlasHeight);
            rectData.Add("m_bundleDatas", bundleDatas);

            if (!rectDatas.TryGetValue(rect.Morph, out var morphRects))
            {
                morphRects = KVObject.Array();
                rectDatas.Add(rect.Morph, morphRects);
            }

            morphRects.Add(rectData);
            atlasRow += rect.RowCount;
        }

#pragma warning disable CA2000 // The morph set keeps the atlas, which holds no unmanaged state
        var atlas = VrfTexture.Create(checked((ushort)atlasWidth), checked((ushort)atlasHeight), VTexFormat.RGBA8888, 0, Vector4.One, [pixels]);
#pragma warning restore CA2000

        return Morph.Create(CreateData(gridWidth, gridHeight, rectDatas), atlas);
    }

    private static int[] CreateDescriptorMorphIds(Source1Model source)
    {
        var morphByFlexDesc = new int[source.FlexDescNames.Length];
        Array.Fill(morphByFlexDesc, -1);

        var nextMorph = 0;

        foreach (var rule in source.FlexRules)
        {
            if (IsFlexDesc(source, rule.FlexDesc) && morphByFlexDesc[rule.FlexDesc] < 0)
            {
                morphByFlexDesc[rule.FlexDesc] = nextMorph;
                nextMorph++;
            }
        }

        for (var flexDesc = 0; flexDesc < morphByFlexDesc.Length; flexDesc++)
        {
            if (morphByFlexDesc[flexDesc] < 0)
            {
                morphByFlexDesc[flexDesc] = nextMorph;
                nextMorph++;
            }
        }

        return morphByFlexDesc;
    }

    private static bool IsFlexDesc(Source1Model source, int index) => index >= 0 && index < source.FlexDescNames.Length;

    private void AddRampMorph(int flexDesc, Vector4 targets)
    {
        if (!IsFlexDesc(source, flexDesc))
        {
            return;
        }

        var ramp = new RampMorph(flexDesc, targets);

        if (rampMorphIds.ContainsKey(ramp))
        {
            return;
        }

        rampMorphIds.Add(ramp, morphByFlexDesc.Length + rampMorphs.Count);
        rampMorphs.Add(ramp);
    }

    private void AddMeshDeltas(SortedDictionary<int, MorphDeltas> deltasByMorph, Source1Mesh mesh, int baseVertex, int vertexCount, Matrix4x4 bindToRest)
    {
        foreach (var flex in mesh.Flexes)
        {
            if (!rampMorphIds.TryGetValue(new RampMorph(flex.FlexDesc, flex.Targets), out var morph))
            {
                continue;
            }

            var pairMorph = -1;

            if (flex.FlexPair >= 0 && rampMorphIds.TryGetValue(new RampMorph(flex.FlexPair, flex.Targets), out var rightMorph))
            {
                pairMorph = rightMorph;
            }

            foreach (var vertex in flex.Vertices)
            {
                if (vertex.Index >= mesh.OutputByOriginalVertexId.Length)
                {
                    continue;
                }

                var output = mesh.OutputByOriginalVertexId[vertex.Index];

                if (output < 0)
                {
                    continue;
                }

                var position = Vector3.TransformNormal(vertex.PositionDelta, bindToRest);
                var normal = Vector3.TransformNormal(vertex.NormalDelta, bindToRest);
                var delta = new MorphVertexDelta(baseVertex + output, position, normal, vertex.WrinkleDelta);

                if (pairMorph < 0)
                {
                    AddDelta(deltasByMorph, morph, delta, 1f, vertexCount);
                }
                else
                {
                    AddDelta(deltasByMorph, morph, delta, 1f - vertex.Side, vertexCount);
                    AddDelta(deltasByMorph, pairMorph, delta, vertex.Side, vertexCount);
                }
            }
        }
    }

    private static void AddDelta(SortedDictionary<int, MorphDeltas> deltasByMorph, int morph, MorphVertexDelta delta, float scale, int vertexCount)
    {
        if (scale == 0f)
        {
            return;
        }

        if (!deltasByMorph.TryGetValue(morph, out var deltas))
        {
            deltas = new MorphDeltas(vertexCount);
            deltasByMorph.Add(morph, deltas);
        }

        deltas.Positions[delta.Vertex] += new Vector4(delta.Position * scale, 0f);
        deltas.NormalWrinkles[delta.Vertex] += new Vector4(delta.Normal * scale, delta.Wrinkle * scale);
    }

    // A rect for each run of grid rows that hold a delta of the morph
    private static void AddDeltaRects(List<DeltaRect> rects, int morph, MorphDeltas deltas, int gridWidth, int gridHeight)
    {
        var firstRow = -1;

        for (var row = 0; row < gridHeight; row++)
        {
            var hasDelta = RowHasDelta(deltas, row, gridWidth);

            if (hasDelta && firstRow < 0)
            {
                firstRow = row;
            }
            else if (!hasDelta && firstRow >= 0)
            {
                rects.Add(new DeltaRect(morph, firstRow, row - firstRow));
                firstRow = -1;
            }
        }

        if (firstRow >= 0)
        {
            rects.Add(new DeltaRect(morph, firstRow, gridHeight - firstRow));
        }
    }

    private static bool RowHasDelta(MorphDeltas deltas, int row, int gridWidth)
    {
        var start = row * gridWidth;
        var end = Math.Min(start + gridWidth, deltas.Positions.Length);

        for (var vertex = start; vertex < end; vertex++)
        {
            if (deltas.Positions[vertex] != Vector4.Zero || deltas.NormalWrinkles[vertex] != Vector4.Zero)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes the deltas a rect covers into the atlas with 8 bits per channel, and returns the bundle data that decodes them.
    /// Each channel is quantized in steps across its own range, with zero on a step so that vertices without a delta stay in place.
    /// </summary>
    private static KVObject EncodeBundle(byte[] pixels, Vector4[] deltas, DeltaRect rect, int gridWidth, int atlasWidth, int atlasHeight, int atlasRow, int atlasColumn)
    {
        var first = rect.FirstRow * gridWidth;
        var end = Math.Min(first + (rect.RowCount * gridWidth), deltas.Length);
        var min = Vector4.Zero;
        var max = Vector4.Zero;

        for (var vertex = first; vertex < end; vertex++)
        {
            min = Vector4.Min(min, deltas[vertex]);
            max = Vector4.Max(max, deltas[vertex]);
        }

        // 254 steps leave room for zero to be rounded onto a step without pushing the extremes out of the 256 levels
        var step = (max - min) / 254f;
        var zeroLevel = Vector4.Round(-min / Vector4.Max(step, new Vector4(float.Epsilon)));
        var offset = -zeroLevel * step;

        for (var vertex = first; vertex < end; vertex++)
        {
            var local = vertex - first;
            var x = atlasColumn + (local % gridWidth);
            var y = atlasRow + (local / gridWidth);
            var pixel = ((y * atlasWidth) + x) * BytesPerTexel;
            var delta = deltas[vertex];

            pixels[pixel] = Quantize(delta.X, offset.X, step.X);
            pixels[pixel + 1] = Quantize(delta.Y, offset.Y, step.Y);
            pixels[pixel + 2] = Quantize(delta.Z, offset.Z, step.Z);
            pixels[pixel + 3] = Quantize(delta.W, offset.W, step.W);
        }

        var bundle = KVObject.Collection();
        bundle.Add("m_flULeftSrc", atlasColumn / (float)atlasWidth);
        bundle.Add("m_flVTopSrc", atlasRow / (float)atlasHeight);
        bundle.Add("m_offsets", KVHelpers.ToKVArray(offset));
        bundle.Add("m_ranges", KVHelpers.ToKVArray(step * 255f));

        return bundle;
    }

    private static byte Quantize(float value, float offset, float step)
    {
        if (step == 0f)
        {
            return 0;
        }

        var level = MathF.Round((value - offset) / step);
        return (byte)Math.Clamp(level, 0f, 255f);
    }

    private KVObject CreateData(int gridWidth, int gridHeight, Dictionary<int, KVObject> rectDatas)
    {
        var flexDescs = KVObject.Array();
        var morphNames = new string[morphByFlexDesc.Length + rampMorphs.Count];

        for (var flexDesc = 0; flexDesc < morphByFlexDesc.Length; flexDesc++)
        {
            morphNames[morphByFlexDesc[flexDesc]] = source.FlexDescNames[flexDesc];
        }

        for (var morph = 0; morph < morphByFlexDesc.Length; morph++)
        {
            var flexDesc = KVObject.Collection();
            flexDesc.Add("m_szFacs", morphNames[morph]);
            flexDescs.Add(flexDesc);
        }

        var rampCountByFlexDesc = new int[morphByFlexDesc.Length];

        for (var i = 0; i < rampMorphs.Count; i++)
        {
            var flexDesc = rampMorphs[i].FlexDesc;
            var name = source.FlexDescNames[flexDesc];

            // A descriptor with more than one ramp names the later ones apart
            if (rampCountByFlexDesc[flexDesc] > 0)
            {
                name = string.Create(CultureInfo.InvariantCulture, $"{name}.{rampCountByFlexDesc[flexDesc]}");
            }

            rampCountByFlexDesc[flexDesc]++;
            morphNames[morphByFlexDesc.Length + i] = name;
        }

        var morphDatas = KVObject.Array();

        for (var morph = 0; morph < morphNames.Length; morph++)
        {
            var morphData = KVObject.Collection();

            // The descriptor morphs move nothing, an empty name leaves them out of the morph targets
            morphData.Add("m_name", morph < morphByFlexDesc.Length ? string.Empty : morphNames[morph]);
            morphData.Add("m_morphRectDatas", rectDatas.GetValueOrDefault(morph) ?? KVObject.Array());
            morphDatas.Add(morphData);
        }

        var bundleTypes = KVObject.Array();
        bundleTypes.Add("MORPH_BUNDLE_TYPE_POSITION_SPEED");
        bundleTypes.Add("MORPH_BUNDLE_TYPE_NORMAL_WRINKLE");

        var data = KVObject.Collection();
        data.Add("m_nWidth", gridWidth);
        data.Add("m_nHeight", gridHeight);
        data.Add("m_bundleTypes", bundleTypes);
        data.Add("m_morphDatas", morphDatas);
        data.Add("m_FlexDesc", flexDescs);
        data.Add("m_FlexControllers", CreateControllers());
        data.Add("m_FlexRules", CreateRules());

        return data;
    }

    private KVObject CreateControllers()
    {
        var controllers = KVObject.Array();

        foreach (var sourceController in source.FlexControllers)
        {
            var controller = KVObject.Collection();
            controller.Add("m_szName", sourceController.Name);
            controller.Add("m_szType", sourceController.Type);
            controller.Add("min", sourceController.Min);
            controller.Add("max", sourceController.Max);
            controllers.Add(controller);
        }

        if (eyelidsFollowEyes)
        {
            controllers.Add(CreateEyeLookController(EyeLookUpController));
            controllers.Add(CreateEyeLookController(EyeLookDownController));
        }

        return controllers;
    }

    private static KVObject CreateEyeLookController(string name)
    {
        var controller = KVObject.Collection();
        controller.Add("m_szName", name);
        controller.Add("m_szType", "default");
        controller.Add("min", 0f);
        controller.Add("max", 1f);

        return controller;
    }

    private KVObject CreateRules()
    {
        var rules = KVObject.Array();
        var computed = new bool[morphByFlexDesc.Length];

        foreach (var sourceRule in source.FlexRules)
        {
            if (!IsFlexDesc(source, sourceRule.FlexDesc))
            {
                continue;
            }

            rules.Add(CreateRule(morphByFlexDesc[sourceRule.FlexDesc], ConvertOps(sourceRule.Ops, computed)));
            computed[sourceRule.FlexDesc] = true;
        }

        foreach (var eyelid in source.Eyelids)
        {
            if (IsFlexDesc(source, eyelid.FlexDesc) && eyelid.EyeballRadius > 0f)
            {
                rules.Add(CreateRule(morphByFlexDesc[eyelid.FlexDesc], CreateEyelidOps(eyelid)));
            }
        }

        for (var i = 0; i < rampMorphs.Count; i++)
        {
            rules.Add(CreateRule(morphByFlexDesc.Length + i, CreateRampOps(rampMorphs[i])));
        }

        return rules;
    }

    private static KVObject CreateRule(int morph, List<Source1FlexOp> ops)
    {
        var flexOps = KVObject.Array();

        foreach (var op in ops)
        {
            var flexOp = KVObject.Collection();
            flexOp.Add("m_OpCode", op.Code);
            flexOp.Add("m_Data", op.Data);
            flexOps.Add(flexOp);
        }

        var rule = KVObject.Collection();
        rule.Add("m_nFlex", morph);
        rule.Add("m_FlexOps", flexOps);

        return rule;
    }

    private List<Source1FlexOp> ConvertOps(Source1FlexOp[] sourceOps, bool[] computed)
    {
        var ops = new List<Source1FlexOp>(sourceOps.Length);

        foreach (var op in sourceOps)
        {
            var code = (FlexOpCode)op.Code;

            if (code == FlexOpCode.Fetch2)
            {
                // Descriptor weights start every evaluation at zero, so one that no earlier rule computed reads as zero
                if (IsFlexDesc(source, op.Data) && computed[op.Data])
                {
                    ops.Add(op with { Data = morphByFlexDesc[op.Data] });
                }
                else
                {
                    ops.Add(CreateConst(0f));
                }

                continue;
            }

            // The controller index that the n-way op pops is pushed as a float, but read back as the bits of an int
            if (code == FlexOpCode.NWay && ops.Count > 0 && (FlexOpCode)ops[^1].Code == FlexOpCode.Const)
            {
                var index = (int)BitConverter.Int32BitsToSingle(ops[^1].Data);
                ops[^1] = ops[^1] with { Data = index };
            }

            ops.Add(op);
        }

        return ops;
    }

    // The height of the lid on the eye: the radius times the sine of the lid angle, which blends the angles of the
    // raised, neutral and lowered heights by the weights of their descriptors, plus the elevation of the eye
    private List<Source1FlexOp> CreateEyelidOps(Source1Eyelid eyelid)
    {
        var radius = eyelid.EyeballRadius;
        var add = new Source1FlexOp((int)FlexOpCode.Add, 0);
        var ops = new List<Source1FlexOp>();

        AddWeightedLidAngle(ops, eyelid.RaiserFlexDesc, eyelid.Heights.X, radius);
        AddWeightedLidAngle(ops, eyelid.NeutralFlexDesc, eyelid.Heights.Y, radius);
        ops.Add(add);
        AddWeightedLidAngle(ops, eyelid.LowererFlexDesc, eyelid.Heights.Z, radius);
        ops.Add(add);

        if (eyelidsFollowEyes)
        {
            AddEyeElevation(ops);
            ops.Add(add);
        }

        ops.Add(new Source1FlexOp((int)FlexOpCode.Sin, 0));
        ops.Add(CreateConst(radius));
        ops.Add(new Source1FlexOp((int)FlexOpCode.Mul, 0));

        return ops;
    }

    // The elevation of the eyes in radians: the two eye look controllers each cover a quarter turn
    private void AddEyeElevation(List<Source1FlexOp> ops)
    {
        var upController = source.FlexControllers.Length;

        ops.Add(new Source1FlexOp((int)FlexOpCode.Fetch1, upController));
        ops.Add(new Source1FlexOp((int)FlexOpCode.Fetch1, upController + 1));
        ops.Add(new Source1FlexOp((int)FlexOpCode.Sub, 0));
        ops.Add(CreateConst(MathF.PI / 2f));
        ops.Add(new Source1FlexOp((int)FlexOpCode.Mul, 0));
    }

    private void AddWeightedLidAngle(List<Source1FlexOp> ops, int flexDesc, float height, float radius)
    {
        if (IsFlexDesc(source, flexDesc))
        {
            ops.Add(new Source1FlexOp((int)FlexOpCode.Fetch2, morphByFlexDesc[flexDesc]));
        }
        else
        {
            ops.Add(CreateConst(0f));
        }

        var angle = MathF.Asin(Math.Clamp(height / radius, -1f, 1f));

        ops.Add(CreateConst(angle));
        ops.Add(new Source1FlexOp((int)FlexOpCode.Mul, 0));
    }

    // The lower of the weight rising from 0 to 1 between the first two targets and falling back to 0 between the last two
    private List<Source1FlexOp> CreateRampOps(RampMorph ramp)
    {
        var fetch = new Source1FlexOp((int)FlexOpCode.Fetch2, morphByFlexDesc[ramp.FlexDesc]);
        var remap = new Source1FlexOp((int)FlexOpCode.RemapValClamped, 0);

        return
        [
            fetch,
            CreateConst(ramp.Targets.X),
            CreateConst(ramp.Targets.Y),
            CreateConst(0f),
            CreateConst(1f),
            remap,
            fetch,
            CreateConst(ramp.Targets.Z),
            CreateConst(ramp.Targets.W),
            CreateConst(1f),
            CreateConst(0f),
            remap,
            new Source1FlexOp((int)FlexOpCode.Min, 0),
        ];
    }

    private static Source1FlexOp CreateConst(float value) => new((int)FlexOpCode.Const, BitConverter.SingleToInt32Bits(value));
}
