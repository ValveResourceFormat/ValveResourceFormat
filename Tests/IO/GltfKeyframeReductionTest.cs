using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.IO;

namespace Tests.IO;

public class GltfKeyframeReductionTest
{
    private static List<float> FrameTimes(int count)
    {
        var times = new List<float>(count);
        for (var i = 0; i < count; i++)
        {
            times.Add(i / 30f);
        }

        return times;
    }

    private static List<int> ReduceTranslations(List<Vector3> values)
        => GltfModelExporter.ReduceKeyframes(FrameTimes(values.Count), values, GltfModelExporter.TranslationTolerance, GltfModelExporter.VectorError);

    [Test]
    public async Task LinearRamp_KeepsFirstAndLast()
    {
        var values = new List<Vector3>();
        for (var i = 0; i < 20; i++)
        {
            values.Add(new Vector3(i * 0.01f, 0.5f, -i * 0.02f));
        }

        var kept = ReduceTranslations(values);

        await Assert.That(kept).IsEquivalentTo([0, 19], EqualityComparer<int>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Constant_KeepsSingleKey()
    {
        var values = new List<Vector3>();
        for (var i = 0; i < 20; i++)
        {
            values.Add(new Vector3(0.25f, 0.5f, 0.75f));
        }

        var kept = ReduceTranslations(values);

        await Assert.That(kept).IsEquivalentTo([0], EqualityComparer<int>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task BumpWithinTolerance_IsRemoved()
    {
        var values = new List<Vector3>();
        for (var i = 0; i < 11; i++)
        {
            values.Add(new Vector3(i * 0.01f, 0f, 0f));
        }

        values[5] += new Vector3(0f, GltfModelExporter.TranslationTolerance * 0.5f, 0f);

        var kept = ReduceTranslations(values);

        await Assert.That(kept).IsEquivalentTo([0, 10], EqualityComparer<int>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task BumpAboveTolerance_IsKept()
    {
        var values = new List<Vector3>();
        for (var i = 0; i < 11; i++)
        {
            values.Add(new Vector3(i * 0.01f, 0f, 0f));
        }

        values[5] += new Vector3(0f, GltfModelExporter.TranslationTolerance * 2f, 0f);

        var kept = ReduceTranslations(values);

        await Assert.That(kept).IsEquivalentTo([0, 4, 5, 6, 10], EqualityComparer<int>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task RotationBumpAboveTolerance_IsKept()
    {
        var bumpAngle = GltfModelExporter.RotationTolerance * 2f;
        var values = new List<Quaternion>();
        for (var i = 0; i < 11; i++)
        {
            var angle = i == 5 ? bumpAngle : 0f;
            values.Add(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle));
        }

        var kept = GltfModelExporter.ReduceKeyframes(FrameTimes(values.Count), values, GltfModelExporter.RotationTolerance, GltfModelExporter.RotationError);

        await Assert.That(kept).IsEquivalentTo([0, 4, 5, 6, 10], EqualityComparer<int>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task RotationOnSlerpPathWithWideKeys_KeepsKeysNlerpCannotReproduce()
    {
        var start = Quaternion.Identity;
        var end = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, float.DegreesToRadians(170f));
        var values = new List<Quaternion>();
        for (var i = 0; i < 4; i++)
        {
            values.Add(Quaternion.Slerp(start, end, i / 3f));
        }

        var kept = GltfModelExporter.ReduceKeyframes(FrameTimes(values.Count), values, GltfModelExporter.RotationTolerance, GltfModelExporter.RotationError);

        await Assert.That(kept.Count).IsGreaterThan(2);
    }

    [Test]
    public async Task MorphWeightsRamp_KeepsFirstAndLast()
    {
        var values = new List<float[]>();
        for (var i = 0; i < 10; i++)
        {
            values.Add([i * 0.1f, 1f - i * 0.1f]);
        }

        var kept = GltfModelExporter.ReduceKeyframes(FrameTimes(values.Count), values, GltfModelExporter.MorphWeightTolerance, GltfModelExporter.WeightsError);

        await Assert.That(kept).IsEquivalentTo([0, 9], EqualityComparer<int>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task QuaternionSignFlips_AreAligned()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 1f);
        List<Quaternion> rotations = [rotation, Quaternion.Negate(rotation), rotation, Quaternion.Negate(rotation)];

        GltfModelExporter.AlignQuaternionHemispheres(rotations);
        var kept = GltfModelExporter.ReduceKeyframes(FrameTimes(rotations.Count), rotations, GltfModelExporter.RotationTolerance, GltfModelExporter.RotationError);

        using (Assert.Multiple())
        {
            await Assert.That(rotations).IsEquivalentTo([rotation, rotation, rotation, rotation], EqualityComparer<Quaternion>.Default, CollectionOrdering.Matching);
            await Assert.That(kept).IsEquivalentTo([0], EqualityComparer<int>.Default, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task RepeatedValues_KeepRunEnds()
    {
        List<float> values = [1f, 2f, 2f, 2f, 3f, 3f];

        var kept = GltfModelExporter.CollapseRepeatedKeyframes(values);

        await Assert.That(kept).IsEquivalentTo([0, 1, 3, 4, 5], EqualityComparer<int>.Default, CollectionOrdering.Matching);
    }
}
