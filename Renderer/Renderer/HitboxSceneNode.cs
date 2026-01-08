using ValveResourceFormat.ResourceTypes.ModelData;

namespace ValveResourceFormat.Renderer
{
    internal class HitboxSceneNode : ShapeSceneNode
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


        /// <summary>
        /// Constructs a node with a single box shape
        /// </summary>
        private HitboxSceneNode(Scene scene, Vector3 minBounds, Vector3 maxBounds, Color32 color) : base(scene, minBounds, maxBounds, color) { }

        /// <summary>
        /// Constructs a node with a single sphere shape
        /// </summary>
        private HitboxSceneNode(Scene scene, Vector3 center, float radius, Color32 color) : base(scene, center, radius, color) { }

        /// <summary>
        /// Constructs a node with a single capsule shape
        /// </summary>
        private HitboxSceneNode(Scene scene, Vector3 from, Vector3 to, float radius, Color32 color) : base(scene, from, to, radius, color) { }

        protected override bool Shaded => false;

        private static Color32 GetHitboxGroupColor(int group)
        {
            if (group < 0 || group >= HitboxColors.Length)
            {
                return HitboxColors[0];
            }
            return HitboxColors[group];
        }

        public static HitboxSceneNode Create(Scene scene, Hitbox hitbox)
        {
            var color = GetHitboxGroupColor(hitbox.GroupId);
            return hitbox.ShapeType switch
            {
                Hitbox.HitboxShape.Box => new HitboxSceneNode(scene, hitbox.MinBounds, hitbox.MaxBounds, color),
                Hitbox.HitboxShape.Sphere => new HitboxSceneNode(scene, hitbox.MinBounds, hitbox.ShapeRadius, color),
                Hitbox.HitboxShape.Capsule => new HitboxSceneNode(scene, hitbox.MinBounds, hitbox.MaxBounds, hitbox.ShapeRadius, color),
                _ => throw new NotImplementedException($"Unknown hitbox shape type: {hitbox.ShapeType}")
            };
        }
    }
}
