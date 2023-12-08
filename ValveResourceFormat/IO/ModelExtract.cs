using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Datamodel;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using RnShapes = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using ValveResourceFormat.ResourceTypes.ModelAnimation;


namespace ValveResourceFormat.IO;

public class ModelExtract
{
    private readonly Resource modelResource;
    private readonly Model model;
    private readonly PhysAggregateData physAggregateData;
    private readonly IFileLoader fileLoader;
    private readonly string fileName;

    public List<(Mesh Mesh, string FileName)> RenderMeshesToExtract { get; } = [];
    public Dictionary<string, IKeyValueCollection> MaterialInputSignatures { get; } = [];

    public List<(HullDescriptor Hull, string FileName)> PhysHullsToExtract { get; } = [];
    public List<(MeshDescriptor Mesh, string FileName)> PhysMeshesToExtract { get; } = [];
    public List<(Animation Anim, string FileName)> AnimationsToExtract { get; } = [];

    public string[] PhysicsSurfaceNames { get; private set; }
    public HashSet<string>[] PhysicsCollisionTags { get; private set; }

    public sealed record SurfaceTagCombo(string SurfacePropName, HashSet<string> InteractAsStrings)
    {
        public SurfaceTagCombo(string surfacePropName, string[] collisionTags)
            : this(surfacePropName, new HashSet<string>(collisionTags))
        { }

        public string StringMaterial => string.Join('+', InteractAsStrings) + '$' + SurfacePropName;
        public override int GetHashCode() => StringMaterial.GetHashCode(StringComparison.OrdinalIgnoreCase);
        public bool Equals(SurfaceTagCombo other) => GetHashCode() == other.GetHashCode();
    }

    public HashSet<SurfaceTagCombo> SurfaceTagCombos { get; } = [];

    public enum ModelExtractType
    {
        Default,
        Map_PhysicsToRenderMesh,
        Map_AggregateSplit,
    }

    public ModelExtractType Type { get; init; } = ModelExtractType.Default;
    public Func<SurfaceTagCombo, string> PhysicsToRenderMaterialNameProvider { get; init; }

    public ModelExtract(Resource modelResource, IFileLoader fileLoader)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);

        this.modelResource = modelResource;
        this.model = (Model)modelResource.DataBlock;
        this.fileLoader = fileLoader;

        var refPhysics = model.GetReferencedPhysNames().FirstOrDefault();
        if (refPhysics != null)
        {
            using var physResource = fileLoader.LoadFile(refPhysics + "_c");

            if (physResource != null)
            {
                physAggregateData = (PhysAggregateData)physResource.DataBlock;
            }
        }
        else
        {
            physAggregateData = model.GetEmbeddedPhys();
        }

        EnqueueMeshes();
        EnqueueAnimations();
    }

    /// <summary>
    /// Extract a single mesh to vmdl+dmx.
    /// </summary>
    /// <param name="mesh">Mesh data</param>
    /// <param name="fileName">File name of the mesh e.g "models/my_mesh.vmesh"</param>
    public ModelExtract(Mesh mesh, string fileName)
    {
        RenderMeshesToExtract.Add((mesh, GetDmxFileName_ForReferenceMesh(fileName)));
        this.fileName = Path.ChangeExtension(fileName, ".vmdl");
    }

    public ModelExtract(PhysAggregateData physAggregateData, string fileName)
    {
        this.physAggregateData = physAggregateData;
        this.fileName = fileName;
        EnqueueMeshes();
    }

    private void EnqueueMeshes()
    {
        EnqueueRenderMeshes();
        EnqueuePhysMeshes();
    }

    public ContentFile ToContentFile()
    {
        var vmdl = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveModel()),
            FileName = GetFileName(),
        };

        foreach (var renderMesh in RenderMeshesToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(renderMesh.FileName),
                () => ToDmxMesh(renderMesh.Mesh, Path.GetFileNameWithoutExtension(renderMesh.FileName), MaterialInputSignatures)
            );
        }

        foreach (var physHull in PhysHullsToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(physHull.FileName),
                () => ToDmxMesh(physHull.Hull)
            );
        }

        foreach (var physMesh in PhysMeshesToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(physMesh.FileName),
                () => ToDmxMesh(physMesh.Mesh)
            );
        }

        foreach (var anim in AnimationsToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(anim.FileName),
                () => ToDmxAnim(model, anim.Anim)
            );
        }

        return vmdl;
    }

    public string ToValveModel()
    {
        var kv = new KVObject(null);

        static KVValue MakeValue(object value)
        {
            var specialType = value switch
            {
                KVValue v => v,
                Vector3 vec3 => MakeArrayValue(new[] { vec3.X, vec3.Y, vec3.Z }),
                _ => null
            };

            if (specialType != null)
            {
                return specialType;
            }

            var basicType = value switch
            {
                string => KVType.STRING,
                bool => KVType.BOOLEAN,
                int => KVType.INT32,
                float => KVType.FLOAT,
                KVObject kv => kv.IsArray ? KVType.ARRAY : KVType.OBJECT,
                _ => throw new NotImplementedException()
            };

            return new KVValue(basicType, value);
        }

        static KVValue MakeArrayValue<T>(IEnumerable<T> values)
        {
            var list = new KVObject(null, isArray: true);
            foreach (var value in values)
            {
                list.AddProperty(null, MakeValue(value));
            }

            return MakeValue(list);
        }

        static void AddItem(KVObject node, KVObject item)
        {
            Debug.Assert(node.IsArray);
            node.AddProperty(null, MakeValue(item));
        }

        static KVObject MakeNode(string className, params (string Name, object Value)[] properties)
        {
            var node = new KVObject(className);
            node.AddProperty("_class", MakeValue(className));
            foreach (var prop in properties)
            {
                node.AddProperty(prop.Name, MakeValue(prop.Value));
            }
            return node;
        }

        static (KVObject Node, KVObject Children) MakeListNode(string className)
        {
            var children = new KVObject(null, isArray: true);
            var node = MakeNode(className, ("children", children));
            return (node, children);
        }

        var root = MakeListNode("RootNode");
        kv.AddProperty("rootNode", MakeValue(root.Node));

        Lazy<KVObject> MakeLazyList(string className)
        {
            return new Lazy<KVObject>(() =>
            {
                var list = MakeListNode(className);
                AddItem(root.Children, list.Node);

                return list.Children;
            });
        }

        var materialGroupList = MakeLazyList("MaterialGroupList");
        var renderMeshList = MakeLazyList("RenderMeshList");
        var animationList = MakeLazyList("AnimationList");
        var physicsShapeList = MakeLazyList("PhysicsShapeList");
        var skeleton = MakeLazyList("Skeleton");

        if (RenderMeshesToExtract.Count != 0)
        {
            foreach (var renderMesh in RenderMeshesToExtract)
            {
                var renderMeshFile = MakeNode(
                    "RenderMeshFile",
                    ("filename", renderMesh.FileName)
                );

                AddItem(renderMeshList.Value, renderMeshFile);
            }
        }

        if (PhysHullsToExtract.Count > 0 || PhysMeshesToExtract.Count > 0)
        {
            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                var globalReplace = PhysicsToRenderMaterialNameProvider == null;
                var remapTable = globalReplace ? null
                    : SurfaceTagCombos.ToDictionary(
                        combo => combo.StringMaterial,
                        combo => PhysicsToRenderMaterialNameProvider(combo)
                    );

                RemapMaterials(remapTable, globalReplace);
            }

            foreach (var (physHull, fileName) in PhysHullsToExtract)
            {
                HandlePhysMeshNode(physHull, fileName);
            }

            foreach (var (physMesh, fileName) in PhysMeshesToExtract)
            {
                HandlePhysMeshNode(physMesh, fileName);
            }
        }

        if (model != null)
        {
            ExtractModelKeyValues(root.Node);

            if (model.Skeleton.Roots.Length > 0)
            {
                AddBonesRecursive(model.Skeleton.Roots, skeleton.Value);
            }

            static void AddBonesRecursive(IEnumerable<Bone> bones, KVObject parent)
            {
                foreach (var bone in bones)
                {
                    var boneDefinitionNode = MakeNode(
                        "Bone",
                        ("name", bone.Name),
                        ("origin", bone.Position),
                        ("angles", ToEulerAngles(bone.Angle)),
                        ("do_not_discard", true)
                    );

                    AddItem(parent, boneDefinitionNode);

                    if (bone.Children.Count > 0)
                    {
                        var childBones = new KVObject(null, isArray: true);
                        boneDefinitionNode.AddProperty("children", MakeValue(childBones));
                        AddBonesRecursive(bone.Children, childBones);
                    }
                }
            }
        }

        if (physAggregateData is not null)
        {
            foreach (var physicsPart in physAggregateData.Parts)
            {
                foreach (var sphere in physicsPart.Shape.Spheres)
                {
                    var physicsShapeSphere = MakeNode(
                        "PhysicsShapeSphere",
                        ("surface_prop", PhysicsSurfaceNames[sphere.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[sphere.CollisionAttributeIndex])),
                        ("radius", sphere.Shape.Radius),
                        ("center", sphere.Shape.Center)
                    );

                    AddItem(physicsShapeList.Value, physicsShapeSphere);
                }

                foreach (var capsule in physicsPart.Shape.Capsules)
                {
                    var physicsShapeCapsule = MakeNode(
                        "PhysicsShapeCapsule",
                        ("surface_prop", PhysicsSurfaceNames[capsule.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[capsule.CollisionAttributeIndex])),
                        ("radius", capsule.Shape.Radius),
                        ("point0", capsule.Shape.Center[0]),
                        ("point1", capsule.Shape.Center[1])
                    );

                    AddItem(physicsShapeList.Value, physicsShapeCapsule);
                }
            }
        }


        return new KV3File(kv, format: "modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca}").ToString();
        //return new KV3File(kv, format: "modeldoc32:version{c5dcef98-b629-46ab-88e3-a17c005c935e}").ToString();

        //
        // Local functions
        //

        void HandlePhysMeshNode<TShape>(ShapeDescriptor<TShape> shapeDesc, string fileName)
            where TShape : struct
        {
            var surfacePropName = PhysicsSurfaceNames[shapeDesc.SurfacePropertyIndex];
            var collisionTags = PhysicsCollisionTags[shapeDesc.CollisionAttributeIndex];

            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                AddItem(renderMeshList.Value, MakeNode("RenderMeshFile", ("filename", fileName)));
                return;
            }

            var className = shapeDesc switch
            {
                HullDescriptor => "PhysicsHullFile",
                MeshDescriptor => "PhysicsMeshFile",
                _ => throw new NotImplementedException()
            };

            // TODO: per faceSet surface_prop
            var physicsShapeFile = MakeNode(
                className,
                ("filename", fileName),
                ("surface_prop", surfacePropName),
                ("collision_tags", string.Join(" ", collisionTags)),
                ("name", shapeDesc.UserFriendlyName ?? fileName)
            );

            AddItem(physicsShapeList.Value, physicsShapeFile);
        }

        void RemapMaterials(
            IReadOnlyDictionary<string, string> remapTable = null,
            bool globalReplace = false,
            string globalDefault = "materials/tools/toolsnodraw.vmat")
        {
            var remaps = new KVObject(null, isArray: true);
            AddItem(materialGroupList.Value,
                MakeNode(
                    "DefaultMaterialGroup",
                    ("remaps", remaps),
                    ("use_global_default", globalReplace),
                    ("global_default_material", globalDefault)
                )
            );

            if (globalReplace || remapTable == null)
            {
                return;
            }

            foreach (var (from, to) in remapTable)
            {
                var remap = new KVObject(null);
                remap.AddProperty("from", MakeValue(from));
                remap.AddProperty("to", MakeValue(to));
                AddItem(remaps, remap);
            }
        }

        void ExtractModelKeyValues(KVObject rootNode)
        {
            if (model.Data.ContainsKey("m_refAnimIncludeModels"))
            {
                foreach (var animIncludeModel in model.Data.GetArray<string>("m_refAnimIncludeModels"))
                {
                    AddItem(animationList.Value, MakeNode("AnimIncludeModel", ("model", animIncludeModel)));
                }
            }

            var breakPieceList = MakeLazyList("BreakPieceList");
            var gameDataList = MakeLazyList("GameDataList");

            var keyvaluesString = model.Data.GetSubCollection("m_modelInfo").GetProperty<string>("m_keyValueText");

            if (string.IsNullOrEmpty(keyvaluesString))
            {
                return;
            }

            KVObject keyvalues;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(keyvaluesString)))
            {
                try
                {
                    keyvalues = KeyValues3.ParseKVFile(ms).Root;
                }
                catch (Exception e)
                {
                    // TODO: Current parser fails when root is "null", so just skip over them for now
                    Console.Error.WriteLine(e.ToString());
                    return;
                }
            }

            if (keyvalues.ContainsKey("anim_graph_resource"))
            {
                rootNode.AddProperty("anim_graph_name", MakeValue(keyvalues.GetProperty<string>("anim_graph_resource")));
            }

            var genericDataClasses = new string[] { "prop_data", "character_arm_config", };
            var genericDataClassesList = new string[] { "ao_proxy_capsule", };

            foreach (var genericDataClass in genericDataClasses)
            {
                if (keyvalues.ContainsKey(genericDataClass))
                {
                    var genericData = keyvalues.GetProperty<KVObject>(genericDataClass);
                    AddGenericGameData(gameDataList.Value, genericDataClass, genericData);
                }
            }

            foreach (var genericDataClass in genericDataClassesList)
            {
                var dataKey = genericDataClass + "_list";
                if (keyvalues.ContainsKey(dataKey))
                {
                    var genericDataList = keyvalues.GetArray<KVObject>(dataKey);
                    foreach (var genericData in genericDataList)
                    {
                        AddGenericGameData(gameDataList.Value, genericDataClass, genericData);
                    }
                }
            }


            if (keyvalues.ContainsKey("break_list"))
            {
                foreach (var breakPiece in keyvalues.GetArray<KVObject>("break_list"))
                {
                    var breakPieceFile = MakeNode("BreakPieceExternal");
                    foreach (var property in breakPiece.Properties)
                    {
                        var (key, value) = (property.Key, property.Value);
                        // Remove resource flag from value
                        if (value is KVFlaggedValue)
                        {
                            value = MakeValue(value.Value);
                        }
                        breakPieceFile.AddProperty(key, value);
                    }
                    AddItem(breakPieceList.Value, breakPieceFile);
                }
            }

            static void AddGenericGameData(KVObject gameDataList, string genericDataClass, KVObject genericData)
            {
                // Remove quotes from keys
                genericData.Properties.Keys.ToList().ForEach(k =>
                {
                    var trimmed = k.Trim('"');
                    if (trimmed != k)
                    {
                        genericData.Properties[trimmed] = genericData.Properties[k];
                        genericData.Properties.Remove(k);
                    }
                });
                var genericGameData = MakeNode("GenericGameData", ("game_class", genericDataClass), ("game_keys", genericData));
                AddItem(gameDataList, genericGameData);
            }
        }
    }

    internal static Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles = new();

        // roll / x
        float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = MathF.Atan2(sinr_cosp, cosr_cosp);

        // pitch / y
        float sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (Math.Abs(sinp) >= 1)
        {
            angles.Y = MathF.CopySign(MathF.PI / 2, sinp);
        }
        else
        {
            angles.Y = MathF.Asin(sinp);
        }

        // yaw / z
        float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = MathF.Atan2(siny_cosp, cosy_cosp);

        return angles * (180 / MathF.PI);
    }

    public string GetFileName()
        => model?.Data.GetProperty<string>("m_name")
            ?? fileName;

    string GetDmxFileName_ForEmbeddedMesh(string subString, int number = 0)
    {
        var fileName = GetFileName();
        return (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + Path.GetFileNameWithoutExtension(fileName)
            + "_"
            + subString
            + (number > 0 ? number : string.Empty)
            + ".dmx")
            .Replace('\\', '/');
    }

    static string GetDmxFileName_ForReferenceMesh(string fileName)
        => Path.ChangeExtension(fileName, ".dmx").Replace('\\', '/');

    string GetDmxFileName_ForAnimation(string animationName)
    {
        var fileName = GetFileName();
        return (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + animationName
            + ".dmx")
            .Replace('\\', '/');
    }

    private void EnqueueRenderMeshes()
    {
        if (model == null)
        {
            return;
        }

        GrabMaterialInputSignatures(modelResource);

        var i = 0;
        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            RenderMeshesToExtract.Add((embedded.Mesh, GetDmxFileName_ForEmbeddedMesh(embedded.Name, i++)));
        }

        foreach (var reference in model.GetReferenceMeshNamesAndLoD())
        {
            using var resource = fileLoader.LoadFile(reference.MeshName + "_c");

            if (resource is null)
            {
                continue;
            }

            GrabMaterialInputSignatures(resource);
            RenderMeshesToExtract.Add(((Mesh)resource.DataBlock, GetDmxFileName_ForReferenceMesh(reference.MeshName)));
        }

        void GrabMaterialInputSignatures(Resource resource)
        {
            var materialReferences = resource?.ExternalReferences?.ResourceRefInfoList.Where(r => r.Name[^4..] == "vmat");
            foreach (var material in materialReferences ?? Enumerable.Empty<ResourceExtRefList.ResourceReferenceInfo>())
            {
                using var materialResource = fileLoader.LoadFile(material.Name + "_c");
                MaterialInputSignatures[material.Name] = (materialResource?.DataBlock as Material)?.GetInputSignature();
            }
        }
    }

    private void EnqueuePhysMeshes()
    {
        if (physAggregateData == null)
        {
            return;
        }

        var knownKeys = StringToken.InvertedTable;

        PhysicsSurfaceNames = physAggregateData.SurfacePropertyHashes.Select(hash =>
        {
            knownKeys.TryGetValue(hash, out var name);
            return name ?? hash.ToString(CultureInfo.InvariantCulture);
        }).ToArray();


        PhysicsCollisionTags = physAggregateData.CollisionAttributes.Select(attributes =>
            (attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings")).ToHashSet()
        ).ToArray();

        var i = 0;
        foreach (var physicsPart in physAggregateData.Parts)
        {
            foreach (var hull in physicsPart.Shape.Hulls)
            {
                PhysHullsToExtract.Add((hull, GetDmxFileName_ForEmbeddedMesh("hull", i++)));

                SurfaceTagCombos.Add(new SurfaceTagCombo(
                    PhysicsSurfaceNames[hull.SurfacePropertyIndex],
                    PhysicsCollisionTags[hull.CollisionAttributeIndex]
                ));
            }

            foreach (var mesh in physicsPart.Shape.Meshes)
            {
                PhysMeshesToExtract.Add((mesh, GetDmxFileName_ForEmbeddedMesh("phys", i++)));

                SurfaceTagCombos.Add(new SurfaceTagCombo(
                    PhysicsSurfaceNames[mesh.SurfacePropertyIndex],
                    PhysicsCollisionTags[mesh.CollisionAttributeIndex]
                ));

                foreach (var surfaceIndex in mesh.Shape.Materials)
                {
                    SurfaceTagCombos.Add(new SurfaceTagCombo(
                        PhysicsSurfaceNames[surfaceIndex],
                        PhysicsCollisionTags[mesh.CollisionAttributeIndex]
                    ));
                }
            }
        }
    }

    private void EnqueueAnimations()
    {
        if (model != null)
        {
            foreach (var anim in model.GetEmbeddedAnimations())
            {
                AnimationsToExtract.Add((anim, GetDmxFileName_ForAnimation(anim.Name)));
            }
        }
    }

    public static byte[] ToDmxMesh(Mesh mesh, string name, Dictionary<string, IKeyValueCollection> materialInputSignatures = null)
    {
        var mdat = mesh.Data;
        var mbuf = mesh.VBIB;
        var indexBuffers = mbuf.IndexBuffers.Select(ib => new Lazy<int[]>(() => GltfModelExporter.ReadIndices(ib, 0, (int)ib.ElementCount, 0))).ToArray();

        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelMultiVertexBufferLayout(name, mbuf.VertexBuffers.Count, out var dmeModel, out var dags, out var dmeVertexBuffers);

        IKeyValueCollection materialInputSignature = null;

        foreach (var sceneObject in mdat.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var vertexBufferInfo = drawCall.GetArray("m_vertexBuffers")[0]; // In what situation can we have more than 1 vertex buffer per draw call?
                var vertexBufferIndex = vertexBufferInfo.GetInt32Property("m_hBuffer");

                var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
                var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");
                ReadOnlySpan<int> indexBuffer = indexBuffers[indexBufferIndex].Value;

                var material = drawCall.GetProperty<string>("m_material");
                materialInputSignature ??= materialInputSignatures?.GetValueOrDefault(material);

                var baseVertex = drawCall.GetInt32Property("m_nBaseVertex");
                var startIndex = drawCall.GetInt32Property("m_nStartIndex");
                var indexCount = drawCall.GetInt32Property("m_nIndexCount");

                GenerateTriangleFaceSetFromIndexBuffer(
                    dags[vertexBufferIndex],
                    indexBuffer[startIndex..(startIndex + indexCount)],
                    baseVertex,
                    material,
                    $"{startIndex}..{startIndex + indexCount}");
            }
        }

        for (var i = 0; i < mbuf.VertexBuffers.Count; i++)
        {
            FillDatamodelVertexData(mbuf.VertexBuffers[i], dmeVertexBuffers[i], materialInputSignature);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    private static DmeModel BuildDmeDagSkeleton(Skeleton skeleton, out DmeTransform[] transforms)
    {
        var dmeSkeleton = new DmeModel();
        var children = new ElementArray();

        transforms = new DmeTransform[skeleton.Bones.Length];
        var boneDags = new DmeDag[skeleton.Bones.Length];

        foreach (var bone in skeleton.Bones)
        {
            var dag = new DmeDag
            {
                Name = bone.Name
            };

            dag.Transform.Name = bone.Name;
            dag.Transform.Position = bone.Position;
            dag.Transform.Orientation = bone.Angle;

            boneDags[bone.Index] = dag;
            transforms[bone.Index] = dag.Transform;
        }

        foreach (var bone in skeleton.Bones)
        {
            var boneDag = boneDags[bone.Index];
            if (bone.Parent != null)
            {
                var parentDag = boneDags[bone.Parent.Index];
                parentDag.Children.Add(boneDag);
            }
            else
            {
                dmeSkeleton.Children.Add(boneDag);
            }
        }

        return dmeSkeleton;
    }

    private static DmeChannel BuildDmeChannel<T>(string name, Element toElement, string toAttribute, out DmeLog<T> log)
    {
        var channel = new DmeChannel
        {
            Name = name,
            ToElement = toElement,
            ToAttribute = toAttribute,
            Mode = 3
        };

        log = [];
        var logLayer = new DmeLogLayer<T>();

        channel.Log = log;
        log.AddLayer(logLayer);

        return channel;
    }

    private static void ProcessBoneFrameForDmeChannel(Bone bone, Frame frame, TimeSpan time, DmeLogLayer<Vector3> positionLayer, DmeLogLayer<Quaternion> orientationLayer)
    {
        var frameBone = frame.Bones[bone.Index];

        positionLayer.Times.Add(time);
        positionLayer.LayerValues[frame.FrameIndex] = frameBone.Position;

        orientationLayer.Times.Add(time);
        orientationLayer.LayerValues[frame.FrameIndex] = frameBone.Angle;
    }

    private static void ProcessFlexFrameForDmeChannel(int flexId, Frame frame, TimeSpan time, DmeLogLayer<float> flexLayer)
    {
        var flexValue = frame.Datas[flexId];

        flexLayer.Times.Add(time);
        flexLayer.LayerValues[frame.FrameIndex] = flexValue;
    }

    public static byte[] ToDmxAnim(Model model, Animation anim)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);

        var skeleton = BuildDmeDagSkeleton(model.Skeleton, out var transforms);

        var animationList = new DmeAnimationList();
        var clip = new DmeChannelsClip();

        clip.TimeFrame.Duration = TimeSpan.FromSeconds((double)(anim.FrameCount - 1) / anim.Fps);
        clip.FrameRate = anim.Fps;

        var frames = new Frame[anim.FrameCount];
        for (var i = 0; i < anim.FrameCount; i++)
        {
            var frame = new Frame(model.Skeleton, model.FlexControllers)
            {
                FrameIndex = i
            };
            anim.DecodeFrame(frame);
            frames[i] = frame;
        }

        foreach (var bone in model.Skeleton.Bones)
        {
            var transform = transforms[bone.Index];

            var positionChannel = BuildDmeChannel<Vector3>($"{bone.Name}_p", transform, "position", out var positionLog);
            var orientationChannel = BuildDmeChannel<Quaternion>($"{bone.Name}_o", transform, "orientation", out var orientationLog);

            var positionLogLayer = positionLog.GetLayer(0);
            var orientationLogLayer = orientationLog.GetLayer(0);

            positionLogLayer.LayerValues = new Vector3[anim.FrameCount];
            orientationLogLayer.LayerValues = new Quaternion[anim.FrameCount];

            for (var i = 0; i < anim.FrameCount; i++)
            {
                var frame = frames[i];

                var time = TimeSpan.FromSeconds((double)i / anim.Fps);

                ProcessBoneFrameForDmeChannel(bone, frame, time, positionLogLayer, orientationLogLayer);
            }

            //If both layers are 0s only, modeldoc will ignore the animations on this bone.
            //In that case, replace the position layer's values with one that has a very small value outside of the range of the animation.
            //Do the same if there's only a single frame, since modeldoc also ignores animations that don't have any movement at all (Fixes #663)
            if (anim.FrameCount == 1 || (positionLogLayer.IsLayerZero() && orientationLogLayer.IsLayerZero()))
            {
                positionLogLayer.Times.Clear();
                positionLogLayer.Times.AddRange(new TimeSpan[] {
                    TimeSpan.FromSeconds(-0.2f),
                    TimeSpan.FromSeconds(-0.1f),
                    TimeSpan.FromSeconds(0f)
                });

                var layerValue = positionLogLayer.LayerValues[0];
                positionLogLayer.LayerValues = [
                    layerValue,
                    layerValue + new Vector3(0, 0, 0.00001f),
                    layerValue,
                ];
            }

            clip.Channels.Add(positionChannel);
            clip.Channels.Add(orientationChannel);
        }
        for (var flexId = 0; flexId < model.FlexControllers.Length; flexId++)
        {
            var flexController = model.FlexControllers[flexId];

            var flexElement = new Element();
            flexElement.Name = flexController.Name;
            flexElement.Add("flexWeight", 0f);

            var flexChannel = BuildDmeChannel<float>($"{flexController.Name}_flex_channel", flexElement, "flexWeight", out var flexLog);
            var flexLogLayer = flexLog.GetLayer(0);
            flexLogLayer.LayerValues = new float[anim.FrameCount];

            for (int i = 0; i < anim.FrameCount; i++)
            {
                Frame frame = frames[i];
                TimeSpan time = TimeSpan.FromSeconds((double)i / anim.Fps);
                ProcessFlexFrameForDmeChannel(flexId, frame, time, flexLogLayer);
            }
            clip.Channels.Add(flexChannel);
        }

        animationList.Animations.Add(clip);

        using var stream = new MemoryStream();

        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = skeleton,
            ["animationList"] = animationList,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["app"] = "sfm", //modeldoc won't import dmx animations without this
                ["source"] = $"Generated with {ValveResourceFormat.Utils.StringToken.VRF_GENERATOR}",
            }
        };

        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    private static void FillDatamodelVertexData(VBIB.OnDiskBufferData vertexBuffer, DmeVertexData vertexData, IKeyValueCollection materialInputSignature)
    {
        var indices = Enumerable.Range(0, (int)vertexBuffer.ElementCount).ToArray(); // May break with non-unit strides, non-tri faces

        foreach (var attribute in vertexBuffer.InputLayoutFields)
        {
            var attributeFormat = VBIB.GetFormatInfo(attribute);
            var semantic = attribute.SemanticName.ToLowerInvariant() + "$" + attribute.SemanticIndex;

            if (attribute.SemanticName is "NORMAL")
            {
                var (normals, tangents) = VBIB.GetNormalTangentArray(vertexBuffer, attribute);
                vertexData.AddIndexedStream(semantic, normals, indices);

                if (tangents.Length > 0)
                {
                    vertexData.AddIndexedStream("tangent$" + attribute.SemanticIndex, tangents, indices);
                }

                continue;
            }
            else if (attribute.SemanticName is "BLENDINDICES")
            {
                vertexData.JointCount = 4;

                var blendIndices = VBIB.GetBlendIndicesArray(vertexBuffer, attribute);
                vertexData.AddStream(semantic, Array.ConvertAll(blendIndices, i => (int)i));
                continue;
            }
            else if (attribute.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS")
            {
                var vectorWeights = VBIB.GetBlendWeightsArray(vertexBuffer, attribute);
                var flatWeights = MemoryMarshal.Cast<Vector4, float>(vectorWeights).ToArray();

                vertexData.AddStream("blendweights$" + attribute.SemanticIndex, flatWeights);
                continue;
            }

            if (materialInputSignature is not null)
            {
                var insgElement = Material.FindD3DInputSignatureElement(materialInputSignature, attribute.SemanticName, attribute.SemanticIndex);

                // Use engine semantics for attributes that need them
                if (insgElement.Semantic is "VertexPaintBlendParams" or "VertexPaintTintColor")
                {
                    semantic = insgElement.Semantic + "$0";
                }
            }

            switch (attributeFormat.ElementCount)
            {
                case 1:
                    var scalar = VBIB.GetScalarAttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, scalar, indices);
                    break;
                case 2:
                    var vec2 = VBIB.GetVector2AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec2, indices);
                    break;
                case 3:
                    var vec3 = VBIB.GetVector3AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec3, indices);
                    break;
                case 4:
                    var vec4 = VBIB.GetVector4AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec4, indices);
                    break;
                default:
                    throw new NotImplementedException($"Stream {semantic} has an unexpected number of components: {attributeFormat.ElementCount}.");
            }
        }

        if (vertexData.VertexFormat.Contains("blendindices$0") && !vertexData.VertexFormat.Contains("blendweights$0"))
        {
            var blendIndicesLength = vertexData.TryGetValue("blendindices$0", out var blendIndices)
                ? ((ICollection<int>)blendIndices).Count
                : throw new InvalidOperationException("blendindices$0 stream not found");
            vertexData.AddStream("blendweights$0", Enumerable.Repeat(1f, blendIndicesLength).ToArray());
        }
    }

    public byte[] ToDmxMesh(HullDescriptor hull)
    {
        var uniformSurface = PhysicsSurfaceNames[hull.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[hull.CollisionAttributeIndex];
        // https://github.com/ValveResourceFormat/ValveResourceFormat/issues/660#issuecomment-1795499191
        var fixRenderMeshCompileCrash = Type == ModelExtractType.Map_PhysicsToRenderMesh;
        return ToDmxMesh(hull.Shape, hull.UserFriendlyName, uniformSurface, uniformCollisionTags, fixRenderMeshCompileCrash);
    }

    public byte[] ToDmxMesh(MeshDescriptor mesh)
    {
        var uniformSurface = PhysicsSurfaceNames[mesh.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[mesh.CollisionAttributeIndex];
        var fixRenderMeshCompileCrash = Type == ModelExtractType.Map_PhysicsToRenderMesh;
        return ToDmxMesh(mesh.Shape, mesh.UserFriendlyName, uniformSurface, uniformCollisionTags, PhysicsSurfaceNames, fixRenderMeshCompileCrash);
    }

    public static byte[] ToDmxMesh(RnShapes.Hull hull, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        bool appendVertexNormalStream = false)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelBaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        // n-gon face set
        var faceSet = new DmeFaceSet() { Name = "hull faces" };
        faceSet.Material.MaterialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
        dag.Shape.FaceSets.Add(faceSet);

        Debug.Assert(hull.Faces.Length + hull.VertexPositions.Length == (hull.Edges.Length / 2) + 2);

        foreach (var face in hull.Faces)
        {
            var startEdge = face.Edge;
            var currentEdge = startEdge;
            do
            {
                var e = hull.Edges[currentEdge];
                faceSet.Faces.Add(e.Origin);
                currentEdge = e.Next;
            }
            while (currentEdge != startEdge);

            faceSet.Faces.Add(-1);
        }

        var indices = Enumerable.Range(0, hull.VertexPositions.Length * 3).ToArray();
        vertexData.AddIndexedStream("position$0", hull.VertexPositions, indices);

        if (appendVertexNormalStream)
        {
            vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(new Vector3(0, 0, 0), hull.VertexPositions.Length).ToArray(), indices);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    public static byte[] ToDmxMesh(RnShapes.Mesh mesh, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        string[] surfaceList,
        bool appendVertexNormalStream = false)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelBaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        if (mesh.Materials.Length == 0)
        {
            var materialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
            GenerateTriangleFaceSet(dag, 0, mesh.Triangles.Length, materialName);
        }
        else
        {
            Debug.Assert(mesh.Materials.Length == mesh.Triangles.Length);
            Debug.Assert(surfaceList.Length > 0);

            Span<DmeFaceSet> faceSets = new DmeFaceSet[surfaceList.Length];
            for (var t = 0; t < mesh.Materials.Length; t++)
            {
                var surfaceIndex = mesh.Materials[t];
                var faceSet = faceSets[surfaceIndex];

                if (faceSet == null)
                {
                    var surface = surfaceList[surfaceIndex];
                    faceSet = faceSets[surfaceIndex] = new DmeFaceSet()
                    {
                        Name = surface + '$' + surfaceIndex
                    };
                    faceSet.Material.MaterialName = new SurfaceTagCombo(surface, uniformCollisionTags).StringMaterial;
                    dag.Shape.FaceSets.Add(faceSet);
                }

                faceSet.Faces.Add(t * 3);
                faceSet.Faces.Add(t * 3 + 1);
                faceSet.Faces.Add(t * 3 + 2);
                faceSet.Faces.Add(-1);
            }
        }

        var indices = new int[mesh.Triangles.Length * 3];
        for (var t = 0; t < mesh.Triangles.Length; t++)
        {
            for (var i = 0; i < 3; i++)
            {
                indices[t * 3 + i] = mesh.Triangles[t].Indices[i];
            }
        }

        vertexData.AddIndexedStream("position$0", mesh.Vertices, indices);

        if (appendVertexNormalStream)
        {
            vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(new Vector3(0, 0, 0), mesh.Vertices.Length).ToArray(), indices);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    private static void DmxModelBaseLayout(string name, out DmeModel dmeModel, out DmeDag dag, out DmeVertexData vertexData)
    {
        DmxModelMultiVertexBufferLayout(name, 1, out dmeModel, out var dags, out var dmeVertexBuffers);
        dag = dags[0];
        vertexData = dmeVertexBuffers[0];
    }

    private static void DmxModelMultiVertexBufferLayout(string name, int vertexBufferCount,
        out DmeModel dmeModel, out DmeDag[] dags, out DmeVertexData[] dmeVertexBuffers)
    {
        dmeModel = new DmeModel() { Name = name };
        dags = new DmeDag[vertexBufferCount];
        dmeVertexBuffers = new DmeVertexData[vertexBufferCount];

        for (var i = 0; i < vertexBufferCount; i++)
        {
            // dmx requires one dag per vertex buffer
            var dag = dags[i] = new DmeDag() { Name = name };
            dmeModel.Children.Add(dag);
            dmeModel.JointList.Add(dag);

            var transformList = new DmeTransformsList();
            transformList.Transforms.Add(new DmeTransform());
            dmeModel.BaseStates.Add(transformList);

            var vertexData = dmeVertexBuffers[i] = new DmeVertexData { Name = "bind" };
            dag.Shape.Name = name;
            dag.Shape.CurrentState = vertexData;
            dag.Shape.BaseStates.Add(vertexData);
        }
    }

    private static void TieElementRoot(Datamodel.Datamodel dmx, DmeModel dmeModel)
    {
        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeModel,
            ["model"] = dmeModel,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["source"] = $"Generated with {ValveResourceFormat.Utils.StringToken.VRF_GENERATOR}",
            }
        };
    }

    private static void GenerateTriangleFaceSet(DmeDag dag, int triangleStart, int triangleEnd, string material)
    {
        var faceSet = new DmeFaceSet() { Name = triangleStart + "-" + triangleEnd };
        dag.Shape.FaceSets.Add(faceSet);

        for (var i = triangleStart; i < triangleEnd; i++)
        {
            faceSet.Faces.Add(i * 3);
            faceSet.Faces.Add(i * 3 + 1);
            faceSet.Faces.Add(i * 3 + 2);
            faceSet.Faces.Add(-1);
        }

        faceSet.Material.MaterialName = material;
    }

    private static void GenerateTriangleFaceSetFromIndexBuffer(DmeDag dag, ReadOnlySpan<int> indices, int baseVertex,
        string material, string name)
    {
        var faceSet = new DmeFaceSet() { Name = name };
        dag.Shape.FaceSets.Add(faceSet);

        for (var i = 0; i < indices.Length; i += 3)
        {
            faceSet.Faces.Add(baseVertex + indices[i]);
            faceSet.Faces.Add(baseVertex + indices[i + 1]);
            faceSet.Faces.Add(baseVertex + indices[i + 2]);
            faceSet.Faces.Add(-1);
        }

        faceSet.Material.MaterialName = material;
    }
}
