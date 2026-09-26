using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>
    /// Builds an <see cref="FeModel"/> out of hand-written KV3, so a compiler law can be exercised on
    /// inputs whose expected result is computed by hand rather than read off a shipped model.
    /// </summary>
    internal static class SyntheticCloth
    {
        private const string Header =
            "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} "
            + "format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->\n";

        public static FeModel Parse(string feModelBody)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Header + feModelBody));
            return new FeModel(KVDocumentExtensions.ParseKV3(stream).Root);
        }

        /// <summary>
        /// Writes an FeModel body that states <c>m_CtrlName</c>, <c>m_nNodeCount</c>, <c>m_nStaticNodes</c> and
        /// <c>m_NodeInvMasses</c>, then <c>m_SkelParents</c> and <c>m_InitPose</c> where given, then <paramref name="body"/>.
        /// The inverse masses default to 0 on the static nodes and 1 on the rest.
        /// </summary>
        public static string Document(string[] names, int staticNodes, Vector3[]? poses = null, int[]? parents = null,
            string? invMasses = null, string body = "")
        {
            var text = new StringBuilder();
            text.Append("{\n");
            text.Append(CultureInfo.InvariantCulture, $"m_CtrlName = [ {string.Join(", ", names.Select(static name => $"\"{name}\""))} ]\n");
            if (parents is not null)
            {
                text.Append(CultureInfo.InvariantCulture, $"m_SkelParents = [ {string.Join(", ", parents)} ]\n");
            }

            text.Append(CultureInfo.InvariantCulture, $"m_nNodeCount = {names.Length}\n");
            text.Append(CultureInfo.InvariantCulture, $"m_nStaticNodes = {staticNodes}\n");
            invMasses ??= string.Join(", ", names.Select((_, node) => node < staticNodes ? "0.0" : "1.0"));
            text.Append(CultureInfo.InvariantCulture, $"m_NodeInvMasses = [ {invMasses} ]\n");
            if (poses is not null)
            {
                text.Append("m_InitPose =\n[\n");
                foreach (var pose in poses)
                {
                    text.Append(Pose(pose.X, pose.Y, pose.Z)).Append('\n');
                }

                text.Append("]\n");
            }

            text.Append(body).Append("\n}");
            return text.ToString();
        }

        /// <summary>Parses <see cref="Document"/>.</summary>
        public static FeModel Model(string[] names, int staticNodes, Vector3[]? poses = null, int[]? parents = null,
            string? invMasses = null, string body = "")
            => Parse(Document(names, staticNodes, poses, parents, invMasses, body));

        /// <summary>Reads the FeModel body of a KV3 fixture in <c>Tests/Files</c>.</summary>
        public static string Fixture(string fileName)
        {
            var text = File.ReadAllText(Path.Combine(TestContext.TestDirectory!, "Files", fileName)).ReplaceLineEndings("\n");
            return text.StartsWith("<!--", StringComparison.Ordinal) ? text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..] : text;
        }

        /// <summary>Parses <see cref="Fixture"/>.</summary>
        public static FeModel Load(string fileName) => Parse(Fixture(fileName));

        /// <summary>A proxy mesh over <paramref name="nodes"/> with every other stream zero and no skin influences.</summary>
        public static FeModel.ProxyMesh Proxy(int[] nodes, float[] clothEnable, List<int[]> faces, Vector3[]? positions = null)
            => new()
            {
                NodeIndices = nodes,
                Positions = positions ?? new Vector3[nodes.Length],
                ClothEnable = clothEnable,
                GoalStrength = new float[nodes.Length],
                GoalDamping = new float[nodes.Length],
                CollisionRadius = new float[nodes.Length],
                Friction = new float[nodes.Length],
                Drag = new float[nodes.Length],
                GroundCollision = new float[nodes.Length],
                GroundFriction = new float[nodes.Length],
                Gravity = new float[nodes.Length],
                VertexAttraction = new float[nodes.Length],
                SkinInfluences = [.. nodes.Select(static _ => Array.Empty<(string, float)>())],
                Faces = faces,
            };

        /// <summary>Formats a float so KV3 always reads it back as a floating point value.</summary>
        public static string Num(float value)
        {
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal)
                ? text
                : text + ".0";
        }

        /// <summary>An identity <c>m_InitPose</c> row at the given position.</summary>
        public static string Pose(float x, float y, float z)
            => $"[ {Num(x)}, {Num(y)}, {Num(z)}, 1.0, 0.0, 0.0, 0.0, 1.0 ],";

        /// <summary>A rigid rod, whose minimum equals its maximum.</summary>
        public static string RigidRod(int a, int b, float length, float relaxation)
            => Rod(a, b, length, length, relaxation);

        /// <summary>A length-banded rod, free to move between its two bounds.</summary>
        public static string BandedRod(int a, int b, float min, float max, float relaxation)
            => Rod(a, b, min, max, relaxation);

        private static string Rod(int a, int b, float min, float max, float relaxation)
            => $"{{ nNode = [ {a}, {b} ] flMinDist = {Num(min)} flMaxDist = {Num(max)} "
                + $"flWeight0 = 0.5 flRelaxationFactor = {Num(relaxation)} }},";
    }
}
