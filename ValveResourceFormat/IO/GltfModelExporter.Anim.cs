using System.Diagnostics;
using System.Linq;
using SharpGLTF.Schema2;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using VAnim = ValveResourceFormat.ResourceTypes.ModelAnimation.Animation;

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
        /// Writes a skeletal animation to the glTF model.
        /// </summary>
        public void WriteAnimation(ModelRoot model, Node[] joints, VAnim animation)
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

            // Root motion is stored separately from bone frames, so bake it into the root bone(s)
            // to keep the skeleton from animating in place. https://github.com/ValveResourceFormat/ValveResourceFormat/issues/955
            var applyRootMotion = animation.HasMovementData();

            for (var f = 0; f < animation.FrameCount; f++)
            {
                Frame.FrameIndex = f;
                animation.DecodeFrame(Frame);
                var time = f / fps;
                var prevFrameTime = (f - 1) / fps;

                var rootMotion = applyRootMotion
                    ? GetRootMotionMatrix(animation.GetMovementOffsetData(f))
                    : Matrix4x4.Identity;

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

                    if (applyRootMotion && Skeleton.Bones[boneID].Parent == null)
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
                outputAnimation.CreateRotationChannel(jointNode, RotationWriter.Channels[boneID], true);
                outputAnimation.CreateTranslationChannel(jointNode, PositionWriter.Channels[boneID], true);
                outputAnimation.CreateScaleChannel(jointNode, ScaleWriter.Channels[boneID], true);
            }
        }

        // Movement rotation is a yaw around the source-engine up axis (Z), matching the DMX exporter.
        private static Matrix4x4 GetRootMotionMatrix(AnimationMovement.MovementData movement)
            => Matrix4x4.CreateRotationZ(float.DegreesToRadians(movement.Angle))
                * Matrix4x4.CreateTranslation(movement.Position);
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
