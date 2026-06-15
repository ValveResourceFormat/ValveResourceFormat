using System.Diagnostics;
using System.Linq;
using SharpGLTF.Schema2;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using VAnim = ValveResourceFormat.ResourceTypes.ModelAnimation.Animation;
using VAnimationClip = ValveResourceFormat.ResourceTypes.ModelAnimation2.AnimationClip;
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
        int BoneCount => Frame.Bones.Length;

        AnimationChannelWriter<Quaternion> RotationWriter;
        AnimationChannelWriter<Vector3> PositionWriter;
        AnimationChannelWriter<Vector3> ScaleWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationWriter"/> class.
        /// </summary>
        public AnimationWriter(Skeleton skeleton, FlexController[] flexControllers)
        {
            Skeleton = skeleton;
            Frame = new(skeleton, flexControllers);

            RotationWriter = AnimationChannelWriter<Quaternion>.Create(BoneCount);
            PositionWriter = AnimationChannelWriter<Vector3>.Create(BoneCount);
            ScaleWriter = AnimationChannelWriter<Vector3>.Create(BoneCount);
        }

        /// <summary>
        /// Writes a skeletal animation to the glTF model. Entries in <paramref name="joints"/> may be
        /// null when an animation targets a skeleton with bones the exported model does not have
        /// (e.g. animation graph clips retargeted by bone name); those bones are skipped.
        /// </summary>
        public void WriteAnimation(ModelRoot model, Node?[] joints, VAnim animation)
        {
            Debug.Assert(joints.Length == BoneCount);

            // Cleanup state
            Frame.Clear(Skeleton);

            RotationWriter.Clear();
            PositionWriter.Clear();
            ScaleWriter.Clear();

            var outputAnimation = model.UseAnimation(animation.Name);

            var fps = animation.Fps;

            // Some models have fps of 0.000, which will make time a NaN
            if (fps == 0)
            {
                fps = 1f;
            }

            // root motion is stored separately from bone frames, so bake it into the root bone(s) to keep
            // the skeleton from animating in place. horizontal travel and yaw only. the engine doesn't
            // apply a vertical movement track to the body.
            var applyRootMotion = animation.HasMovementData();

            // No cloth solver here, so mirror the renderer (BaseAnimationController.GetSkinningMatrices):
            // pin each cloth root to the cloth anchor bone instead of writing its raw, solver-less clip data.
            var clothAnchor = Skeleton.ClothSimulationRoot;
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

            // bake additive clips over the bind pose, same as the renderer
            var additive = animation.Clip?.IsAdditive ?? false;

            for (var f = 0; f < animation.FrameCount; f++)
            {
                Frame.FrameIndex = f;
                animation.DecodeFrame(Frame);

                if (additive)
                {
                    for (var boneID = 0; boneID < BoneCount; boneID++)
                    {
                        var bind = new FrameBone(Skeleton.Bones[boneID].Position, 1f, Skeleton.Bones[boneID].Angle);
                        Frame.Bones[boneID] = Frame.Bones[boneID].BlendAdd(bind, 1f);
                    }
                }

                var time = f / fps;
                var prevFrameTime = (f - 1) / fps;

                var rootMotion = Matrix4x4.Identity;
                if (applyRootMotion)
                {
                    var movement = animation.GetMovementOffsetData(f);
                    var movementPosition = new Vector3(movement.Position.X, movement.Position.Y, 0f);
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
                        var anchorFrame = Frame.Bones[b.Index];
                        var anchorScale = anchorFrame.Scale;
                        if (float.IsNaN(anchorScale) || float.IsInfinity(anchorScale))
                        {
                            anchorScale = 0.0f;
                        }

                        anchorPose *= Matrix4x4.CreateScale(anchorScale)
                            * Matrix4x4.CreateFromQuaternion(anchorFrame.Angle)
                            * Matrix4x4.CreateTranslation(anchorFrame.Position);
                    }

                    clothSkinning = anchorInverseBindPose * anchorPose;
                }

                for (var boneID = 0; boneID < BoneCount; boneID++)
                {
                    var boneFrame = Frame.Bones[boneID];

                    var position = boneFrame.Position;
                    var rotation = boneFrame.Angle;
                    var scalarBoneScale = boneFrame.Scale;

                    if (float.IsNaN(scalarBoneScale) || float.IsInfinity(scalarBoneScale))
                    {
                        // See https://github.com/ValveResourceFormat/ValveResourceFormat/issues/527 (NaN)
                        // and https://github.com/ValveResourceFormat/ValveResourceFormat/issues/570 (inf)
                        scalarBoneScale = 0.0f;
                    }

                    var scale = new Vector3(scalarBoneScale);

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

                    RotationWriter.SubmitKeyframe(boneID, time, prevFrameTime, rotation);
                    PositionWriter.SubmitKeyframe(boneID, time, prevFrameTime, position);
                    ScaleWriter.SubmitKeyframe(boneID, time, prevFrameTime, scale);
                }
            }

            for (var boneID = 0; boneID < BoneCount; boneID++)
            {
                if (animation.FrameCount == 0)
                {
                    RotationWriter.Channels[boneID].Add(0f, Skeleton.Bones[boneID].Angle);
                    PositionWriter.Channels[boneID].Add(0f, Skeleton.Bones[boneID].Position);
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
    }

    // Animation-graph clips aren't part of GetAllAnimations; write them here, retargeted by bone name.
    private void WriteAnimationGraphClips(ModelRoot exportedModel, VModel model, Node[] joints, HashSet<string> animationFilter)
    {
        var retargets = new Dictionary<string, (AnimationWriter Writer, Node?[] Joints)?>();

        foreach (var clipName in AnimationGraphLoader.GetClipNames(model, FileLoader))
        {
            CancellationToken.ThrowIfCancellationRequested();

            if (FileLoader.LoadFileCompiled(clipName)?.DataBlock is not VAnimationClip clip
                || (animationFilter.Count > 0 && !animationFilter.Contains(clip.Name)))
            {
                continue;
            }

            if (!retargets.TryGetValue(clip.SkeletonName, out var retarget))
            {
                retargets[clip.SkeletonName] = retarget = BuildClipRetarget(model, joints, clip.SkeletonName);
            }

            if (retarget != null)
            {
                retarget.Value.Writer.WriteAnimation(exportedModel, retarget.Value.Joints, new VAnim(clip));
            }
        }
    }

    // Loads a clip's skeleton and maps its bones onto the model's joints by name. Null if none match.
    private (AnimationWriter Writer, Node?[] Joints)? BuildClipRetarget(VModel model, Node[] joints, string clipSkeletonName)
    {
        if (FileLoader.LoadFileCompiled(clipSkeletonName)?.DataBlock is not BinaryKV3 skeletonData)
        {
            return null;
        }

        var clipSkeleton = Skeleton.FromSkeletonData(skeletonData.Data);
        var remappedJoints = new Node?[clipSkeleton.Bones.Length];
        var matched = false;

        for (var i = 0; i < clipSkeleton.Bones.Length; i++)
        {
            var modelBone = model.Skeleton[clipSkeleton.Bones[i].Name];
            if (modelBone != null)
            {
                remappedJoints[i] = joints[modelBone.Index];
                matched = true;
            }
        }

        return matched ? (new AnimationWriter(clipSkeleton, model.FlexControllers), remappedJoints) : null;
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
