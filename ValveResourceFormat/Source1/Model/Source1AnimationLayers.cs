namespace ValveResourceFormat.Source1.Model;

/// <summary>A sequence layered on top of the base sequence of a <see cref="Source1AnimationLayers"/>.</summary>
internal sealed class Source1SequenceLayer
{
    /// <summary>The layered sequence. Delta sequences are added to the pose, absolute ones are blended in per bone.</summary>
    public required Source1Sequence Sequence { get; init; }

    /// <summary>Weight from 0 to 1.</summary>
    public float Weight { get; set; } = 1f;

    /// <summary>Position in the layered sequence from 0 to 1, or null to follow the cycle of the base sequence.</summary>
    public float? Cycle { get; set; }
}

/// <summary>
/// A base sequence with layers on top, evaluated with one set of pose parameters. This is how the engine builds a
/// pose from the main sequence and its overlays, for example a stand sequence with an aim matrix delta driven
/// by the pitch and yaw pose parameters.
/// </summary>
internal sealed class Source1AnimationLayers
{
    private readonly Source1Model model;

    /// <summary>Creates a stack with pose parameters at their default values.</summary>
    /// <param name="model">Model the sequences belong to.</param>
    public Source1AnimationLayers(Source1Model model)
    {
        ArgumentNullException.ThrowIfNull(model);

        this.model = model;
        PoseParameterValues = model.CreateDefaultPoseParameters();
    }

    /// <summary>The sequence the pose starts from, null for the reference pose.</summary>
    public Source1Sequence? BaseSequence { get; set; }

    /// <summary>Position in the base sequence from 0 to 1.</summary>
    public float BaseCycle { get; set; }

    /// <summary>Layers applied in order on top of the base sequence.</summary>
    public List<Source1SequenceLayer> Layers { get; } = [];

    /// <summary>Whether the autoplay sequences of the model are applied after the layers.</summary>
    public bool ApplyAutoplay { get; set; }

    /// <summary>Time in seconds that drives autoplay sequences.</summary>
    public float AutoplayTime { get; set; }

    /// <summary>Pose parameter values in their own units, indexed like <see cref="Source1Model.PoseParameters"/>.</summary>
    public float[] PoseParameterValues { get; }

    /// <summary>Sets a pose parameter by name.</summary>
    /// <returns>False when the model has no pose parameter of that name.</returns>
    public bool SetPoseParameter(string name, float value)
    {
        var index = model.FindPoseParameter(name);

        if (index < 0)
        {
            return false;
        }

        PoseParameterValues[index] = value;
        return true;
    }

    /// <summary>Adds a layer that follows the cycle of the base sequence.</summary>
    public Source1SequenceLayer AddLayer(Source1Sequence sequence, float weight = 1f)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        var layer = new Source1SequenceLayer { Sequence = sequence, Weight = weight };
        Layers.Add(layer);
        return layer;
    }

    /// <summary>Evaluates the stack to bone-local transforms of the model.</summary>
    /// <param name="positions">Receives local positions, at least as many as the model has bones.</param>
    /// <param name="rotations">Receives local rotations, at least as many as the model has bones.</param>
    public void Evaluate(Span<Vector3> positions, Span<Quaternion> rotations)
    {
        model.GetReferencePose(positions, rotations);

        BaseSequence?.Accumulate(BaseCycle, PoseParameterValues, 1f, positions, rotations);

        foreach (var layer in Layers)
        {
            layer.Sequence.Accumulate(layer.Cycle ?? BaseCycle, PoseParameterValues, layer.Weight, positions, rotations);
        }

        if (!ApplyAutoplay)
        {
            return;
        }

        foreach (var sequence in model.AutoplaySequences)
        {
            var cycle = sequence.GetCyclesPerSecond(PoseParameterValues) * AutoplayTime;
            cycle -= MathF.Floor(cycle);
            sequence.Accumulate(cycle, PoseParameterValues, 1f, positions, rotations);
        }
    }
}
