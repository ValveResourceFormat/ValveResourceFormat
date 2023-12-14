using Datamodel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class AnimationMovement
    {
        public int EndFrame { get; set; }
        public MotionFlag MotionFlags { get; set; }
        public float V0 { get; set; }
        public float V1 { get; set; }
        public float Angle { get; set; }
        public Vector3 Vector { get; set; }
        public Vector3 Position { get; set; }

        public AnimationMovement(IKeyValueCollection frameBlock)
        {
            EndFrame = frameBlock.GetInt32Property("endframe");
            MotionFlags = (MotionFlag)frameBlock.GetInt32Property("motionflags");
            V0 = frameBlock.GetInt32Property("v0");
            V1 = frameBlock.GetInt32Property("v1");
            Angle = frameBlock.GetFloatProperty("angle");
            Vector = new Vector3(frameBlock.GetFloatArray("vector"));
            Position = new Vector3(frameBlock.GetFloatArray("position"));
        }

        public Matrix4x4 GetMatrix()
        {
            var rotationRad = Angle * 0.0174532925f; //Deg to rad
            return Matrix4x4.CreateRotationZ(rotationRad) * Matrix4x4.CreateTranslation(Position);
        }
    }
}
