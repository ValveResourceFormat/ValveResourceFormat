using NUnit.Framework;
using ValveResourceFormat.IO;

namespace Tests;

[TestFixture]
public class GltfJointsTest
{
    [Test]
    public void FourJoints_Basic_NoZerosOrDuplicates_ShouldNotChange()
    {
        ushort[] joints = [1, 2, 3, 4];
        float[] weights = [0.4f, 0.3f, 0.2f, 0.1f];
        ushort[] expectedJoints = [1, 2, 3, 4];
        float[] expectedWeights = [0.4f, 0.3f, 0.2f, 0.1f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 4);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void FourJoints_WithZeroWeights_ShouldSetJointsToZero()
    {
        ushort[] joints = [1, 2, 3, 4];
        float[] weights = [0.5f, 0.5f, 0.0f, 0.0f];
        ushort[] expectedJoints = [1, 2, 0, 0];
        float[] expectedWeights = [0.5f, 0.5f, 0.0f, 0.0f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 4);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void FourJoints_WithDuplicates_ShouldMergeWeights()
    {
        ushort[] joints = [1, 2, 1, 3];
        float[] weights = [0.3f, 0.3f, 0.2f, 0.2f];
        ushort[] expectedJoints = [1, 2, 3, 0];
        float[] expectedWeights = [0.5f, 0.3f, 0.2f, 0.0f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 4);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void FourJoints_MultipleSets_ShouldProcessCorrectly()
    {
        ushort[] joints = [1, 2, 3, 4, 5, 6, 5, 7];
        float[] weights = [0.4f, 0.3f, 0.2f, 0.1f, 0.3f, 0.3f, 0.2f, 0.2f];
        ushort[] expectedJoints = [1, 2, 3, 4, 5, 6, 7, 0];
        float[] expectedWeights = [0.4f, 0.3f, 0.2f, 0.1f, 0.5f, 0.3f, 0.2f, 0.0f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 4);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void FourJoints_WithMultipleDuplicates_ShouldMergeAll()
    {
        ushort[] joints = [1, 1, 1, 1];
        float[] weights = [0.25f, 0.25f, 0.25f, 0.25f];
        ushort[] expectedJoints = [1, 0, 0, 0];
        float[] expectedWeights = [1.0f, 0.0f, 0.0f, 0.0f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 4);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void EightJoints_Basic_NoZerosOrDuplicates_ShouldNotChange()
    {
        ushort[] joints = [1, 2, 3, 4, 5, 6, 7, 8];
        float[] weights = [0.2f, 0.2f, 0.15f, 0.15f, 0.1f, 0.1f, 0.05f, 0.05f];
        ushort[] expectedJoints = [1, 2, 3, 4, 5, 6, 7, 8];
        float[] expectedWeights = [0.2f, 0.2f, 0.15f, 0.15f, 0.1f, 0.1f, 0.05f, 0.05f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 8);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void EightJoints_WithZeroWeights_ShouldSetJointsToZero()
    {
        ushort[] joints = [1, 2, 3, 4, 5, 6, 7, 8];
        float[] weights = [0.3f, 0.3f, 0.2f, 0.2f, 0.0f, 0.0f, 0.0f, 0.0f];
        ushort[] expectedJoints = [1, 2, 3, 4, 0, 0, 0, 0];
        float[] expectedWeights = [0.3f, 0.3f, 0.2f, 0.2f, 0.0f, 0.0f, 0.0f, 0.0f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 8);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void EightJoints_WithDuplicates_ShouldMergeWeights()
    {
        ushort[] joints = [1, 2, 3, 4, 1, 5, 6, 7];
        float[] weights = [0.2f, 0.2f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f];
        ushort[] expectedJoints = [1, 2, 3, 4, 5, 6, 7, 0];
        float[] expectedWeights = [0.3f, 0.2f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.0f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 8);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void EightJoints_WithMultipleDuplicates_ShouldMergeAll()
    {
        ushort[] joints = [1, 2, 1, 3, 2, 4, 3, 4];
        float[] weights = [0.15f, 0.15f, 0.15f, 0.15f, 0.1f, 0.1f, 0.1f, 0.1f];
        ushort[] expectedJoints = [1, 2, 3, 4, 0, 0, 0, 0];
        float[] expectedWeights = [0.3f, 0.25f, 0.25f, 0.2f, 0.0f, 0.0f, 0.0f, 0.0f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 8);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void EightJoints_ComplexCase_ShouldHandleCorrectly()
    {
        ushort[] joints = [1, 1, 1, 2, 2, 3, 3, 4];
        float[] weights = [0.1f, 0.1f, 0.1f, 0.2f, 0.2f, 0.15f, 0.15f, 0.0f];
        ushort[] expectedJoints = [1, 2, 3, 0, 0, 0, 0, 0];
        float[] expectedWeights = [0.3f, 0.4f, 0.3f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 8);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void EightJoints_AllDuplicates_ShouldCollapse()
    {
        ushort[] joints = [1, 1, 1, 1, 1, 1, 1, 1];
        float[] weights = [0.125f, 0.125f, 0.125f, 0.125f, 0.125f, 0.125f, 0.125f, 0.125f];
        ushort[] expectedJoints = [1, 0, 0, 0, 0, 0, 0, 0];
        float[] expectedWeights = [1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 8);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void EmptyArrays_ShouldHandleGracefully()
    {
        ushort[] joints = [];
        float[] weights = [];
        ushort[] expectedJoints = [];
        float[] expectedWeights = [];
        GltfModelExporter.FixDuplicateJoints(joints, weights, 4);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void SpanSlices_ShouldProcessCorrectly()
    {
        ushort[] joints = [9, 9, 9, 1, 2, 1, 3, 9, 9];
        float[] weights = [9f, 9f, 9f, 0.4f, 0.3f, 0.2f, 0.1f, 9f, 9f];

        var jointsSpan = new Span<ushort>(joints, 3, 4);
        var weightsSpan = new Span<float>(weights, 3, 4);

        GltfModelExporter.FixDuplicateJoints(jointsSpan, weightsSpan, 4);

        ushort[] expectedJoints = [9, 9, 9, 1, 2, 3, 0, 9, 9];
        float[] expectedWeights = [9f, 9f, 9f, 0.6f, 0.3f, 0.1f, 0.0f, 9f, 9f];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void FourJoints_LongerData_ShouldProcessAllSets()
    {
        // Testing with 5 sets of 4 joints
        ushort[] joints = [
            1, 2, 1, 3,    // First set - has duplicates
            4, 5, 6, 7,    // Second set - no duplicates
            8, 8, 8, 8,    // Third set - all duplicates
            9, 10, 0, 0,   // Fourth set - has zeros
            11, 11, 12, 12 // Fifth set - paired duplicates
        ];
        float[] weights = [
            0.4f, 0.3f, 0.2f, 0.1f,
            0.25f, 0.25f, 0.25f, 0.25f,
            0.25f, 0.25f, 0.25f, 0.25f,
            0.5f, 0.5f, 0.0f, 0.0f,
            0.3f, 0.2f, 0.3f, 0.2f
        ];

        ushort[] expectedJoints = [
            1, 2, 3, 0,      // Merged duplicate 1
            4, 5, 6, 7,      // No change
            8, 0, 0, 0,      // All merged to first position
            9, 10, 0, 0,     // Zeros stay zeros
            11, 12, 0, 0     // Merged duplicates
        ];
        float[] expectedWeights = [
            0.6f, 0.3f, 0.1f, 0.0f,
            0.25f, 0.25f, 0.25f, 0.25f,
            1.0f, 0.0f, 0.0f, 0.0f,
            0.5f, 0.5f, 0.0f, 0.0f,
            0.5f, 0.5f, 0.0f, 0.0f
        ];

        GltfModelExporter.FixDuplicateJoints(joints, weights, 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void EightJoints_LongerData_ShouldProcessAllSets()
    {
        // Testing with 3 sets of 8 joints
        ushort[] joints = [
            1, 2, 3, 1, 4, 5, 6, 2,       // First set - has duplicates
            7, 8, 9, 10, 11, 12, 13, 14,  // Second set - no duplicates
            15, 15, 15, 15, 0, 0, 0, 0    // Third set - duplicates and zeros
        ];
        float[] weights = [
            0.2f, 0.2f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f,
            0.125f, 0.125f, 0.125f, 0.125f, 0.125f, 0.125f, 0.125f, 0.125f,
            0.25f, 0.25f, 0.25f, 0.25f, 0.0f, 0.0f, 0.0f, 0.0f
        ];

        ushort[] expectedJoints = [
            1, 2, 3, 4, 5, 6, 0, 0,       // Merged duplicates 1 and 2
            7, 8, 9, 10, 11, 12, 13, 14,  // No change
            15, 0, 0, 0, 0, 0, 0, 0       // Merged duplicates, zeros remain
        ];
        float[] expectedWeights = [
            0.3f, 0.3f, 0.1f, 0.1f, 0.1f, 0.1f, 0.0f, 0.0f,
            0.125f, 0.125f, 0.125f, 0.125f, 0.125f, 0.125f, 0.125f, 0.125f,
            1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f
        ];

        GltfModelExporter.FixDuplicateJoints(joints, weights, 8);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }

    [Test]
    public void FourJoints_ComplexDuplicates_ShouldHandleCorrectly()
    {
        // Test case with duplicates spread throughout the array
        ushort[] joints = [
            1, 1, 2, 2,    // All duplicates within set
            3, 4, 5, 3,    // Duplicate at beginning and end
            6, 7, 6, 7     // Alternating duplicates
        ];
        float[] weights = [
            0.3f, 0.2f, 0.3f, 0.2f,
            0.4f, 0.2f, 0.1f, 0.3f,
            0.4f, 0.4f, 0.1f, 0.1f
        ];

        ushort[] expectedJoints = [
            1, 2, 0, 0,    // Merged duplicates
            3, 4, 5, 0,    // Merged beginning and end
            6, 7, 0, 0     // Merged alternating duplicates
        ];
        float[] expectedWeights = [
            0.5f, 0.5f, 0.0f, 0.0f,
            0.7f, 0.2f, 0.1f, 0.0f,
            0.5f, 0.5f, 0.0f, 0.0f
        ];

        GltfModelExporter.FixDuplicateJoints(joints, weights, 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(joints, Is.EqualTo(expectedJoints).AsCollection);
            Assert.That(weights, Is.EqualTo(expectedWeights).AsCollection.Within(0.0001f));
        }
    }
}
