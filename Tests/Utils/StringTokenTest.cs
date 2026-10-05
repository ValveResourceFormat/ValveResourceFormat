using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace Tests.Utils
{
    public class StringTokenTest
    {
        [Test]
        public async Task EnsureUniqueStringToken()
        {
            var seen = new Dictionary<uint, string>(EntityLumpKnownKeys.KnownKeys.Length);

            foreach (var key in EntityLumpKnownKeys.KnownKeys)
            {
                var token = StringToken.Get(key);

                await Assert.That(token).IsEqualTo(StringToken.Get(key.ToLowerInvariant())).Because($"{key} must hash the same as its lowercase form.");

                if (seen.TryGetValue(token, out var collision))
                {
                    Fail.Test($"{key} ({token}) collides with {collision}");
                }

                seen[token] = key;
            }
        }

        [Test]
        public async Task EnsureKnownKeysAreSorted()
        {
            // Case is ignored so that changing the casing of a key never moves it
            var comparer = CultureInfo.InvariantCulture.CompareInfo.GetStringComparer(CompareOptions.NumericOrdering | CompareOptions.IgnoreCase);
            var keys = EntityLumpKnownKeys.KnownKeys;

            for (var i = 1; i < keys.Length; i++)
            {
                if (comparer.Compare(keys[i - 1], keys[i]) >= 0)
                {
                    Fail.Test($"{nameof(EntityLumpKnownKeys)} must be sorted: \"{keys[i - 1]}\" should come after \"{keys[i]}\"");
                }
            }

            await Assert.That(keys).IsNotEmpty();
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
        [Arguments("", 0u)]
        [Arguments("foo", 0x5DA24C1Au)]
        [Arguments("Foo", 0x5DA24C1Au)]
        [Arguments("targetname", 0x4137AF6Bu)]
        [Arguments("TargetName", 0x4137AF6Bu)]
        [Arguments("Äx", 0x7FF8CDE1u)]
        [Arguments("äx", 0x83075362u)]
        public async Task HashesLikeTheEngine(string key, uint expected)
        {
            // Only ASCII letters are case folded, everything else is hashed as UTF-8 bytes
            await Assert.That(StringToken.Get(key)).IsEqualTo(expected);
        }

        [Test]
        public async Task HashesLongStringsLikeShortOnes()
        {
            var key = string.Concat(Enumerable.Repeat("AbÄ", 200));
            var lower = string.Concat(Enumerable.Repeat("abÄ", 200));

            await Assert.That(StringToken.Get(key)).IsEqualTo(StringToken.Get(lower));
            await Assert.That(StringToken.Get(key)).IsNotEqualTo(StringToken.Get(key.ToLowerInvariant()));
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
