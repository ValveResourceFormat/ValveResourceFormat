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
            var stringToMurmurHash = StringToken.InitializeLookup().StringToToken;

            foreach (var key in stringToMurmurHash)
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
