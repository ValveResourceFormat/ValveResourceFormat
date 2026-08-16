using System.Threading.Tasks;
using ValveResourceFormat.Utils;

namespace Tests
{
    public class StringTokenTest
    {
        [Test]
        public async Task EnsureUniqueStringToken()
        {
            var seen = new Dictionary<uint, string>(EntityLumpKnownKeys.KnownKeys.Length);

            foreach (var key in EntityLumpKnownKeys.KnownKeys)
            {
                await Assert.That(key).IsEqualTo(key.ToLowerInvariant()).Because($"{nameof(EntityLumpKnownKeys)} keys must be in lowercase.");

                var token = StringToken.Get(key);

                if (seen.TryGetValue(token, out var collision))
                {
                    Fail.Test($"{key} ({token}) collides with {collision}");
                }

                seen[token] = key;
            }
        }


        [Test]
        public async Task EnsureStoresCustomKnownKeys()
        {
            var key = "my custom stringtoken key";
            await Assert.That(EntityLumpKnownKeys.KnownKeys).DoesNotContain(key);

            var addedHash = StringToken.Store(key);
            var inverseLookupKey = StringToken.GetKnownString(addedHash);
            await Assert.That(inverseLookupKey).IsEqualTo(key);
        }

        [Test]
        public async Task EnsurePreservesStringCase()
        {
            var key = "MyPreservedCaseKey";

            var addedHash = StringToken.Store(key);
            var inverseLookupKey = StringToken.GetKnownString(addedHash);
            await Assert.That(inverseLookupKey).IsEqualTo(key);
        }

        [Test]
        public async Task EnsureStoresLowerCaseHash()
        {
            var key = "MyUppercaseKey";
            var key2 = "myuppercasekey";

            var upperCaseHash = StringToken.Store(key);
            var lowerCaseHash = StringToken.Store(key2);

            await Assert.That(upperCaseHash).IsEqualTo(lowerCaseHash);
        }
    }
}
