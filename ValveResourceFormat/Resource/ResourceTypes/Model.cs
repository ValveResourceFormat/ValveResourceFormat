using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpGLTF.Schema2;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;
using Animation = ValveResourceFormat.ResourceTypes.ModelAnimation.Animation;

namespace ValveResourceFormat.ResourceTypes
{
    public class Model : KeyValuesOrNTRO
    {
        private List<Animation> CachedAnimations;

        public Skeleton GetSkeleton(int meshIndex)
        {
            return Skeleton.FromModelData(Data, meshIndex);
        }

        public IEnumerable<(int MeshIndex, string MeshName, long LoDMask)> GetReferenceMeshNamesAndLoD()
        {
            var refLODGroupMasks = Data.GetIntegerArray("m_refLODGroupMasks");
            var refMeshes = Data.GetArray<string>("m_refMeshes");
            var result = new List<(int MeshIndex, string MeshName, long LoDMask)>();

            for (var meshIndex = 0; meshIndex < refMeshes.Length; meshIndex++)
            {
                var refMesh = refMeshes[meshIndex];

                if (refMesh == null)
                {
                    continue;
                }

                result.Add((meshIndex, refMesh, refLODGroupMasks[meshIndex]));
            }

            return result;
        }

        public IEnumerable<(Mesh Mesh, long LoDMask)> GetEmbeddedMeshesAndLoD()
            => GetEmbeddedMeshes().Zip(Data.GetIntegerArray("m_refLODGroupMasks"), (l, r) => (l, r));

        public IEnumerable<Mesh> GetEmbeddedMeshes()
        {
            var meshes = new List<Mesh>();

            if (Resource.ContainsBlockType(BlockType.CTRL))
            {
                var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
                var embeddedMeshes = ctrl.Data.GetArray("embedded_meshes");

                if (embeddedMeshes == null)
                {
                    return meshes;
                }

                foreach (var embeddedMesh in embeddedMeshes)
                {
                    var name = embeddedMesh.GetStringProperty("name");
                    var meshIndex = (int)embeddedMesh.GetIntegerProperty("mesh_index");
                    var dataBlockIndex = (int)embeddedMesh.GetIntegerProperty("data_block");
                    var vbibBlockIndex = (int)embeddedMesh.GetIntegerProperty("vbib_block");

                    meshes.Add(new Mesh(meshIndex, name, Resource.GetBlockByIndex(dataBlockIndex) as ResourceData, Resource.GetBlockByIndex(vbibBlockIndex) as VBIB));
                }
            }

            return meshes;
        }

        public PhysAggregateData GetEmbeddedPhys()
        {
            if (!Resource.ContainsBlockType(BlockType.CTRL))
            {
                return null;
            }

            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedPhys = ctrl.Data.GetSubCollection("embedded_physics");

            if (embeddedPhys == null)
            {
                return null;
            }

            var physBlockIndex = (int)embeddedPhys.GetIntegerProperty("phys_data_block");
            return (PhysAggregateData)Resource.GetBlockByIndex(physBlockIndex);
        }

        public IEnumerable<string> GetReferencedPhysNames()
            => Data.GetArray<string>("m_refPhysicsData");

        public IEnumerable<string> GetReferencedAnimationGroupNames()
            => Data.GetArray<string>("m_refAnimGroups");

        public IEnumerable<Animation> GetEmbeddedAnimations()
        {
            var embeddedAnimations = new List<Animation>();

            if (!Resource.ContainsBlockType(BlockType.CTRL))
            {
                return embeddedAnimations;
            }

            var ctrl = Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedAnimation = ctrl.Data.GetSubCollection("embedded_animation");

            if (embeddedAnimation == null)
            {
                return embeddedAnimations;
            }

            var groupDataBlockIndex = (int)embeddedAnimation.GetIntegerProperty("group_data_block");
            var animDataBlockIndex = (int)embeddedAnimation.GetIntegerProperty("anim_data_block");

            var animationGroup = Resource.GetBlockByIndex(groupDataBlockIndex) as KeyValuesOrNTRO;
            var decodeKey = animationGroup.Data.GetSubCollection("m_decodeKey");

            var animationDataBlock = Resource.GetBlockByIndex(animDataBlockIndex) as KeyValuesOrNTRO;

            return Animation.FromData(animationDataBlock.Data, decodeKey);
        }

        public IEnumerable<Animation> GetAllAnimations(IFileLoader fileLoader)
        {
            if (CachedAnimations != null)
            {
                return CachedAnimations;
            }

            var animGroupPaths = GetReferencedAnimationGroupNames();
            var animations = GetEmbeddedAnimations().ToList();

            // Load animations from referenced animation groups
            foreach (var animGroupPath in animGroupPaths)
            {
                var animGroup = fileLoader.LoadFile(animGroupPath + "_c");
                if (animGroup != default)
                {
                    animations.AddRange(AnimationGroupLoader.LoadAnimationGroup(animGroup, fileLoader));
                }
            }

            CachedAnimations = animations.ToList();

            return CachedAnimations;
        }

        public IEnumerable<string> GetMeshGroups()
            => Data.GetArray<string>("m_meshGroups");

        public IEnumerable<string> GetMaterialGroups()
           => Data.GetArray<IKeyValueCollection>("m_materialGroups").Select(group => group.GetProperty<string>("m_name"));

        public IEnumerable<string> GetDefaultMeshGroups()
        {
            var defaultGroupMask = Data.GetUnsignedIntegerProperty("m_nDefaultMeshGroupMask");

            return GetMeshGroups().Where((group, index) => ((ulong)(1 << index) & defaultGroupMask) != 0);
        }

        public IEnumerable<bool> GetActiveMeshMaskForGroup(string groupName)
        {
            var groupIndex = GetMeshGroups().ToList().IndexOf(groupName);
            var meshGroupMasks = Data.GetIntegerArray("m_refMeshGroupMasks");
            if (groupIndex >= 0)
            {
                return meshGroupMasks.Select(mask => (mask & 1 << groupIndex) != 0);
            }
            else
            {
                return meshGroupMasks.Select(_ => false);
            }
        }

        public void GenerateMorphTargets(IFileLoader fileLoader, ModelRoot exportModel)
        {
            if (!Resource.ContainsBlockType(BlockType.MRPH))
            {
                return;
            }

            var mrph = Resource.GetBlockByType(BlockType.MRPH) as KeyValuesOrNTRO;
            if (mrph == null)
            {
                return;
            }

            SharpGLTF.Schema2.Mesh mesh = null;
            if (exportModel.LogicalMeshes.Count > 0)
            {
                mesh = exportModel.LogicalMeshes[0];
            }

            if (mesh == null)
            {
                return;
            }

            //load vmorf atlas
            var atlasPath = mrph.Data.GetProperty<string>("m_pTextureAtlas") + "_c";
            var textureResource = fileLoader.LoadFile(atlasPath);
            if (textureResource == null)
            {
                Console.WriteLine($"morph atlas not found: {atlasPath}");
                return;
            }

            var morphDatas = mrph.Data.GetSubCollection("m_morphDatas");
            var width = mrph.Data.GetInt32Property("m_nWidth");
            var height = mrph.Data.GetInt32Property("m_nHeight");
            var bundleTypes = mrph.Data.GetSubCollection("m_bundleTypes");

            var texture = (ResourceTypes.Texture) textureResource.DataBlock;
            var texWidth = texture.Width;
            var texHeight = texture.Height;
            var skiaBitmap = texture.GenerateBitmap();

            int morphIndex = 0;
            foreach (var pair in morphDatas)
            {
                var morphData = pair.Value as IKeyValueCollection;
                if (morphData == null)
                {
                    continue;
                }

                var morphName = morphData.GetProperty<string>("m_name");
                Vector3[,] rectData = new Vector3[height, width];
                for (int i = 0; i < height; i++)
                {
                    for (int j = 0; j < width; j++)
                    {
                        rectData[i,j] = Vector3.Zero;
                    }
                }

                var morphRectDatas = morphData.GetSubCollection("m_morphRectDatas");
                foreach (var morphRectData in morphRectDatas)
                {
                    var rect = morphRectData.Value as IKeyValueCollection;
                    var xLeftDst= rect.GetInt32Property("m_nXLeftDst");
                    var yTopDst = rect.GetInt32Property("m_nYTopDst");
                    var rectWidth = (int)Math.Round(rect.GetFloatProperty("m_flUWidthSrc") * texWidth, 0);
                    var rectHeight = (int)Math.Round(rect.GetFloatProperty("m_flVHeightSrc") * texHeight, 0);
                    var bundleDatas = rect.GetSubCollection("m_bundleDatas");
                    foreach (var bundleData in bundleDatas)
                    {
                        //TODO: Just for MORPH_BUNDLE_TYPE_POSITION_SPEED temporary, need improve in the future.
                        if (bundleTypes.GetStringProperty(bundleData.Key) != "MORPH_BUNDLE_TYPE_POSITION_SPEED")
                        {
                            continue;
                        }

                        var bundle = bundleData.Value as IKeyValueCollection;
                        var rectU = (int)Math.Round(bundle.GetFloatProperty("m_flULeftSrc") * texWidth, 0);
                        var rectV = (int)Math.Round(bundle.GetFloatProperty("m_flVTopSrc") * texHeight, 0);
                        var ranges = bundle.GetFloatArray("m_ranges");
                        var offsets = bundle.GetFloatArray("m_offsets");

                        for (var row = rectV; row < rectV + rectHeight; row++)
                        {
                            for (var col = rectU; col < rectU + rectWidth; col++)
                            {
                                var color = skiaBitmap.GetPixel(col, row);
                                var dstI = row - rectV + yTopDst;
                                var dstJ = col - rectU + xLeftDst;
                                rectData[dstI, dstJ] = new Vector3(
                                    color.Red / 255f * ranges[0] + offsets[0],
                                    color.Green / 255f * ranges[1] + offsets[1],
                                    color.Blue / 255f * ranges[2] + offsets[2]
                                    );
                            }
                        }
                    }
                }

                List<byte> flexData = new List<byte>();
                for (int i = 0; i < height; i++)
                {
                    for (int j = 0; j < width; j++)
                    {
                        flexData.AddRange(BitConverter.GetBytes(rectData[i,j].X));
                        flexData.AddRange(BitConverter.GetBytes(rectData[i,j].Y));
                        flexData.AddRange(BitConverter.GetBytes(rectData[i,j].Z));
                    }
                }

                AddMorphToModel(morphName, flexData, mesh, exportModel, morphIndex++);
            }
        }

        private static void AddMorphToModel(string morphName, List<byte> flexData, SharpGLTF.Schema2.Mesh mesh, ModelRoot model, int morphIndex)
        {
            if (mesh.Primitives.Count <= 0)
            {
                return;
            }
            var primitive = mesh.Primitives[0];
            var dict = new Dictionary<string, Accessor>();
            {
                var acc = model.CreateAccessor();
                acc.Name = morphName;

                var buff = model.UseBufferView(flexData.ToArray(), 0, flexData.Count);
                acc.SetData(buff, 0, flexData.Count / 12, DimensionType.VEC3, EncodingType.FLOAT, false);
                dict.Add("POSITION", acc);
            }
            primitive.SetMorphTargetAccessors(morphIndex, dict);
        }
    }
}
