using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.ResourceTypes
{
    public partial class Model
    {
        private const ushort CurrentVersion = 1;

        /// <summary>
        /// Creates a model resource with embedded meshes, ready for <see cref="ValveResourceFormat.Resource.Serialize"/>.
        /// The model can also be read directly from the returned resource.
        /// </summary>
        /// <remarks>
        /// Set <see cref="ValveResourceFormat.Resource.FileName"/> on the resource to a name that ends in <c>.vmdl_c</c> before reading a serialized model back.
        /// Meshes are written with uncompressed buffers.
        /// Attachments and hitbox sets are given as <c>m_attachments</c> and <c>m_hitboxsets</c> in <paramref name="extra"/>.
        /// </remarks>
        /// <param name="name">The model name.</param>
        /// <param name="meshes">The meshes to embed. A mesh is addressed by its position in this list.</param>
        /// <param name="bones">The skeleton of the model, empty for a model that has none.</param>
        /// <param name="materialGroups">The material groups of the model, the first of which is the default.</param>
        /// <param name="meshGroups">The names of the mesh groups of the model.</param>
        /// <param name="defaultMeshGroupMask">The mesh groups that are on by default, bit N for group N.</param>
        /// <param name="extra">Members that are added to the model data, replacing generated ones of the same name.</param>
        /// <param name="animations">The embedded animations of the model, which <see cref="SequenceGroup"/> reads.</param>
        /// <param name="embeddedPhysics">The data of the physics aggregate to embed, which <see cref="GetEmbeddedPhys"/> reads.</param>
        /// <returns>A resource whose data block is the new model.</returns>
        public static Resource Create(
            string name,
            IReadOnlyList<ModelMeshSource> meshes,
            IReadOnlyList<ModelBone> bones,
            IReadOnlyList<ModelMaterialGroup> materialGroups,
            IReadOnlyList<string> meshGroups,
            ulong defaultMeshGroupMask,
            KVObject? extra = null,
            ModelAnimationSource? animations = null,
            KVObject? embeddedPhysics = null)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(meshes);
            ArgumentNullException.ThrowIfNull(bones);
            ArgumentNullException.ThrowIfNull(materialGroups);
            ArgumentNullException.ThrowIfNull(meshGroups);

            var resource = new Resource(ResourceType.Model, CurrentVersion);
            var model = new Model { Resource = resource };
            var embeddedMeshes = KVObject.Array();
            var modelMeshes = new List<ModelMesh>(meshes.Count);

            for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                var source = meshes[meshIndex];

                // The renderer caches mesh buffers by this name, so it carries the model name like a read embedded mesh does
                var mesh = new Mesh(BlockType.MDAT) { Resource = resource, Name = $"{name}:{source.Name}" };
                mesh.Data = Mesh.CreateData(source);

                var buffers = new MBUF { Resource = resource };
                buffers.VertexBuffers.AddRange(source.Buffers.VertexBuffers);
                buffers.IndexBuffers.AddRange(source.Buffers.IndexBuffers);
                mesh.VBIB = buffers;

                var dataBlockIndex = resource.AddBlock(mesh);
                var vbibBlockIndex = resource.AddBlock(buffers);
                var morphBlockIndex = -1;

                if (source.Morph != null)
                {
                    morphBlockIndex = resource.AddBlock(source.Morph);
                    mesh.MorphData = source.Morph;
                }

                var embedded = KVObject.Collection();
                embedded.Add("name", source.Name);
                embedded.Add("mesh_index", meshIndex);
                embedded.Add("data_block", dataBlockIndex);
                embedded.Add("vbib_block", vbibBlockIndex);
                embedded.Add("morph_block", morphBlockIndex);
                embeddedMeshes.Add(embedded);

                modelMeshes.Add(new ModelMesh(mesh, meshIndex, source.Name, source.LodMask));
            }

            var control = KVObject.Collection();
            control.Add("embedded_meshes", embeddedMeshes);

            if (animations != null)
            {
                control.Add("embedded_animation", AddAnimationBlocks(resource, animations));
            }

            if (embeddedPhysics != null)
            {
                var physics = new PhysAggregateData(BlockType.PHYS) { Resource = resource, Data = embeddedPhysics };
                var embedded = KVObject.Collection();
                embedded.Add("phys_data_block", resource.AddBlock(physics));
                control.Add("embedded_physics", embedded);
            }

            resource.AddBlock(new BinaryKV3(control, KV3IDLookup.Get("generic"), BlockType.CTRL) { Resource = resource });

            model.Data = CreateData(name, meshes, bones, materialGroups, meshGroups, defaultMeshGroupMask, extra);
            model.cachedEmbeddedMeshes = modelMeshes;
            resource.AddBlock(model);

            Mesh.ReadHitboxSets(model.Data, model.HitboxSets);
            Mesh.ReadAttachments(model.Data, model.Attachments);

            return resource;
        }

        private static KVObject AddAnimationBlocks(Resource resource, ModelAnimationSource animations)
        {
            var embeddedAnimation = KVObject.Collection();
            embeddedAnimation.Add("group_data_block", AddDataBlock(resource, BlockType.AGRP, animations.AnimationGroup));
            embeddedAnimation.Add("anim_data_block", AddDataBlock(resource, BlockType.ANIM, animations.AnimationData));
            embeddedAnimation.Add("seqgroup_data_block", AddDataBlock(resource, BlockType.ASEQ, animations.SequenceGroup));

            return embeddedAnimation;
        }

        private static int AddDataBlock(Resource resource, BlockType type, KVObject data)
            => resource.AddBlock(new KeyValuesOrNTRO(type, string.Empty) { Resource = resource, Data = data });

        private static KVObject CreateData(
            string name,
            IReadOnlyList<ModelMeshSource> meshes,
            IReadOnlyList<ModelBone> bones,
            IReadOnlyList<ModelMaterialGroup> materialGroups,
            IReadOnlyList<string> meshGroups,
            ulong defaultMeshGroupMask,
            KVObject? extra)
        {
            var refMeshes = KVObject.Array();
            var meshGroupMasks = KVObject.Array();
            var lodGroupMasks = KVObject.Array();
            var remappingTable = KVObject.Array();
            var remappingTableStarts = KVObject.Array();

            foreach (var mesh in meshes)
            {
                refMeshes.Add(CreateResourceReference(string.Empty));
                meshGroupMasks.Add(mesh.MeshGroupMask);
                lodGroupMasks.Add(mesh.LodMask);
                remappingTableStarts.Add(remappingTable.Count);

                foreach (var bone in mesh.BoneRemap)
                {
                    remappingTable.Add(bone);
                }
            }

            var meshGroupNames = KVObject.Array();

            foreach (var meshGroup in meshGroups)
            {
                meshGroupNames.Add(meshGroup);
            }

            var groups = KVObject.Array();

            foreach (var materialGroup in materialGroups)
            {
                var materials = KVObject.Array();

                foreach (var material in materialGroup.Materials)
                {
                    materials.Add(CreateResourceReference(material));
                }

                var group = KVObject.Collection();
                group.Add("m_name", materialGroup.Name);
                group.Add("m_materials", materials);
                groups.Add(group);
            }

            var data = KVObject.Collection();
            data.Add("m_name", name);
            data.Add("m_modelInfo", KVObject.Collection());
            data.Add("m_ExtParts", KVObject.Array());
            data.Add("m_refMeshes", refMeshes);
            data.Add("m_refMeshGroupMasks", meshGroupMasks);
            data.Add("m_refPhysGroupMasks", KVObject.Array());
            data.Add("m_refLODGroupMasks", lodGroupMasks);
            data.Add("m_lodGroupSwitchDistances", KVObject.Array());
            data.Add("m_refPhysicsData", KVObject.Array());
            data.Add("m_refPhysicsHitboxData", KVObject.Array());
            data.Add("m_refAnimGroups", KVObject.Array());
            data.Add("m_refSequenceGroups", KVObject.Array());
            data.Add("m_meshGroups", meshGroupNames);
            data.Add("m_materialGroups", groups);
            data.Add("m_nDefaultMeshGroupMask", defaultMeshGroupMask);
            data.Add("m_modelSkeleton", CreateSkeletonData(bones));
            data.Add("m_remappingTable", remappingTable);
            data.Add("m_remappingTableStarts", remappingTableStarts);

            Merge(data, extra);

            return data;
        }

        private static KVObject CreateSkeletonData(IReadOnlyList<ModelBone> bones)
        {
            var names = KVObject.Array();
            var parents = KVObject.Array();
            var flags = KVObject.Array();
            var positions = KVObject.Array();
            var rotations = KVObject.Array();
            var scales = KVObject.Array();

            foreach (var bone in bones)
            {
                names.Add(bone.Name);
                parents.Add(bone.ParentIndex);
                flags.Add((int)bone.Flags);
                positions.Add(ToKVArray(bone.Position));
                rotations.Add(ToKVArray(bone.Rotation));
                scales.Add(bone.Scale);
            }

            var skeleton = KVObject.Collection();
            skeleton.Add("m_boneName", names);
            skeleton.Add("m_nParent", parents);
            skeleton.Add("m_nFlag", flags);
            skeleton.Add("m_bonePosParent", positions);
            skeleton.Add("m_boneRotParent", rotations);
            skeleton.Add("m_boneScaleParent", scales);

            return skeleton;
        }

        internal static KVObject CreateResourceReference(string path) => new(path) { Flag = KVFlag.Resource };

        private static void Merge(KVObject target, KVObject? extra)
        {
            if (extra == null)
            {
                return;
            }

            foreach (var (key, value) in extra)
            {
                target[key] = value;
            }
        }
    }
}
