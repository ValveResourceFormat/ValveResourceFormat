using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using OpenTK.Graphics.OpenGL;
using GUI.Utils;
using System.Windows.Forms;

namespace GUI.Types.Renderer
{
    class HitboxSetSceneNode : SceneNode
    {
        private static readonly Color32[] HitboxColors = [
            new(1f, 0f, 1f, 0.14f),
            new(1f, 0.5f, 0.5f, 0.14f),
            new(0.5f, 1f, 0.5f, 0.14f),
            new(1f, 1f, 0.5f, 0.14f),
            new(0.5f, 0.5f, 1f, 0.14f),
            new(1f, 0.5f, 1f, 0.14f),
            new(0.5f, 1f, 1f, 0.14f),
            new(1f, 1f, 1f, 0.14f),
            new(1f, 0.5f, 0.25f, 0.14f),
        ];
        public bool Enabled { get; set; } = true;

        readonly AnimationController animationController;
        readonly Skeleton skeleton;
        readonly Hitbox[] hitboxSet;

        readonly PhysSceneNode[] physSceneNodes;
        readonly Matrix4x4[] boneMatrices;
        readonly int[] hitboxBoneIndexes;

        public HitboxSetSceneNode(Scene scene, AnimationController animationController, Skeleton skeleton, Hitbox[] hitboxSet)
            : base(scene)
        {
            this.skeleton = skeleton;
            this.animationController = animationController;
            this.hitboxSet = hitboxSet;

            boneMatrices = new Matrix4x4[skeleton.Bones.Length];
            physSceneNodes = new PhysSceneNode[hitboxSet.Length];
            hitboxBoneIndexes = new int[hitboxSet.Length];

            var boneIndexes = skeleton.Bones.Select((b, i) => (b, i))
                                            .ToDictionary(p => p.b.Name, p => p.i);
            for (var i = 0; i < hitboxSet.Length; i++)
            {
                var hitbox = hitboxSet[i];
                physSceneNodes[i] = CreatePhysNode(scene, hitbox);
                hitboxBoneIndexes[i] = boneIndexes[hitbox.BoneName];
            }
        }

        private static Color32 GetHitboxGroupColor(int group)
        {
            if (group < 0 || group >= HitboxColors.Length)
            {
                return HitboxColors[0];
            }
            return HitboxColors[group];
        }

        private static PhysSceneNode CreatePhysNode(Scene scene, Hitbox hitbox)
        {
            var color = GetHitboxGroupColor(hitbox.GroupId);
            return hitbox.ShapeType switch
            {
                Hitbox.HitboxShape.Sphere => PhysSceneNode.CreateSphereNode(scene, hitbox.MinBounds, hitbox.ShapeRadius, color),
                Hitbox.HitboxShape.Capsule => PhysSceneNode.CreateCapsuleNode(scene, hitbox.MinBounds, hitbox.MaxBounds, hitbox.ShapeRadius, color),
                Hitbox.HitboxShape.Box => PhysSceneNode.CreateBoxNode(scene, hitbox.MinBounds, hitbox.MaxBounds, color),
                _ => throw new NotImplementedException($"Unknown hitbox shape type: {hitbox.ShapeType}")
            };
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (!Enabled)
            {
                return;
            }

            LocalBoundingBox = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

            Frame frame = null;

            if (animationController.ActiveAnimation != null)
            {
                if (animationController.IsPaused)
                {
                    frame = animationController.FrameCache.GetFrame(animationController.ActiveAnimation, animationController.Frame);
                }
                else
                {
                    frame = animationController.FrameCache.GetInterpolatedFrame(animationController.ActiveAnimation, animationController.Time);
                }
            }

            foreach (var root in skeleton.Roots)
            {
                GetAnimationMatrixRecursive(root, Matrix4x4.Identity, frame);
            }
        }

        private void GetAnimationMatrixRecursive(Bone bone, Matrix4x4 bindPose, Frame frame)
        {
            if (frame != null)
            {
                var transform = frame.Bones[bone.Index];
                bindPose = Matrix4x4.CreateScale(transform.Scale)
                    * Matrix4x4.CreateFromQuaternion(transform.Angle)
                    * Matrix4x4.CreateTranslation(transform.Position)
                    * bindPose;
            }
            else
            {
                bindPose = bone.BindPose * bindPose;
            }

            boneMatrices[bone.Index] = bindPose;

            foreach (var child in bone.Children)
            {
                GetAnimationMatrixRecursive(child, bindPose, frame);
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            GL.DepthFunc(DepthFunction.Always);
            for (var i = 0; i < hitboxSet.Length; i++)
            {
                var shape = physSceneNodes[i];
                var hitbox = hitboxSet[i];
                var boneId = hitboxBoneIndexes[i];

                if (hitbox.TranslationOnly)
                {
                    Matrix4x4.Decompose(boneMatrices[boneId], out _, out _, out var translation);
                    shape.Transform = Matrix4x4.CreateTranslation(translation);
                }
                else
                {
                    shape.Transform = boneMatrices[boneId];
                }
                shape.Enabled = true;
                shape.Render(context);
            }
            GL.DepthFunc(DepthFunction.Greater);
        }
    }
}
