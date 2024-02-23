using GUI.Utils;

namespace GUI.Types.Renderer
{
    internal class HitboxSceneNode : ShapeSceneNode
    {
        public HitboxSceneNode(Scene scene, List<SimpleVertex> verts, List<int> inds) : base(scene, verts, inds)
        {
        }

        public static HitboxSceneNode CreateSphereNode(Scene scene, Vector3 center, float radius, Color32 color)
        {
            var verts = new List<SimpleVertex>();
            var inds = new List<int>();
            AddSphere(verts, inds, center, radius, color);

            var aabb = new AABB(new Vector3(radius), new Vector3(-radius));
            return new HitboxSceneNode(scene, verts, inds)
            {
                LocalBoundingBox = aabb
            };
        }

        public static HitboxSceneNode CreateCapsuleNode(Scene scene, Vector3 from, Vector3 to, float radius, Color32 color)
        {
            var verts = new List<SimpleVertex>();
            var inds = new List<int>();
            AddCapsule(verts, inds, from, to, radius, color);

            var min = Vector3.Min(from, to);
            var max = Vector3.Max(from, to);
            var aabb = new AABB(min - new Vector3(radius), max + new Vector3(radius));
            return new HitboxSceneNode(scene, verts, inds)
            {
                LocalBoundingBox = aabb,
            };
        }

        public static HitboxSceneNode CreateBoxNode(Scene scene, Vector3 minBounds, Vector3 maxBounds, Color32 color)
        {
            var inds = new List<int>();
            var verts = new List<SimpleVertex>
            {
                new(new(minBounds.X, minBounds.Y, minBounds.Z), color),
                new(new(minBounds.X, minBounds.Y, maxBounds.Z), color),
                new(new(minBounds.X, maxBounds.Y, maxBounds.Z), color),
                new(new(minBounds.X, maxBounds.Y, minBounds.Z), color),

                new(new(maxBounds.X, minBounds.Y, minBounds.Z), color),
                new(new(maxBounds.X, minBounds.Y, maxBounds.Z), color),
                new(new(maxBounds.X, maxBounds.Y, maxBounds.Z), color),
                new(new(maxBounds.X, maxBounds.Y, minBounds.Z), color),
            };

            AddFace(inds, 0, 1, 2, 3);
            AddFace(inds, 1, 5, 6, 2);
            AddFace(inds, 5, 4, 7, 6);
            AddFace(inds, 0, 3, 7, 4);
            AddFace(inds, 3, 2, 6, 7);
            AddFace(inds, 1, 0, 4, 5);

            var aabb = new AABB(minBounds, maxBounds);

            return new HitboxSceneNode(scene, verts, inds)
            {
                LocalBoundingBox = aabb,
            };
        }

        public override void Update(Scene.UpdateContext context)
        {

        }
    }
}
