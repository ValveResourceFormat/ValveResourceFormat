using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Utils;
using S2Model = ValveResourceFormat.ResourceTypes.Model;

namespace ValveResourceFormat.Source1.Model;

/// <summary>Converts a Source 1 model to an in-memory Source 2 model with one embedded mesh per sub-model and level of detail.</summary>
internal static class Source1ModelConverter
{
    private const int MaxMeshGroups = 64;

    // Attachment flag that keeps the attachment from turning with its bone
    private const int WorldAlignAttachmentFlag = 0x10000;

    private const string LightingOriginAttachmentName = "lighting_origin";

    // The bone the renderer moves to make the eyes look at a point, and how far in front of the eyes it rests
    private const string EyeTargetBoneName = "eye_target";
    private const string EyeBoneName = "eyeball";
    private const float EyeTargetDistance = 100f;

    // A Source 1 jiggle bone points along the Z axis of its bone with its left along X and its up along Y, and a Source 2 one
    // points along X with its left along Y and its up along Z. The converted jiggle bone has the Z, X and Y axes of its
    // Source 1 frame as its X, Y and Z axes, and this rotation takes positions in the converted frame to the Source 1 one.
    private static readonly Quaternion JiggleFrameToSource1 = new(-0.5f, -0.5f, -0.5f, 0.5f);

    [StructLayout(LayoutKind.Sequential)]
    private struct ConvertedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Vector2 TexCoord;
        public uint BoneIndices;
        public uint BoneWeights;
    }

    private static readonly VertexField[] VertexFields =
    [
        new(nameof(ConvertedVertex.Position), "POSITION", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(ConvertedVertex.Normal), "NORMAL", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(ConvertedVertex.Tangent), "TANGENT", 0, DXGI_FORMAT.R32G32B32A32_FLOAT),
        new(nameof(ConvertedVertex.TexCoord), "TEXCOORD", 0, DXGI_FORMAT.R32G32_FLOAT),
        new(nameof(ConvertedVertex.BoneIndices), "BLENDINDICES", 0, DXGI_FORMAT.R8G8B8A8_UINT),
        new(nameof(ConvertedVertex.BoneWeights), "BLENDWEIGHT", 0, DXGI_FORMAT.R8G8B8A8_UNORM),
    ];

    /// <summary>Converts a model.</summary>
    /// <param name="source">The model to convert.</param>
    /// <param name="name">Name of the resulting model.</param>
    /// <param name="fileSystem">The files of the game, to pick between the search paths of a material and to read its shader.</param>
    /// <param name="modelMaterials">Receives the materials of the eyes and teeth of the model, which the model names. Without it they are drawn with the material of the game.</param>
    internal static S2Model Convert(Source1Model source, string name, Source1FileSystem? fileSystem, Source1ModelMaterials? modelMaterials)
    {
        var modelName = Source1Paths.ToModelPath(name);
        var bindToRest = GetBindToRest(source);
        var eyeTarget = FindEyeTarget(source, bindToRest);
        var materialPaths = new ModelMaterialPaths(source, ResolveMaterialPaths(source, path => fileSystem?.Exists(path) ?? false), bindToRest, eyeTarget, fileSystem, modelMaterials);
        var flexConverter = new Source1FlexConverter(source, eyeTarget != null);
        var meshGroupNames = new List<string>();
        var meshNames = new ModelNameSet();
        var meshes = new List<ModelMeshSource>();
        var sourceMeshes = new List<Source1Mesh>();
        var defaultMask = 0UL;

        for (var partIndex = 0; partIndex < source.BodyParts.Length; partIndex++)
        {
            var part = source.BodyParts[partIndex];

            for (var subIndex = 0; subIndex < part.Models.Length; subIndex++)
            {
                var subModel = part.Models[subIndex];
                var groupIndex = meshGroupNames.Count;
                var choiceName = Path.GetFileNameWithoutExtension(subModel.Name);
                var groupName = string.Create(CultureInfo.InvariantCulture, $"{part.Name}_@{subIndex}_#&{choiceName}");
                meshGroupNames.Add(groupName);

                var inGroups = groupIndex < MaxMeshGroups;
                var groupBit = inGroups ? 1UL << groupIndex : 0UL;

                if (subIndex == 0)
                {
                    defaultMask |= groupBit;
                }

                for (var lod = 0; lod < subModel.Lods.Length; lod++)
                {
                    var lodMeshes = subModel.Lods[lod];

                    if (lodMeshes.Length == 0)
                    {
                        continue;
                    }

                    var meshName = meshNames.Create(part.Name, partIndex, string.Create(CultureInfo.InvariantCulture, $"_{subIndex}_lod{lod}"));

                    meshes.Add(CreateMesh(source, meshName, lodMeshes, groupBit, bindToRest, eyeTarget, materialPaths, flexConverter));
                    sourceMeshes.AddRange(lodMeshes);
                }
            }
        }

        var modelData = KVObject.Collection();
        modelData.Add("m_lodGroupSwitchDistances", CreateSwitchDistances(source));
        modelData.Add("m_attachments", CreateAttachments(source));
        modelData.Add("m_hitboxsets", CreateHitboxSets(source));
        modelData.Add("m_boneFlexDrivers", CreateBoneFlexDrivers(source));

        if (eyeTarget != null && source.Eyelids.Length > 0)
        {
            modelData.Add("m_modelInfo", CreateModelInfo());
        }

        var bones = CreateBones(source, bindToRest, eyeTarget);
        var materialGroups = CreateMaterialGroups(source, sourceMeshes, materialPaths);
        var animations = Source1AnimationBaker.Bake(source);
#pragma warning disable CA2000 // The model keeps its resource, which holds no unmanaged state
        var model = (S2Model)S2Model.Create(modelName, meshes, bones, materialGroups, meshGroupNames, defaultMask, modelData, animations, CreateEmbeddedPhysics(source)).DataBlock!;
#pragma warning restore CA2000
        model.Resource.FileName = modelName;
        Source1ResourceReferences.Update(model.Resource);

        return model;
    }

    private static ModelMeshSource CreateMesh(
        Source1Model source,
        string name,
        Source1Mesh[] lodMeshes,
        ulong groupMask,
        Matrix4x4 bindToRest,
        Source1EyeTarget? eyeTarget,
        ModelMaterialPaths materialPaths,
        Source1FlexConverter flexConverter)
    {
        var vertexBuffers = new List<ConvertedVertex[]>();
        var indices = new List<uint>();
        var drawCalls = new List<MeshDrawCall>();
        var baseVertices = new List<int>();
        var vertexCount = 0;
        var boneCount = 0;

        // The meshes of a level of detail have a vertex buffer each and share one index buffer, each drawn from its own range
        foreach (var sourceMesh in lodMeshes)
        {
            var materialPath = materialPaths.Get(sourceMesh, 0);
            var drawCall = new MeshDrawCall(materialPath, 0, sourceMesh.Vertices.Length, indices.Count, sourceMesh.Indices.Length)
            {
                VertexBufferIndex = vertexBuffers.Count,
            };

            drawCalls.Add(drawCall);
            baseVertices.Add(vertexCount);
            vertexBuffers.Add(CreateVertices(sourceMesh, bindToRest));
            vertexCount += sourceMesh.Vertices.Length;
            foreach (var index in sourceMesh.Indices)
            {
                indices.Add(index);
            }

            boneCount = Math.Max(boneCount, GetBoneCount(source, sourceMesh));

            // The shader of an eyeball skins the origin of the eye by its bone, and the view target by the target bone
            if (sourceMesh.Eyeball is { } eyeball)
            {
                boneCount = Math.Max(boneCount, Math.Min(eyeball.Bone + 1, source.Bones.Length));

                if (eyeTarget is { } target)
                {
                    boneCount = Math.Max(boneCount, target.Index + 1);
                }
            }
        }

        var (min, max) = Source1VertexBuffers.GetBounds(vertexBuffers, vertex => vertex.Position);

        var restToBind = Matrix4x4.Invert(bindToRest, out var inverse) ? inverse : Matrix4x4.Identity;
        var meshBones = new List<MeshBone>(boneCount);
        var boneRemap = new int[boneCount];

        for (var i = 0; i < boneCount; i++)
        {
            boneRemap[i] = i;

            if (eyeTarget is { } target && i == target.Index)
            {
                meshBones.Add(new MeshBone(EyeTargetBoneName, source.Bones[target.Parent].ExportName, Matrix4x4.CreateTranslation(-target.Position)));
                continue;
            }

            var bone = source.Bones[i];
            var parentName = (uint)bone.ParentIndex < (uint)source.Bones.Length ? source.Bones[bone.ParentIndex].ExportName : string.Empty;

            meshBones.Add(new MeshBone(bone.ExportName, parentName, restToBind * GetPoseToBone(source, i)));
        }

        var buffers = Source1VertexBuffers.Create(vertexBuffers, VertexFields, indices.ToArray());

        return new ModelMeshSource(name, buffers, drawCalls, min, max)
        {
            MeshGroupMask = groupMask,
            LodMask = 1L << lodMeshes[0].Lod,
            Bones = meshBones,
            BoneRemap = boneRemap,
            Morph = flexConverter.CreateMorph(lodMeshes, baseVertices, vertexCount, bindToRest),
        };
    }

    /// <summary>
    /// Gets the rotation from the space the vertices and PoseToBone are in to the space the sequences pose the bones in.
    /// The two differ by a rotation of the whole model, which leaves the reference pose lying down unless the vertices are rotated with it.
    /// It is taken from the root bone in the first frame of the "ref" sequence, or of the first sequence that can be played.
    /// </summary>
    internal static Matrix4x4 GetBindToRest(Source1Model source)
    {
        if (source.Bones.Length == 0)
        {
            return Matrix4x4.Identity;
        }

        var restSequence = FindRestSequence(source);

        if (restSequence == null)
        {
            return Matrix4x4.Identity;
        }

        var positions = new Vector3[source.Bones.Length];
        var rotations = new Quaternion[source.Bones.Length];
        var boneToModel = new Matrix4x4[source.Bones.Length];

        try
        {
            restSequence.Blends[0].GetFrame(0, positions, rotations);
        }
        catch (InvalidDataException)
        {
            return Matrix4x4.Identity;
        }

        source.BuildBoneTransforms(positions, rotations, boneToModel);

        var bindToRest = source.Bones[0].PoseToBone * boneToModel[0];

        // Only the orientation is wanted, the model stays where its vertices are
        bindToRest.Translation = Vector3.Zero;

        return bindToRest;
    }

    /// <summary>Gets the rotation from the frame of a converted bone to the frame of its Source 1 bone.</summary>
    /// <param name="source">The model the bone belongs to.</param>
    /// <param name="bone">Index of the bone, which is -1 for the parent of a root bone.</param>
    internal static Quaternion GetFrameToSource1(Source1Model source, int bone)
    {
        if ((uint)bone < (uint)source.Bones.Length && source.Bones[bone].Jiggle != null)
        {
            return JiggleFrameToSource1;
        }

        return Quaternion.Identity;
    }

    /// <summary>Gets the transform from the space of a Source 1 bone to the space of its converted bone.</summary>
    internal static Matrix4x4 GetSource1ToFrame(Source1Model source, int bone)
        => Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(GetFrameToSource1(source, bone)));

    /// <summary>Gets the transform from the space the vertices are bound in to the space of a converted bone.</summary>
    internal static Matrix4x4 GetPoseToBone(Source1Model source, int bone)
        => source.Bones[bone].PoseToBone * GetSource1ToFrame(source, bone);

    /// <summary>Carries the local transforms of every bone in a pose from the frames of the Source 1 bones to the converted ones.</summary>
    /// <param name="source">The model the pose belongs to.</param>
    /// <param name="positions">Position of every bone relative to its parent.</param>
    /// <param name="rotations">Rotation of every bone relative to its parent.</param>
    /// <param name="isDelta">Whether the pose is a delta, whose rotations are applied in the frame of their bone.</param>
    internal static void ToBoneFrames(Source1Model source, Span<Vector3> positions, Span<Quaternion> rotations, bool isDelta)
    {
        for (var i = 0; i < source.Bones.Length; i++)
        {
            var frameToSource1 = GetFrameToSource1(source, i);
            var parentToSource1 = GetFrameToSource1(source, source.Bones[i].ParentIndex);

            if (frameToSource1.IsIdentity && parentToSource1.IsIdentity)
            {
                continue;
            }

            var source1ToParent = Quaternion.Conjugate(parentToSource1);
            var source1ToFrame = Quaternion.Conjugate(frameToSource1);

            positions[i] = Vector3.Transform(positions[i], source1ToParent);
            rotations[i] = isDelta
                ? source1ToFrame * rotations[i] * frameToSource1
                : source1ToParent * rotations[i] * frameToSource1;
        }
    }

    private static Source1Sequence? FindRestSequence(Source1Model source)
    {
        Source1Sequence? firstPlayable = null;

        foreach (var sequence in source.Sequences)
        {
            if (sequence.IsDelta || !sequence.IsPlayable)
            {
                continue;
            }

            if (string.Equals(sequence.Name, "ref", StringComparison.OrdinalIgnoreCase))
            {
                return sequence;
            }

            firstPlayable ??= sequence;
        }

        return firstPlayable;
    }

    private static string[] ResolveMaterialPaths(Source1Model source, Func<string, bool> materialExists)
    {
        var paths = new string[source.MaterialNames.Length];

        for (var i = 0; i < paths.Length; i++)
        {
            string? resolved = null;

            foreach (var path in source.GetMaterialPaths(i))
            {
                resolved ??= path;

                if (materialExists(path))
                {
                    resolved = path;
                    break;
                }
            }

            paths[i] = resolved ?? $"materials/{source.MaterialNames[i]}.vmt";
        }

        return paths;
    }

    private static int GetBoneCount(Source1Model source, Source1Mesh mesh)
    {
        var highest = 0;

        foreach (var vertex in mesh.Vertices)
        {
            highest = Math.Max(highest, vertex.Bone0);

            if (vertex.NumBones > 1)
            {
                highest = Math.Max(highest, vertex.Bone1);
            }

            if (vertex.NumBones > 2)
            {
                highest = Math.Max(highest, vertex.Bone2);
            }
        }

        return Math.Min(highest + 1, source.Bones.Length);
    }

    private static int GetMaterialIndex(Source1Model source, Source1Mesh mesh, int skin)
    {
        var families = source.SkinFamilies;

        if (skin < families.Length && mesh.SkinRefIndex < families[skin].Length)
        {
            return families[skin][mesh.SkinRefIndex];
        }

        return mesh.MaterialIndex;
    }

    private static KVObject CreateSwitchDistances(Source1Model source)
    {
        var switchDistances = KVObject.Array();

        foreach (var switchPoint in source.LodSwitchPoints)
        {
            switchDistances.Add(switchPoint);
        }

        return switchDistances;
    }

    // The eyelids follow the elevation of the view target above the eye: the angle from the up axis of the eye bone
    // is 90 degrees when level, and each constraint covers one direction of the full quarter turn.
    private static KVObject CreateModelInfo()
    {
        var constraints = KVObject.Array();
        constraints.Add(CreateEyeLookConstraint(Source1FlexConverter.EyeLookDownController, 90f, 180f));
        constraints.Add(CreateEyeLookConstraint(Source1FlexConverter.EyeLookUpController, 90f, 0f));

        var keyValues = KVObject.Collection();
        keyValues.Add("BoneConstraintList", constraints);

        var modelInfo = KVObject.Collection();
        modelInfo.Add("m_keyValueText", keyValues.ToKV3String());

        return modelInfo;
    }

    private static KVObject CreateEyeLookConstraint(string morphName, float inputMin, float inputMax)
    {
        var remap = KVObject.Array();
        remap.Add(inputMin);
        remap.Add(inputMax);
        remap.Add(0f);
        remap.Add(1f);

        var constraint = KVObject.Collection();
        constraint.Add("_class", "CBoneConstraintDotToMorph");
        constraint.Add("m_sBoneName", EyeBoneName);
        constraint.Add("m_sTargetBoneName", EyeTargetBoneName);
        constraint.Add("m_sMorphChannelName", morphName);
        constraint.Add("m_flRemap", remap);

        return constraint;
    }

    private static KVObject CreateBoneFlexDrivers(Source1Model source)
    {
        var drivers = KVObject.Array();

        foreach (var driver in source.BoneFlexDrivers)
        {
            if ((uint)driver.Bone >= (uint)source.Bones.Length)
            {
                continue;
            }

            var boneName = source.Bones[driver.Bone].ExportName;
            var controls = KVObject.Array();

            foreach (var control in driver.Controls)
            {
                var isComponent = (uint)control.Component <= 2;
                var isController = (uint)control.FlexController < (uint)source.FlexControllers.Length;

                if (!isComponent || !isController)
                {
                    continue;
                }

                var controllerName = source.FlexControllers[control.FlexController].Name;
                var controlData = KVObject.Collection();
                controlData.Add("m_nBoneComponent", control.Component);
                controlData.Add("m_flexController", controllerName);
                controlData.Add("m_flexControllerToken", StringToken.Get(controllerName));
                controlData.Add("m_flMin", control.Min);
                controlData.Add("m_flMax", control.Max);
                controls.Add(controlData);
            }

            var driverData = KVObject.Collection();
            driverData.Add("m_boneName", boneName);
            driverData.Add("m_boneNameToken", StringToken.Get(boneName));
            driverData.Add("m_controls", controls);
            drivers.Add(driverData);
        }

        return drivers;
    }

    private static KVObject CreateAttachments(Source1Model source)
    {
        var attachments = KVObject.Array();
        var names = new HashSet<string>();

        foreach (var sourceAttachment in source.Attachments)
        {
            if ((uint)sourceAttachment.BoneIndex >= (uint)source.Bones.Length || !names.Add(sourceAttachment.Name))
            {
                continue;
            }

            var ignoreRotation = (sourceAttachment.Flags & WorldAlignAttachmentFlag) != 0;
            var local = GetAttachmentLocal(source, sourceAttachment);

            attachments.Add(CreateAttachment(sourceAttachment.Name, source.Bones[sourceAttachment.BoneIndex].ExportName, local.Translation, local, ignoreRotation));
        }

        if (source.Bones.Length > 0 && names.Add(LightingOriginAttachmentName))
        {
            attachments.Add(CreateLightingOriginAttachment(source));
        }

        return attachments;
    }

    // The collision of the model is the parts of the embedded physics. A jiggle bone is a control node of its soft body,
    // named after the bone it moves.
    private static KVObject? CreateEmbeddedPhysics(Source1Model source)
    {
        var nodeNames = KVObject.Array();
        var jiggleBones = KVObject.Array();

        foreach (var bone in source.Bones)
        {
            if (bone.Jiggle is not { } jiggle)
            {
                continue;
            }

            jiggleBones.Add(CreateJiggleBone(nodeNames.Count, jiggle));
            nodeNames.Add(bone.ExportName);
        }

        var physics = Source1ModelPhysicsConverter.Convert(source);

        if (jiggleBones.Count == 0)
        {
            return physics;
        }

        physics ??= CreateEmptyPhysics();

        var feModel = KVObject.Collection();
        feModel.Add("m_nNodeCount", nodeNames.Count);
        feModel.Add("m_CtrlName", nodeNames);
        feModel.Add("m_JiggleBones", jiggleBones);
        physics.Add("m_pFeModel", feModel);

        return physics;
    }

    private static KVObject CreateEmptyPhysics()
    {
        var physics = KVObject.Collection();
        physics.Add("m_nFlags", 0);
        physics.Add("m_bindPose", KVObject.Array());
        physics.Add("m_parts", KVObject.Array());
        physics.Add("m_joints", KVObject.Array());
        physics.Add("m_boneNames", KVObject.Array());
        physics.Add("m_boneParents", KVObject.Array());
        physics.Add("m_surfacePropertyHashes", KVObject.Array());
        physics.Add("m_collisionAttributes", KVObject.Array());

        return physics;
    }

    private static KVObject CreateJiggleBone(int node, FeModel.FeJiggleBone jiggle)
    {
        var data = KVObject.Collection();
        data.Add("m_nFlags", (int)jiggle.Flags);
        data.Add("m_flLength", jiggle.Length);
        data.Add("m_flTipMass", jiggle.TipMass);
        data.Add("m_flYawStiffness", jiggle.YawStiffness);
        data.Add("m_flYawDamping", jiggle.YawDamping);
        data.Add("m_flPitchStiffness", jiggle.PitchStiffness);
        data.Add("m_flPitchDamping", jiggle.PitchDamping);
        data.Add("m_flAlongStiffness", jiggle.AlongStiffness);
        data.Add("m_flAlongDamping", jiggle.AlongDamping);
        data.Add("m_flAngleLimit", jiggle.AngleLimit);
        data.Add("m_flMinYaw", jiggle.MinYaw);
        data.Add("m_flMaxYaw", jiggle.MaxYaw);
        data.Add("m_flYawFriction", jiggle.YawFriction);
        data.Add("m_flYawBounce", jiggle.YawBounce);
        data.Add("m_flMinPitch", jiggle.MinPitch);
        data.Add("m_flMaxPitch", jiggle.MaxPitch);
        data.Add("m_flPitchFriction", jiggle.PitchFriction);
        data.Add("m_flPitchBounce", jiggle.PitchBounce);
        data.Add("m_flBaseMass", jiggle.BaseMass);
        data.Add("m_flBaseStiffness", jiggle.BaseStiffness);
        data.Add("m_flBaseDamping", jiggle.BaseDamping);
        data.Add("m_flBaseMinLeft", jiggle.BaseMinLeft);
        data.Add("m_flBaseMaxLeft", jiggle.BaseMaxLeft);
        data.Add("m_flBaseLeftFriction", jiggle.BaseLeftFriction);
        data.Add("m_flBaseMinUp", jiggle.BaseMinUp);
        data.Add("m_flBaseMaxUp", jiggle.BaseMaxUp);
        data.Add("m_flBaseUpFriction", jiggle.BaseUpFriction);
        data.Add("m_flBaseMinForward", jiggle.BaseMinForward);
        data.Add("m_flBaseMaxForward", jiggle.BaseMaxForward);
        data.Add("m_flBaseForwardFriction", jiggle.BaseForwardFriction);
        data.Add("m_flRadius0", jiggle.Radius0);
        data.Add("m_flRadius1", jiggle.Radius1);
        data.Add("m_vPoint0", KVHelpers.ToKVArray(jiggle.Point0));
        data.Add("m_vPoint1", KVHelpers.ToKVArray(jiggle.Point1));
        data.Add("m_nCollisionMask", jiggle.CollisionMask);

        var indexed = KVObject.Collection();
        indexed.Add("m_nNode", node);
        indexed.Add("m_nJiggleParent", (long)uint.MaxValue);
        indexed.Add("m_jiggleBone", data);

        return indexed;
    }

    // The renderer lights a model at its lighting origin attachment. The model is lit at its illumination position, which
    // is relative to an attachment of the model when it names one, and to the model otherwise.
    private static KVObject CreateLightingOriginAttachment(Source1Model source)
    {
        var illumination = source.IlluminationPosition;
        var attachmentIndex = source.IlluminationAttachmentIndex - 1;

        if ((uint)attachmentIndex < (uint)source.Attachments.Length && (uint)source.Attachments[attachmentIndex].BoneIndex < (uint)source.Bones.Length)
        {
            var sourceAttachment = source.Attachments[attachmentIndex];
            var local = GetAttachmentLocal(source, sourceAttachment);
            var offset = Vector3.Transform(illumination, local);

            return CreateAttachment(LightingOriginAttachmentName, source.Bones[sourceAttachment.BoneIndex].ExportName, offset, local, false);
        }

        return CreateAttachment(LightingOriginAttachmentName, source.Bones[0].ExportName, Vector3.Transform(illumination, GetPoseToBone(source, 0)), Matrix4x4.Identity, false);
    }

    private static Matrix4x4 GetAttachmentLocal(Source1Model source, Source1Attachment attachment)
        => attachment.Local * GetSource1ToFrame(source, attachment.BoneIndex);

    private static KVObject CreateAttachment(string name, string boneName, Vector3 offset, Matrix4x4 rotation, bool ignoreRotation)
    {
        var attachment = KVObject.Collection();
        attachment.Add("m_name", name);
        attachment.Add("m_bIgnoreRotation", ignoreRotation);
        attachment.Add("m_nInfluences", 1);
        attachment.Add("m_influenceNames", KVHelpers.MakeArray(boneName));
        attachment.Add("m_vInfluenceRotations", KVHelpers.MakeArray(KVHelpers.ToKVArray(Quaternion.CreateFromRotationMatrix(rotation))));
        attachment.Add("m_vInfluenceOffsets", KVHelpers.MakeArray(KVHelpers.ToKVArray(offset)));
        attachment.Add("m_influenceWeights", KVHelpers.MakeArray(1f));
        attachment.Add("m_bInfluenceRootTransform", KVHelpers.MakeArray(false));

        return attachment;
    }

    private static KVObject CreateHitboxSets(Source1Model source)
    {
        var sets = KVObject.Array();
        var names = new HashSet<string>();

        foreach (var sourceSet in source.HitboxSets)
        {
            if (!names.Add(sourceSet.Name))
            {
                continue;
            }

            var hitboxes = KVObject.Array();

            foreach (var sourceHitbox in sourceSet.Hitboxes)
            {
                if ((uint)sourceHitbox.BoneIndex >= (uint)source.Bones.Length)
                {
                    continue;
                }

                var bone = source.Bones[sourceHitbox.BoneIndex];

                // The frame of a converted bone only swaps the axes of the Source 1 one, so the box stays aligned to it
                var source1ToFrame = GetSource1ToFrame(source, sourceHitbox.BoneIndex);
                var min = Vector3.Transform(sourceHitbox.Min, source1ToFrame);
                var max = Vector3.Transform(sourceHitbox.Max, source1ToFrame);

                var hitbox = KVObject.Collection();
                hitbox.Add("m_name", sourceHitbox.Name);
                hitbox.Add("m_sSurfaceProperty", bone.SurfaceProperty);
                hitbox.Add("m_sBoneName", bone.ExportName);
                hitbox.Add("m_vMinBounds", KVHelpers.ToKVArray(Vector3.Min(min, max)));
                hitbox.Add("m_vMaxBounds", KVHelpers.ToKVArray(Vector3.Max(min, max)));
                hitbox.Add("m_flShapeRadius", 0f);
                hitbox.Add("m_nGroupId", sourceHitbox.Group);
                hitbox.Add("m_nShapeType", (int)Hitbox.HitboxShape.Box);
                hitbox.Add("m_bTranslationOnly", false);

                hitboxes.Add(hitbox);
            }

            var set = KVObject.Collection();
            set.Add("m_name", sourceSet.Name);
            set.Add("m_hitboxes", hitboxes);
            sets.Add(set);
        }

        return sets;
    }

    /// <summary>
    /// Places the view target of the eyes of a model in front of the eyes, on the bone of the first eyeball.
    /// It is <see langword="null"/> for a model without eyeballs.
    /// </summary>
    private static Source1EyeTarget? FindEyeTarget(Source1Model source, Matrix4x4 bindToRest)
    {
        var parent = -1;
        var origin = Vector3.Zero;
        var forward = Vector3.Zero;
        var up = Vector3.Zero;
        var count = 0;

        foreach (var part in source.BodyParts)
        {
            foreach (var subModel in part.Models)
            {
                foreach (var lod in subModel.Lods)
                {
                    foreach (var mesh in lod)
                    {
                        if (mesh.Eyeball is not { } eyeball || (uint)eyeball.Bone >= (uint)source.Bones.Length)
                        {
                            continue;
                        }

                        var boneToBind = GetBoneToBind(source, eyeball.Bone, bindToRest);

                        if (parent == -1)
                        {
                            parent = eyeball.Bone;
                        }

                        origin += Vector3.Transform(eyeball.Origin, boneToBind);
                        forward += Vector3.Normalize(Vector3.TransformNormal(eyeball.Forward, boneToBind));
                        up += Vector3.Normalize(Vector3.TransformNormal(eyeball.Up, boneToBind));
                        count++;
                    }
                }
            }
        }

        if (count == 0)
        {
            return null;
        }

        var center = origin / count;
        var forwardAxis = Vector3.Normalize(forward);
        var upAxis = Vector3.Normalize(up - (forwardAxis * Vector3.Dot(up, forwardAxis)));
        var leftAxis = Vector3.Cross(upAxis, forwardAxis);

        var eyeToBind = new Matrix4x4(
            forwardAxis.X, forwardAxis.Y, forwardAxis.Z, 0f,
            leftAxis.X, leftAxis.Y, leftAxis.Z, 0f,
            upAxis.X, upAxis.Y, upAxis.Z, 0f,
            center.X, center.Y, center.Z, 1f);

        var position = center + (forwardAxis * EyeTargetDistance);

        return new Source1EyeTarget(source.Bones.Length, parent, position, eyeToBind);
    }

    /// <summary>Gets the transform from the space of a converted bone to the space the converted vertices are in.</summary>
    internal static Matrix4x4 GetBoneToModel(Source1Model source, int bone, Matrix4x4 bindToRest)
    {
        var boneToPose = Matrix4x4.Invert(GetPoseToBone(source, bone), out var inverse) ? inverse : Matrix4x4.Identity;

        return boneToPose * bindToRest;
    }

    // Takes a position in the space of a Source 1 bone to the space the converted vertices are in
    private static Matrix4x4 GetBoneToBind(Source1Model source, int bone, Matrix4x4 bindToRest)
    {
        var boneToPose = Matrix4x4.Invert(source.Bones[bone].PoseToBone, out var inverse) ? inverse : Matrix4x4.Identity;

        return boneToPose * bindToRest;
    }

    private static List<ModelBone> CreateBones(Source1Model source, Matrix4x4 bindToRest, Source1EyeTarget? eyeTarget)
    {
        var bones = new List<ModelBone>(source.Bones.Length + 1);
        var flexDriverBones = new HashSet<int>();

        foreach (var driver in source.BoneFlexDrivers)
        {
            flexDriverBones.Add(driver.Bone);
        }

        // The vertices are bound to the inverse of PoseToBone, and carried into rest space by the same rotation.
        // The local rest pose in the bone data is not in rest space, so it is derived from the bind transforms.
        var bindToModel = new Matrix4x4[source.Bones.Length];

        for (var i = 0; i < bindToModel.Length; i++)
        {
            bindToModel[i] = GetBoneToModel(source, i, bindToRest);
        }

        for (var i = 0; i < source.Bones.Length; i++)
        {
            var bone = source.Bones[i];
            var local = (uint)bone.ParentIndex < (uint)i ? RelativeTo(bindToModel[i], bindToModel[bone.ParentIndex]) : bindToModel[i];

            Matrix4x4.Decompose(local, out _, out var rotation, out var translation);

            var flags = flexDriverBones.Contains(i) ? ModelSkeletonBoneFlags.BoneFlexDriver : ModelSkeletonBoneFlags.NoBoneFlags;
            bones.Add(new ModelBone(bone.ExportName, bone.ParentIndex, translation, rotation, 1f, flags));
        }

        if (eyeTarget is { } target)
        {
            var parentBind = bindToModel[target.Parent];
            var targetLocal = RelativeTo(Matrix4x4.CreateTranslation(target.Position), parentBind);
            var eyeLocal = RelativeTo(target.EyeToBind, parentBind);

            Matrix4x4.Decompose(targetLocal, out _, out var targetRotation, out var targetTranslation);
            Matrix4x4.Decompose(eyeLocal, out _, out var eyeRotation, out var eyeTranslation);

            bones.Add(new ModelBone(EyeTargetBoneName, target.Parent, targetTranslation, targetRotation, 1f));
            bones.Add(new ModelBone(EyeBoneName, target.Parent, eyeTranslation, eyeRotation, 1f));
        }

        return bones;
    }

    // Takes a transform in model space to the space of a parent, which it stays in when the parent cannot be inverted
    private static Matrix4x4 RelativeTo(Matrix4x4 transform, Matrix4x4 parent)
        => Matrix4x4.Invert(parent, out var parentInverse) ? transform * parentInverse : transform;

    private static List<ModelMaterialGroup> CreateMaterialGroups(Source1Model source, List<Source1Mesh> meshes, ModelMaterialPaths materialPaths)
    {
        var defaultMaterials = new List<string>();
        var firstMeshOfMaterial = new Dictionary<string, Source1Mesh>(StringComparer.OrdinalIgnoreCase);

        foreach (var mesh in meshes)
        {
            var path = materialPaths.Get(mesh, 0);

            if (firstMeshOfMaterial.TryAdd(path, mesh))
            {
                defaultMaterials.Add(path);
            }
        }

        var skinCount = Math.Max(1, source.SkinFamilies.Length);
        var groups = new List<ModelMaterialGroup>(skinCount);

        for (var skin = 0; skin < skinCount; skin++)
        {
            var materials = new List<string>(defaultMaterials.Count);

            foreach (var defaultMaterial in defaultMaterials)
            {
                var mesh = firstMeshOfMaterial[defaultMaterial];
                materials.Add(materialPaths.Get(mesh, skin));
            }

            groups.Add(new ModelMaterialGroup(GetSkinName(skin), materials));
        }

        return groups;
    }

    // The path of the material of a mesh. The material of an eyeball or of teeth is a copy with a name of its own, which
    // carries what the model knows about the eyeball or the mouth to its shader.
    private sealed class ModelMaterialPaths(Source1Model source, string[] paths, Matrix4x4 bindToRest, Source1EyeTarget? eyeTarget, Source1FileSystem? fileSystem, Source1ModelMaterials? modelMaterials)
    {
        private readonly Dictionary<string, bool> isTeeth = new(StringComparer.OrdinalIgnoreCase);

        public string Get(Source1Mesh mesh, int skin)
        {
            var index = GetMaterialIndex(source, mesh, skin);

            if ((uint)index >= (uint)paths.Length)
            {
                return string.Empty;
            }

            var path = GetMaterialPath(mesh, paths[index]);

            if ((source.Flags & (int)MdlFile.StudioFlags.AmbientBoost) != 0)
            {
                return Source1Paths.AddVariant(path, Source1ModelMaterials.AmbientBoostVariant);
            }

            return path;
        }

        private string GetMaterialPath(Source1Mesh mesh, string gamePath)
        {
            var path = Source1Paths.ToMaterialPath(gamePath);

            if (modelMaterials == null)
            {
                return path;
            }

            if (mesh.Eyeball is { } eyeball && eyeTarget is { } target && (uint)eyeball.Bone < (uint)source.Bones.Length)
            {
                return modelMaterials.AddEyeball(path, ToBindSpace(eyeball), target);
            }

            if (source.Mouth is { } mouth && (uint)mouth.Bone < (uint)source.Bones.Length && IsTeeth(gamePath))
            {
                return modelMaterials.AddMouth(path, mouth with { Forward = ToBindDirection(mouth.Bone, mouth.Forward) });
            }

            return path;
        }

        private Source1Eyeball ToBindSpace(Source1Eyeball eyeball)
        {
            var bind = GetBoneToBind(source, eyeball.Bone, bindToRest);

            return eyeball with
            {
                Origin = Vector3.Transform(eyeball.Origin, bind),
                Up = Vector3.Normalize(Vector3.TransformNormal(eyeball.Up, bind)),
                Forward = Vector3.Normalize(Vector3.TransformNormal(eyeball.Forward, bind)),
            };
        }

        private Vector3 ToBindDirection(int bone, Vector3 direction)
        {
            return Vector3.Normalize(Vector3.TransformNormal(direction, GetBoneToBind(source, bone, bindToRest)));
        }

        private bool IsTeeth(string path)
        {
            if (fileSystem == null)
            {
                return false;
            }

            if (isTeeth.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var teeth = false;

            try
            {
                teeth = VmtFile.Load(path, fileSystem.OpenFile).NormalizedShaderName == "teeth";
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
            }

            isTeeth[path] = teeth;

            return teeth;
        }
    }

    private static ConvertedVertex[] CreateVertices(Source1Mesh mesh, Matrix4x4 bindToRest)
    {
        var vertices = new ConvertedVertex[mesh.Vertices.Length];

        for (var i = 0; i < vertices.Length; i++)
        {
            var source = mesh.Vertices[i];
            var tangent = Vector3.TransformNormal(new Vector3(source.Tangent.X, source.Tangent.Y, source.Tangent.Z), bindToRest);

            vertices[i] = new ConvertedVertex
            {
                Position = Vector3.Transform(source.Position, bindToRest),
                Normal = Vector3.TransformNormal(source.Normal, bindToRest),
                Tangent = new Vector4(tangent, -source.Tangent.W),
                TexCoord = source.TexCoord,
                BoneIndices = PackBoneIndices(source),
                BoneWeights = PackBoneWeights(source),
            };
        }

        return vertices;
    }

    private static uint PackBoneIndices(in Source1Vertex vertex)
    {
        var bone1 = vertex.NumBones > 1 ? vertex.Bone1 : (byte)0;
        var bone2 = vertex.NumBones > 2 ? vertex.Bone2 : (byte)0;

        return vertex.Bone0 | ((uint)bone1 << 8) | ((uint)bone2 << 16);
    }

    private static uint PackBoneWeights(in Source1Vertex vertex)
    {
        if (vertex.NumBones <= 1)
        {
            return 255;
        }

        var weight1 = Source1VertexBuffers.ToUnorm8(vertex.Weight1);
        var weight2 = vertex.NumBones > 2 ? Source1VertexBuffers.ToUnorm8(vertex.Weight2) : (byte)0;

        // The first weight takes what the others leave so the weights keep summing to one
        var weight0 = (byte)Math.Max(255 - weight1 - weight2, 0);

        return weight0 | ((uint)weight1 << 8) | ((uint)weight2 << 16);
    }

    /// <summary>Gets the name of the material group that a converted model has for a skin family.</summary>
    internal static string GetSkinName(int skin) => string.Create(CultureInfo.InvariantCulture, $"skin{skin}");

    // A skin index the model does not have shows the first skin family
    internal static string GetSkinName(Source1Model source, int skin)
    {
        var skinCount = Math.Max(1, source.SkinFamilies.Length);
        var index = (uint)skin < (uint)skinCount ? skin : 0;

        return GetSkinName(index);
    }
}
