using System;
using System.Collections.Generic;
using System.Text;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.SceneNodes;

class WorldPanelSceneNode : ShapeSceneNode
{
    public enum Orientation
    {
        Default = 0,
        FaceUser = 1,
        FaceUserUpright = 2,
    }

    private Orientation orientation;
    private Matrix4x4 rigidTransform;

    public WorldPanelSceneNode(Scene scene, float width, float height, Orientation orientation, Matrix4x4 rigidTransform, Color32 color) : base(scene, new Vector3(0f), new Vector3(width, height, 0f), color)
    {
        this.orientation = orientation;
        this.rigidTransform = rigidTransform;
    }

    public override void Update(Scene.UpdateContext context)
    {
        if (orientation == Orientation.Default)
        {
            base.Update(context);
            return;
        }
        var rotation = context.Camera.BillboardMatrix;
        if (orientation == Orientation.FaceUserUpright)
        {
            var normal = context.Camera.Location - rigidTransform.Translation;
            normal.Z = 0f;
            normal = normal.LengthSquared() > 1e-6f ? Vector3.Normalize(normal) : Vector3.UnitX;

            var right = Vector3.Cross(Vector3.UnitZ, normal);
            rotation = new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                0f, 0f, 1f, 0f,
                normal.X, normal.Y, normal.Z, 0f,
                0f, 0f, 0f, 1f);
        }

        Transform = rotation
            * Matrix4x4.CreateTranslation(rigidTransform.Translation);
    }
}
