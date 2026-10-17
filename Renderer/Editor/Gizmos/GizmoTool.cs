using System.Globalization;
using System.Linq;
using ValveResourceFormat.Renderer.Editor.Selection;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>Which gizmo the selection is shown with, as Hammer's manipulation modes pick it.</summary>
public enum ManipulationMode
{
    /// <summary>The handles on the faces of the bounds of the selection, which scale it.</summary>
    Select,

    /// <summary>The translate gizmo.</summary>
    Translate,

    /// <summary>The rotate gizmo.</summary>
    Rotate,

    /// <summary>The scale gizmo.</summary>
    Scale,
}

/// <summary>Which axes a gizmo follows.</summary>
public enum GizmoTransformSpace
{
    /// <summary>Those of the world.</summary>
    World,

    /// <summary>Those of the first selected object.</summary>
    Local,
}

/// <summary>
/// Shows the gizmo of the current manipulation mode on what is selected, and moves it by the drags of the gizmo,
/// the way Hammer's selection tool does. The gizmo starts at the origin of the first selected object, follows the
/// axes of the world or of that object, and each drag is applied in full from where the selection was when it
/// started. Drags snap to the grid unless snapping is off, which holding Ctrl reverses.
/// </summary>
/// <remarks>
/// It is used from the render thread, except for <see cref="IsHovered"/> and <see cref="IsDragging"/>, which the UI
/// thread reads to know whether a press is the gizmo's.
/// </remarks>
public sealed class GizmoTool : IGizmoSnapping
{
    private const float MinimumGridSize = 0.125f;
    private const float MaximumGridSize = 512f;

    // How small a scaled box may become while not snapping
    private const float UnsnappedMinimumSize = 0.125f;

    private static readonly float[] RotationSnapAngles = [0.25f, 0.5f, 1f, 5f, 5.625f, 11.25f, 15f, 22.5f, 30f, 45f, 90f];

    private readonly SelectionSet selection;
    private readonly Func<SceneNode, IGizmoTransformable?> findTransformable;
    private readonly TranslateGizmo translateGizmo;
    private readonly RotateGizmo rotateGizmo;
    private readonly ScaleGizmo scaleGizmo;
    private readonly BoxScaleGizmo boxScaleGizmo;
    private readonly List<IGizmoTransformable> objects = [];

    private IReadOnlyList<SceneNode>? selectedNodes;
    private Gizmo? shownGizmo;
    private ManipulationMode setupMode;
    private GizmoTransformSpace setupSpace;
    private bool needsSetup;

    // The drag button as Hammer's gizmo manipulator follows it: pressed and where, released or undone since the last
    // update, and whether it is held on a part
    private Vector2? pendingPress;
    private bool pendingRelease;
    private bool pendingAbort;
    private bool isPressed;
    private Vector2 pressedAt;

    private (ManipulationMode Mode, GizmoTransformSpace Space, float GridSize, float RotationSnapAngle, bool Snapping)? statusState;
    private string statusText = string.Empty;
    private Vector3 pivot;
    private Vector3 pivotAtDragStart;
    private Matrix4x4 snapFrame = Matrix4x4.Identity;
    private volatile bool isShown;
    private volatile bool isHovered;
    private volatile bool isDragging;

    /// <summary>Initializes the tool for a selection.</summary>
    /// <param name="selection">What is selected.</param>
    /// <param name="findTransformable">Finds what a gizmo moves for a selected node, or <see langword="null"/> when it moves nothing.</param>
    public GizmoTool(SelectionSet selection, Func<SceneNode, IGizmoTransformable?> findTransformable)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(findTransformable);

        this.selection = selection;
        this.findTransformable = findTransformable;

        translateGizmo = new TranslateGizmo(this);
        rotateGizmo = new RotateGizmo(this);
        scaleGizmo = new ScaleGizmo(this);
        boxScaleGizmo = new BoxScaleGizmo(this);
    }

    /// <summary>Gets or sets which gizmo the selection is shown with.</summary>
    public ManipulationMode Mode { get; set; } = ManipulationMode.Translate;

    /// <summary>Gets or sets which axes the gizmo follows.</summary>
    public GizmoTransformSpace Space { get; set; } = GizmoTransformSpace.World;

    /// <summary>Gets or sets whether drags snap, unless <see cref="ToggleSnapping"/> reverses it.</summary>
    public bool SnappingEnabled { get; set; } = true;

    /// <summary>Gets or sets whether snapping is reversed, as it is while Ctrl is held.</summary>
    public bool ToggleSnapping { get; set; }

    /// <summary>Gets or sets whether turns snap while drags do.</summary>
    public bool RotationSnappingEnabled { get; set; } = true;

    /// <inheritdoc/>
    public float GridSize { get; private set; } = 64f;

    /// <summary>Gets the angle turns snap to, in degrees.</summary>
    public float RotationSnapAngle { get; private set; } = 15f;

    /// <summary>Gets or sets whether a face of the bounds scales about their center rather than the opposite face.</summary>
    public bool BoxScalesAboutCenter
    {
        get => boxScaleGizmo.ScalesAboutCenter;
        set => boxScaleGizmo.ScalesAboutCenter = value;
    }

    /// <summary>Gets or sets whether a face of the bounds scales without snapping, every axis it drags by the same.</summary>
    public bool BoxScalesUniformly
    {
        get => boxScaleGizmo.ScalesUniformly;
        set => boxScaleGizmo.ScalesUniformly = value;
    }

    /// <inheritdoc/>
    public bool IsSnappingEnabled => SnappingEnabled != ToggleSnapping;

    /// <inheritdoc/>
    public bool IsRotationSnappingEnabled => IsSnappingEnabled && RotationSnappingEnabled;

    /// <summary>Gets whether a gizmo was shown at the last update, from any thread.</summary>
    public bool IsShown => isShown;

    /// <summary>Gets whether a part of the gizmo was under the cursor at the last update, from any thread.</summary>
    public bool IsHovered => isHovered;

    /// <summary>Gets whether a part of the gizmo is being dragged, from any thread.</summary>
    public bool IsDragging => isDragging;

    /// <summary>Gets the shapes of the gizmo, in world space, as the last update drew them.</summary>
    public ToolGeometry Shapes { get; } = new();

    /// <summary>Gets the mode and space, the size of the grid and the rotation snap, and whether drags snap, as a line of text.</summary>
    public string StatusText
    {
        get
        {
            var state = (Mode, Space, GridSize, RotationSnapAngle, IsSnappingEnabled);

            if (statusState != state)
            {
                statusState = state;

                var snapping = IsSnappingEnabled ? string.Empty : "    Snapping off";
                statusText = string.Create(CultureInfo.InvariantCulture, $"{Mode} ({Space})    Grid {GridSize}    Rotation snap {RotationSnapAngle}{snapping}");
            }

            return statusText;
        }
    }

    /// <summary>Presses the button that drags, which the next update passes to the part under where it was pressed.</summary>
    /// <param name="position">Where it was pressed, in pixels from the top left of the view.</param>
    public void Press(Vector2 position)
    {
        pendingPress = position;
    }

    /// <summary>Releases the button that drags, ending the drag where it is.</summary>
    public void Release()
    {
        pendingRelease = true;
    }

    /// <summary>Switches the gizmo between the axes of the world and those of the first selected object.</summary>
    public void ToggleSpace()
    {
        Space = Space == GizmoTransformSpace.World ? GizmoTransformSpace.Local : GizmoTransformSpace.World;
    }

    /// <summary>Undoes the drag, putting what it moved back where it was.</summary>
    public void Abort()
    {
        pendingAbort = true;
    }

    /// <summary>Halves the size of the grid, down to an eighth of a unit.</summary>
    public void DecreaseGridSize()
    {
        GridSize = MathF.Max(MinimumGridSize, GridSize * 0.5f);
    }

    /// <summary>Doubles the size of the grid, up to 512 units.</summary>
    public void IncreaseGridSize()
    {
        GridSize = MathF.Min(MaximumGridSize, GridSize * 2f);
    }

    /// <summary>Snaps turns to the next smaller of Hammer's rotation snap angles.</summary>
    public void DecreaseRotationSnapAngle()
    {
        RotationSnapAngle = RotationSnapAngles[Math.Max(GetRotationSnapIndex() - 1, 0)];
    }

    /// <summary>Snaps turns to the next larger of Hammer's rotation snap angles.</summary>
    public void IncreaseRotationSnapAngle()
    {
        RotationSnapAngle = RotationSnapAngles[Math.Min(GetRotationSnapIndex() + 1, RotationSnapAngles.Length - 1)];
    }

    // The first angle no smaller than the current one, or the last
    private int GetRotationSnapIndex()
    {
        var index = Array.FindIndex(RotationSnapAngles, angle => angle >= RotationSnapAngle);
        return index < 0 ? RotationSnapAngles.Length - 1 : index;
    }

    /// <summary>
    /// Shows the gizmo of the mode on the selection, follows the cursor with it, and moves the selection while a part
    /// of it is dragged.
    /// </summary>
    /// <param name="camera">The camera the view is seen with.</param>
    /// <param name="mouse">Where the cursor is, in pixels from the top left of the view.</param>
    /// <param name="canHover">Whether the cursor is over the view and not moving the camera, so it can point at a part.</param>
    public void Update(Camera camera, Vector2 mouse, bool canHover)
    {
        ArgumentNullException.ThrowIfNull(camera);

        UpdateSelection();

        var gizmo = GetGizmo();

        if (gizmo != shownGizmo)
        {
            if (shownGizmo != null)
            {
                EndDrag(shownGizmo, commit: true);
                shownGizmo.ClearHover();
            }

            shownGizmo = gizmo;
            needsSetup = true;
        }

        Shapes.Clear();

        var press = pendingPress;
        var released = pendingRelease;
        var aborted = pendingAbort;
        (pendingPress, pendingRelease, pendingAbort) = (null, false, false);

        isShown = gizmo != null;

        if (gizmo == null)
        {
            isHovered = false;
            return;
        }

        if (needsSetup || setupMode != Mode || setupSpace != Space)
        {
            Setup(gizmo);
        }

        if (aborted && gizmo.IsDragging)
        {
            EndDrag(gizmo, commit: false);
        }
        else if (isPressed)
        {
            UpdatePress(gizmo, camera, mouse, released);
        }
        else if (press is { } at && gizmo.HitTest(camera, at) is var part and not GizmoPart.None)
        {
            // A drag only starts once the cursor moves, so a click on the gizmo does nothing
            isPressed = true;
            pressedAt = at;
            gizmo.SetActivePart(part);
            UpdatePress(gizmo, camera, mouse, released);
        }
        else if (canHover)
        {
            gizmo.UpdateHover(camera, mouse);
        }
        else
        {
            gizmo.ClearHover();
        }

        isHovered = gizmo.HoveredPart != GizmoPart.None;
        isDragging = gizmo.IsDragging;

        gizmo.Render(Shapes, camera);
    }

    private void UpdatePress(Gizmo gizmo, Camera camera, Vector2 mouse, bool released)
    {
        if (!gizmo.IsDragging && mouse != pressedAt && !released && gizmo.BeginDrag(camera, pressedAt))
        {
            pivotAtDragStart = pivot;

            foreach (var transformable in objects)
            {
                transformable.BeginTransform();
            }
        }

        if (gizmo.IsDragging)
        {
            gizmo.UpdateDrag(camera, mouse);
            Apply(gizmo);
        }

        if (released)
        {
            EndDrag(gizmo, commit: true);
        }
    }

    // Every object goes back to where it was when the drag started and takes all of it
    private void Apply(Gizmo gizmo)
    {
        var result = gizmo.Result;

        foreach (var transformable in objects)
        {
            transformable.ApplyGizmoTransform(result);
        }

        // The pivot moves along with what is moved, but stays where it is for a turn or a scale
        if ((result.Flags & (GizmoResultFlags.Scale | GizmoResultFlags.Rotate)) == 0)
        {
            pivot = Vector3.Transform(pivotAtDragStart, result.Transform);
        }

        var (worldBounds, localBounds) = GetBounds();
        gizmo.SetBounds(worldBounds, localBounds);
    }

    private void EndDrag(Gizmo gizmo, bool commit)
    {
        var wasDragging = gizmo.IsDragging;

        gizmo.EndDrag();
        isPressed = false;
        isDragging = false;

        if (!wasDragging)
        {
            return;
        }

        foreach (var transformable in objects)
        {
            transformable.EndTransform(commit);
        }

        // A box scale puts the pivot back where the selection starts it, as does undoing a drag
        if (!commit)
        {
            pivot = pivotAtDragStart;
        }
        else if (gizmo == boxScaleGizmo && objects.Count > 0)
        {
            pivot = objects[0].Origin;
        }

        needsSetup = true;
    }

    // The objects of the selected nodes, each once and none moved by another one selected, in the order selected
    private void UpdateSelection()
    {
        var nodes = selection.Nodes;

        if (ReferenceEquals(nodes, selectedNodes))
        {
            return;
        }

        if (shownGizmo != null)
        {
            EndDrag(shownGizmo, commit: true);
        }

        selectedNodes = nodes;
        objects.Clear();

        foreach (var node in nodes)
        {
            if (findTransformable(node) is { } transformable && !objects.Contains(transformable))
            {
                objects.Add(transformable);
            }
        }

        objects.RemoveAll(transformable => HasSelectedParent(transformable));

        // Every change of the selection puts the pivot at the origin of its first object
        pivot = objects.Count > 0 ? objects[0].Origin : Vector3.Zero;
        needsSetup = true;
    }

    private bool HasSelectedParent(IGizmoTransformable transformable)
    {
        for (var parent = transformable.Parent; parent != null; parent = parent.Parent)
        {
            if (objects.Contains(parent))
            {
                return true;
            }
        }

        return false;
    }

    // The gizmo of the mode, when every selected object can be moved that way
    private Gizmo? GetGizmo()
    {
        if (objects.Count == 0)
        {
            return null;
        }

        var (gizmo, required) = Mode switch
        {
            ManipulationMode.Select => ((Gizmo)boxScaleGizmo, GizmoTransformModes.Scale | GizmoTransformModes.BoxScale),
            ManipulationMode.Rotate => (rotateGizmo, GizmoTransformModes.Rotate),
            ManipulationMode.Scale => (scaleGizmo, GizmoTransformModes.Scale),
            _ => (translateGizmo, GizmoTransformModes.Translate),
        };

        return objects.All(transformable => (transformable.SupportedTransformModes & required) == required) ? gizmo : null;
    }

    private void Setup(Gizmo gizmo)
    {
        if (gizmo.IsDragging)
        {
            return;
        }

        var first = objects[0];
        var isLocal = Space == GizmoTransformSpace.Local;

        // Local space follows the first object, though several objects only follow it while moved
        var orientation = objects.Count == 1 || (isLocal && Mode == ManipulationMode.Translate)
            ? first.RigidTransform
            : Matrix4x4.Identity;

        snapFrame = isLocal ? orientation : Matrix4x4.Identity;

        var (worldBounds, localBounds) = GetBounds(orientation);

        gizmo.Setup(new GizmoSetup(
            objects.Count > 1 ? GizmoSelectionKind.Multiple : GizmoSelectionKind.Single,
            isLocal,
            first.Origin,
            pivot,
            worldBounds,
            localBounds,
            orientation,
            Matrix4x4.Identity));

        setupMode = Mode;
        setupSpace = Space;
        needsSetup = false;
    }

    private (AABB World, AABB Local) GetBounds() => GetBounds(snapFrame);

    // The bounds of the selection in the world, and in the space of the first object
    private (AABB World, AABB Local) GetBounds(Matrix4x4 orientation)
    {
        var toLocal = Matrix4x4.Invert(orientation, out var inverse) ? inverse : Matrix4x4.Identity;
        var world = objects[0].GetBounds(Matrix4x4.Identity);
        var local = objects[0].GetBounds(toLocal);

        for (var i = 1; i < objects.Count; i++)
        {
            world = world.Union(objects[i].GetBounds(Matrix4x4.Identity));
            local = local.Union(objects[i].GetBounds(toLocal));
        }

        return (world, local);
    }

    /// <inheritdoc/>
    public float SnapAngle(float degrees) => (int)(degrees / RotationSnapAngle) * RotationSnapAngle;

    /// <inheritdoc/>
    public float ClampMinimumSize(float size) => MathF.Max(IsSnappingEnabled ? GridSize : UnsnappedMinimumSize, size);

    /// <inheritdoc/>
    public bool SnapPosition(ref Vector3 position, GizmoSnapConstraint constraint, Vector3 direction)
    {
        if (!Matrix4x4.Invert(snapFrame, out var toGrid))
        {
            return false;
        }

        // To the nearest line of the grid in its own space
        var onGrid = Vector3.Transform(position, toGrid) / GridSize;
        onGrid = new Vector3(MathF.Floor(onGrid.X + 0.5f), MathF.Floor(onGrid.Y + 0.5f), MathF.Floor(onGrid.Z + 0.5f)) * GridSize;

        var offset = Vector3.Transform(onGrid, snapFrame) - position;

        offset = constraint switch
        {
            GizmoSnapConstraint.Axis => direction * Vector3.Dot(offset, direction),
            GizmoSnapConstraint.Plane => offset - direction * Vector3.Dot(offset, direction),
            _ => offset,
        };

        position += offset;
        return true;
    }
}
