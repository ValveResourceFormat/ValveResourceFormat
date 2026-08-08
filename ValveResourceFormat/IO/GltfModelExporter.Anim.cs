using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using SharpGLTF.Schema2;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using VAnim = ValveResourceFormat.ResourceTypes.ModelAnimation.Animation;
using VMesh = ValveResourceFormat.ResourceTypes.Mesh;
using VModel = ValveResourceFormat.ResourceTypes.Model;

namespace ValveResourceFormat.IO;

public partial class GltfModelExporter
{
    /// <summary>
    /// Manages the writing of skeletal animation data to glTF format.
    /// </summary>
    public class AnimationWriter
    {
        Skeleton Skeleton { get; init; }
        Frame Frame { get; init; }
        FlexController[] FlexControllers { get; init; }
        int BoneCount => Frame.Bones.Length;

        AnimationChannelWriter<Quaternion> RotationWriter;
        AnimationChannelWriter<Vector3> PositionWriter;
        AnimationChannelWriter<Vector3> ScaleWriter;

        readonly Vector3[] localPositions;
        readonly Quaternion[] localRotations;
        readonly Vector3[] localScales;

        /// <summary>
        /// Gets whether additive animations are composed over the bind pose rather than written as delta tracks.
        /// </summary>
        public bool ComposeAdditive { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationWriter"/> class.
        /// </summary>
        public AnimationWriter(Skeleton skeleton, FlexController[] flexControllers)
        {
            Skeleton = skeleton;
            Frame = new(skeleton, flexControllers);
            FlexControllers = flexControllers;

            RotationWriter = AnimationChannelWriter<Quaternion>.Create(BoneCount);
            PositionWriter = AnimationChannelWriter<Vector3>.Create(BoneCount);
            ScaleWriter = AnimationChannelWriter<Vector3>.Create(BoneCount);

            localPositions = new Vector3[BoneCount];
            localRotations = new Quaternion[BoneCount];
            localScales = new Vector3[BoneCount];
        }

        /// <summary>
        /// Writes a skeletal animation to the glTF model. Entries in <paramref name="joints"/> may be
        /// null when an animation targets a skeleton with bones the exported model does not have
        /// (e.g. animation graph clips retargeted by bone name); those bones are skipped.
        /// </summary>
        public void WriteAnimation(ModelRoot model, Node?[] joints, VAnim animation, string? animationName = null)
        {
            WriteCore(model, joints, animation, animationName, retargeter: null);
        }

        /// <summary>
        /// Writes an animation authored on another skeleton, retargeted onto the model by world pose.
        /// Retargeting matches world poses, so an additive animation is composed over the source
        /// skeleton's bind pose either way, and taken back to a delta over the model's bind pose when
        /// deltas are wanted.
        /// </summary>
        public void WriteRetargetedAnimation(ModelRoot model, Node?[] joints, VAnim animation, string animationName, SkeletonRetargeter retargeter)
        {
            if (!retargeter.HasMappedBones)
            {
                return;
            }

            WriteCore(model, joints, animation, animationName, retargeter);
        }

        /// <summary>
        /// Writes the animation's channels: per frame, the per-bone local transforms are produced
        /// (decoded directly, or through <paramref name="retargeter"/> when given), then scale
        /// sanitizing, cloth pinning, root motion, delta conversion and coordinate baking are applied
        /// and the glTF channels emitted.
        /// </summary>
        private void WriteCore(ModelRoot model, Node?[] joints, VAnim animation, string? animationName, SkeletonRetargeter? retargeter)
        {
            Debug.Assert(joints.Length == BoneCount);

            var writeDeltas = animation.IsAdditive && !ComposeAdditive;

            Frame? clipFrame = null;
            Matrix4x4[]? modelWorld = null;
            Matrix4x4[]? inverseWorld = null;

            if (retargeter != null)
            {
                clipFrame = new Frame(retargeter.SourceSkeleton, FlexControllers);
                modelWorld = new Matrix4x4[BoneCount];
                inverseWorld = new Matrix4x4[BoneCount];
            }
            else if (writeDeltas)
            {
                Frame.ClearToIdentity();
            }
            else
            {
                Frame.Clear(Skeleton);
            }

            RotationWriter.Clear();
            PositionWriter.Clear();
            ScaleWriter.Clear();

            var outputAnimation = model.UseAnimation(animationName ?? animation.Name);
            WriteAdditiveExtras(outputAnimation, animation, ComposeAdditive);

            var fps = animation.Fps;

            // Some models have fps of 0.000, which will make time a NaN
            if (fps <= 0)
            {
                fps = 1f;
            }

            // root motion is stored separately from bone frames, so bake it into the root bone(s) to keep
            // the skeleton from animating in place. horizontal travel and yaw only. the engine doesn't
            // apply a vertical movement track to the body.
            var applyRootMotion = animation.HasMovementData() && !writeDeltas;

            // No cloth solver here, so mirror the renderer (AnimationController.GetSkinningMatrices):
            // pin each cloth root to the cloth anchor bone instead of writing its raw, solver-less clip data.
            var clothAnchor = writeDeltas ? null : Skeleton.ClothSimulationRoot;
            var anchorInverseBindPose = Matrix4x4.Identity;
            if (clothAnchor != null)
            {
                var anchorBindPose = Matrix4x4.Identity;
                for (var b = clothAnchor; b != null; b = b.Parent)
                {
                    anchorBindPose *= b.BindPose;
                }

                if (!Matrix4x4.Invert(anchorBindPose, out anchorInverseBindPose))
                {
                    anchorInverseBindPose = Matrix4x4.Identity;
                }
            }

            for (var f = 0; f < animation.FrameCount; f++)
            {
                if (retargeter != null)
                {
                    DecodeRetargetedFrameLocals(animation, retargeter, f, clipFrame!, modelWorld!, inverseWorld!);
                }
                else
                {
                    DecodeFrameLocals(animation, f, writeDeltas);
                }

                for (var boneID = 0; boneID < BoneCount; boneID++)
                {
                    var s = localScales[boneID];
                    localScales[boneID] = new Vector3(SanitizeScale(s.X), SanitizeScale(s.Y), SanitizeScale(s.Z));
                }

                var time = f / fps;
                var prevFrameTime = (f - 1) / fps;

                var rootMotion = Matrix4x4.Identity;
                if (applyRootMotion)
                {
                    var movement = animation.GetMovementOffsetData(f);
                    // Legacy movement data is planar; NM clip root motion natively carries vertical travel.
                    var movementPosition = animation is ClipAnimation
                        ? movement.Position
                        : new Vector3(movement.Position.X, movement.Position.Y, 0f);
                    rootMotion = Matrix4x4.CreateRotationZ(float.DegreesToRadians(movement.Angle))
                        * Matrix4x4.CreateTranslation(movementPosition);
                }

                // Anchor skinning matrix this frame (renderer's modelBones[clothSimRoot]).
                var clothSkinning = Matrix4x4.Identity;
                if (clothAnchor != null)
                {
                    var anchorPose = Matrix4x4.Identity;
                    for (var b = clothAnchor; b != null; b = b.Parent)
                    {
                        anchorPose *= Matrix4x4.CreateScale(localScales[b.Index])
                            * Matrix4x4.CreateFromQuaternion(localRotations[b.Index])
                            * Matrix4x4.CreateTranslation(localPositions[b.Index]);
                    }

                    clothSkinning = anchorInverseBindPose * anchorPose;
                }

                for (var boneID = 0; boneID < BoneCount; boneID++)
                {
                    var position = localPositions[boneID];
                    var rotation = localRotations[boneID];
                    var scale = localScales[boneID];

                    var bone = Skeleton.Bones[boneID];

                    if (clothAnchor != null && bone.Parent == null && bone.IsProceduralCloth)
                    {
                        // Pin to the anchor; cloth bones are roots, so apply root motion too.
                        var local = bone.BindPose * clothSkinning;
                        if (applyRootMotion)
                        {
                            local *= rootMotion;
                        }

                        if (Matrix4x4.Decompose(local, out var clothS, out var clothR, out var clothT))
                        {
                            position = clothT;
                            rotation = clothR;
                            scale = clothS;
                        }
                    }
                    else if (applyRootMotion && bone.Parent == null)
                    {
                        var local = Matrix4x4.CreateScale(scale)
                            * Matrix4x4.CreateFromQuaternion(rotation)
                            * Matrix4x4.CreateTranslation(position);

                        if (Matrix4x4.Decompose(local * rootMotion, out var s, out var r, out var t))
                        {
                            position = t;
                            rotation = r;
                            scale = s;
                        }
                        else
                        {
                            position += rootMotion.Translation;
                        }
                    }

                    if (writeDeltas && retargeter != null)
                    {
                        position -= bone.Position;
                        rotation = Quaternion.Conjugate(bone.Angle) * rotation;
                        scale = Vector3.One;
                    }

                    (position, rotation) = writeDeltas
                        ? BakeAdditiveDeltaConversion(position, rotation, bone.Parent == null)
                        : BakeConversion(position, rotation, bone.Parent == null);

                    RotationWriter.SubmitKeyframe(boneID, time, prevFrameTime, rotation);
                    PositionWriter.SubmitKeyframe(boneID, time, prevFrameTime, position);
                    ScaleWriter.SubmitKeyframe(boneID, time, prevFrameTime, scale);
                }
            }

            for (var boneID = 0; boneID < BoneCount; boneID++)
            {
                if (animation.FrameCount == 0)
                {
                    var bone = Skeleton.Bones[boneID];
                    var (restPosition, restRotation) = writeDeltas
                        ? (Vector3.Zero, Quaternion.Identity)
                        : BakeConversion(bone.Position, bone.Angle, bone.Parent == null);
                    RotationWriter.Channels[boneID].Add(0f, restRotation);
                    PositionWriter.Channels[boneID].Add(0f, restPosition);
                    ScaleWriter.Channels[boneID].Add(0f, Vector3.One);
                }

                var jointNode = joints[boneID];
                if (jointNode == null)
                {
                    continue;
                }

                outputAnimation.CreateRotationChannel(jointNode, RotationWriter.Channels[boneID], true);
                outputAnimation.CreateTranslationChannel(jointNode, PositionWriter.Channels[boneID], true);
                outputAnimation.CreateScaleChannel(jointNode, ScaleWriter.Channels[boneID], true);
            }
        }

        /// <summary>
        /// Fills the local transform buffers with the frame decoded straight onto the model skeleton.
        /// When deltas are written, the decoded frame already contains them, and scale stays at one.
        /// </summary>
        private void DecodeFrameLocals(VAnim animation, int frameIndex, bool writeDeltas)
        {
            Frame.FrameIndex = frameIndex;
            animation.DecodeFrame(Frame);

            if (animation.IsAdditive && !writeDeltas)
            {
                animation.ComposeAdditiveOverBindPose(Frame.Bones, Skeleton);
            }

            for (var boneID = 0; boneID < BoneCount; boneID++)
            {
                var boneFrame = Frame.Bones[boneID];
                localPositions[boneID] = boneFrame.Position;
                localRotations[boneID] = boneFrame.Angle;
                localScales[boneID] = writeDeltas ? Vector3.One : new Vector3(boneFrame.Scale);
            }
        }

        /// <summary>
        /// Fills the local transform buffers by decoding the frame on the source skeleton, retargeting
        /// the world pose onto the model skeleton and converting back to per-bone locals. Additive
        /// frames are always composed first, since retargeting matches absolute world poses.
        /// </summary>
        private void DecodeRetargetedFrameLocals(VAnim animation, SkeletonRetargeter retargeter, int frameIndex, Frame clipFrame, Matrix4x4[] modelWorld, Matrix4x4[] inverseWorld)
        {
            clipFrame.FrameIndex = frameIndex;
            animation.DecodeFrame(clipFrame);

            if (animation.IsAdditive)
            {
                animation.ComposeAdditiveOverBindPose(clipFrame.Bones, retargeter.SourceSkeleton);
            }

            retargeter.Retarget(clipFrame, modelWorld);

            for (var boneID = 0; boneID < BoneCount; boneID++)
            {
                if (!Matrix4x4.Invert(modelWorld[boneID], out inverseWorld[boneID]))
                {
                    inverseWorld[boneID] = Matrix4x4.Identity;
                }
            }

            for (var boneID = 0; boneID < BoneCount; boneID++)
            {
                var bone = Skeleton.Bones[boneID];
                var inverseParent = bone.Parent != null ? inverseWorld[bone.Parent.Index] : Matrix4x4.Identity;
                var local = modelWorld[boneID] * inverseParent;

                if (!Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation))
                {
                    scale = Vector3.One;
                    rotation = Quaternion.Identity;
                    translation = local.Translation;
                }

                localPositions[boneID] = translation;
                localRotations[boneID] = rotation;
                localScales[boneID] = scale;
            }
        }

        // See https://github.com/ValveResourceFormat/ValveResourceFormat/issues/527 (NaN)
        // and https://github.com/ValveResourceFormat/ValveResourceFormat/issues/570 (inf)
        private static float SanitizeScale(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    /// <summary>
    /// Writes animations authored on another skeleton, retargeted onto the model by world pose.
    /// One retargeter serves every clip targeting the same skeleton.
    /// </summary>
    private void WriteAnimationGraphClips(ModelRoot exportedModel, Scene scene, VModel model, Node?[] joints, HashSet<string> animationFilter)
    {
        var clipRetargeters = new Dictionary<string, SkeletonRetargeter?>();

        // Secondary animations (the clip's tracks for further skeletons, e.g. the weapon of a
        // viewmodel clip) get their own skeleton nodes and writer, created once per skeleton and shared.
        var secondarySkeletons = new Dictionary<string, (AnimationWriter Writer, Node[] Joints)?>();

        (AnimationWriter Writer, Node[] Joints)? GetOrCreateSecondarySkeleton(string skeletonName)
        {
            if (secondarySkeletons.TryGetValue(skeletonName, out var cached))
            {
                return cached;
            }

            (AnimationWriter Writer, Node[] Joints)? created = null;
            if (Skeleton.FromSkeletonResource(FileLoader, skeletonName) is { } skeleton)
            {
                var (skeletonNode, secondaryJoints) = CreateGltfSkeleton(scene, skeleton, skeletonName);
                if (skeletonNode != null && secondaryJoints != null)
                {
                    var meshNode = CreateSkeletonVisualizationMesh(exportedModel, scene, skeleton, secondaryJoints);
                    meshNode.Name = $"{skeletonName}.empty_mesh_reference";
                    var writer = new AnimationWriter(skeleton, []) { ComposeAdditive = ComposeAdditiveAnimations };
                    created = (writer, secondaryJoints);
                }
            }

            secondarySkeletons[skeletonName] = created;
            return created;
        }

        // UseAnimation is find-or-create by name, so a clip sharing a name with an already-written
        // animation (embedded, or an earlier clip) would merge its channels onto it. Keep the first, skip the rest.
        var writtenNames = exportedModel.LogicalAnimations.Select(a => a.Name).ToHashSet();

        var retargetWriter = new AnimationWriter(model.Skeleton, model.FlexControllers) { ComposeAdditive = ComposeAdditiveAnimations };

        foreach (var animation in model.GetAllAnimations(FileLoader))
        {
            CancellationToken.ThrowIfCancellationRequested();

            if (animation is not ClipAnimation clipAnimation)
            {
                continue;
            }

            var targetSkeletonName = clipAnimation.TargetSkeletonName;
            var animationName = ClipAnimationName(clipAnimation.Name);

            if (!IncludeAnimation(animationFilter, animationName))
            {
                continue;
            }

            if (writtenNames.Contains(animationName))
            {
                ProgressReporter?.Report($"Skipping animation graph clip '{animationName}': an animation with that name was already exported.");
                continue;
            }

            if (!clipRetargeters.TryGetValue(targetSkeletonName, out var retargeter))
            {
                var clipSkeleton = Skeleton.FromSkeletonResource(FileLoader, targetSkeletonName);
                retargeter = clipSkeleton != null ? new SkeletonRetargeter(model.Skeleton, clipSkeleton) : null;
                clipRetargeters[targetSkeletonName] = retargeter;
            }

            if (retargeter != null)
            {
                retargetWriter.WriteRetargetedAnimation(exportedModel, joints, clipAnimation, animationName, retargeter);

                foreach (var secondaryClip in clipAnimation.Clip.SecondaryAnimations)
                {
                    if (GetOrCreateSecondarySkeleton(secondaryClip.SkeletonName) is { } secondary)
                    {
                        secondary.Writer.WriteAnimation(exportedModel, secondary.Joints, new ClipAnimation(secondaryClip), animationName);
                    }
                }

                writtenNames.Add(animationName);
            }
        }
    }

    // glTF holds all animations in one flat named list, so AG2 clips are labelled by their resource
    // path with the .vnmclip extension stripped (the path keeps them unique across clip folders).
    private static string ClipAnimationName(string clipName) => Path.ChangeExtension(clipName, null)!;

    /// <summary>
    /// Flags an additive animation in its glTF extras. glTF has no notion of additive animation, so an
    /// uncomposed one carries per-bone deltas the consumer composes onto the bind pose itself: add the
    /// translation, post-multiply the rotation. Scale is not part of the delta and stays at one.
    /// </summary>
    private static void WriteAdditiveExtras(SharpGLTF.Schema2.Animation outputAnimation, VAnim animation, bool composed)
    {
        if (!animation.IsAdditive)
        {
            return;
        }

        outputAnimation.Extras = new JsonObject
        {
            ["additive"] = true,
            ["additive_base"] = "bindpose",
            ["additive_composed"] = composed,
        };
    }

    /// <summary>
    /// Writes morph-target weight animation. glTF has no flex controllers, so each mesh's flex rules
    /// are evaluated per frame from the animation's controller values (Frame.Datas) and the resulting
    /// morph weights are baked.
    /// </summary>
    private void WriteMorphAnimations(ModelRoot exportedModel, VModel model, List<(Node Node, VMesh Mesh)> morphedMeshes, HashSet<string> animationFilter)
    {
        var meshTargets = new List<(Node Node, List<int> MorphFlexIds, Dictionary<int, FlexRule> RuleByFlexId)>();

        foreach (var (node, mesh) in morphedMeshes)
        {
            var morph = mesh.MorphData!;
            var descriptors = morph.GetFlexDescriptors();
            var flexData = morph.GetFlexVertexData();

            // The mesh's glTF morph targets are the flex descriptors that have flex data, in descriptor order.
            var morphFlexIds = new List<int>();
            for (var d = 0; d < descriptors.Count; d++)
            {
                if (flexData.ContainsKey(descriptors[d]))
                {
                    morphFlexIds.Add(d);
                }
            }

            if (morphFlexIds.Count == 0)
            {
                continue;
            }

            var ruleByFlexId = new Dictionary<int, FlexRule>();
            foreach (var rule in morph.FlexRules)
            {
                ruleByFlexId[rule.FlexID] = rule;
            }

            meshTargets.Add((node, morphFlexIds, ruleByFlexId));
        }

        if (meshTargets.Count == 0)
        {
            return;
        }

        var frame = new Frame(model.Skeleton, model.FlexControllers);

        foreach (var animation in model.GetAllAnimations(FileLoader))
        {
            if (animation is ClipAnimation || animation.FrameCount == 0 || !animation.HasFlexData || !IncludeAnimation(animationFilter, animation.Name))
            {
                continue;
            }

            // The frame is shared across animations; each animation only writes the flex channels it animates.
            frame.Clear(model.Skeleton);

            var fps = animation.Fps <= 0f ? 1f : animation.Fps;
            var keyframes = new Dictionary<float, float[]>[meshTargets.Count];
            var anyWeight = new bool[meshTargets.Count];
            for (var t = 0; t < meshTargets.Count; t++)
            {
                keyframes[t] = [];
            }

            for (var f = 0; f < animation.FrameCount; f++)
            {
                frame.FrameIndex = f;
                animation.DecodeFrame(frame);

                for (var t = 0; t < meshTargets.Count; t++)
                {
                    var (_, morphFlexIds, ruleByFlexId) = meshTargets[t];
                    var weights = new float[morphFlexIds.Count];
                    for (var i = 0; i < morphFlexIds.Count; i++)
                    {
                        if (ruleByFlexId.TryGetValue(morphFlexIds[i], out var rule))
                        {
                            weights[i] = rule.Evaluate(frame.Datas);
                            anyWeight[t] |= weights[i] != 0f;
                        }
                    }

                    keyframes[t][f / fps] = weights;
                }
            }

            for (var t = 0; t < meshTargets.Count; t++)
            {
                if (anyWeight[t])
                {
                    exportedModel.UseAnimation(animation.Name).CreateMorphChannel(meshTargets[t].Node, keyframes[t], meshTargets[t].MorphFlexIds.Count);
                }
            }
        }
    }

    record struct AnimationChannelWriter<T>(Dictionary<float, T>[] Channels, T?[] LastValue, bool[] ValueOmmited) where T : struct
    {
        public static AnimationChannelWriter<T> Create(int boneCount) => new()
        {
            Channels = [.. Enumerable.Range(0, boneCount).Select(_ => new Dictionary<float, T>())],
            LastValue = new T?[boneCount],
            ValueOmmited = new bool[boneCount],
        };

        public readonly void SubmitKeyframe(int boneID, float time, float prevTime, T value)
        {
            var lastValue = LastValue[boneID];

            if (lastValue != null && lastValue.Value.Equals(value))
            {
                ValueOmmited[boneID] = true;
                return;
            }

            if (lastValue != null && ValueOmmited[boneID])
            {
                ValueOmmited[boneID] = false;

                // Restore keyframe before current frame, as otherwise interpolation will
                // begin from the first instance of identical frame, and not from previous frame
                Channels[boneID].Add(prevTime, lastValue.Value);
            }

            Channels[boneID].Add(time, value);
            LastValue[boneID] = value;
        }

        public readonly void Clear()
        {
            for (var i = 0; i < Channels.Length; i++)
            {
                Channels[i].Clear();
                LastValue[i] = default; // null
                ValueOmmited[i] = false;
            }
        }
    }
}
