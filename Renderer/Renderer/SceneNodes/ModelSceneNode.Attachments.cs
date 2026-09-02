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

        /// <summary>Gets the list of nodes attached to this model and the attachment points used.</summary>
        public List<(SceneNode Node, string AttachmentName, Vector3 Offset, Quaternion Rotation)> AttachedNodes { get; } = [];

        private void UpdateAttachments(Scene.UpdateContext context)
        {
            foreach (var attachment in AttachedNodes)
            {
                var child = attachment.Node;

                // keep the child's own scale; the parent drives the rest of its transform
                var localTransform = Matrix4x4.CreateScale(GetScale(child.Transform)) * Matrix4x4.CreateFromQuaternion(attachment.Rotation) * Matrix4x4.CreateTranslation(attachment.Offset);
                child.Transform = localTransform * GetAttachmentOrSelfTransform(attachment.AttachmentName);
                child.Update(context);
            }
        }

        // The parent anchor for an attached child: the attachment point's world transform, the world
        // transform of a bone with that name when no attachment matches, or the model's own transform when
        // no name is given or it matches neither. Rigid (no scale) because Source 2 does not propagate the
        // parent's scale to attachment-parented children.
        private Matrix4x4 GetAttachmentOrSelfTransform(string attachmentName)
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

        // Rotation and translation only, with scale removed.
        private static Matrix4x4 GetRigidTransform(Matrix4x4 transform)
        {
            Matrix4x4.Decompose(transform, out _, out var rotation, out var translation);
            return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
        }

        private static Vector3 GetScale(Matrix4x4 transform)
        {
            Matrix4x4.Decompose(transform, out var scale, out _, out _);
            return scale;
        }

        /// <summary>
        /// Attaches another <see cref="SceneNode"/> to this model with optional attachment point, offset and rotation.
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
            node.Parent = this;
            AttachedNodes.RemoveAll(entry => entry.Node == node);
            AttachedNodes.Add((node, attachmentName, offset, rotation));
        }

        /// <summary>
        /// Places <paramref name="child"/> once at the named attachment point or bone (or the model's own
        /// transform when no name is given), with <paramref name="offset"/> applied in that anchor's frame.
        /// Unlike <see cref="AttachNode"/>, the child does not track the model afterwards. Works for any scene node.
        /// </summary>
        public void PlaceNode(SceneNode child, string attachmentName, Vector3 offset)
        {
            child.Transform = Matrix4x4.CreateTranslation(offset) * GetAttachmentOrSelfTransform(attachmentName);
        }

        /// <summary>
        /// Attaches <paramref name="node"/> so it keeps its current world position relative to this model,
        /// following the model if it later moves. Used for plain <c>parentname</c> parenting (no attachment point),
        /// where the child stays where it was authored instead of snapping onto the parent.
        /// </summary>
        /// <param name="node">The child to attach.</param>
        public void AttachNodeKeepingTransform(SceneNode node)
        {
            Matrix4x4.Invert(GetRigidTransform(Transform), out var anchorInverse);
            var local = GetRigidTransform(node.Transform) * anchorInverse;
            Matrix4x4.Decompose(local, out _, out var rotation, out var offset);
            AttachNode(node, offset: offset, rotation: rotation);
        }

        /// <summary>
        /// Gets the world transform for the specified attachment point.
        /// </summary>
        public Matrix4x4 GetAttachmentTransform(string attachmentName)
        {
            var transform = Matrix4x4.Identity;

            var attachment = Attachments.GetValueOrDefault(attachmentName);
            if (attachment != null)
            {
                for (var i = 0; i < attachment.Length; i++)
                {
                    var influence = attachment[i];
                    var boneIndex = AnimationController.FrameCache.Skeleton.GetBoneIndex(influence.Name);
                    if (boneIndex != -1)
                    {
                        var boneTransform = AnimationController.Pose[boneIndex];
                        var influenceTransform = Matrix4x4.CreateFromQuaternion(influence.Rotation) * Matrix4x4.CreateTranslation(influence.Offset);
                        transform *= Matrix4x4.Lerp(Matrix4x4.Identity, influenceTransform * boneTransform, influence.Weight);
                    }
                }

                if (attachment.IgnoreRotation)
                {
                    // Dropping the rotation leaves no per-axis frame to scale along, so the transform's
                    // scale is taken as uniform.
                    var scale = transform.M22;
                    var translation = transform.Translation;
                    transform = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(translation);
                }
            }

            return transform * Transform;
        }
    }
}
