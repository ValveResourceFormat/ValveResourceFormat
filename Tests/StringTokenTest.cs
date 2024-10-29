using NUnit.Framework;
using ValveResourceFormat.Utils;

namespace Tests
{
    public class StringTokenTest
    {
        [Test]
        public void EnsureUniqueStringToken()
        {
            var seen = new Dictionary<uint, string>(EntityLumpKnownKeys.KnownKeys.Length);

            foreach (var key in EntityLumpKnownKeys.KnownKeys)
            {
                Assert.That(key, Is.EqualTo(key.ToLowerInvariant()), $"{nameof(EntityLumpKnownKeys)} keys must be in lowercase.");

                var token = StringToken.Get(key);

                if (seen.TryGetValue(token, out var collision))
                {
                    Assert.Fail($"{key} ({token}) collides with {collision}");
                }

                seen[token] = key;
            }
        }


        [Test]
        public void EnsureStoresCustomKnownKeys()
        {
            var key = "my custom stringtoken key";
            Assert.That(EntityLumpKnownKeys.KnownKeys, Does.Not.Contain(key));

            var addedHash = StringToken.Store(key);
            var inverseLookupKey = StringToken.GetKnownString(addedHash);
            Assert.That(inverseLookupKey, Is.EqualTo(key));
        }
    }
}
