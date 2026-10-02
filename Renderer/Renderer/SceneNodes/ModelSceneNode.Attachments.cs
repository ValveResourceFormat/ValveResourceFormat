using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Anchors other scene nodes to this model, at an attachment point, at a bone, or at the model
    /// itself.
    /// </summary>
    public partial class ModelSceneNode
    {
        /// <summary>
        /// Attachment points from model data.
        /// </summary>
        public Dictionary<string, Attachment> Attachments { get; }

        /// <summary>
        /// The frame a child follows: the attachment point's world transform, a bone's when no attachment
        /// matches, or the model's own transform. Rigid, with no scale.
        /// </summary>
        public override Matrix4x4 GetChildFrame(string? attachmentName)
        {
            if (!string.IsNullOrEmpty(attachmentName))
            {
                if (Attachments.ContainsKey(attachmentName))
                {
                    return GetRigidTransform(GetAttachmentTransform(attachmentName));
                }

                var boneIndex = AnimationController.Skeleton.GetBoneIndex(attachmentName);
                if (boneIndex != -1)
                {
                    return GetRigidTransform(AnimationController.Pose[boneIndex] * Transform);
                }
            }

            return GetRigidTransform(Transform);
        }

        /// <summary>
        /// Whether the given name resolves to an anchor on this model: an attachment point,
        /// or a bone when no attachment has that name.
        /// </summary>
        public bool HasAttachmentOrBone(string name)
            => Attachments.ContainsKey(name) || AnimationController.Skeleton.GetBoneIndex(name) != -1;

        /// <summary>
        /// Attaches another <see cref="SceneNode"/> to this model with optional attachment point, offset and rotation,
        /// keeping the node's own scale. Shorthand for <see cref="SceneNode.SetParent"/>.
        /// </summary>
        /// <param name="node">The child model to attach.</param>
        /// <param name="attachmentName">The attachment point name.</param>
        /// <param name="offset">The local offset from the attachment point.</param>
        /// <param name="rotation">The local rotation from the attachment point.</param>
        public void AttachNode(SceneNode node,
            string attachmentName = "",
            Vector3 offset = default,
            Quaternion rotation = default)
        {
            Matrix4x4.Decompose(node.Transform, out var scale, out _, out _);

            node.SetParent(this, attachmentName,
                Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(offset));
        }

        /// <summary>
        /// Places <paramref name="child"/> once at the named attachment point or bone (or the model's own
        /// transform when no name is given), with <paramref name="offset"/> applied in that anchor's frame.
        /// Unlike <see cref="AttachNode"/>, the child does not track the model afterwards.
        /// </summary>
        public void PlaceNode(SceneNode child, string attachmentName, Vector3 offset)
        {
            child.Transform = Matrix4x4.CreateTranslation(offset) * GetChildFrame(attachmentName);
        }

        /// <summary>
        /// Gets the world transform for the specified attachment point.
        /// </summary>
        public Matrix4x4 GetAttachmentTransform(string attachmentName)
        {
            var attachment = Attachments.GetValueOrDefault(attachmentName);
            if (attachment == null)
            {
                return Transform;
            }

            return GetAttachmentLocalTransform(attachment, AnimationController.FrameCache.Skeleton, AnimationController.Pose) * Transform;
        }

        /// <summary>
        /// Computes the model-local transform of an attachment from the given bone pose.
        /// </summary>
        public static Matrix4x4 GetAttachmentLocalTransform(Attachment attachment, Skeleton skeleton, Matrix4x4[] pose)
        {
            var transform = Matrix4x4.Identity;

            for (var i = 0; i < attachment.Length; i++)
            {
                var influence = attachment[i];
                var boneIndex = skeleton.GetBoneIndex(influence.Name);
                if (boneIndex != -1)
                {
                    var boneTransform = pose[boneIndex];
                    var influenceTransform = Matrix4x4.CreateFromQuaternion(influence.Rotation) * Matrix4x4.CreateTranslation(influence.Offset);
                    transform *= Matrix4x4.Lerp(Matrix4x4.Identity, influenceTransform * boneTransform, influence.Weight);
                }
            }

            if (attachment.IgnoreRotation)
            {
                // The transform's scale is taken as uniform.
                var scale = transform.M22;
                var translation = transform.Translation;
                transform = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(translation);
            }

            return transform;
        }
    }
}
