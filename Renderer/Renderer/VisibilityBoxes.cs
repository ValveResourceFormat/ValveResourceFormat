namespace ValveResourceFormat.Renderer;

/// <summary>What an <c>info_visibility_box</c> hides, from its <c>cull_mode</c>.</summary>
public enum VisibilityBoxMode
{
    /// <summary>Hides objects entirely inside the box.</summary>
    Inside,

    /// <summary>Hides objects that touch none of the outside boxes.</summary>
    Outside,

    /// <summary>Counts as <see cref="Outside"/> for a view whose eye is inside the box, and is ignored otherwise.</summary>
    OutsideWhenEyeInside,
}

/// <summary>
/// One <c>info_visibility_box</c> volume: an oriented box centred on its origin, held as six inward facing planes.
/// </summary>
/// <param name="mode">What the box hides.</param>
/// <param name="size">The full edge lengths along the box's own axes.</param>
public sealed class VisibilityBox(VisibilityBoxMode mode, Vector3 size)
{
    private readonly Vector4[] planes = new Vector4[6];

    /// <summary>Gets what the box hides.</summary>
    public VisibilityBoxMode Mode { get; } = mode;

    /// <summary>Gets the full edge lengths along the box's own axes.</summary>
    public Vector3 Size { get; } = size;

    /// <summary>Gets whether the box culls.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Enables the box at a placement. An enabled box keeps its planes, so a new placement only takes
    /// effect after <see cref="Disable"/>.
    /// </summary>
    /// <param name="transform">The box's rigid world transform; scale is ignored.</param>
    public void Enable(Matrix4x4 transform)
    {
        if (IsEnabled)
        {
            return;
        }

        var origin = transform.Translation;

        for (var axis = 0; axis < 3; axis++)
        {
            var (direction, halfSize) = axis switch
            {
                0 => (new Vector3(transform.M11, transform.M12, transform.M13), Size.X * 0.5f),
                1 => (new Vector3(transform.M21, transform.M22, transform.M23), Size.Y * 0.5f),
                _ => (new Vector3(transform.M31, transform.M32, transform.M33), Size.Z * 0.5f),
            };

            direction = Vector3.Normalize(direction);

            planes[axis] = InwardPlane(-direction, origin + direction * halfSize);
            planes[axis + 3] = InwardPlane(direction, origin - direction * halfSize);
        }

        IsEnabled = true;
    }

    /// <summary>Disables the box.</summary>
    public void Disable() => IsEnabled = false;

    private static Vector4 InwardPlane(Vector3 normal, Vector3 point) => new(normal, -Vector3.Dot(normal, point));

    /// <summary>The smallest signed distance over the six planes of the box corner nearest (<paramref name="sign"/> -1) or farthest (+1) along each plane.</summary>
    private float MinPlaneDistance(Vector3 center, Vector3 extents, float sign)
    {
        var distance = float.MaxValue;

        foreach (var plane in planes)
        {
            var normal = new Vector3(plane.X, plane.Y, plane.Z);
            var radius = Vector3.Dot(Vector3.Abs(normal), extents);

            distance = MathF.Min(distance, Vector3.Dot(normal, center) + plane.W + sign * radius);
        }

        return distance;
    }

    /// <summary>Whether a point is inside the box.</summary>
    public bool Contains(Vector3 point) => MinPlaneDistance(point, Vector3.Zero, 0f) >= 0f;

    /// <summary>Whether an axis aligned box lies entirely inside this one.</summary>
    public bool Contains(in AABB bounds) => MinPlaneDistance(bounds.Center, bounds.Size * 0.5f, -1f) >= 0f;

    /// <summary>
    /// Whether an axis aligned box reaches the inner side of every plane. Exact for a box along the world
    /// axes; for a rotated one it can also pass bounds just outside a corner or edge.
    /// </summary>
    public bool Touches(in AABB bounds) => MinPlaneDistance(bounds.Center, bounds.Size * 0.5f, 1f) >= 0f;
}

/// <summary>
/// The boxes one view culls with, split once for the view's eye: an object is culled when it lies
/// entirely inside any inside box, or when there are outside boxes and it touches none of them.
/// </summary>
public sealed class VisibilityBoxCuller
{
    private readonly List<VisibilityBox> inside = [];
    private readonly List<VisibilityBox> outside = [];

    /// <summary>Gets whether any box takes part in this view.</summary>
    public bool IsActive => inside.Count > 0 || outside.Count > 0;

    /// <summary>Gets how many boxes cull what they contain.</summary>
    public int InsideCount => inside.Count;

    /// <summary>Gets how many boxes cull what lies outside them.</summary>
    public int OutsideCount => outside.Count;

    /// <summary>Splits the enabled boxes for a view.</summary>
    /// <param name="boxes">The boxes of the view's scene.</param>
    /// <param name="eye">The view's eye, or <see langword="null"/> for a view that culls with no box.</param>
    public void Build(IReadOnlyList<VisibilityBox> boxes, Vector3? eye)
    {
        inside.Clear();
        outside.Clear();

        if (eye is not { } eyePosition)
        {
            return;
        }

        foreach (var box in boxes)
        {
            if (!box.IsEnabled)
            {
                continue;
            }

            switch (box.Mode)
            {
                case VisibilityBoxMode.Inside:
                    inside.Add(box);
                    break;

                case VisibilityBoxMode.Outside:
                    outside.Add(box);
                    break;

                case VisibilityBoxMode.OutsideWhenEyeInside when box.Contains(eyePosition):
                    outside.Add(box);
                    break;
            }
        }
    }

    /// <summary>Whether this view's boxes hide an object with the given world bounds.</summary>
    public bool IsCulled(in AABB bounds)
    {
        foreach (var box in inside)
        {
            if (box.Contains(bounds))
            {
                return true;
            }
        }

        if (outside.Count == 0)
        {
            return false;
        }

        foreach (var box in outside)
        {
            if (box.Touches(bounds))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The <c>info_visibility_box</c> volumes of one scene. A box only culls in the scene it was spawned in,
/// so one in a 3D sky affects only the sky.
/// </summary>
public sealed class VisibilityBoxSet
{
    private readonly List<VisibilityBox> boxes = [];
    private Vector3? viewEye;

    /// <summary>Gets or sets whether the boxes cull at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets how many boxes the scene has, enabled or not.</summary>
    public int Count => boxes.Count;

    /// <summary>Gets the boxes the scene's own view culls with this frame, from <see cref="PrepareView"/>.</summary>
    public VisibilityBoxCuller View { get; } = new();

    /// <summary>Gets scratch lists for a view with an eye of its own, such as a light's shadow.</summary>
    internal VisibilityBoxCuller SecondaryView { get; } = new();

    /// <summary>Adds a box to the scene.</summary>
    public void Add(VisibilityBox box) => boxes.Add(box);

    /// <summary>Removes a box from the scene.</summary>
    public void Remove(VisibilityBox box) => boxes.Remove(box);

    /// <summary>Splits the boxes for the scene's view this frame.</summary>
    /// <param name="eye">Where the view is, or <see langword="null"/> when nothing may be culled this frame.</param>
    public void PrepareView(Vector3? eye)
    {
        viewEye = Enabled ? eye : null;
        View.Build(boxes, viewEye);
    }

    /// <summary>Splits the boxes into <see cref="SecondaryView"/> for another eye, culling nothing when <see cref="PrepareView"/> culled nothing.</summary>
    internal VisibilityBoxCuller PrepareSecondaryView(Vector3 eye)
    {
        SecondaryView.Build(boxes, viewEye.HasValue ? eye : null);
        return SecondaryView;
    }
}
