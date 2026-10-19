using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using SharpGLTF.Schema2;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using VAnim = ValveResourceFormat.ResourceTypes.ModelAnimation.AnimationBase;
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
        /// Gets whether keys that linear interpolation of the kept keys reproduces within tolerance are
        /// dropped. When false, every frame is written except the inner keys of runs of repeated values.
        /// </summary>
        public bool ReduceKeyframes { get; init; } = true;

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationWriter"/> class.
        /// </summary>
        public AnimationWriter(Skeleton skeleton, FlexController[] flexControllers)
        {
            Skeleton = skeleton;
            Frame = new(skeleton, flexControllers);
            FlexControllers = flexControllers;

            RotationWriter = AnimationChannelWriter<Quaternion>.Create(BoneCount, RotationTolerance, RotationError);
            PositionWriter = AnimationChannelWriter<Vector3>.Create(BoneCount, TranslationTolerance, VectorError);
            ScaleWriter = AnimationChannelWriter<Vector3>.Create(BoneCount, ScaleTolerance, VectorError);

            localPositions = new Vector3[BoneCount];
            localRotations = new Quaternion[BoneCount];
            localScales = new Vector3[BoneCount];
        }

        /// <summary>
        /// Writes a skeletal animation to the glTF model. Null joints, for bones the model lacks
        /// (e.g. animation graph clips retargeted by name), are skipped.
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

            var outputAnimation = UseAnimation(model, animationName ?? animation.Name);
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

                    RotationWriter.SubmitKeyframe(boneID, time, rotation);
                    PositionWriter.SubmitKeyframe(boneID, time, position);
                    ScaleWriter.SubmitKeyframe(boneID, time, scale);
                }
            }

            var lastFrameTime = (animation.FrameCount - 1) / fps;
            var isFirstJoint = true;

            for (var boneID = 0; boneID < BoneCount; boneID++)
            {
                if (animation.FrameCount == 0)
                {
                    var bone = Skeleton.Bones[boneID];
                    var (restPosition, restRotation) = writeDeltas
                        ? (Vector3.Zero, Quaternion.Identity)
                        : BakeConversion(bone.Position, bone.Angle, bone.Parent == null);
                    RotationWriter.SubmitKeyframe(boneID, 0f, restRotation);
                    PositionWriter.SubmitKeyframe(boneID, 0f, restPosition);
                    ScaleWriter.SubmitKeyframe(boneID, 0f, Vector3.One);
                }

                var jointNode = joints[boneID];
                if (jointNode == null)
                {
                    continue;
                }

                var sampledRotations = RotationWriter.Values[boneID];
                AlignQuaternionHemispheres(sampledRotations);

                var rotations = RotationWriter.GetKeyframes(boneID, ReduceKeyframes);

                // glTF and Blender take a clip's length from its keys, and a channel reduced to a single
                // key no longer spans the clip, so the first joint's rotation always keeps the last frame.
                if (isFirstJoint && rotations.Count == 1 && animation.FrameCount > 1)
                {
                    rotations.Add(lastFrameTime, sampledRotations[^1]);
                }

                isFirstJoint = false;

                outputAnimation.CreateRotationChannel(jointNode, rotations, true);
                outputAnimation.CreateTranslationChannel(jointNode, PositionWriter.GetKeyframes(boneID, ReduceKeyframes), true);

                // Bones have no rest scale, so a missing channel already means a scale of one.
                if (!IsConstantOne(ScaleWriter.Values[boneID]))
                {
                    outputAnimation.CreateScaleChannel(jointNode, ScaleWriter.GetKeyframes(boneID, ReduceKeyframes), true);
                }
            }
        }

        // Scale recovered from matrix products (retargeting, root motion, pinned cloth) carries
        // float rounding of around 1e-6 on bones that do not scale at all.
        private const float ScaleOneTolerance = 1e-5f;

        private static bool IsConstantOne(List<Vector3> values)
        {
            foreach (var value in values)
            {
                if (Vector3.Distance(value, Vector3.One) > ScaleOneTolerance)
                {
                    return false;
                }
            }

            return true;
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
            => float.IsFinite(value) ? value : 0f;
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
                var skeletonNodeName = Path.GetFileNameWithoutExtension(skeletonName);
                var (skeletonNode, secondaryJoints) = CreateGltfSkeleton(scene, skeleton, skeletonNodeName);
                if (skeletonNode != null && secondaryJoints != null)
                {
                    CreateSkeletonVisualizationMesh(exportedModel, scene, skeleton, secondaryJoints, skeletonNodeName);
                    var writer = new AnimationWriter(skeleton, []) { ComposeAdditive = ComposeAdditiveAnimations, ReduceKeyframes = ReduceAnimationKeyframes };
                    created = (writer, secondaryJoints);
                }
            }

            secondarySkeletons[skeletonName] = created;
            return created;
        }

        // UseAnimation is find-or-create by name, so a clip sharing a name with an already-written
        // animation (embedded, or an earlier clip) would merge its channels onto it. Keep the first, skip the rest.
        var writtenNames = exportedModel.LogicalAnimations.Select(a => a.Name).ToHashSet();

        var retargetWriter = new AnimationWriter(model.Skeleton, model.FlexControllers) { ComposeAdditive = ComposeAdditiveAnimations, ReduceKeyframes = ReduceAnimationKeyframes };

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

    /// <summary>
    /// Scale channels are only written for bones that scale, but importers keep the last value of a
    /// channel an animation does not key. Every animation therefore gets a constant scale of one for
    /// each joint that has a scale channel in any other animation.
    /// </summary>
    private static void AddMissingScaleChannels(ModelRoot exportedModel)
    {
        var scaledNodes = new HashSet<Node>();

        foreach (var animation in exportedModel.LogicalAnimations)
        {
            foreach (var channel in animation.Channels)
            {
                if (channel.TargetNodePath == PropertyPath.scale)
                {
                    scaledNodes.Add(channel.TargetNode);
                }
            }
        }

        if (scaledNodes.Count == 0)
        {
            return;
        }

        var identityScale = new Dictionary<float, Vector3> { [0f] = Vector3.One };

        foreach (var animation in exportedModel.LogicalAnimations)
        {
            var animatedNodes = new HashSet<Node>();
            var animationScaledNodes = new HashSet<Node>();

            foreach (var channel in animation.Channels)
            {
                animatedNodes.Add(channel.TargetNode);

                if (channel.TargetNodePath == PropertyPath.scale)
                {
                    animationScaledNodes.Add(channel.TargetNode);
                }
            }

            foreach (var node in scaledNodes)
            {
                // Animations of other models in the same export never touch this node.
                if (!animatedNodes.Contains(node) || animationScaledNodes.Contains(node))
                {
                    continue;
                }

                animation.CreateScaleChannel(node, identityScale, true);
            }
        }
    }

    // glTF holds all animations in one flat named list, so AG2 clips are labelled by their resource
    // path with the .vnmclip extension stripped (the path keeps them unique across clip folders).
    private static string ClipAnimationName(string clipName) => Path.ChangeExtension(clipName, null)!;

    private static SharpGLTF.Schema2.Animation UseAnimation(ModelRoot root, string name)
    {
        var animation = root.LogicalAnimations.FirstOrDefault(item => item.Name == name);
        return animation ?? root.CreateAnimation(name);
    }

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
        var meshTargets = new List<(Node Node, List<int> MorphFlexIds, Dictionary<int, FlexRule> RuleByFlexId, float[] FlexValues)>();

        foreach (var (node, mesh) in morphedMeshes)
        {
            var morph = mesh.MorphData!;
            var descriptors = morph.GetFlexDescriptors();
            var withData = morph.GetFlexNamesWithData();

            // The mesh's glTF morph targets are the flex descriptors that have flex data, in descriptor order.
            var morphFlexIds = new List<int>();
            for (var d = 0; d < descriptors.Count; d++)
            {
                if (withData.Contains(descriptors[d]))
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

            meshTargets.Add((node, morphFlexIds, ruleByFlexId, new float[FlexRule.GetFlexValueCount(morph.FlexRules)]));
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
            var times = new List<float>(animation.FrameCount);
            var sampledWeights = new List<float[]>[meshTargets.Count];
            var anyWeight = new bool[meshTargets.Count];
            for (var t = 0; t < meshTargets.Count; t++)
            {
                sampledWeights[t] = new List<float[]>(animation.FrameCount);
            }

            for (var f = 0; f < animation.FrameCount; f++)
            {
                frame.FrameIndex = f;
                animation.DecodeFrame(frame);
                times.Add(f / fps);

                for (var t = 0; t < meshTargets.Count; t++)
                {
                    var (_, morphFlexIds, ruleByFlexId, flexValues) = meshTargets[t];
                    var weights = new float[morphFlexIds.Count];
                    for (var i = 0; i < morphFlexIds.Count; i++)
                    {
                        var flexId = morphFlexIds[i];

                        if (ruleByFlexId.TryGetValue(flexId, out var rule))
                        {
                            weights[i] = rule.Evaluate(frame.Datas, model.FlexControllers, flexValues);
                            flexValues[flexId] = weights[i];
                            anyWeight[t] |= weights[i] != 0f;
                        }
                    }

                    sampledWeights[t].Add(weights);
                }
            }

            for (var t = 0; t < meshTargets.Count; t++)
            {
                if (!anyWeight[t])
                {
                    continue;
                }

                var keyframes = new Dictionary<float, float[]>();
                if (ReduceAnimationKeyframes)
                {
                    foreach (var index in ReduceKeyframes(times, sampledWeights[t], MorphWeightTolerance, WeightsError))
                    {
                        keyframes.Add(times[index], sampledWeights[t][index]);
                    }
                }
                else
                {
                    for (var f = 0; f < times.Count; f++)
                    {
                        keyframes.Add(times[f], sampledWeights[t][f]);
                    }
                }

                UseAnimation(exportedModel, animation.Name).CreateMorphChannel(meshTargets[t].Node, keyframes, meshTargets[t].MorphFlexIds.Count);
            }
        }
    }

    // The angle between the interpolated and the sampled rotation.
    internal static readonly float RotationTolerance = float.DegreesToRadians(0.01f);

    // 0.01 mm, as translations are written in glTF meters.
    internal const float TranslationTolerance = 1e-5f;

    internal const float ScaleTolerance = 1e-5f;

    internal const float MorphWeightTolerance = 1e-4f;

    /// <summary>
    /// How far <paramref name="actual"/> is from interpolating <paramref name="from"/> to
    /// <paramref name="to"/> by <paramref name="amount"/>.
    /// </summary>
    internal delegate float KeyframeInterpolationError<T>(T from, T to, float amount, T actual);

    // Blender interpolates quaternion keys per component and normalizes the result, which strays
    // from the slerp the glTF spec asks for when kept keys are far apart, so both must be in tolerance.
    internal static float RotationError(Quaternion from, Quaternion to, float amount, Quaternion actual)
    {
        var slerpError = MathUtils.AngleBetween(Quaternion.Slerp(from, to, amount), actual);
        var nlerpError = MathUtils.AngleBetween(Quaternion.Lerp(from, to, amount), actual);
        return MathF.Max(slerpError, nlerpError);
    }

    internal static float VectorError(Vector3 from, Vector3 to, float amount, Vector3 actual)
        => Vector3.Distance(Vector3.Lerp(from, to, amount), actual);

    internal static float WeightsError(float[] from, float[] to, float amount, float[] actual)
    {
        var error = 0f;

        for (var i = 0; i < actual.Length; i++)
        {
            var interpolated = float.Lerp(from[i], to[i], amount);
            error = MathF.Max(error, MathF.Abs(interpolated - actual[i]));
        }

        return error;
    }

    /// <summary>
    /// Flips each rotation onto the hemisphere of the one before it. Importers interpolate between
    /// consecutive keys, and some do so per component, which takes the long way round between
    /// quaternions of opposite sign.
    /// </summary>
    internal static void AlignQuaternionHemispheres(List<Quaternion> rotations)
    {
        for (var i = 1; i < rotations.Count; i++)
        {
            if (Quaternion.Dot(rotations[i - 1], rotations[i]) < 0f)
            {
                rotations[i] = Quaternion.Negate(rotations[i]);
            }
        }
    }

    /// <summary>
    /// Selects the keys that linear interpolation between the kept keys cannot reproduce within
    /// <paramref name="tolerance"/>. The first and last keys are kept, unless every key is within
    /// tolerance of the first, which then stands alone and is held for the whole clip.
    /// </summary>
    /// <returns>The indices of the kept keys, in order.</returns>
    internal static List<int> ReduceKeyframes<T>(List<float> times, List<T> values, float tolerance, KeyframeInterpolationError<T> interpolationError)
    {
        var count = values.Count;
        if (count == 0)
        {
            return [];
        }

        // Holding the first value is interpolating from it to itself.
        var first = values[0];
        var isConstant = true;
        for (var i = 1; i < count; i++)
        {
            if (interpolationError(first, first, 0f, values[i]) > tolerance)
            {
                isConstant = false;
                break;
            }
        }

        if (isConstant)
        {
            return [0];
        }

        var keep = new bool[count];
        keep[0] = true;
        keep[count - 1] = true;

        // Ramer-Douglas-Peucker: keep the key that interpolating between the ends of a segment
        // reproduces worst, and split the segment there, until every dropped key is within tolerance.
        var segments = new Stack<(int Start, int End)>();
        segments.Push((0, count - 1));

        while (segments.Count > 0)
        {
            var (start, end) = segments.Pop();
            var duration = times[end] - times[start];
            var worstIndex = -1;
            var worstError = tolerance;

            for (var i = start + 1; i < end; i++)
            {
                var amount = (times[i] - times[start]) / duration;
                var error = interpolationError(values[start], values[end], amount, values[i]);

                if (error > worstError)
                {
                    worstError = error;
                    worstIndex = i;
                }
            }

            if (worstIndex < 0)
            {
                continue;
            }

            keep[worstIndex] = true;
            segments.Push((start, worstIndex));
            segments.Push((worstIndex, end));
        }

        var kept = new List<int>();
        for (var i = 0; i < count; i++)
        {
            if (keep[i])
            {
                kept.Add(i);
            }
        }

        return kept;
    }

    /// <summary>
    /// Selects the first and last keys, and every key that differs from the key before or after it,
    /// so a run of repeated values keeps only its ends.
    /// </summary>
    /// <returns>The indices of the kept keys, in order.</returns>
    internal static List<int> CollapseRepeatedKeyframes<T>(List<T> values)
    {
        var comparer = EqualityComparer<T>.Default;
        var kept = new List<int>();

        for (var i = 0; i < values.Count; i++)
        {
            var isEnd = i == 0 || i == values.Count - 1;

            if (isEnd || !comparer.Equals(values[i], values[i - 1]) || !comparer.Equals(values[i], values[i + 1]))
            {
                kept.Add(i);
            }
        }

        return kept;
    }

    /// <summary>
    /// Buffers every sampled frame of each bone's channel, so its keys are selected once the whole
    /// clip is known.
    /// </summary>
    record struct AnimationChannelWriter<T>(List<float>[] Times, List<T>[] Values, float Tolerance, KeyframeInterpolationError<T> InterpolationError) where T : struct
    {
        public static AnimationChannelWriter<T> Create(int boneCount, float tolerance, KeyframeInterpolationError<T> interpolationError) => new()
        {
            Times = [.. Enumerable.Range(0, boneCount).Select(_ => new List<float>())],
            Values = [.. Enumerable.Range(0, boneCount).Select(_ => new List<T>())],
            Tolerance = tolerance,
            InterpolationError = interpolationError,
        };

        public readonly void SubmitKeyframe(int boneID, float time, T value)
        {
            Times[boneID].Add(time);
            Values[boneID].Add(value);
        }

        /// <summary>
        /// Gets the channel's keyframes, reduced within tolerance or with runs of repeated values collapsed.
        /// </summary>
        public readonly Dictionary<float, T> GetKeyframes(int boneID, bool reduce)
        {
            var times = Times[boneID];
            var values = Values[boneID];
            var kept = reduce
                ? ReduceKeyframes(times, values, Tolerance, InterpolationError)
                : CollapseRepeatedKeyframes(values);

            var keyframes = new Dictionary<float, T>(kept.Count);
            foreach (var index in kept)
            {
                keyframes.Add(times[index], values[index]);
            }

            return keyframes;
        }

        public readonly void Clear()
        {
            for (var i = 0; i < Times.Length; i++)
            {
                Times[i].Clear();
                Values[i].Clear();
            }
        }
    }
}
