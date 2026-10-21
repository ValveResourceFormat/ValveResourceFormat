using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>What a tool sets a gizmo up with for the objects it moves.</summary>
/// <param name="Kind">What the selection is.</param>
/// <param name="IsLocalSpace">Whether the gizmo is turned with the first selected object rather than with the world.</param>
/// <param name="OriginalPivot">Where the pivot is until it is moved: the origin of the first selected object.</param>
/// <param name="Pivot">Where the gizmo is, and what it turns and scales about.</param>
/// <param name="WorldBounds">The bounds of the selection in the world.</param>
/// <param name="LocalBounds">The bounds of the selection in the space of <paramref name="Orientation"/>.</param>
/// <param name="Orientation">Where the first selected object is and how it is turned, without its scale.</param>
/// <param name="WorkPlane">The axes of the world the gizmo follows outside local space.</param>
public readonly record struct GizmoSetup(
    GizmoSelectionKind Kind,
    bool IsLocalSpace,
    Vector3 OriginalPivot,
    Vector3 Pivot,
    AABB WorldBounds,
    AABB LocalBounds,
    Matrix4x4 Orientation,
    Matrix4x4 WorkPlane);

/// <summary>
/// A handle that moves, turns or scales what is selected when one of its parts is dragged, the way Hammer's
/// gizmos do. Each part is drawn as a pick object, and the part under the cursor is the one its shapes are hit
/// nearest by. A drag reports everything it has done since it started, in <see cref="Result"/>.
/// </summary>
public abstract class Gizmo
{
    /// <summary>How long the axes are on the screen, in pixels.</summary>
    protected const float AxisLengthPixels = 100f;

    /// <summary>How wide the lines of the rotate gizmo are on the screen, in pixels.</summary>
    protected const float LineThicknessPixels = 3f;

    /// <summary>How many times wider than its arrow head the band an axis is picked by is, on each side.</summary>
    protected const float AxisPickScale = 1.5f;

    /// <summary>The size of the handle at the center of the translate gizmo.</summary>
    protected const float CenterHandleSize = 4f;

    private const float MinimumAxisPixels = 10f;
    private const float MaximumAxisPixels = 400f;

    // An axis this close to the view hides along with the planes it is in
    private const float HiddenAxisAlignment = 0.999f;

    // How far a ray through the cursor reaches
    private const float RayLength = 4096f * 3f;

    /// <summary>The color of the X axis.</summary>
    protected static readonly Color32 AxisXColor = new(200, 30, 30, 255);

    /// <summary>The color of the Y axis.</summary>
    protected static readonly Color32 AxisYColor = new(50, 200, 30, 255);

    /// <summary>The color of the Z axis.</summary>
    protected static readonly Color32 AxisZColor = new(30, 110, 200, 255);

    /// <summary>The color of the part under the cursor or being dragged.</summary>
    protected static readonly Color32 HighlightColor = new(255, 230, 0, 255);

    /// <summary>The color of where a snapped drag would be without snapping.</summary>
    protected static readonly Color32 GhostColor = new(128, 128, 128, 128);

    private readonly ToolGeometry hitTestGeometry = new();
    private readonly ToolPickTargets hitTestTargets = new();
    private readonly ToolPickTargets renderTargets = new();

    /// <summary>Initializes a gizmo.</summary>
    /// <param name="snapping">How its drags snap, or <see langword="null"/> for not at all.</param>
    protected Gizmo(IGizmoSnapping? snapping)
    {
        Snapping = snapping;
    }

    /// <summary>Gets how the drags snap.</summary>
    protected IGizmoSnapping? Snapping { get; }

    /// <summary>Gets what the current drag has done since it started.</summary>
    public GizmoResult Result { get; } = new();

    /// <summary>Gets the part under the cursor, kept while a part is pressed or dragged.</summary>
    public GizmoPart HoveredPart { get; private set; } = GizmoPart.None;

    /// <summary>Gets the part pressed or being dragged.</summary>
    public GizmoPart ActivePart { get; private set; } = GizmoPart.None;

    /// <summary>Gets whether a part is being dragged.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Gets what the selection is.</summary>
    protected GizmoSelectionKind Kind { get; private set; }

    /// <summary>Gets whether the gizmo is turned with the first selected object.</summary>
    protected bool IsLocalSpace { get; private set; }

    /// <summary>Gets where the pivot is until it is moved.</summary>
    protected Vector3 OriginalPivot { get; private set; }

    /// <summary>Gets where the gizmo is, and what it turns and scales about.</summary>
    public Vector3 Pivot { get; protected set; }

    /// <summary>Gets the bounds of the selection in the world.</summary>
    protected AABB WorldBounds { get; private set; }

    /// <summary>Gets the bounds of the selection in the space of <see cref="Orientation"/>.</summary>
    protected AABB LocalBounds { get; private set; }

    /// <summary>Gets the axes of the gizmo, its rows the X, Y and Z axes.</summary>
    protected Matrix4x4 Frame { get; private set; } = Matrix4x4.Identity;

    /// <summary>Gets where the first selected object is and how it is turned.</summary>
    protected Matrix4x4 Orientation { get; private set; } = Matrix4x4.Identity;

    /// <summary>Gets the axes of the world the gizmo follows outside local space.</summary>
    protected Matrix4x4 WorkPlane { get; private set; } = Matrix4x4.Identity;

    /// <summary>Gets or sets the normal of the plane the cursor is followed in while dragging.</summary>
    protected Vector3 DragPlaneNormal { get; set; }

    /// <summary>Gets or sets how far the plane the cursor is followed in is from the world origin, along its normal.</summary>
    protected float DragPlaneDistance { get; set; }

    /// <summary>Sets the gizmo up for the selection, ending any drag.</summary>
    /// <param name="setup">What the gizmo is set up with.</param>
    public void Setup(in GizmoSetup setup)
    {
        if (IsDragging)
        {
            EndDrag();
        }

        Kind = setup.Kind;
        IsLocalSpace = setup.IsLocalSpace;
        OriginalPivot = setup.OriginalPivot;
        Pivot = setup.Pivot;
        WorldBounds = setup.WorldBounds;
        LocalBounds = setup.LocalBounds;
        Orientation = setup.Orientation;
        WorkPlane = setup.WorkPlane;
        Frame = IsLocalSpace ? Orientation : WorkPlane;

        OnSetup();
    }

    /// <summary>Reacts to the gizmo being set up again.</summary>
    protected virtual void OnSetup()
    {
    }

    /// <summary>Changes the bounds of the selection, as they change while it is dragged.</summary>
    /// <param name="worldBounds">The bounds in the world.</param>
    /// <param name="localBounds">The bounds in the space of the first selected object.</param>
    public void SetBounds(AABB worldBounds, AABB localBounds)
    {
        WorldBounds = worldBounds;
        LocalBounds = localBounds;
    }

    /// <summary>Finds the part under the cursor, unless a part is pressed or dragged.</summary>
    /// <param name="camera">The camera the gizmo is seen with.</param>
    /// <param name="mouse">The cursor, in pixels from the top left of the view.</param>
    public void UpdateHover(Camera camera, Vector2 mouse)
    {
        if (IsDragging || ActivePart != GizmoPart.None)
        {
            return;
        }

        SetHoveredPart(HitTest(camera, mouse));
    }

    /// <summary>Stops showing a part as under the cursor, unless a part is pressed or dragged.</summary>
    public void ClearHover()
    {
        if (!IsDragging && ActivePart == GizmoPart.None)
        {
            SetHoveredPart(GizmoPart.None);
        }
    }

    private void SetHoveredPart(GizmoPart part)
    {
        if (part != HoveredPart)
        {
            HoveredPart = part;
            OnHoveredPartChanged();
        }
    }

    /// <summary>Finds the part the cursor is over, by the shapes the gizmo is drawn and picked with.</summary>
    /// <param name="camera">The camera the gizmo is seen with.</param>
    /// <param name="mouse">The cursor, in pixels from the top left of the view.</param>
    /// <returns>The part, or <see cref="GizmoPart.None"/>.</returns>
    public GizmoPart HitTest(Camera camera, Vector2 mouse)
    {
        ArgumentNullException.ThrowIfNull(camera);

        hitTestGeometry.Clear();
        hitTestTargets.Clear();
        Draw(new ToolRenderContext(hitTestGeometry) { Camera = camera }, camera, hitTestTargets);

        return hitTestTargets.Pick(camera, mouse) is { } part ? (GizmoPart)part : GizmoPart.None;
    }

    /// <summary>Presses a part, which a drag then starts from.</summary>
    /// <param name="part">The part, or <see cref="GizmoPart.None"/> to release it.</param>
    public void SetActivePart(GizmoPart part)
    {
        if (!IsDragging)
        {
            ActivePart = part;
        }
    }

    /// <summary>Starts dragging the pressed part, or the part under the cursor when none is pressed.</summary>
    /// <param name="camera">The camera the gizmo is seen with.</param>
    /// <param name="mouse">Where the drag starts, in pixels from the top left of the view.</param>
    /// <returns>Whether a part is being dragged.</returns>
    public bool BeginDrag(Camera camera, Vector2 mouse)
    {
        ArgumentNullException.ThrowIfNull(camera);

        if (ActivePart == GizmoPart.None)
        {
            ActivePart = HitTest(camera, mouse);
        }

        if (ActivePart == GizmoPart.None)
        {
            return false;
        }

        Result.Reset();
        OnBeginDrag(camera, mouse);
        IsDragging = true;
        FillResult();

        return true;
    }

    /// <summary>Follows the cursor with the dragged part, updating <see cref="Result"/>.</summary>
    /// <param name="camera">The camera the gizmo is seen with.</param>
    /// <param name="mouse">The cursor, in pixels from the top left of the view.</param>
    public void UpdateDrag(Camera camera, Vector2 mouse)
    {
        ArgumentNullException.ThrowIfNull(camera);

        if (!IsDragging)
        {
            return;
        }

        OnUpdateDrag(camera, mouse);
        FillResult();
    }

    /// <summary>Ends the drag, or releases the pressed part.</summary>
    public void EndDrag()
    {
        if (IsDragging)
        {
            OnEndDrag();
            IsDragging = false;
            Result.Reset();
        }

        ActivePart = GizmoPart.None;
    }

    /// <summary>Draws the gizmo, over the scene.</summary>
    /// <param name="output">What the gizmo is drawn into.</param>
    /// <param name="camera">The camera the gizmo is seen with, which it keeps its size on the screen for.</param>
    public void Render(ToolGeometry output, Camera camera)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(camera);

        renderTargets.Clear();
        Draw(new ToolRenderContext(output) { Camera = camera }, camera, renderTargets);
    }

    /// <summary>Draws every part of the gizmo, each inside a pick object of its part.</summary>
    /// <param name="context">What the gizmo is drawn with.</param>
    /// <param name="camera">The camera the gizmo is seen with.</param>
    /// <param name="picks">Where the pick objects go.</param>
    protected abstract void Draw(ToolRenderContext context, Camera camera, ToolPickTargets picks);

    /// <summary>Starts a drag of <see cref="ActivePart"/>.</summary>
    protected abstract void OnBeginDrag(Camera camera, Vector2 mouse);

    /// <summary>Follows the cursor with <see cref="ActivePart"/>.</summary>
    protected abstract void OnUpdateDrag(Camera camera, Vector2 mouse);

    /// <summary>Ends a drag of <see cref="ActivePart"/>.</summary>
    protected virtual void OnEndDrag()
    {
    }

    /// <summary>Reacts to another part coming under the cursor.</summary>
    protected virtual void OnHoveredPartChanged()
    {
    }

    /// <summary>Gets what the drag is, for <see cref="GizmoResult.Flags"/>.</summary>
    protected virtual GizmoResultFlags ResultFlags => GizmoResultFlags.None;

    private void FillResult()
    {
        Result.Part = ActivePart;
        Result.Pivot = Pivot;
        Result.Frame = Frame;
        Result.Orientation = Orientation;
        Result.Flags = ResultFlags;
    }

    /// <summary>Gets an axis of the gizmo.</summary>
    /// <param name="axis">0, 1 or 2 for X, Y or Z.</param>
    protected Vector3 GetAxis(int axis) => axis switch
    {
        0 => new(Frame.M11, Frame.M12, Frame.M13),
        1 => new(Frame.M21, Frame.M22, Frame.M23),
        _ => new(Frame.M31, Frame.M32, Frame.M33),
    };

    /// <summary>Gets the color a part is drawn in.</summary>
    /// <param name="part">The part.</param>
    /// <param name="color">Its color.</param>
    /// <param name="highlighted">The part under the cursor or being dragged.</param>
    /// <param name="enabled">Whether it is the gizmo itself rather than where an unsnapped drag would be.</param>
    protected Color32 GetPartColor(GizmoPart part, Color32 color, GizmoPart highlighted, bool enabled = true)
    {
        if (!enabled)
        {
            return GhostColor;
        }

        if (part == highlighted)
        {
            return HighlightColor;
        }

        if (!IsLocalSpace)
        {
            return color;
        }

        // Local space fades the axes towards grey
        static byte Fade(byte channel) => (byte)(203 - (int)((203 - channel) * 0.9f));

        return new Color32(Fade(color.R), Fade(color.G), Fade(color.B), color.A);
    }

    /// <summary>Gets the color of an axis.</summary>
    protected static Color32 GetAxisColor(int axis) => axis switch
    {
        0 => AxisXColor,
        1 => AxisYColor,
        _ => AxisZColor,
    };

    /// <summary>
    /// Gets how many world units a pixel covers at a point, by how far the ray through the pixel next to it is
    /// from the point at the same distance from the camera. A point behind the camera is taken in front of it.
    /// </summary>
    /// <param name="camera">The camera.</param>
    /// <param name="point">The point.</param>
    /// <param name="pixels">How many pixels away the ray is taken, for precision.</param>
    protected static float GetWorldUnitsPerPixel(Camera camera, Vector3 point, float pixels)
    {
        ArgumentNullException.ThrowIfNull(camera);

        var depth = Vector3.Dot(point - camera.Location, camera.Forward);

        if (depth < 0f)
        {
            point -= 2f * depth * camera.Forward;
        }

        if (camera.WorldToScreenPoint(point) is not { } screen || pixels <= 0f)
        {
            return 0f;
        }

        var distance = Vector3.Distance(point, camera.Location);
        var offset = camera.Location + camera.ScreenPointToDirection(screen + new Vector2(pixels, 0f)) * distance;

        return Vector3.Distance(offset, point) / pixels;
    }

    /// <summary>Gets a size on the screen in world units at a point, the pixels kept between ten and four hundred.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="point">The point.</param>
    /// <param name="pixels">The size, in pixels.</param>
    protected static float GetScreenSize(Camera camera, Vector3 point, float pixels)
    {
        var clamped = pixels < MinimumAxisPixels ? MinimumAxisPixels : MathF.Min(pixels, MaximumAxisPixels);
        return GetWorldUnitsPerPixel(camera, point, clamped) * clamped;
    }

    /// <summary>Gets whether an axis points so nearly along the view of a point that it is hidden.</summary>
    protected static bool IsAlongView(Camera camera, Vector3 point, Vector3 axis)
    {
        ArgumentNullException.ThrowIfNull(camera);

        var view = MathUtils.SafeNormalize(point - camera.Location);
        return MathF.Abs(Vector3.Dot(view, axis)) > HiddenAxisAlignment;
    }

    /// <summary>
    /// Of the two axes other than one, gets the one more along the view, whose plane holds that axis and faces the
    /// camera most.
    /// </summary>
    /// <param name="axis">0, 1 or 2 for X, Y or Z.</param>
    /// <param name="viewDirection">The direction through the cursor.</param>
    protected Vector3 GetPlaneNormalAlongView(int axis, Vector3 viewDirection)
    {
        var a = GetAxis((axis + 1) % 3);
        var b = GetAxis((axis + 2) % 3);

        return MathF.Abs(Vector3.Dot(a, viewDirection)) <= MathF.Abs(Vector3.Dot(b, viewDirection)) ? b : a;
    }

    /// <summary>Sets the plane the cursor is followed in while dragging.</summary>
    protected void SetDragPlane(Vector3 normal, Vector3 point)
    {
        DragPlaneNormal = normal;
        DragPlaneDistance = Vector3.Dot(normal, point);
    }

    /// <summary>
    /// Gets where the ray through the cursor meets the plane the cursor is followed in. Behind the camera, it is
    /// as far along the ray as it reaches.
    /// </summary>
    protected Vector3 IntersectMouseWithDragPlane(Camera camera, Vector2 mouse)
    {
        ArgumentNullException.ThrowIfNull(camera);

        var start = camera.Location;
        var delta = camera.ScreenPointToDirection(mouse) * RayLength;
        var denominator = Vector3.Dot(DragPlaneNormal, delta);
        var t = denominator != 0f ? (DragPlaneDistance - Vector3.Dot(DragPlaneNormal, start)) / denominator : 0f;

        if (t < 0f)
        {
            t = 1f;
        }

        return start + delta * t;
    }

    /// <summary>Gets a direction in the space of the gizmo.</summary>
    protected Vector3 ToFrame(Vector3 direction)
        => Matrix4x4.Invert(Frame, out var inverse) ? Vector3.TransformNormal(direction, inverse) : direction;

    /// <summary>Gets the frame of the gizmo with its origin at a point.</summary>
    protected Matrix4x4 GetFrameAt(Vector3 origin) => Frame with { Translation = origin };
}
