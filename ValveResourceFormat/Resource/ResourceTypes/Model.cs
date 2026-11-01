using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a model resource containing meshes, skeleton, and animations.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/PermModelData_t">PermModelData_t</seealso>
    public class Model : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the model name.
        /// </summary>
        public string Name => Data.GetStringProperty("m_name");

        /// <summary>
        /// Gets the key-values data from the model info.
        /// </summary>
        [NotNull]
        public KVObject KeyValues
        {
            get
            {
                cachedKeyValues ??= ParseKeyValuesText();
                cachedKeyValues ??= new KVObject(string.Empty);

                return cachedKeyValues;
            }
        }

        /// <summary>
        /// Gets the NM skeletons this model can be animated on, by resource path, in declaration order.
        /// </summary>
        public string[] NmSkeletonRefs => Data.GetArray<string>("m_vecNmSkeletonRefs") ?? [];

        /// <summary>
        /// Gets the animation graphs bound to this model, in declaration order. The first is the default.
        /// </summary>
        public IReadOnlyList<(string Identifier, string GraphPath)> AnimGraph2References
            => cachedAnimGraph2References ??= ReadAnimGraph2References();

        private (string Identifier, string GraphPath)[] ReadAnimGraph2References()
        {
            var graphRefs = Data.GetArray("m_animGraph2Refs");

            if (graphRefs == null)
            {
                return [];
            }

            var references = new (string Identifier, string GraphPath)[graphRefs.Count];

            for (var i = 0; i < graphRefs.Count; i++)
            {
                references[i] = (graphRefs[i].GetStringProperty("m_sIdentifier"), graphRefs[i].GetStringProperty("m_hGraph"));
            }

            return references;
        }

        /// <summary>
        /// Gets the bone constraints the model was authored with, in compiled order.
        /// </summary>
        public IReadOnlyList<BoneConstraint> BoneConstraints => cachedBoneConstraints ??= BoneConstraint.ReadList(KeyValues);

        /// <summary>
        /// Gets the compiled data of every bone constraint of one class, in compiled order.
        /// </summary>
        /// <param name="className">The compiled constraint class, e.g. <c>CTiltTwistConstraint</c>.</param>
        public IEnumerable<KVObject> GetBoneConstraints(string className)
        {
            foreach (var constraint in BoneConstraints)
            {
                if (constraint.ClassName == className)
                {
                    yield return constraint.Data;
                }
            }
        }

        /// <summary>
        /// Gets the skeleton for this model.
        /// </summary>
        public Skeleton Skeleton
        {
            get
            {
                cachedSkeleton ??= Skeleton.FromModelData(Data);
                return cachedSkeleton;
            }
        }

        /// <summary>
        /// Gets the flex controllers for this model.
        /// </summary>
        public FlexController[] FlexControllers
        {
            get
            {
                cachedFlexControllers ??= GetFlexControllers();
                return cachedFlexControllers;
            }
        }

        /// <summary>
        /// Gets the pins that hold each procedural cloth root of the skeleton to the first animated bone up its cloth
        /// control node's parent chain, or else to <see cref="Skeleton.ClothSimulationRoot"/>.
        /// Cloth roots without an anchor have no pin.
        /// </summary>
        public ClothRootPin[] ClothRootPins => cachedClothRootPins ??= GetClothRootPins();

        private List<AnimationBase>? CachedAnimations;
        private KVObject? cachedKeyValues;
        private Skeleton? cachedSkeleton;
        private FlexController[]? cachedFlexControllers;
        private ClothRootPin[]? cachedClothRootPins;
        private EmbeddedSequenceGroup? cachedSequenceGroup;
        private SequenceAnimation[]? cachedEmbeddedAnimations;
        private List<ModelMesh>? cachedEmbeddedMeshes;
        private ModelLodInfo? cachedLodInfo;
        private BoneConstraint[]? cachedBoneConstraints;
        private BoneRemapTable? cachedBoneRemapTable;
        private ModelMeshGroups? cachedMeshGroups;
        private (string Identifier, string GraphPath)[]? cachedAnimGraph2References;

        /// <summary>
        /// Gets the hitbox sets for this model.
        /// </summary>
        public Dictionary<string, Hitbox[]> HitboxSets { get; private set; } = [];

        /// <summary>
        /// Gets the attachments for this model.
        /// </summary>
        public Dictionary<string, Attachment> Attachments { get; private set; } = [];

        private ClothRootPin[] GetClothRootPins()
        {
            var skeleton = Skeleton;
            var clothRoots = skeleton.Roots.Where(static root => root.IsProceduralCloth).ToArray();
            if (clothRoots.Length == 0)
            {
                return [];
            }

            var fallbackAnchor = skeleton.ClothSimulationRoot?.Index ?? -1;

            var nodeNames = Array.Empty<string>();
            var nodeAnchors = Array.Empty<int>();
            var feModel = GetEmbeddedPhys()?.FeModel;
            if (feModel != null)
            {
                nodeNames = feModel.CtrlName;
                nodeAnchors = GetClothNodeAnchors(skeleton, feModel);
            }

            var inverseBindPose = BindPoseModelObject.GetInverseBindPose(this);
            var pins = new List<ClothRootPin>(clothRoots.Length);

            foreach (var root in clothRoots)
            {
                var anchor = fallbackAnchor;
                var node = Array.IndexOf(nodeNames, root.Name);
                if (node >= 0 && nodeAnchors[node] >= 0)
                {
                    anchor = nodeAnchors[node];
                }

                if (anchor < 0)
                {
                    continue;
                }

                pins.Add(new ClothRootPin(root.Index, anchor, root.BindPose * inverseBindPose[anchor]));
            }

            return [.. pins];
        }

        private static int[] GetClothNodeAnchors(Skeleton skeleton, FeModel feModel)
        {
            var nodeNames = feModel.CtrlName;
            var nodeCount = nodeNames.Length;

            var parents = new int[nodeCount];
            Array.Fill(parents, -1);

            foreach (var offset in feModel.CtrlOffsets)
            {
                if (offset.CtrlChild >= 0 && offset.CtrlChild < nodeCount)
                {
                    parents[offset.CtrlChild] = offset.CtrlParent;
                }
            }

            var skelParents = feModel.SkelParents;
            for (var node = 0; node < skelParents.Length && node < nodeCount; node++)
            {
                if (skelParents[node] >= 0)
                {
                    parents[node] = skelParents[node];
                }
            }

            var animatedBones = new int[nodeCount];
            for (var node = 0; node < nodeCount; node++)
            {
                var boneIndex = skeleton.GetBoneIndex(nodeNames[node]);
                var isAnimated = boneIndex >= 0 && !skeleton.Bones[boneIndex].IsProceduralCloth;
                animatedBones[node] = isAnimated ? boneIndex : -1;
            }

            var anchors = new int[nodeCount];
            Array.Fill(anchors, -1);

            for (var node = 0; node < nodeCount; node++)
            {
                var parent = parents[node];

                // Bounded by the node count, so a cyclic parent chain cannot loop forever
                for (var depth = 0; depth < nodeCount && parent >= 0 && parent < nodeCount; depth++)
                {
                    if (animatedBones[parent] >= 0)
                    {
                        anchors[node] = animatedBones[parent];
                        break;
                    }

                    parent = parents[parent];
                }
            }

            return anchors;
        }

        private FlexController[] GetFlexControllers()
        {
            if (Resource.GetBlockByType(BlockType.MRPH) is not Morph morph)
            {
                return [];
            }

            var flexControllersData = morph.Data.GetArray("m_FlexControllers") ?? [];

            var flexControllers = flexControllersData.Select(d =>
            {
                var name = d.GetStringProperty("m_szName");
                var type = d.GetStringProperty("m_szType");
                var min = d.GetFloatProperty("min");
                var max = d.GetFloatProperty("max");
                return new FlexController(name, type, min, max);
            });
            return flexControllers.ToArray();
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            base.Read(reader);

            if (Resource.GetBlockByType(BlockType.MDAT) is Mesh mesh)
            {
                HitboxSets = mesh.HitboxSets;
                Attachments = mesh.Attachments;
            }
        }

        /// <summary>
        /// Populates cached flex controller data from an externally loaded morph resource.
        /// </summary>
        /// <param name="morph">The morph data whose flex controllers should be reused.</param>
        public void SetExternalMorphData(Morph? morph)
        {
            // The model's own morph block wins; otherwise use the morph set its meshes carry (separate vmorf).
            if (FlexControllers.Length == 0)
            {
                cachedFlexControllers = morph?.FlexControllers;
            }
        }

        /// <summary>
        /// Populates flex controllers, hitbox sets and attachments the model does not have from an external mesh resource.
        /// </summary>
        /// <param name="mesh">The mesh providing supplemental data.</param>
        public void SetExternalMeshData(Mesh mesh)
        {
            SetExternalMorphData(mesh.MorphData);

            // Like the embedded MDAT case, the first mesh that carries hitboxes or attachments supplies them.
            if (HitboxSets.Count == 0)
            {
                HitboxSets = mesh.HitboxSets;
            }

            if (Attachments.Count == 0)
            {
                Attachments = mesh.Attachments;
            }
        }

        /// <summary>
        /// Get the bone remap table of a specific mesh.
        /// This is used to remap bone indices in the mesh <see cref="VBIB"/> to bone indices of the model skeleton.
        /// </summary>
        public int[]? GetRemapTable(int meshIndex) => BoneRemapTable.GetMeshTable(meshIndex);

        /// <summary>
        /// Gets the inverse bind pose a mesh is skinned with, one matrix per bone of the mesh's slice of the
        /// remap table. A bone is skinned by its pose times the mesh's own inverse bind pose, not the model skeleton's.
        /// </summary>
        /// <param name="mesh">The mesh to get the inverse bind pose of.</param>
        /// <param name="meshIndex">The index of the mesh in the model.</param>
        /// <param name="modelInverseBindPose">
        /// The inverse bind pose of every model bone, used for a mesh that does not carry one for every bone it addresses.
        /// </param>
        /// <returns>One matrix per mesh bone, indexed like the mesh's remap table slice.</returns>
        public Matrix4x4[] GetSkinningInverseBindPose(Mesh mesh, int meshIndex, ReadOnlySpan<Matrix4x4> modelInverseBindPose)
        {
            var remapTable = GetRemapTable(meshIndex);
            if (remapTable == null)
            {
                return [];
            }

            var meshInverseBindPose = mesh.ReadInverseBindPose();
            if (meshInverseBindPose != null && meshInverseBindPose.Length == remapTable.Length)
            {
                return meshInverseBindPose;
            }

            var inverseBindPose = new Matrix4x4[remapTable.Length];

            for (var i = 0; i < remapTable.Length; i++)
            {
                var modelBone = remapTable[i];
                var modelBoneExists = modelBone >= 0 && modelBone < modelInverseBindPose.Length;

                inverseBindPose[i] = modelBoneExists ? modelInverseBindPose[modelBone] : Matrix4x4.Identity;
            }

            return inverseBindPose;
        }

        /// <summary>
        /// Gets the model's bone remap table, which maps the bone indices a mesh's <c>BLENDINDICES</c>
        /// carry to bone indices of the model skeleton.
        /// </summary>
        public BoneRemapTable BoneRemapTable => cachedBoneRemapTable ??= new BoneRemapTable(Data);

        /// <summary>
        /// Gets the model's mesh groups, and the body groups their names encode.
        /// </summary>
        public ModelMeshGroups MeshGroups => cachedMeshGroups ??= new ModelMeshGroups(Data);

        /// <summary>
        /// Gets the model's references to meshes that live in their own vmesh. Slots filled by an
        /// embedded mesh are left out.
        /// </summary>
        public IEnumerable<ModelMeshReference> GetReferenceMeshNamesAndLoD()
        {
            var refMeshes = Data.GetArray<string>("m_refMeshes");

            if (refMeshes == null)
            {
                return [];
            }

            var result = new List<ModelMeshReference>(refMeshes.Length);

            for (var meshIndex = 0; meshIndex < refMeshes.Length; meshIndex++)
            {
                var refMesh = refMeshes[meshIndex];

                if (!string.IsNullOrEmpty(refMesh))
                {
                    result.Add(new ModelMeshReference(meshIndex, refMesh, LodInfo.GetMeshMask(meshIndex)));
                }
            }

            return result;
        }

        /// <summary>
        /// Gets this model's level-of-detail structure (which meshes belong to which LOD level and the
        /// per-level switch values). Built once and cached.
        /// </summary>
        public ModelLodInfo LodInfo => cachedLodInfo ??= new ModelLodInfo(Data);

        /// <summary>
        /// Gets the embedded meshes present in the given LOD <paramref name="level"/>.
        /// </summary>
        public IEnumerable<ModelMesh> GetEmbeddedMeshesForLod(int level)
            => GetEmbeddedMeshes().Where(m => LodInfo.IsMeshInLevel(m.MeshIndex, level));

        /// <summary>
        /// Gets the referenced mesh names present in the given LOD <paramref name="level"/>.
        /// </summary>
        public IEnumerable<ModelMeshReference> GetReferenceMeshNamesForLod(int level)
            => GetReferenceMeshNamesAndLoD().Where(m => LodInfo.IsMeshInLevel(m.MeshIndex, level));

        /// <summary>
        /// Gets the meshes embedded in the model itself.
        /// </summary>
        /// <remarks>
        /// A mesh's own index addresses the model's mask tables, which cover embedded and referenced
        /// meshes alike.
        /// </remarks>
        public IEnumerable<ModelMesh> GetEmbeddedMeshes()
        {
            if (cachedEmbeddedMeshes != null)
            {
                return cachedEmbeddedMeshes;
            }

            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedMeshes = ctrl?.Data.Root.GetArray("embedded_meshes");

            if (embeddedMeshes == null)
            {
                cachedEmbeddedMeshes = [];
                return cachedEmbeddedMeshes;
            }

            var meshes = new List<ModelMesh>(embeddedMeshes.Count);

            foreach (var embeddedMesh in embeddedMeshes)
            {
                if (!embeddedMesh.ContainsKey("vbib_block")) // MVTX MIDX update
                {
                    meshes.Add(ParseEmbeddedMesh2(embeddedMesh));
                    continue;
                }

                var name = embeddedMesh.GetStringProperty("name");
                var meshIndex = embeddedMesh.GetInt32Property("mesh_index");
                var dataBlockIndex = embeddedMesh.GetInt32Property("data_block");
                var vbibBlockIndex = embeddedMesh.GetInt32Property("vbib_block");

                var mesh = Resource.GetBlockByIndex(dataBlockIndex) as Mesh;
                Debug.Assert(mesh is not null);
                var vbib = Resource.GetBlockByIndex(vbibBlockIndex) as VBIB;
                Debug.Assert(vbib is not null);
                mesh.VBIB = vbib;
                mesh.Name = $"{Resource.FileName}:{name}";

                var morphBlockIndex = embeddedMesh.GetInt32Property("morph_block");
                if (morphBlockIndex >= 0)
                {
                    mesh.MorphData = Resource.GetBlockByIndex(morphBlockIndex) as Morph;
                }

                if (embeddedMesh.ContainsKey("tools_vb_block"))
                {
                    var toolsVbBlockIndex = embeddedMesh.GetInt32Property("tools_vb_block");
                    if (toolsVbBlockIndex >= 0 && Resource.GetBlockByIndex(toolsVbBlockIndex) is TBUF toolsBuffer)
                    {
                        mesh.VBIB.AddToolsBuffers(toolsBuffer.VertexBuffers);
                    }
                }

                meshes.Add(new ModelMesh(mesh, meshIndex, name, LodInfo.GetMeshMask(meshIndex)));
            }

            cachedEmbeddedMeshes = meshes;
            return cachedEmbeddedMeshes;
        }

        private ModelMesh ParseEmbeddedMesh2(KVObject embeddedMesh)
        {
            var name = embeddedMesh.GetStringProperty("m_Name");
            var meshIndex = embeddedMesh.GetInt32Property("m_nMeshIndex");
            var dataBlockIndex = embeddedMesh.GetInt32Property("m_nDataBlock");

            var mesh = Resource.GetBlockByIndex(dataBlockIndex) as Mesh;
            Debug.Assert(mesh is not null);
            mesh.VBIB = new VBIB(Resource, embeddedMesh)
            {
                Resource = Resource
            };
            mesh.Name = $"{Resource.FileName}:{name}";

            var morphBlockIndex = embeddedMesh.GetInt32Property("m_nMorphBlock");
            if (morphBlockIndex >= 0)
            {
                mesh.MorphData = Resource.GetBlockByIndex(morphBlockIndex) as Morph;
            }

            return new ModelMesh(mesh, meshIndex, name, LodInfo.GetMeshMask(meshIndex));
        }

        /// <summary>
        /// Gets embedded physics data from the model.
        /// </summary>
        /// <returns>The physics aggregate data, or null if not present.</returns>
        public PhysAggregateData? GetEmbeddedPhys()
        {
            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedPhys = ctrl?.Data.Root.GetSubCollection("embedded_physics");

            if (embeddedPhys == null)
            {
                return null;
            }

            var physBlockIndex = embeddedPhys.GetInt32Property("phys_data_block");
            return (PhysAggregateData)Resource.GetBlockByIndex(physBlockIndex);
        }

        /// <summary>
        /// Gets referenced physics data names.
        /// </summary>
        /// <returns>Enumerable of physics data names.</returns>
        public IEnumerable<string> GetReferencedPhysNames()
            => Data.GetArray<string>("m_refPhysicsData");

        /// <summary>
        /// Gets referenced animation group names.
        /// </summary>
        /// <returns>Enumerable of animation group names.</returns>
        public IEnumerable<string> GetReferencedAnimationGroupNames()
            => Data.GetArray<string>("m_refAnimGroups");

        /// <summary>
        /// Gets the model's embedded animation data and the legacy sequence group behind it.
        /// </summary>
        public EmbeddedSequenceGroup SequenceGroup
            => cachedSequenceGroup ??= Resource != null ? new EmbeddedSequenceGroup(Resource) : EmbeddedSequenceGroup.Empty;

        /// <summary>
        /// Gets embedded animations from the model.
        /// </summary>
        /// <returns>Enumerable of animations.</returns>
        public IEnumerable<SequenceAnimation> GetEmbeddedAnimations()
            => cachedEmbeddedAnimations ??= [.. GetEmbeddedAnimations(Skeleton, FlexControllers)];

        /// <summary>
        /// Gets embedded animations from the model, with remap tables built for the provided skeleton and flex controllers.
        /// </summary>
        public IEnumerable<SequenceAnimation> GetEmbeddedAnimations(Skeleton skeleton, FlexController[] flexControllers)
        {
            var group = SequenceGroup;

            if (group.AnimationData is not { } animationData || group.DecodeKey is not { } decodeKey)
            {
                return [];
            }

            var animations = group.SequenceData is { } sequenceData
                ? SequenceAnimation.FromSequenceData(sequenceData, animationData, decodeKey, skeleton, flexControllers)
                : SequenceAnimation.FromData(animationData, decodeKey, skeleton, flexControllers);

            group.ReleaseDataBlocks();

            return animations;
        }

        /// <summary>
        /// Gets animations referenced from other models.
        /// </summary>
        /// <param name="fileLoader">The file loader to use.</param>
        /// <returns>Enumerable of animations.</returns>
        public IEnumerable<AnimationBase> GetReferencedAnimations(IFileLoader fileLoader)
        {
            var refAnimModels = Data.GetArray<string>("m_refAnimIncludeModels");
            if (refAnimModels == null || refAnimModels.Length == 0)
            {
                return [];
            }

            var allAnims = new List<AnimationBase>();
            foreach (var modelName in refAnimModels)
            {
                if (string.IsNullOrEmpty(modelName))
                {
                    continue;
                }

                using var resource = fileLoader.LoadFileCompiled(modelName);
                if (resource?.DataBlock is not Model model)
                {
                    continue;
                }

                allAnims.AddRange(model.GetEmbeddedAnimations(Skeleton, FlexControllers));
                allAnims.AddRange(model.GetAnimationGroupAnimations(fileLoader, Skeleton, FlexControllers));
            }

            return allAnims;
        }

        /// <summary>
        /// Gets the animations this model reaches through the standalone animation groups it references.
        /// </summary>
        /// <param name="fileLoader">The file loader to use.</param>
        /// <returns>Enumerable of animations.</returns>
        public IEnumerable<SequenceAnimation> GetAnimationGroupAnimations(IFileLoader fileLoader) => GetAnimationGroupAnimations(fileLoader, Skeleton, FlexControllers);

        /// <summary>
        /// Gets the animations this model reaches through the standalone animation groups it references.
        /// With remap tables built for the provided skeleton and flex controllers.
        /// </summary>
        public IEnumerable<SequenceAnimation> GetAnimationGroupAnimations(IFileLoader fileLoader, Skeleton skeleton, FlexController[] flexControllers)
        {
            var animGroupPaths = GetReferencedAnimationGroupNames();

            if (animGroupPaths == null)
            {
                yield break;
            }

            foreach (var animGroupPath in animGroupPaths)
            {
                if (string.IsNullOrEmpty(animGroupPath))
                {
                    continue;
                }

                using var animGroup = fileLoader.LoadFileCompiled(animGroupPath);

                if (animGroup == default)
                {
                    continue;
                }

                foreach (var animation in AnimationGroupLoader.LoadAnimationGroup(animGroup, fileLoader, skeleton, flexControllers))
                {
                    yield return animation;
                }
            }
        }

        /// <summary>
        /// Gets all animations from this model including embedded, referenced, and animation groups.
        /// </summary>
        /// <param name="fileLoader">The file loader to use.</param>
        /// <returns>Enumerable of all animations.</returns>
        public IEnumerable<AnimationBase> GetAllAnimations(IFileLoader fileLoader)
            => GetAllAnimations(fileLoader, includeAnimationGraphClips: true);

        /// <summary>
        /// Gets all animations from this model including embedded, referenced, and animation groups.
        /// </summary>
        /// <param name="fileLoader">The file loader to use.</param>
        /// <param name="includeAnimationGraphClips">Whether to load the clips of the model's animation graphs too.</param>
        /// <returns>Enumerable of all animations.</returns>
        public IEnumerable<AnimationBase> GetAllAnimations(IFileLoader fileLoader, bool includeAnimationGraphClips)
        {
            if (CachedAnimations != null && includeAnimationGraphClips)
            {
                return CachedAnimations;
            }

            var animations = GetEmbeddedAnimations().ToList<AnimationBase>();

            animations.AddRange(GetAnimationGroupAnimations(fileLoader));

            if (includeAnimationGraphClips)
            {
                // Animation graph (AG2) clips are part of the model's animation set.
                var clipNames = IO.AnimationGraphLoader.GetClipNames(this, fileLoader);
                var clips = IO.AnimationGraphLoader.LoadClips(clipNames, fileLoader);

                foreach (var clipName in clipNames)
                {
                    if (clips.TryGetValue(clipName, out var clip))
                    {
                        animations.Add(clip);
                    }
                }
            }

            animations.AddRange(GetReferencedAnimations(fileLoader));

            var additiveSequences = IO.AnimationGraph1Additive.GetAdditiveSequences(this, fileLoader);

            // Legacy sequences sharing an additive clip's name (retarget sources) inherit its flag.
            foreach (var animation in animations)
            {
                if (animation is ClipAnimation { IsAdditive: true })
                {
                    additiveSequences.Add(System.IO.Path.GetFileNameWithoutExtension(animation.Name));
                }
            }

            // '@' autoplay aliases inherit the wrapped sequence's flag.
            foreach (var animation in animations)
            {
                if (animation is not SequenceAnimation sequenceAnimation)
                {
                    continue;
                }

                var sequenceName = animation.Name.StartsWith('@') ? animation.Name[1..] : animation.Name;

                sequenceAnimation.IsAdditive |= additiveSequences.Contains(sequenceName);
            }

            if (includeAnimationGraphClips)
            {
                CachedAnimations = animations;
            }

            return animations;
        }

        /// <summary>
        /// Gets the material groups defined in the model.
        /// </summary>
        /// <returns>Enumerable of material group names and their materials.</returns>
        public IEnumerable<(string Name, string[] Materials)> GetMaterialGroups()
           => Data.GetArray("m_materialGroups")
                .Select(group => (group.GetStringProperty("m_name"), group.GetArray<string>("m_materials")));

        /// <summary>
        /// Gets the material attributes the anim graph is allowed to drive, with their channel count
        /// (1 for a scalar float target, 4 for a color target).
        /// </summary>
        /// <returns>Enumerable of attribute name and channel count pairs.</returns>
        public IEnumerable<(string AttributeName, int NumChannels)> GetAnimatedMaterialAttributes()
            => (Data.GetArray("m_AnimatedMaterialAttributes") ?? [])
                .Select(attr => (attr.GetStringProperty("m_AttributeName"), attr.GetInt32Property("m_nNumChannels")));

        KVObject? ParseKeyValuesText()
        {
            var keyvaluesString = Data.GetSubCollection("m_modelInfo").GetStringProperty("m_keyValueText");

            const int NullKeyValuesLengthLimit = 140;
            if (string.IsNullOrEmpty(keyvaluesString)
            || !keyvaluesString.StartsWith("<!-- kv3 ", StringComparison.Ordinal)
            || keyvaluesString.Length < NullKeyValuesLengthLimit)
            {
                return null;
            }

            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(keyvaluesString));
            return KVDocumentExtensions.ParseKV3(ms).Root;
        }
    }
}
