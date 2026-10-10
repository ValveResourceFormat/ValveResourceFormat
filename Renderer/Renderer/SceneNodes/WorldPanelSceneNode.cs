using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// The rectangle a Panorama world panel is drawn on. It lies in its local XY plane facing +Z, with
/// the width along X and the height along Y.
/// </summary>
internal sealed class WorldPanelSceneNode : ShapeSceneNode
{
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/client/WorldTextPanelOrientation_t">WorldTextPanelOrientation_t</seealso>
    public enum PanelOrientation
    {
        Default = 0,
        FaceUser = 1,
        FaceUserUpright = 2,
    }

    private readonly PanelOrientation orientation;

    public WorldPanelSceneNode(Scene scene, Vector2 size, Vector2 anchor, PanelOrientation orientation, Color32 color)
        : base(scene, new Vector3(-size * anchor, 0f), new Vector3(size - size * anchor, 0f), color)
    {
        this.orientation = orientation;
    }

    public override void Update(Scene.UpdateContext context)
    {
        if (orientation is not PanelOrientation.FaceUser and not PanelOrientation.FaceUserUpright)
        {
            return;
        }

        // Only the position is kept, the panel's own rotation is replaced by one facing the camera
        var position = Transform.Translation;
        var normal = Flatten(context.Camera.Location - position);

        // Too close to the panel for its direction to mean anything, so face back along the view instead
        if (normal.LengthSquared() < 1f)
        {
            normal = Flatten(-context.Camera.Forward);
        }

        // The frame's left and up span the panel and forward is its normal. Left is the viewer's right,
        // since the frame faces the viewer.
        var frame = EntityTransformHelper.ForwardDirectionToRotationMatrix(normal);

        Transform = new Matrix4x4(
            frame.M21, frame.M22, frame.M23, 0f,
            frame.M31, frame.M32, frame.M33, 0f,
            frame.M11, frame.M12, frame.M13, 0f,
            position.X, position.Y, position.Z, 1f);
    }

    private Vector3 Flatten(Vector3 direction)
        => orientation == PanelOrientation.FaceUserUpright ? direction with { Z = 0f } : direction;
}
