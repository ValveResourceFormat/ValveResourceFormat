using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using OpenTK.Graphics.OpenGL;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    class HitboxSetSceneNode : SceneNode
    {
        private static readonly Color32[] HitboxColors = [
            new(1f, 1f, 1f, 0.14f), //HITGROUP_GENERIC
            new(1f, 0.5f, 0.5f, 0.14f), //HITGROUP_HEAD
            new(0.5f, 1f, 0.5f, 0.14f), //HITGROUP_CHEST
            new(1f, 1f, 0.5f, 0.14f), //HITGROUP_STOMACH
            new(0.5f, 0.5f, 1f, 0.14f), //HITGROUP_LEFTARM
            new(1f, 0.5f, 1f, 0.14f), //HITGROUP_RIGHTARM
            new(0.5f, 1f, 1f, 0.14f), //HITGROUP_LEFTLEG
            new(1f, 1f, 1f, 0.14f), //HITGROUP_RIGHTLEG
            new(1f, 0.5f, 0.25f, 0.14f), //HITGROUP_NECK
        ];

        class HitboxSetData
        {
            public Hitbox[] HitboxSet { get; init; }
            public HitboxSceneNode[] SceneNodes { get; init; }
            public int[] HitboxBoneIndexes { get; init; }
        }

        readonly AnimationController animationController;
        readonly Skeleton skeleton;

        readonly Matrix4x4[] boneMatrices;
        readonly Dictionary<string, HitboxSetData> hitboxSets = new();
        HitboxSetData currentSet;

        public HitboxSetSceneNode(Scene scene, AnimationController animationController, Skeleton skeleton, Dictionary<string, Hitbox[]> hitboxSets)
            : base(scene)
        {
            this.skeleton = skeleton;
            this.animationController = animationController;
            boneMatrices = new Matrix4x4[skeleton.Bones.Length];

            var boneIndexes = skeleton.Bones.Select((b, i) => (b, i))
                                            .ToDictionary(p => p.b.Name, p => p.i);

            foreach (var pair in hitboxSets)
            {
                AddHitboxSet(pair.Key, pair.Value, boneIndexes);
            }
        }

        private void AddHitboxSet(string name, Hitbox[] hitboxSet, Dictionary<string, int> boneIndexes)
        {
            var sceneNodes = new HitboxSceneNode[hitboxSet.Length];
            var hitboxBoneIndexes = new int[hitboxSet.Length];

            for (var i = 0; i < hitboxSet.Length; i++)
            {
                var hitbox = hitboxSet[i];
                sceneNodes[i] = CreateSceneNode(Scene, hitbox);

                if (string.IsNullOrEmpty(hitbox.BoneName))
                {
                    hitboxBoneIndexes[i] = -1;
                }
                else
                {
                    hitboxBoneIndexes[i] = boneIndexes[hitbox.BoneName];
                }
            }

            var data = new HitboxSetData
            {
                HitboxSet = hitboxSet,
                HitboxBoneIndexes = hitboxBoneIndexes,
                SceneNodes = sceneNodes
            };

            hitboxSets.Add(name, data);
        }

        private static Color32 GetHitboxGroupColor(int group)
        {
            if (group < 0 || group >= HitboxColors.Length)
            {
                return HitboxColors[0];
            }
            return HitboxColors[group];
        }

        private static HitboxSceneNode CreateSceneNode(Scene scene, Hitbox hitbox)
        {
            var color = GetHitboxGroupColor(hitbox.GroupId);
            return hitbox.ShapeType switch
            {
                Hitbox.HitboxShape.Sphere => HitboxSceneNode.CreateSphereNode(scene, hitbox.MinBounds, hitbox.ShapeRadius, color),
                Hitbox.HitboxShape.Capsule => HitboxSceneNode.CreateCapsuleNode(scene, hitbox.MinBounds, hitbox.MaxBounds, hitbox.ShapeRadius, color),
                Hitbox.HitboxShape.Box => HitboxSceneNode.CreateBoxNode(scene, hitbox.MinBounds, hitbox.MaxBounds, color),
                _ => throw new NotImplementedException($"Unknown hitbox shape type: {hitbox.ShapeType}")
            };
        }

        public void SetHitboxSet(string set)
        {
            if (set == null)
            {
                currentSet = null;
                return;
            }

            currentSet = hitboxSets[set];
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (currentSet == null)
            {
                return;
            }

            LocalBoundingBox = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));
            animationController?.GetBoneMatrices(boneMatrices);
        }

        private void RenderHitboxSet(Scene.RenderContext context, HitboxSetData hitboxSetData)
        {
            var hitboxSet = hitboxSetData.HitboxSet;
            for (var i = 0; i < hitboxSet.Length; i++)
            {
                var shape = hitboxSetData.SceneNodes[i];
                var hitbox = hitboxSet[i];
                var boneId = hitboxSetData.HitboxBoneIndexes[i];
                var targetTransform = boneId == -1 ? Matrix4x4.Identity : boneMatrices[boneId];

                if (hitbox.TranslationOnly)
                {
                    Matrix4x4.Decompose(targetTransform, out _, out _, out var translation);
                    shape.Transform = Matrix4x4.CreateTranslation(translation);
                }
                else
                {
                    shape.Transform = targetTransform;
                }
                shape.Render(context);
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (currentSet == null)
            {
                return;
            }

            GL.DepthFunc(DepthFunction.Always);
            RenderHitboxSet(context, currentSet);
            GL.DepthFunc(DepthFunction.Greater);
        }
    }
}
