using System.Globalization;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// The bone controllers of a model, grouped by the input that drives them. The input is a value in the units of the
/// first controller that reads it, which turns into a setting from zero to one that every controller of the input
/// maps onto its own range.
/// </summary>
internal sealed class Source1BoneControllerSet
{
    private const int ChannelCount = 6;
    private const int RotationMask = 0x38;

    // Inputs past the first four are driven by the mouth
    private const int MaxPlainInputField = 3;

    // A rotation is split into steps of at most a quarter turn, which a blend between two rotations follows exactly
    private const float MaxRotationStep = 90f;

    /// <summary>The controllers that read one input and the bones they move.</summary>
    internal sealed class Group(int index, int inputField, Source1BoneController first, int[] bones)
    {
        /// <summary>Position of the group in <see cref="Source1BoneControllerSet.Groups"/>.</summary>
        public int Index { get; } = index;

        /// <summary>The name of the pose parameter that drives the group.</summary>
        public string Name { get; } = inputField > MaxPlainInputField
            ? "mouth"
            : string.Create(CultureInfo.InvariantCulture, $"controller{inputField}");

        /// <summary>The controller whose range gives the units of the input.</summary>
        public Source1BoneController First { get; } = first;

        /// <summary>The lowest value of the input.</summary>
        public float Low { get; } = MathF.Min(first.Start, first.End);

        /// <summary>The highest value of the input.</summary>
        public float High { get; } = MathF.Max(first.Start, first.End);

        /// <summary>The value of the input when nothing sets it: zero, held within its range.</summary>
        public float Rest => Math.Clamp(0f, Low, High);

        /// <summary>Whether the input turns a bone.</summary>
        public bool IsRotation { get; } = (first.Type & RotationMask) != 0;

        /// <summary>The bones the controllers of the group move.</summary>
        public int[] Bones { get; } = bones;

        /// <summary>Gets the setting from zero to one that a value of the input gives.</summary>
        public float GetSetting(float input)
        {
            var start = First.Start;
            var end = First.End;

            if (IsRotation)
            {
                input = WrapAngle(input, start, end);
            }

            return Math.Clamp((input - start) / (end - start), 0f, 1f);
        }

        // An angle whose range is a full turn wraps around it, any other range takes the angle closest to its middle
        private static float WrapAngle(float angle, float start, float end)
        {
            if (end < start)
            {
                angle = -angle;
            }

            if (start + 359f < end)
            {
                if (angle > 360f)
                {
                    return angle - ((int)(angle / 360f) * 360f);
                }

                if (angle < 0f)
                {
                    return angle + ((int)(1f - (angle / 360f)) * 360f);
                }

                return angle;
            }

            var middle = (start + end) * 0.5f;

            if (angle > middle + 180f)
            {
                angle -= 360f;
            }

            if (angle < middle - 180f)
            {
                angle += 360f;
            }

            return angle;
        }

        /// <summary>Gets the values of the input that split the way from <see cref="Rest"/> to <paramref name="end"/> into steps.</summary>
        public float[] GetSteps(float end)
        {
            var distance = MathF.Abs(end - Rest);
            var stepCount = IsRotation ? (int)MathF.Ceiling(distance / MaxRotationStep) : 1;
            var steps = new float[stepCount + 1];

            for (var i = 0; i <= stepCount; i++)
            {
                steps[i] = Rest + ((end - Rest) * i / stepCount);
            }

            return steps;
        }
    }

    private readonly Source1Model source;
    private readonly Dictionary<int, Group> groupsByInputField = [];

    /// <summary>The groups that move at least one bone.</summary>
    public Group[] Groups { get; }

    /// <summary>The bones that any group moves.</summary>
    public int[] Bones { get; }

    private Source1BoneControllerSet(Source1Model source, Group[] groups)
    {
        this.source = source;
        Groups = groups;

        var bones = new SortedSet<int>();

        foreach (var group in groups)
        {
            bones.UnionWith(group.Bones);
            groupsByInputField[group.First.InputField] = group;
        }

        Bones = [.. bones];
    }

    /// <summary>Creates the set of a model.</summary>
    /// <returns>The set, or <see langword="null"/> when no controller of the model has a range to move a bone by.</returns>
    public static Source1BoneControllerSet? Create(Source1Model source)
    {
        var groups = new List<Group>();
        var seenFields = new HashSet<int>();

        foreach (var controller in source.BoneControllers)
        {
            if (!seenFields.Add(controller.InputField) || controller.Start == controller.End)
            {
                continue;
            }

            var bones = FindBones(source, controller.InputField);

            if (bones.Length > 0)
            {
                groups.Add(new Group(groups.Count, controller.InputField, controller, bones));
            }
        }

        return groups.Count == 0 ? null : new Source1BoneControllerSet(source, [.. groups]);
    }

    private static int[] FindBones(Source1Model source, int inputField)
    {
        var bones = new List<int>();

        for (var bone = 0; bone < source.Bones.Length; bone++)
        {
            foreach (var index in source.Bones[bone].Controllers)
            {
                if ((uint)index < (uint)source.BoneControllers.Length && source.BoneControllers[index].InputField == inputField)
                {
                    bones.Add(bone);
                    break;
                }
            }
        }

        return [.. bones];
    }

    /// <summary>Gets the value every input has when nothing sets it.</summary>
    public float[] GetRestValues()
    {
        var values = new float[Groups.Length];

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Groups[i].Rest;
        }

        return values;
    }

    /// <summary>Moves every bone a controller acts on.</summary>
    /// <param name="values">The value of the input of every group.</param>
    /// <param name="positions">The position of every bone relative to its parent.</param>
    /// <param name="rotations">The rotation of every bone relative to its parent.</param>
    public void Apply(ReadOnlySpan<float> values, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        foreach (var bone in Bones)
        {
            Apply(bone, values, ref positions[bone], ref rotations[bone]);
        }
    }

    /// <summary>Moves a bone by the controllers that act on it.</summary>
    /// <param name="bone">The index of the bone.</param>
    /// <param name="values">The value of the input of every group.</param>
    /// <param name="position">The position of the bone relative to its parent.</param>
    /// <param name="rotation">The rotation of the bone relative to its parent.</param>
    public void Apply(int bone, ReadOnlySpan<float> values, ref Vector3 position, ref Quaternion rotation)
    {
        var controllers = source.Bones[bone].Controllers;

        for (var channel = 0; channel < ChannelCount; channel++)
        {
            if (!TryGetValue(controllers[channel], values, out var value))
            {
                continue;
            }

            switch (channel)
            {
                case 0:
                    position.X += value;
                    break;
                case 1:
                    position.Y += value;
                    break;
                case 2:
                    position.Z += value;
                    break;
                case 3:
                    rotation = Rotate(rotation, Vector3.UnitX, value);
                    break;
                case 4:
                    rotation = Rotate(rotation, Vector3.UnitY, value);
                    break;
                default:
                    rotation = Rotate(rotation, Vector3.UnitZ, value);
                    break;
            }
        }
    }

    // Each rotation controller turns the bone about one axis of its parent, one after the other
    private static Quaternion Rotate(Quaternion rotation, Vector3 axis, float degrees)
    {
        var adjust = Quaternion.CreateFromAxisAngle(axis, float.DegreesToRadians(degrees));

        return Quaternion.Normalize(adjust * rotation);
    }

    private bool TryGetValue(int controllerIndex, ReadOnlySpan<float> values, out float value)
    {
        value = 0f;

        if ((uint)controllerIndex >= (uint)source.BoneControllers.Length)
        {
            return false;
        }

        var controller = source.BoneControllers[controllerIndex];

        if (!groupsByInputField.TryGetValue(controller.InputField, out var group))
        {
            return false;
        }

        var setting = group.GetSetting(values[group.Index]);
        value = controller.Start + ((controller.End - controller.Start) * setting);

        return true;
    }
}
