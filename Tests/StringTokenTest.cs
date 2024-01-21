using NUnit.Framework;
using ValveResourceFormat.Utils;

namespace Tests
{
    public class StringTokenTest
    {
        [Test]
        public void EnsureUniqueStringToken()
        {
            var seen = new Dictionary<uint, string>();

            foreach (var key in StringToken.Lookup)
            {
                Assert.That(key.Key, Is.EqualTo(key.Key.ToLowerInvariant()));

                if (seen.TryGetValue(key.Value, out var collision))
                {
                    Assert.Fail($"{key.Key} ({key.Value}) collides with {collision}");
                }

                seen[key.Value] = key.Key;
            }
        }
    }
}
