using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class AnimationMovement
    {
        public readonly struct MovementData
        {
            public Vector3 Position { get; }
            public float Angle { get; }

            public MovementData(Vector3 position, float angle)
            {
                Position = position;
                Angle = angle;
            }
        }

        public int EndFrame { get; }
        public ModelAnimationMotionFlags MotionFlags { get; }
        public float V0 { get; }
        public float V1 { get; }
        public float Angle { get; }
        public Vector3 Vector { get; }
        public Vector3 Position { get; }

        public AnimationMovement(KVObject frameBlock)
        {
            EndFrame = frameBlock.GetInt32Property("endframe");
            MotionFlags = (ModelAnimationMotionFlags)frameBlock.GetInt32Property("motionflags");
            V0 = frameBlock.GetInt32Property("v0");
            V1 = frameBlock.GetInt32Property("v1");
            Angle = frameBlock.GetFloatProperty("angle");
            Vector = new Vector3(frameBlock.GetFloatArray("vector"));
            Position = new Vector3(frameBlock.GetFloatArray("position"));
        }

        public static MovementData Lerp(AnimationMovement a, AnimationMovement b, float t)
        {
            if (a == null && b == null)
            {
                return new();
            }

            if (a == null)
            {
                return Lerp(Vector3.Zero, 0, b.Position, b.Angle, t);
            }
            else if (b == null)
            {
                return Lerp(a.Position, a.Angle, Vector3.Zero, 0f, t);
            }
            else
            {
                return Lerp(a.Position, a.Angle, b.Position, b.Angle, t);
            }
        }

        private static MovementData Lerp(Vector3 aPos, float aAngle, Vector3 bPos, float bAngle, float t)
        {
            var position = Vector3.Lerp(aPos, bPos, t);
            var angle = float.Lerp(aAngle, bAngle, t);

            return new MovementData(position, angle);
        }
    }
}
